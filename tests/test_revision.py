import contextlib
import http.server
import threading
import unittest
from unittest.mock import patch
from reset_explode_linux import integrations, speedtest, system


class Revision(unittest.TestCase):
    def test_steam_flatpak_command(self):
        with (
            patch.object(
                integrations.shutil,
                "which",
                side_effect=lambda n: "/usr/bin/flatpak" if n == "flatpak" else None,
            ),
            patch("subprocess.run") as run,
        ):
            run.return_value.returncode = 0
            argv, _, _ = integrations.game_command({"kind": "steam", "target": "123"})
            self.assertEqual(
                argv,
                ["flatpak", "run", "com.valvesoftware.Steam", "steam://rungameid/123"],
            )

    def test_memory_consumers_are_sorted(self):
        rows = system.processes()
        self.assertTrue(rows)
        self.assertTrue(all(p["rss"] >= 0 for p in rows))
        self.assertEqual(
            [p["memory_percent"] or 0 for p in rows],
            sorted([p["memory_percent"] or 0 for p in rows], reverse=True),
        )

    def test_speed_measurement_with_real_local_http(self):
        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                size = int(self.path.split("bytes=")[1])
                self.send_response(200)
                self.end_headers()
                self.wfile.write(b"0" * size)

            def do_POST(self):
                self.rfile.read(int(self.headers["Content-Length"]))
                self.send_response(200)
                self.end_headers()

            def log_message(self, *args):
                pass

        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            result = speedtest.measure(
                f"http://127.0.0.1:{server.server_port}", sizes=(1,), samples=2
            )
            self.assertGreater(result["Download (Mbps)"], 0)
            self.assertGreater(result["Upload (Mbps)"], 0)
            self.assertEqual(result["Tráfego de teste (MiB)"], 2)
        finally:
            server.shutdown()
            server.server_close()
            thread.join()

    def test_short_download_rejected(self):
        import io

        with patch.object(
            speedtest.urllib.request,
            "urlopen",
            side_effect=lambda *a, **k: contextlib.closing(io.BytesIO(b"")),
        ):
            with self.assertRaisesRegex(ValueError, "incompleto"):
                speedtest.measure(sizes=(1,), samples=1)
