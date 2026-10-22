"""Explicitly started workloads. Never invoked by startup or hardware collection."""

from __future__ import annotations
import argparse, concurrent.futures, hashlib, json, os, pathlib, re, statistics, subprocess, tempfile, time


def cpu_unit(seconds):
    data = b"RESET Linux benchmark\0" * (4 * 1024 * 1024 // 21)
    end = time.monotonic() + seconds
    n = 0
    while time.monotonic() < end:
        hashlib.sha256(data).digest()
        n += len(data)
    return n


def cpu_bench(seconds, threads):
    start = time.monotonic()
    with concurrent.futures.ProcessPoolExecutor(max_workers=threads) as pool:
        total = sum(pool.map(cpu_unit, [seconds] * threads))
    elapsed = time.monotonic() - start
    return {
        "teste": "CPU SHA-256",
        "workers": threads,
        "seconds": round(elapsed, 3),
        "MiB/s": round(total / elapsed / 1024**2, 2),
        "nota": "Mede esta carga específica; não corresponde à nota do benchmark Windows/Cinebench.",
    }


def memory_bench():
    n = 32 * 1024**2
    source = bytearray(os.urandom(n))
    dest = bytearray(n)
    start = time.perf_counter()
    for _ in range(32):
        dest[:] = source
    elapsed = time.perf_counter() - start
    if dest != source:
        raise RuntimeError("Falha ao verificar cópia em memória.")
    return {
        "teste": "Cópia e verificação de RAM (Python)",
        "MiB/s": round(n * 32 / elapsed / 1024**2, 2),
        "seconds": round(elapsed, 4),
        "verificado": True,
        "nota": "Inclui overhead do Python e caches; não é latência da DRAM.",
    }


def disk_bench(directory):
    root = pathlib.Path(directory).expanduser().resolve()
    if not root.is_dir():
        raise ValueError("A pasta de teste precisa existir.")
    n = 64 * 1024**2
    if __import__("shutil").disk_usage(root).free < n * 3:
        raise ValueError("Espaço livre insuficiente para o teste temporário.")
    block = os.urandom(1024**2)
    with tempfile.TemporaryFile(dir=root, prefix=".reset-bench-") as f:
        start = time.perf_counter()
        for _ in range(64):
            f.write(block)
        f.flush()
        os.fsync(f.fileno())
        write = time.perf_counter() - start
        f.seek(0)
        start = time.perf_counter()
        count = 0
        while chunk := f.read(1024**2):
            count += len(chunk)
        read = time.perf_counter() - start
        if count != n:
            raise RuntimeError("Tamanho lido diferente do escrito.")
    return {
        "teste": "I/O sequencial temporário (64 MiB)",
        "escrita_MiB/s": round(64 / write, 2),
        "leitura_com_cache_MiB/s": round(64 / read, 2),
        "nota": "A leitura pode vir do cache do Linux; não compara com teste de I/O direto do Windows.",
    }


def ping(host):
    p = subprocess.run(
        ["ping", "-n", "-c", "5", "-W", "2", host],
        capture_output=True,
        text=True,
        timeout=16,
        env={**os.environ, "LC_ALL": "C"},
    )
    times = [float(x) for x in re.findall(r"time[=<]([\d.]+)", p.stdout)]
    lost = re.search(r"([\d.]+)% packet loss", p.stdout)
    return {
        "destino": host,
        "latência_média_ms": round(statistics.mean(times), 2) if times else None,
        "jitter_desvio_ms": round(statistics.pstdev(times), 2) if times else None,
        "perda_percentual": float(lost[1]) if lost else None,
        "retorno": p.returncode,
    }


def network_bench():
    hosts = ["1.1.1.1", "8.8.8.8"]
    try:
        p = subprocess.run(
            ["ip", "-j", "route", "show", "default"],
            capture_output=True,
            text=True,
            timeout=3,
        )
        gateway = next(
            (r.get("gateway") for r in json.loads(p.stdout) if r.get("gateway")), None
        )
        if gateway:
            hosts.insert(0, gateway)
    except (OSError, ValueError, StopIteration):
        pass
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        return {
            "teste": "Rede — latência, jitter e perda",
            "resultados": list(pool.map(ping, hosts)),
        }


def decode_result(text):
    """Read ordinary JSON results or the last result frame of a streamed job."""
    try:
        item = json.loads(text)
        return (
            item["result"]
            if isinstance(item, dict) and item.get("event") == "result"
            else item
        )
    except ValueError:
        for line in reversed(text.splitlines()):
            try:
                item = json.loads(line)
            except ValueError:
                continue
            if isinstance(item, dict) and item.get("event") == "result":
                return item["result"]
            if isinstance(item, dict) and "erro" in item:
                return item
        raise ValueError("Nenhum resultado completo recebido.")


def speed_job():
    from .speedtest import measure

    return measure(
        progress=lambda event: print(json.dumps(event, ensure_ascii=False), flush=True)
    )


def main():
    p = argparse.ArgumentParser()
    p.add_argument("task", choices=["cpu", "memory", "disk", "network", "speed"])
    p.add_argument("--seconds", type=int, default=5)
    p.add_argument("--threads", type=int, default=1)
    p.add_argument("--directory", default=str(pathlib.Path.home()))
    a = p.parse_args()
    if not 1 <= a.seconds <= 60 or not 1 <= a.threads <= max(1, os.cpu_count() or 1):
        p.error("Duração ou número de workers inválido.")
    try:
        result = {
            "cpu": lambda: cpu_bench(a.seconds, a.threads),
            "memory": memory_bench,
            "disk": lambda: disk_bench(a.directory),
            "network": network_bench,
            "speed": speed_job,
        }[a.task]()
        if a.task == "speed":
            print(
                json.dumps({"event": "result", "result": result}, ensure_ascii=False),
                flush=True,
            )
        else:
            print(json.dumps(result, ensure_ascii=False, indent=2), flush=True)
    except Exception as e:
        print(json.dumps({"erro": str(e)}, ensure_ascii=False), flush=True)
        raise SystemExit(1)


if __name__ == "__main__":
    main()
