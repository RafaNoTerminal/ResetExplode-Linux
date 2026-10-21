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
                speedtest.measure(base="http://local.invalid", sizes=(1,), samples=1)


class EnergyRegression(unittest.TestCase):
    def energy_for(self, status, online=None):
        import tempfile
        from pathlib import Path

        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            battery = root / "BAT0"
            battery.mkdir()
            for key, value in {
                "type": "Battery",
                "status": status,
                "capacity": "80",
                "energy_full": "40000000",
                "energy_full_design": "50000000",
            }.items():
                (battery / key).write_text(value)
            if online is not None:
                adapter = root / "AC"
                adapter.mkdir()
                (adapter / "type").write_text("Mains")
                (adapter / "online").write_text(str(online))
            with (
                patch.object(system.psutil, "sensors_battery", return_value=None),
                patch.object(system, "command", return_value=("", "", -1)),
            ):
                return system.energy(root)

    def test_plugged_with_charge_paused(self):
        result = self.energy_for("Not charging", 1)
        self.assertEqual(result["Alimentação"]["Carregador conectado"], "Sim")
        self.assertEqual(
            result["BAT0"]["Estado da bateria"], "Carga pausada pelo sistema"
        )
        self.assertEqual(result["BAT0"]["Capacidade atual"], "40.00 Wh")
        self.assertEqual(result["BAT0"]["Desgaste estimado"], "20.0%")

    def test_charging_without_ac_node(self):
        result = self.energy_for("Charging")
        self.assertEqual(result["Alimentação"]["Carregador conectado"], "Sim")
        self.assertEqual(result["BAT0"]["Estado da bateria"], "Carregando")

    def test_unknown_does_not_mean_unplugged(self):
        result = self.energy_for("Unknown")
        self.assertEqual(
            result["Alimentação"]["Carregador conectado"], "Não informado pelo sistema"
        )

    def test_full_does_not_override_disconnected_adapter(self):
        result = self.energy_for("Full", 0)
        self.assertEqual(result["Alimentação"]["Carregador conectado"], "Não")


class SpeedFallback(unittest.TestCase):
    def test_403_uses_alternative(self):
        import urllib.error

        error = urllib.error.HTTPError(
            speedtest.DEFAULT_SERVER, 403, "Forbidden", {}, None
        )
        with (
            patch.object(
                speedtest, "_measure", side_effect=[error, {"Download (Mbps)": 10}]
            ) as run,
            patch.object(
                speedtest,
                "discover_alternative",
                return_value=(
                    "https://example.org",
                    ("/ping", "/down?ckSize={mib}", "/up"),
                ),
            ),
        ):
            result = speedtest.measure()
            self.assertEqual(result["Download (Mbps)"], 10)
            self.assertIn("LibreSpeed", result["Servidor alternativo"])
            self.assertEqual(run.call_count, 2)

    def test_failure_has_readable_error(self):
        with (
            patch.object(speedtest, "_measure", side_effect=OSError("offline")),
            patch.object(
                speedtest, "discover_alternative", side_effect=OSError("offline")
            ),
        ):
            with self.assertRaisesRegex(ValueError, "Não foi possível medir"):
                speedtest.measure()

    def test_local_endpoint_never_calls_directory(self):
        with (
            patch.object(speedtest, "_measure", side_effect=OSError("offline")),
            patch.object(speedtest, "discover_alternative") as discover,
        ):
            with self.assertRaises(OSError):
                speedtest.measure(base="http://localhost:9999")
            discover.assert_not_called()
