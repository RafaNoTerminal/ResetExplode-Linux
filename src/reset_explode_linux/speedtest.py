"""Native network measurement: persistent sessions, parallel transfers, no CLI.

Only measurement traffic is sent; no result submission or client inventory.
Public HTTP test servers are independent of this client and may impose limits.
"""

from __future__ import annotations
import concurrent.futures
import dataclasses
import json
import os
import statistics
import threading
import time
import urllib.parse
import uuid
import requests

DIRECTORY = (
    "https://www.speedtest.net/api/js/servers?engine=js&limit=12&https_functional=true"
)
MIB = 1024**2


@dataclasses.dataclass(frozen=True)
class Server:
    name: str
    download: str
    upload: str
    latency: str
    size: int
    identifier: str = ""


def session():
    client = requests.Session()
    client.headers.update(
        {
            "User-Agent": "RBT-Diagnostico-Linux/0.2.0",
            "Accept-Encoding": "identity",
            "Cache-Control": "no-cache",
        }
    )
    return client


def latency(client, url):
    start = time.perf_counter()
    with client.get(url, timeout=(3, 3), stream=True) as response:
        response.raise_for_status()
        payload = response.raw.read(1024)
        if b"test=test" not in payload:
            raise ValueError("O servidor não respondeu ao diagnóstico de latência.")
    return (time.perf_counter() - start) * 1000


def discover():
    with session() as client:
        with client.get(DIRECTORY, timeout=(4, 5), stream=True) as response:
            response.raise_for_status()
            payload = response.raw.read(256 * 1024 + 1)
    if len(payload) > 256 * 1024:
        raise ValueError("Diretório de servidores excedeu o limite.")
    items = json.loads(payload)
    if not isinstance(items, list):
        raise ValueError("Diretório de servidores inválido.")

    def probe(item):
        if not isinstance(item, dict):
            return None
        parts = urllib.parse.urlsplit(str(item.get("url", "")))
        if (
            parts.scheme not in ("http", "https")
            or not parts.hostname
            or parts.username
            or parts.password
        ):
            return None
        initial = urllib.parse.urljoin(parts.geturl(), "random4000x4000.jpg")
        try:
            with session() as client:
                # Some providers advertise an old HTTP name and redirect to their
                # certificate-valid TLS host. Follow normal redirects, never disable TLS verification.
                with client.head(
                    initial, timeout=(3, 3), allow_redirects=True
                ) as response:
                    response.raise_for_status()
                    if urllib.parse.urlsplit(response.url).scheme != "https":
                        return None
                    size = int(response.headers.get("Content-Length", 0))
                    if not 64 * 1024 <= size <= 64 * MIB:
                        return None
                    base = response.url.rsplit("/", 1)[0] + "/"
                    download = response.url
                target = Server(
                    " · ".join(
                        str(item[k])
                        for k in ("sponsor", "name", "country")
                        if item.get(k)
                    ),
                    download,
                    base + "upload.php",
                    base + "latency.txt",
                    size,
                    str(item.get("id", "")),
                )
                latency(client, target.latency)  # warm connection
                samples = [latency(client, target.latency) for _ in range(2)]
                return statistics.median(samples), target
        except (requests.RequestException, ValueError):
            return None

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        reachable = [
            result for result in pool.map(probe, items[:12]) if result is not None
        ]
    if not reachable:
        raise ValueError(
            "Não foi possível acessar um servidor próximo com TLS válido. Tente novamente ou verifique a conexão/proxy."
        )
    return min(reachable, key=lambda item: item[0])[1]


class Budget:
    def __init__(self, maximum):
        self.remaining = maximum
        self.lock = threading.Lock()

    def reserve(self, amount):
        with self.lock:
            if amount > self.remaining:
                return False
            self.remaining -= amount
            return True


class Meter:
    def __init__(self):
        self.lock = threading.Lock()
        self.total = 0
        self.finished_at = 0.0
        self.errors = []

    def add(self, amount):
        with self.lock:
            self.total += amount

    def error(self, message):
        with self.lock:
            if message not in self.errors:
                self.errors.append(message)


def transfer(server, clients, upload, seconds, maximum, progress=None):
    """Aggregate bytes divided by one shared wall clock, never sum per-stream rates."""
    meter = Meter()
    budget = Budget(maximum)
    start_event = threading.Event()
    start = [0.0]
    phase = "Upload" if upload else "Download"

    def run(client):
        body_size = min(64 * 1024, maximum)
        body_ceiling = min(2 * MIB, maximum)
        # Random reusable body avoids a fresh entropy allocation for every request.
        payload = b"content1=" + os.urandom(body_size - 9) if upload else None
        start_event.wait()
        deadline = start[0] + seconds
        while time.perf_counter() < deadline:
            size = body_size if upload else server.size
            if not budget.reserve(size):
                break
            try:
                if upload:
                    request_start = time.perf_counter()
                    with client.post(
                        server.upload,
                        data=payload,
                        headers={"Content-Type": "application/octet-stream"},
                        timeout=(3, 3),
                        stream=True,
                    ) as response:
                        if response.status_code == 413 and body_size > 8 * 1024:
                            body_size //= 2
                            body_ceiling = min(body_ceiling, body_size)
                            payload = b"content1=" + payload[9:body_size]
                            continue
                        response.raise_for_status()
                        answer = response.raw.read(1024).strip()
                        if answer != f"size={size}".encode():
                            raise ValueError(
                                "O servidor não confirmou o tamanho do upload."
                            )
                    meter.add(size)
                    if (
                        time.perf_counter() - request_start < 0.15
                        and body_size < body_ceiling
                    ):
                        body_size = min(body_size * 2, body_ceiling)
                        payload = b"content1=" + os.urandom(body_size - 9)
                else:
                    received = 0
                    params = {"reset_nonce": uuid.uuid4().hex}
                    with client.get(
                        server.download, params=params, timeout=(3, 3), stream=True
                    ) as response:
                        response.raise_for_status()
                        if response.headers.get("Content-Encoding", "identity") not in (
                            "",
                            "identity",
                        ):
                            raise ValueError("O servidor comprimiu os dados do teste.")
                        for chunk in response.iter_content(256 * 1024):
                            received += len(chunk)
                            if received > server.size:
                                raise ValueError(
                                    "Resposta de download maior que a anunciada."
                                )
                            meter.add(len(chunk))
                            if time.perf_counter() >= deadline:
                                return
                    if received != server.size:
                        raise ValueError("Download incompleto.")
            except (requests.RequestException, ValueError) as exc:
                meter.error(f"{phase}: {type(exc).__name__}")
                break

    def task(client):
        try:
            return run(client)
        finally:
            with meter.lock:
                meter.finished_at = max(meter.finished_at, time.perf_counter())

    with concurrent.futures.ThreadPoolExecutor(max_workers=len(clients)) as pool:
        futures = [pool.submit(task, client) for client in clients]
        start[0] = time.perf_counter()
        start_event.set()
        previous = start[0]
        previous_bytes = 0
        while not all(f.done() for f in futures):
            time.sleep(0.2)
            now = time.perf_counter()
            with meter.lock:
                count = meter.total
            if progress:
                progress(
                    phase,
                    count * 8 / max(now - start[0], 1e-6) / 1e6,
                    (count - previous_bytes) * 8 / max(now - previous, 1e-6) / 1e6,
                )
            previous, previous_bytes = now, count
        for future in futures:
            future.result()
    elapsed = max(meter.finished_at - start[0], 1e-6)
    # Download includes bytes delivered before a deadline/partial stream close;
    # upload only includes application bodies explicitly acknowledged by the server.
    count = meter.total
    return {
        "Mbps": round(count * 8 / elapsed / 1e6, 2) if count else None,
        "bytes": count,
        "seconds": round(elapsed, 3),
        "errors": meter.errors,
        "reserved": maximum - budget.remaining,
    }


def measure(progress=None, server=None, seconds=8, streams=8, max_bytes=1024**3):
    if not 0 < seconds <= 15 or not 1 <= streams <= 12 or max_bytes < 8 * 1024:
        raise ValueError("Parâmetros do teste fora dos limites.")
    metrics = {
        "Teste": "Velocidade · RBT Diagnóstico Linux",
        "Motor": "Nativo Python · HTTP/TLS paralelo",
    }

    def emit(phase, rate=None):
        if progress:
            progress(
                {
                    "event": "progress",
                    "phase": phase,
                    "metrics": dict(metrics),
                    "rate": rate,
                }
            )

    emit("Selecionando servidor")
    server = server or discover()
    metrics.update({"Servidor": server.name, "ID do servidor": server.identifier})
    clients = []
    try:

        def warm(_):
            client = session()
            try:
                latency(client, server.latency)
                return client
            except (requests.RequestException, ValueError):
                client.close()
                return None

        with concurrent.futures.ThreadPoolExecutor(max_workers=streams) as pool:
            clients = [
                client
                for client in pool.map(warm, range(streams))
                if client is not None
            ]
        if not clients:
            raise ValueError("O servidor escolhido deixou de responder.")
        emit("Latência")
        samples = [latency(clients[0], server.latency) for _ in range(5)]
        metrics.update(
            {
                "Latência (ms)": round(statistics.median(samples), 2),
                "Jitter (ms)": round(statistics.pstdev(samples), 2),
                "Conexões paralelas": len(clients),
            }
        )
        actual_bytes = 0
        for upload, phase in ((False, "Download"), (True, "Upload")):
            emit(phase)

            def update(name, average, rate):
                metrics[name + " (Mbps)"] = round(average, 2)
                emit(name, rate)

            result = transfer(
                server, clients, upload, seconds, max_bytes, progress=update
            )
            metrics[phase + " (Mbps)"] = result["Mbps"]
            actual_bytes += result["bytes"]
            if result["errors"] or result["Mbps"] is None:
                metrics[phase + " · aviso"] = (
                    "Medição parcial/indisponível; o servidor interrompeu ou recusou transferências."
                )
        metrics["Tráfego medido (MiB)"] = round(actual_bytes / MIB, 2)
        metrics["Método"] = (
            "Motor do aplicativo; sessões persistentes TLS e transferências paralelas. Taxa agregada calculada sobre o mesmo intervalo de tempo. Latência/jitter HTTP em conexão aquecida; não mede perda de pacotes ou bufferbloat."
        )
        metrics["Duração por etapa (s)"] = seconds
        if not any(
            metrics.get(key) is not None for key in ("Download (Mbps)", "Upload (Mbps)")
        ):
            raise ValueError(
                "O servidor não permitiu transferências suficientes para medir. Tente novamente."
            )
        emit("Concluído")
        return metrics
    finally:
        for client in clients:
            client.close()
