"""Linux collectors. Read-only, no root, no serial numbers or network secrets."""

from __future__ import annotations
import glob, json, os, pathlib, platform, shutil, subprocess, time, shlex
import psutil

P = pathlib.Path


# Debian keeps hardware tools and game wrappers outside some desktop PATHs.
for directory in ("/usr/sbin", "/sbin", "/usr/games"):
    if directory not in os.environ.get("PATH", "").split(os.pathsep):
        os.environ["PATH"] = os.environ.get("PATH", "") + os.pathsep + directory


def gpu_benchmark():
    names = (
        ("glmark2-wayland", "glmark2")
        if os.environ.get("WAYLAND_DISPLAY")
        else ("glmark2", "glmark2-wayland")
    )
    return next((shutil.which(name) for name in names if shutil.which(name)), "glmark2")


def read(path, default=""):
    try:
        return P(path).read_text(errors="replace").strip()
    except (OSError, ValueError):
        return default


def command(argv, timeout=8):
    try:
        p = subprocess.run(
            argv,
            capture_output=True,
            text=True,
            timeout=timeout,
            env={**os.environ, "LC_ALL": "C"},
        )
        return p.stdout.strip(), p.stderr.strip(), p.returncode
    except (OSError, subprocess.TimeoutExpired) as exc:
        return "", str(exc), -1


def size(n):
    if n is None:
        return "Indisponível"
    for unit in ["B", "KiB", "MiB", "GiB", "TiB", "PiB"]:
        if abs(n) < 1024:
            return f"{n:.1f} {unit}"
        n /= 1024
    return f"{n:.1f} EiB"


def os_release():
    return {
        k: v.strip('"')
        for line in read("/etc/os-release").splitlines()
        if "=" in line
        for k, v in [line.split("=", 1)]
    }


def inventory():
    cpu = next(
        (
            line.split(":", 1)[1].strip()
            for line in read("/proc/cpuinfo").splitlines()
            if line.startswith("model name")
        ),
        "Desconhecido",
    )
    vm = psutil.virtual_memory()
    board = "/sys/class/dmi/id/"
    return {
        "Sistema": {
            "Distribuição": os_release().get("PRETTY_NAME", platform.system()),
            "Kernel": platform.release(),
            "Arquitetura": platform.machine(),
            "Sessão": os.environ.get("XDG_SESSION_TYPE", "Desconhecida"),
        },
        "CPU": {
            "Modelo": cpu,
            "Núcleos físicos": psutil.cpu_count(logical=False) or "Indisponível",
            "Threads": psutil.cpu_count(),
            "RAM instalada": size(vm.total),
        },
        "Placa-mãe e BIOS": {
            "Fabricante": read(board + "board_vendor", "Indisponível"),
            "Modelo": read(board + "board_name", "Indisponível"),
            "BIOS": read(board + "bios_version", "Indisponível"),
            "Data": read(board + "bios_date", "Indisponível"),
        },
        "Vídeo": gpu_info(),
    }


def gpu_info():
    out, _, _ = command(["lspci", "-mm"], 4)
    result = {}
    for line in out.splitlines():
        if not any(
            x in line.lower()
            for x in ("vga compatible", "3d controller", "display controller")
        ):
            continue
        fields = shlex.split(line)
        if len(fields) < 4:
            continue
        driver = P("/sys/bus/pci/devices/0000:" + fields[0]) / "driver"
        result[f"GPU {len(result) + 1}"] = {
            "Fabricante": fields[2],
            "Modelo": fields[3],
            "Driver do kernel": driver.resolve().name
            if driver.exists()
            else "Não identificado",
            "Endereço PCI": fields[0],
        }
    return result or {"GPU": "lspci indisponível; consulte os sensores DRM"}


def sensor_rows():
    rows = []
    try:
        for chip, entries in psutil.sensors_temperatures().items():
            for i, e in enumerate(entries):
                rows.append(
                    {
                        "id": f"{chip}:{i}",
                        "chip": chip,
                        "name": e.label or f"Temperatura {i + 1}",
                        "value": e.current,
                        "unit": "°C",
                        "high": e.high,
                    }
                )
    except (OSError, AttributeError):
        pass
    for hw in sorted(P("/sys/class/hwmon").glob("hwmon*")):
        chip = read(hw / "name", hw.name)
        for pattern, unit, factor in [
            ("fan*_input", "RPM", 1),
            ("power*_average", "W", 1e6),
            ("power*_input", "W", 1e6),
            ("in*_input", "V", 1000),
            ("freq*_input", "MHz", 1e6),
        ]:
            for f in hw.glob(pattern):
                try:
                    value = float(read(f)) / factor
                except ValueError:
                    continue
                rows.append(
                    {
                        "id": str(f),
                        "chip": chip,
                        "name": read(
                            f.with_name(
                                f.name.replace("_input", "_label").replace(
                                    "_average", "_label"
                                )
                            ),
                            f.stem,
                        ),
                        "value": value,
                        "unit": unit,
                        "high": None,
                    }
                )
    for card in sorted(P("/sys/class/drm").glob("card[0-9]*")):
        f = card / "device/gpu_busy_percent"
        if f.exists():
            try:
                rows.append(
                    {
                        "id": str(f),
                        "chip": card.name,
                        "name": "Uso da GPU",
                        "value": float(read(f)),
                        "unit": "%",
                        "high": None,
                    }
                )
            except ValueError:
                pass
    return rows


def snapshot():
    vm = psutil.virtual_memory()
    freq = psutil.cpu_freq()
    s = sensor_rows()
    per = psutil.cpu_percent(percpu=True)
    cpu_t = next(
        (
            r["value"]
            for r in s
            if r["unit"] == "°C"
            and any(
                x in (r["chip"] + " " + r["name"]).lower()
                for x in ["k10temp", "coretemp", "tctl", "tdie", "package"]
            )
        ),
        None,
    )
    gpu = next((r["value"] for r in s if r["name"] == "Uso da GPU"), None)
    return {
        "time": time.time(),
        "cpu": sum(per) / len(per) if per else 0,
        "cores": per,
        "ram": vm.percent,
        "ram_used": vm.used,
        "ram_total": vm.total,
        "swap": psutil.swap_memory().percent,
        "mhz": freq.current if freq else None,
        "cpu_temp": cpu_t,
        "gpu": gpu,
        "sensors": s,
    }


def disks():
    result = []
    for p in psutil.disk_partitions(all=False):
        if p.fstype in ["squashfs", "overlay", "tmpfs"]:
            continue
        try:
            u = psutil.disk_usage(p.mountpoint)
        except OSError:
            continue
        result.append(
            {
                "Dispositivo": p.device,
                "Montagem": p.mountpoint,
                "Sistema de arquivos": p.fstype,
                "Capacidade": size(u.total),
                "Usado": size(u.used),
                "Livre": size(u.free),
                "Uso": f"{u.percent:.1f}%",
            }
        )
    return result


def physical_disks():
    out, err, rc = command(
        ["lsblk", "--json", "-d", "-o", "NAME,PATH,MODEL,SIZE,TRAN,ROTA,TYPE"], 4
    )
    try:
        return [x for x in json.loads(out)["blockdevices"] if x.get("type") == "disk"]
    except (ValueError, KeyError):
        return []


def disk_health(device):
    if device not in [d["path"] for d in physical_disks()]:
        raise ValueError("Escolha um disco físico detectado.")
    if not shutil.which("smartctl"):
        return {"status": "Instale smartmontools para ler o S.M.A.R.T."}
    out, err, rc = command(["smartctl", "--json", "--all", device], 15)
    try:
        data = json.loads(out)
    except ValueError:
        return {"status": err or "Sem resposta S.M.A.R.T."}
    # smartctl exit status is a bit mask; nonzero can mean an actual health failure.
    status = data.get("smart_status", {}).get("passed")
    errors = data.get("messages", [])
    return {
        "Avaliação": ("Sem falha reportada" if status else "Falha reportada")
        if status is not None
        else "Leitura indisponível (pode exigir permissão)",
        "Temperatura": str(data.get("temperature", {}).get("current", "Indisponível"))
        + " °C",
        "Horas ligado": data.get("power_on_time", {}).get("hours", "Indisponível"),
        "Ciclos": data.get("power_cycle_count", "Indisponível"),
        "NVMe": data.get("nvme_smart_health_information_log", {}),
        "Avisos": [m.get("string", "") for m in errors],
    }


def energy():
    rows = {}
    for supply in P("/sys/class/power_supply").glob("*"):
        info = {}
        for k in [
            "type",
            "status",
            "capacity",
            "cycle_count",
            "energy_full",
            "energy_full_design",
            "charge_full",
            "charge_full_design",
            "power_now",
            "voltage_now",
            "online",
        ]:
            v = read(supply / k)
            if v:
                info[k] = v
        full = info.get("energy_full", info.get("charge_full"))
        design = info.get("energy_full_design", info.get("charge_full_design"))
        if full and design and float(design) > 0:
            info["desgaste_percentual"] = round(
                max(0, 100 - 100 * float(full) / float(design)), 1
            )
        rows[supply.name] = info
    out, _, rc = command(["powerprofilesctl", "get"], 3)
    if rc == 0:
        rows["Perfil de energia"] = out
    return rows or {
        "Energia": "Não há bateria ou informações de alimentação disponíveis."
    }


def network():
    result = {}
    for name, stat in psutil.net_if_stats().items():
        result[name] = {
            "Ativa": stat.isup,
            "Velocidade (Mbps)": stat.speed,
            "MTU": stat.mtu,
        }
    return result


def drivers():
    pci, _, _ = command(["lspci", "-nnk"], 6)
    usb, _, _ = command(["lsusb"], 6)
    return {
        "PCI e drivers": pci or "Instale pciutils",
        "USB": usb or "Instale usbutils",
        "Módulos do kernel": read("/proc/modules"),
        "Portas seriais": ", ".join(
            sorted(glob.glob("/dev/ttyUSB*") + glob.glob("/dev/ttyACM*"))
        )
        or "Nenhuma placa serial USB detectada",
    }


def processes(limit=100):
    rows = []
    for p in psutil.process_iter(
        ["pid", "name", "cpu_percent", "memory_percent", "memory_info"]
    ):
        try:
            row = {k: v for k, v in p.info.items() if k != "memory_info"}
            row["rss"] = p.info["memory_info"].rss if p.info.get("memory_info") else 0
            rows.append(row)
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue
    return sorted(rows, key=lambda x: x.get("memory_percent") or 0, reverse=True)[
        :limit
    ]


def bottleneck(sample):
    if sample.get("cpu_temp") is not None and sample["cpu_temp"] >= 90:
        return (
            "Temperatura da CPU elevada. Confira refrigeração e limites do fabricante."
        )
    if sample["ram"] >= 90:
        return "Pressão de memória alta. Pode haver uso de swap e pausas."
    gpu = sample.get("gpu")
    cpu = sample["cpu"]
    core = max(sample.get("cores") or [cpu])
    if gpu is not None and gpu >= 95 and core < 95:
        return "Amostra indica limite de GPU. Reduzir resolução/efeitos pode ajudar."
    if core >= 95 and (gpu is None or gpu < 85):
        return "Amostra indica possível limite de CPU em uma thread."
    return "Sem gargalo evidente nesta amostra. Não é uma previsão de FPS ou diagnóstico definitivo."


def tools():
    names = [
        "sensors",
        "smartctl",
        "glmark2-wayland",
        "glmark2",
        "stress-ng",
        "mangohud",
        "gamemoderun",
        "powerprofilesctl",
        "OpenRGB",
        "openrgb",
        "nvidia-smi",
        "ping",
        "ip",
        "arduino-ide",
        "xdg-open",
        "apt-get",
        "dnf",
        "pacman",
        "flatpak",
    ]
    return {name: shutil.which(name) for name in names}
