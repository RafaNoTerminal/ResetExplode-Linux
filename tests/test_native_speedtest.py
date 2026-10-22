import contextlib
import http.server
import json
import socket
import threading
import unittest
from unittest.mock import patch
from reset_explode_linux import speedtest


@contextlib.contextmanager
def backend(upload_limit=4 * speedtest.MIB, reject_upload=False):
    lock = threading.Lock()
    info = {"bodies": [], "connections": set(), "maximum_active": 0, "active": 0}
    size = 64 * 1024

    class Handler(http.server.BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def handle(self):
            try:
                return super().handle()
            except (ConnectionResetError, BrokenPipeError):
                pass  # Expected when a deadline/413 closes a fixture connection.

        def setup(self):
            super().setup()
            self.connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)

        def reply(self, data, code=200):
            self.send_response(code)
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            with lock:
                info["connections"].add(self.client_address)
            if self.path.startswith("/latency.txt"):
                self.reply(b"test=test")
            else:
                with lock:
                    info["active"] += 1
                    info["maximum_active"] = max(info["maximum_active"], info["active"])
                try:
                    import time

                    time.sleep(0.01)
                    self.reply(b"x" * size)
                finally:
                    with lock:
                        info["active"] -= 1

        def do_POST(self):
            count = int(self.headers["Content-Length"])
            self.rfile.read(count)
            with lock:
                info["bodies"].append(count)
                info["connections"].add(self.client_address)
            if reject_upload:
                self.reply(b"forbidden", 403)
            elif count > upload_limit:
                self.reply(b"too large", 413)
            else:
                self.reply(f"size={count}".encode())

        def log_message(self, *args):
            pass

    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    server.daemon_threads = True
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    base = f"http://127.0.0.1:{server.server_port}"
    try:
        yield (
            speedtest.Server(
                "Fixture",
                base + "/download",
                base + "/upload",
                base + "/latency.txt",
                size,
            ),
            info,
        )
    finally:
        server.shutdown()
        server.server_close()
        thread.join()


class NativeSpeedtest(unittest.TestCase):
    def test_parallel_sessions_results_and_progress_without_cli(self):
        events = []
        with (
            backend() as (server, info),
            patch("subprocess.Popen", side_effect=AssertionError("CLI forbidden")),
        ):
            result = speedtest.measure(
                server=server,
                seconds=0.3,
                streams=2,
                max_bytes=4 * speedtest.MIB,
                progress=events.append,
            )
        self.assertGreater(result["Download (Mbps)"], 0)
        self.assertGreater(result["Upload (Mbps)"], 0)
        self.assertEqual(result["Conexões paralelas"], 2)
        self.assertGreaterEqual(info["maximum_active"], 2)
        self.assertLessEqual(len(info["connections"]), 2, "Connections were not reused")
        self.assertEqual(events[-1]["phase"], "Concluído")
        self.assertIn("Nativo", result["Motor"])
        self.assertNotIn("Perda de pacotes (%)", result)

    def test_413_adapts_and_budget_counts_rejected_bodies(self):
        with backend(upload_limit=128 * 1024) as (server, info):
            result = speedtest.measure(
                server=server, seconds=0.4, streams=1, max_bytes=8 * speedtest.MIB
            )
        self.assertGreater(result["Download (Mbps)"], 0)
        self.assertGreater(result["Upload (Mbps)"], 0)
        self.assertIn(128 * 1024, info["bodies"])
        self.assertLessEqual(sum(info["bodies"]), 8 * speedtest.MIB)

    def test_upload_rejected_preserves_download(self):
        with backend(reject_upload=True) as (server, _):
            result = speedtest.measure(
                server=server, seconds=0.1, streams=1, max_bytes=4 * speedtest.MIB
            )
        self.assertGreater(result["Download (Mbps)"], 0)
        self.assertIsNone(result["Upload (Mbps)"])
        self.assertIn("Upload · aviso", result)

    def test_budget_is_thread_safe(self):
        budget = speedtest.Budget(100)

        def reserve(_):
            return budget.reserve(3)

        import concurrent.futures

        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            successes = sum(pool.map(reserve, range(100)))
        self.assertEqual(successes, 33)
        self.assertEqual(budget.remaining, 1)

    def test_invalid_parameters_do_not_contact_network(self):
        with patch.object(speedtest, "discover", side_effect=AssertionError("network")):
            for values in ({"seconds": 0}, {"streams": 0}, {"max_bytes": 0}):
                with self.assertRaises(ValueError):
                    speedtest.measure(**values)

    def test_directory_unavailable_does_not_fake_measurements(self):
        with patch.object(speedtest, "discover", side_effect=ValueError("offline")):
            with self.assertRaisesRegex(ValueError, "offline"):
                speedtest.measure()

    def test_stream_decoder(self):
        from reset_explode_linux.worker import decode_result

        result = {"Motor": "Nativo", "Download (Mbps)": 12}
        text = (
            json.dumps({"event": "progress", "phase": "Download"})
            + "\n"
            + json.dumps({"event": "result", "result": result})
        )
        self.assertEqual(decode_result(text), result)
