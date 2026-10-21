"""Desktop integrations; no system modifications at import time."""

from __future__ import annotations
import configparser, json, os, pathlib, re, shlex, shutil, uuid, hashlib
from . import storage
from .system import read

P = pathlib.Path


def startups():
    user = P(os.environ.get("XDG_CONFIG_HOME", str(P.home() / ".config"))) / "autostart"
    roots = [
        P(x) / "autostart"
        for x in os.environ.get("XDG_CONFIG_DIRS", "/etc/xdg").split(":")
    ] + [user]
    files = {p.name: p for root in roots for p in root.glob("*.desktop")}
    result = []
    for name, p in sorted(files.items()):
        c = configparser.ConfigParser(interpolation=None, strict=False)
        try:
            c.read(p)
            d = c["Desktop Entry"]
        except (OSError, KeyError, configparser.Error):
            continue
        result.append(
            {
                "file": str(p),
                "id": name,
                "name": d.get("Name", name),
                "enabled": d.get("Hidden", "false").lower() != "true"
                and d.get("X-GNOME-Autostart-enabled", "true").lower() != "false",
                "exec": d.get("Exec", ""),
            }
        )
    return result


def set_startup(identifier, enabled):
    found = next((s for s in startups() if s["id"] == identifier), None)
    if not found:
        raise ValueError("Programa de inicialização não encontrado.")
    root = P(os.environ.get("XDG_CONFIG_HOME", str(P.home() / ".config"))) / "autostart"
    root.mkdir(parents=True, exist_ok=True)
    target = root / identifier
    backup = storage.state_dir() / "autostart-backups"
    backup.mkdir(exist_ok=True)
    marker = backup / (identifier + ".json")
    if not marker.exists():
        previous = target.read_text() if target.exists() else None
        marker.write_text(json.dumps({"previous": previous}))
        marker.chmod(0o600)
    c = configparser.ConfigParser(interpolation=None, strict=False)
    c.optionxform = str
    c.read(found["file"])
    c["Desktop Entry"]["Hidden"] = "false" if enabled else "true"
    c["Desktop Entry"]["X-GNOME-Autostart-enabled"] = "true" if enabled else "false"
    with target.open("w") as f:
        c.write(f, space_around_delimiters=False)


def restore_startups():
    root = P(os.environ.get("XDG_CONFIG_HOME", str(P.home() / ".config"))) / "autostart"
    for marker in (storage.state_dir() / "autostart-backups").glob("*.json"):
        previous = json.loads(marker.read_text())["previous"]
        target = root / marker.name[:-5]
        if previous is None:
            target.unlink(missing_ok=True)
        else:
            target.write_text(previous)
        marker.unlink()


def steam_roots():
    return [
        p
        for p in [
            P.home() / ".local/share/Steam",
            P.home() / ".steam/steam",
            P.home() / ".var/app/com.valvesoftware.Steam/.local/share/Steam",
        ]
        if p.exists()
    ]


def scan_games():
    result = []
    seen = set()
    for steam in steam_roots():
        libraries = [steam]
        for value in re.findall(
            r'"path"\s+"([^"]+)"', read(steam / "steamapps/libraryfolders.vdf")
        ):
            libraries.append(P(value.replace("\\\\", "\\")))
        for lib in libraries:
            for manifest in (lib / "steamapps").glob("appmanifest_*.acf"):
                fields = dict(re.findall(r'"([^"\n]+)"\s+"([^"\n]*)"', read(manifest)))
                appid = fields.get("appid")
                if not appid or appid in seen:
                    continue
                name = fields.get("name", appid)
                if name.startswith(
                    ("Proton", "Steam Linux", "Steamworks", "Steam Controller")
                ):
                    continue
                seen.add(appid)
                result.append(
                    {
                        "id": "steam-" + appid,
                        "name": name,
                        "kind": "steam",
                        "target": appid,
                        "args": "",
                        "gamemode": False,
                        "overlay": False,
                    }
                )
    # Heroic Epic/GOG installations use a user-owned JSON manifest.
    for folder in [
        P.home() / ".config/heroic",
        P.home() / ".var/app/com.heroicgameslauncher.hgl/config/heroic",
    ]:
        for file in [
            folder / "legendaryConfig/legendary/installed.json",
            folder / "gog_store/installed.json",
        ]:
            try:
                data = json.loads(file.read_text())
            except (OSError, ValueError):
                continue
            entries = data.values() if isinstance(data, dict) else data
            for e in entries:
                if not isinstance(e, dict):
                    continue
                path = e.get("install_path") or e.get("install_path_path")
                exe = e.get("executable")
                if path and exe:
                    result.append(
                        {
                            "id": "heroic-" + str(e.get("app_name", uuid.uuid4().hex)),
                            "name": e.get("title", e.get("app_name", "Heroic")),
                            "kind": "file",
                            "target": str(P(path) / exe),
                            "args": "",
                            "gamemode": False,
                            "overlay": False,
                        }
                    )
    desktop_root = (
        P(os.environ.get("XDG_DATA_HOME", str(P.home() / ".local/share")))
        / "applications"
    )
    for file in desktop_root.glob("*.desktop"):
        config = configparser.ConfigParser(interpolation=None, strict=False)
        try:
            config.read(file)
            entry = config["Desktop Entry"]
            if (
                "Game" not in entry.get("Categories", "").split(";")
                or entry.get("Hidden", "false").lower() == "true"
            ):
                continue
            result.append(
                {
                    "id": "desktop-" + file.name,
                    "name": entry.get("Name", file.stem),
                    "kind": "desktop",
                    "target": str(file),
                    "args": "",
                }
            )
        except (OSError, configparser.Error, KeyError):
            continue
    return result


def proton_candidates():
    paths = []
    for root in steam_roots():
        paths.extend((root / "steamapps/common").glob("Proton*/proton"))
    paths.extend((P.home() / ".steam/root/compatibilitytools.d").glob("*/proton"))
    return sorted(set(paths), key=lambda p: ("Experimental" not in str(p), str(p)))


def safe_game_id(identifier):
    return hashlib.sha256(str(identifier).encode()).hexdigest()[:24]


def game_command(game):
    if game["kind"] == "steam":
        uri = "steam://rungameid/" + str(int(game["target"]))
        if shutil.which("steam"):
            return ["steam", uri], None, None
        if shutil.which("flatpak"):
            import subprocess

            check = subprocess.run(
                ["flatpak", "info", "com.valvesoftware.Steam"],
                capture_output=True,
                timeout=5,
            )
            if check.returncode == 0:
                return ["flatpak", "run", "com.valvesoftware.Steam", uri], None, None
        raise ValueError("Steam não encontrada. Instale a Steam para abrir este jogo.")
    if game["kind"] == "desktop":
        target = P(game["target"]).expanduser().resolve()
        if not target.is_file() or target.suffix != ".desktop":
            raise ValueError("Atalho do jogo não encontrado.")
        return ["gio", "launch", str(target)], None, None
    target = P(game["target"]).expanduser().resolve()
    if not target.is_file():
        raise ValueError("Executável não encontrado.")
    args = shlex.split(game.get("args", ""))
    env = os.environ.copy()
    argv = [str(target), *args]
    if target.suffix.lower() == ".exe":
        selected = game.get("proton")
        candidates = proton_candidates()
        proton = P(selected) if selected else (candidates[0] if candidates else None)
        if not proton or not proton.is_file():
            raise ValueError(
                "Instale Proton pelo Steam ou configure seu caminho no perfil."
            )
        prefix = storage.state_dir() / "prefixes" / safe_game_id(game["id"])
        prefix.mkdir(parents=True, exist_ok=True)
        env.update(
            STEAM_COMPAT_DATA_PATH=str(prefix),
            STEAM_COMPAT_CLIENT_INSTALL_PATH=str(steam_roots()[0])
            if steam_roots()
            else str(P.home() / ".local/share/Steam"),
        )
        argv = [str(proton), "run", str(target), *args]
    if game.get("gamemode"):
        path = shutil.which("gamemoderun")
        if not path:
            raise ValueError("GameMode não está instalado.")
        argv = [path, *argv]
    if game.get("overlay"):
        path = shutil.which("mangohud")
        if not path:
            raise ValueError("MangoHud não está instalado.")
        argv = [path, *argv]
        env["MANGOHUD_CONFIG"] = (
            "fps,frametime,frame_timing,cpu_stats,gpu_stats,ram,vram,cpu_temp,gpu_temp,toggle_hud=Shift_R+F12"
        )
    return argv, target.parent, env


def steam_options(game):
    opts = []
    if game.get("gamemode"):
        opts.append("gamemoderun")
    if game.get("overlay"):
        opts.append("mangohud")
    return " ".join([*opts, "%command%", game.get("args", "")]).strip()


# Packages are selected from fixed entries, never shell fragments from the UI.
PACKAGES = {
    "Sensores": {"apt-get": "lm-sensors", "dnf": "lm_sensors", "pacman": "lm_sensors"},
    "Saúde dos discos": {
        "apt-get": "smartmontools",
        "dnf": "smartmontools",
        "pacman": "smartmontools",
    },
    "Overlay FPS": {"apt-get": "mangohud", "dnf": "mangohud", "pacman": "mangohud"},
    "Stress CPU/RAM": {
        "apt-get": "stress-ng",
        "dnf": "stress-ng",
        "pacman": "stress-ng",
    },
    "Benchmark GPU": {
        "apt-get": "glmark2-wayland",
        "dnf": "glmark2",
        "pacman": "glmark2",
    },
    "GameMode": {"apt-get": "gamemode", "dnf": "gamemode", "pacman": "gamemode"},
    "Iluminação OpenRGB": {"apt-get": "openrgb", "dnf": "OpenRGB", "pacman": "openrgb"},
    "VLC": {"apt-get": "vlc", "dnf": "vlc", "pacman": "vlc"},
    "Chromium": {"apt-get": "chromium", "dnf": "chromium", "pacman": "chromium"},
    "7-Zip": {"apt-get": "7zip", "dnf": "7zip", "pacman": "7zip"},
}


def install_command(entry):
    if entry not in PACKAGES:
        raise ValueError("Pacote não permitido.")
    for manager in ["apt-get", "dnf", "pacman"]:
        executable = shutil.which(manager)
        package = PACKAGES[entry].get(manager)
        if executable and package:
            return [
                "pkexec",
                executable,
                *(
                    {
                        "apt-get": ["install", "-y", package],
                        "dnf": ["install", "-y", package],
                        "pacman": ["-S", "--needed", "--noconfirm", package],
                    }[manager]
                ),
            ]
    raise ValueError(
        "Instale a ferramenta pelo gerenciador de pacotes da sua distribuição."
    )
