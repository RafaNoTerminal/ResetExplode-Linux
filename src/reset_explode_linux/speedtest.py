"""Bounded HTTPS throughput measurement; no third-party client or webpage."""

from __future__ import annotations
import statistics
import time
import urllib.request

DEFAULT_SERVER = "https://speed.cloudflare.com"


def measure(base=DEFAULT_SERVER, sizes=(1, 4, 16, 32), samples=5):
    """Single-stream application throughput, not an ISP capacity certification.

    A compatible user-hosted endpoint may be passed as base. Download and upload
    stop ramping after a request lasts two seconds, with a fixed byte ceiling.
    """

    def request(path, data=None):
        req = urllib.request.Request(
            base.rstrip("/") + path,
            data=data,
            headers={
                "User-Agent": "ResetExplode-Linux/0.1",
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

    latencies = [request("/__down?bytes=0")[1] * 1000 for _ in range(samples)]
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
                "/__up" if upload else f"/__down?bytes={size}",
                b"\0" * size if upload else None,
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
