"""Bounded HTTPS throughput measurement; no third-party client or webpage."""

from __future__ import annotations
import json
import os
import urllib.error
import statistics
import time
import urllib.request

DEFAULT_SERVER = "https://speed.cloudflare.com"


def _measure(
    base=DEFAULT_SERVER, sizes=(1, 4, 16, 32), samples=5, paths=None, progress=None
):
    """Single-stream application throughput, not an ISP capacity certification.

    A compatible user-hosted endpoint may be passed as base. Download and upload
    stop ramping after a request lasts two seconds, with a fixed byte ceiling.
    """

    ping_path, down_path, up_path = paths or (
        "/__down?bytes=0",
        "/__down?bytes={bytes}",
        "/__up",
    )

    output = {}

    def notify(phase, rate=None):
        if progress:
            progress(
                {
                    "event": "progress",
                    "phase": phase,
                    "metrics": dict(output),
                    "rate": rate,
                }
            )

    notify("Conectando")

    def request(path, data=None, downloading=False):
        req = urllib.request.Request(
            base.rstrip("/") + path,
            data=data,
            headers={
                "User-Agent": "ResetExplode-Linux/0.1.2",
                "Accept-Encoding": "identity",
                "Cache-Control": "no-cache",
                "Content-Type": "application/octet-stream",
            },
        )
        start = time.perf_counter()
        count = 0
        last_update = start
        with urllib.request.urlopen(req, timeout=15) as response:
            while chunk := response.read(256 * 1024):
                count += len(chunk)
                now = time.perf_counter()
                if downloading and now - last_update >= 0.15:
                    notify("Download", count * 8 / max(now - start, 1e-6) / 1e6)
                    last_update = now
                if count > 64 * 1024**2:
                    raise ValueError(
                        "Resposta do servidor excedeu o limite de segurança."
                    )
        return count, max(time.perf_counter() - start, 1e-6)

    notify("Latência")
    latencies = [request(ping_path)[1] * 1000 for _ in range(samples)]
    output = {
        "Teste": "Velocidade · ResetExplode Linux",
        "Servidor": base,
        "Latência HTTPS (ms)": round(statistics.median(latencies), 2),
        "Variação da latência (ms)": round(statistics.pstdev(latencies), 2),
    }
    transferred = 0
    notify("Download")
    total_bytes = total_seconds = 0
    for mib in sizes:
        size = mib * 1024**2
        count, elapsed = request(
            down_path.format(bytes=size, mib=mib), downloading=True
        )
        if count != size:
            raise ValueError("Download incompleto; velocidade não calculada.")
        total_bytes += count
        total_seconds += elapsed
        transferred += count
        output["Download (Mbps)"] = round(total_bytes * 8 / total_seconds / 1e6, 2)
        notify("Download", output["Download (Mbps)"])
        if elapsed >= 2:
            break

    # Small POST bodies are accepted by more public backends. On 413, reduce
    # the next body; never retry the rejected body unchanged or bypass policy.
    notify("Upload")
    uploaded = upload_seconds = 0
    body_size = 512 * 1024
    target = sum(int(mib * 1024**2) for mib in sizes)
    upload_start = time.perf_counter()
    warning = None
    while uploaded < target and time.perf_counter() - upload_start < 6:
        size = min(body_size, target - uploaded)
        try:
            _, elapsed = request(up_path, os.urandom(size))
        except urllib.error.HTTPError as exc:
            if exc.code == 413 and size > 8 * 1024:
                body_size = max(8 * 1024, size // 2)
                notify("Reduzindo bloco de upload")
                continue
            warning = f"Upload não concluído: servidor respondeu HTTP {exc.code}."
            break
        except (OSError, ValueError) as exc:
            warning = "Upload não concluído: " + str(exc)
            break
        uploaded += size
        transferred += size
        upload_seconds += elapsed
        output["Upload (Mbps)"] = round(uploaded * 8 / upload_seconds / 1e6, 2)
        notify("Upload", output["Upload (Mbps)"])
    if not uploaded:
        output["Upload (Mbps)"] = None
    if warning:
        output["Aviso"] = warning + " Download e latência foram preservados."
        if uploaded:
            output["Upload parcial"] = (
                "Calculado somente com os blocos aceitos antes da interrupção."
            )
    output["Bloco de upload (KiB)"] = round(body_size / 1024, 1)
    output["Tráfego de teste (MiB)"] = round(transferred / 1024**2, 2)
    output["Método"] = (
        "Uma conexão HTTPS por vez; inclui TLS e resposta do servidor. Não mede bufferbloat ou perda de pacotes."
    )
    notify("Concluído")
    return output


SERVER_LIST = "https://librespeed.org/backend-servers/servers.php"


def discover_alternative():
    request = urllib.request.Request(
        SERVER_LIST, headers={"User-Agent": "ResetExplode-Linux/0.1.2"}
    )
    with urllib.request.urlopen(request, timeout=8) as response:
        data = response.read(256 * 1024 + 1)
    if len(data) > 256 * 1024:
        raise ValueError("Lista de servidores excedeu o limite.")
    servers = json.loads(data)
    candidates = [
        s
        for s in servers
        if isinstance(s, dict) and str(s.get("server", "")).startswith("https://")
    ]
    candidates.sort(
        key=lambda s: (
            not any(
                region in s.get("name", "").lower()
                for region in ("brazil", "brasil", "sao paulo", "são paulo")
            )
        )
    )
    best = None
    for server in candidates[:4]:
        base = server["server"].rstrip("/")
        paths = (
            tuple(
                "/" + server[key].lstrip("/") for key in ("pingURL", "dlURL", "ulURL")
            )
            if all(key in server for key in ("pingURL", "dlURL", "ulURL"))
            else None
        )
        if paths is None:
            continue
        # Paths from the directory must remain relative to the advertised server.
        if any(".." in path or "://" in path for path in paths):
            continue
        start = time.perf_counter()
        try:
            with urllib.request.urlopen(base + paths[0], timeout=3) as response:
                response.read(1024)
            latency = time.perf_counter() - start
            if best is None or latency < best[0]:
                best = (latency, base, paths)
        except (OSError, ValueError):
            continue
    if best is None:
        raise ValueError(
            "Nenhum servidor alternativo respondeu. Tente outra conexão ou novamente mais tarde."
        )
    _, base, paths = best
    return base, (paths[0], paths[1] + "?ckSize={mib}", paths[2])


def measure(base=DEFAULT_SERVER, sizes=(1, 4, 16, 32), samples=5, progress=None):
    """Try a public alternative once on endpoint failure; no attempts to evade blocks.

    Each attempt sends at most 107 MiB including rejected bodies; two attempts
    total at most 214 MiB, excluding small headers and control requests.
    Custom/local endpoints never trigger requests to the public directory.
    """
    try:
        return _measure(base, sizes, samples, progress=progress)
    except (OSError, ValueError) as first:
        if base != DEFAULT_SERVER:
            raise
        try:
            alternative, paths = discover_alternative()
            if progress:
                progress(
                    {
                        "event": "progress",
                        "phase": "Trocando servidor",
                        "metrics": {},
                        "rate": None,
                    }
                )
            result = _measure(
                alternative, sizes, samples, paths=paths, progress=progress
            )
            result["Servidor alternativo"] = (
                "LibreSpeed — o primeiro servidor não aceitou ou não concluiu o teste."
            )
            return result
        except (OSError, ValueError) as second:
            raise ValueError(
                "Não foi possível medir a velocidade. O servidor principal falhou e a alternativa também. Confira a conexão/proxy e tente novamente. Detalhes: "
                + str(first)
                + "; "
                + str(second)
            ) from second
