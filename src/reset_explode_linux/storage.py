"""All machine-specific state stays outside the source/repository."""

from __future__ import annotations
import json, os, pathlib, tempfile, time

P = pathlib.Path


def config_dir():
    p = (
        P(os.environ.get("XDG_CONFIG_HOME", str(P.home() / ".config")))
        / "reset-explode-linux"
    )
    p.mkdir(parents=True, exist_ok=True)
    return p


def state_dir():
    p = (
        P(os.environ.get("XDG_STATE_HOME", str(P.home() / ".local/state")))
        / "reset-explode-linux"
    )
    p.mkdir(parents=True, exist_ok=True)
    return p


def load(name, default):
    try:
        return json.loads((config_dir() / name).read_text())
    except (OSError, ValueError):
        return default


def save(name, value):
    target = config_dir() / name
    fd, tmp = tempfile.mkstemp(dir=target.parent, prefix="." + name)
    try:
        with os.fdopen(fd, "w") as f:
            json.dump(value, f, ensure_ascii=False, indent=2)
        os.replace(tmp, target)
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)


def history(record):
    p = state_dir() / "history.json"
    try:
        items = json.loads(p.read_text())
    except (OSError, ValueError):
        items = []
    items.append({"time": time.time(), **record})
    p.write_text(json.dumps(items[-100:], ensure_ascii=False, indent=2))


def histories():
    try:
        return json.loads((state_dir() / "history.json").read_text())
    except (OSError, ValueError):
        return []
