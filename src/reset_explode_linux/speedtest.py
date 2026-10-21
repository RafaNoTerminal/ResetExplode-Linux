"""Bounded HTTPS throughput measurement; no third-party client or webpage."""

from __future__ import annotations
import json
import os
import urllib.error
import urllib.parse
import statistics
import time
import urllib.request

DEFAULT_SERVER = "https://speed.cloudflare.com"


def _measure(base=DEFAULT_SERVER, sizes=(1, 4, 16, 32), samples=5, paths=None):
    """Single-stream application throughput, not an ISP capacity certification.

    A compatible user-hosted endpoint may be passed as base. Download and upload
    stop ramping after a request lasts two seconds, with a fixed byte ceiling.
    """

    ping_path, down_path, up_path = paths or (
        "/__down?bytes=0",
        "/__down?bytes={bytes}",
        "/__up",
    )

    def request(path, data=None):
        req = urllib.request.Request(
            base.rstrip("/") + path,
            data=data,
            headers={
                "User-Agent": "ResetExplode-Linux/0.1.1",
                "Accept-Encoding": "identity",
                "Cache-Control": "no-cache",
                "Content-Type": "application/octet-stream",
            },
        )
        start = time.perf_counter()
        count = 0
        with urllib.request.urlopen(req, timeout=15) as response:
            while chunk := response.read(256 * 1024):
                count += len(chunk)
                if count > 64 * 1024**2:
                    raise ValueError(
                        "Resposta do servidor excedeu o limite de segurança."
                    )
        return count, max(time.perf_counter() - start, 1e-6)

    latencies = [request(ping_path)[1] * 1000 for _ in range(samples)]
    output = {
        "Teste": "Velocidade · ResetExplode Linux",
        "Servidor": base,
        "Latência HTTPS (ms)": round(statistics.median(latencies), 2),
        "Variação da latência (ms)": round(statistics.pstdev(latencies), 2),
    }
    transferred = 0
    for upload, name in [(False, "Download (Mbps)"), (True, "Upload (Mbps)")]:
        total_bytes = 0
        total_seconds = 0
        for mib in sizes:
            size = mib * 1024**2
            count, elapsed = request(
                up_path if upload else down_path.format(bytes=size, mib=mib),
                os.urandom(size) if upload else None,
            )
            if not upload and count != size:
                raise ValueError("Download incompleto; velocidade não calculada.")
            total_bytes += size
            total_seconds += elapsed
            transferred += size
            if elapsed >= 2:
                break
        output[name] = round(total_bytes * 8 / total_seconds / 1e6, 2)
    output["Tráfego de teste (MiB)"] = round(transferred / 1024**2, 2)
    output["Método"] = (
        "Uma conexão HTTPS por vez; inclui TLS e resposta do servidor. Não mede bufferbloat ou perda de pacotes."
    )
    return output


SERVER_LIST = "https://librespeed.org/backend-servers/servers.php"


def discover_alternative():
    request = urllib.request.Request(
        SERVER_LIST, headers={"User-Agent": "ResetExplode-Linux/0.1.1"}
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


def measure(base=DEFAULT_SERVER, sizes=(1, 4, 16, 32), samples=5):
    """Try a public alternative once on endpoint failure; no attempts to evade blocks.

    Each attempt transfers at most 106 MiB; two attempts total at most 212 MiB.
    Custom/local endpoints never trigger requests to the public directory.
    """
    try:
        return _measure(base, sizes, samples)
    except (OSError, ValueError) as first:
        if base != DEFAULT_SERVER:
            raise
        try:
            alternative, paths = discover_alternative()
            result = _measure(alternative, sizes, samples, paths=paths)
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
