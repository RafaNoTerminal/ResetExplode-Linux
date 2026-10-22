"""Optional adapter for an independently installed official Ookla CLI.

No binary redistribution, license acceptance or automatic installation. Only
normalized measurement fields leave this module; IPs/ISP/result IDs are omitted.
"""

from __future__ import annotations
import json
import math
import pathlib
import shutil
import subprocess


def find_cli():
    candidates = [
        shutil.which("speedtest"),
        shutil.which("speedtest-ookla"),
        str(pathlib.Path.home() / ".local/bin/speedtest"),
    ]
    for candidate in dict.fromkeys(candidates):
        if not candidate:
            continue
        try:
            check = subprocess.run(
                [candidate, "--version"], capture_output=True, text=True, timeout=3
            )
            if check.returncode == 0 and "Speedtest by Ookla" in check.stdout:
                return candidate
        except (OSError, subprocess.TimeoutExpired):
            continue
    return None


def number(value):
    if (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(value)
        or value < 0
    ):
        return None
    return value


def normalize(data):
    """Explicit field allowlist: do not export the original CLI payload."""
    output = {
        "Teste": "Speedtest · Ookla CLI oficial",
        "Motor": "Ookla (instalado separadamente)",
    }
    ping = data.get("ping", {})
    for source, name in (("latency", "Latência (ms)"), ("jitter", "Jitter (ms)")):
        value = number(ping.get(source))
        if value is not None:
            output[name] = round(value, 2)
    for direction, label in (
        ("download", "Download (Mbps)"),
        ("upload", "Upload (Mbps)"),
    ):
        measurement = data.get(direction, {})
        value = number(measurement.get("bandwidth"))
        if value is not None:
            output[label] = round(value * 8 / 1e6, 2)
        latency = number(measurement.get("latency", {}).get("iqm"))
        if latency is not None:
            output["Latência durante " + direction + " (ms)"] = round(latency, 2)
    loss = number(data.get("packetLoss"))
    if loss is not None:
        output["Perda de pacotes (%)"] = round(loss, 2)
    server = data.get("server", {})
    if isinstance(server, dict):
        output["Servidor"] = " · ".join(
            str(server[key])
            for key in ("name", "location", "country")
            if server.get(key)
        )
        if server.get("id") is not None:
            output["ID do servidor"] = server["id"]
    total = sum(
        number(data.get(direction, {}).get("bytes")) or 0
        for direction in ("download", "upload")
    )
    output["Tráfego medido (MiB)"] = round(total / 1024**2, 2)
    output["Método"] = (
        "Motor oficial Speedtest/Ookla com seleção própria de servidor. Não é o teste HTTPS simplificado nem o teste oficial Cloudflare."
    )
    return output


def measure(progress=None, executable=None):
    cli = executable or find_cli()
    if not cli:
        raise ValueError(
            "Speedtest oficial da Ookla não encontrado. Instale-o pelas instruções do fabricante ou selecione Diagnóstico HTTPS. O pacote Python speedtest-cli é outro programa."
        )
    # This child remains in the worker's process group so Parar terminates both.
    proc = subprocess.Popen(
        [cli, "--format=json", "--progress=yes", "--progress-update-interval=500"],
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        errors="replace",
    )
    metrics = {}
    result = None
    try:
        for line in proc.stdout:
            try:
                data = json.loads(line)
            except ValueError:
                continue
            if not isinstance(data, dict):
                continue
            kind = data.get("type")
            if kind == "result":
                result = normalize(data)
            elif kind in ("ping", "download", "upload", "testStart"):
                update = normalize(data)
                metrics.update(
                    {
                        key: value
                        for key, value in update.items()
                        if key
                        in (
                            "Download (Mbps)",
                            "Upload (Mbps)",
                            "Latência (ms)",
                            "Jitter (ms)",
                        )
                    }
                )
                direction = {
                    "download": "Download",
                    "upload": "Upload",
                    "ping": "Latência",
                    "testStart": "Conectando",
                }[kind]
                rate = (
                    metrics.get(direction + " (Mbps)")
                    if kind in ("download", "upload")
                    else None
                )
                if progress:
                    progress(
                        {
                            "event": "progress",
                            "phase": direction,
                            "metrics": dict(metrics),
                            "rate": rate,
                        }
                    )
        code = proc.wait(timeout=10)
        if (
            code
            or result is None
            or any(key not in result for key in ("Download (Mbps)", "Upload (Mbps)"))
        ):
            raise ValueError(
                "O Speedtest/Ookla não concluiu a medição. Execute speedtest no terminal para conferir o erro e, se for o primeiro uso, revisar os termos do fabricante. Não há aceite automático pelo aplicativo."
            )
        if progress:
            progress(
                {
                    "event": "progress",
                    "phase": "Concluído",
                    "metrics": result,
                    "rate": None,
                }
            )
        return result
    finally:
        if proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=3)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()
        if proc.stdout:
            proc.stdout.close()
