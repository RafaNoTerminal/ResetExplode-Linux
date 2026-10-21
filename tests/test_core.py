import json, os, pathlib, tempfile, unittest
from unittest.mock import patch
from reset_explode_linux import system, storage, integrations, worker, report


class Collectors(unittest.TestCase):
    def test_inventory_has_no_serial_or_hostname(self):
        data = system.inventory()
        self.assertIn("CPU", data)
        self.assertIn("Sistema", data)
        self.assertNotIn("serial", json.dumps(data).lower())
        self.assertNotIn("Nome do computador", json.dumps(data))

    def test_unavailable_sensor_is_not_zero_temperature(self):
        sample = {"cpu": 10, "cores": [10], "gpu": None, "ram": 20, "cpu_temp": None}
        self.assertIn("Sem gargalo evidente", system.bottleneck(sample))
        sample.update(gpu=99)
        self.assertIn("limite de GPU", system.bottleneck(sample))
        sample.update(cpu_temp=94)
        self.assertIn("Temperatura", system.bottleneck(sample))

    def test_unknown_file_and_command_fail_without_throwing(self):
        self.assertEqual(system.read("/missing/reset/file", "absent"), "absent")
        self.assertNotEqual(system.command(["missing-reset-command"])[2], 0)

    def test_snapshot_is_live_and_finite(self):
        x = system.snapshot()
        self.assertGreater(x["ram_total"], 0)
        self.assertGreaterEqual(x["cpu"], 0)
        self.assertLessEqual(x["cpu"], 100)

    def test_smartctl_path_is_validated(self):
        with patch.object(
            system, "physical_disks", return_value=[{"path": "/dev/valid"}]
        ):
            with self.assertRaises(ValueError):
                system.disk_health("/dev/other")


class State(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.env = patch.dict(
            os.environ,
            {
                "XDG_CONFIG_HOME": self.temp.name + "/config",
                "XDG_STATE_HOME": self.temp.name + "/state",
            },
        )
        self.env.start()

    def tearDown(self):
        self.env.stop()
        self.temp.cleanup()

    def test_atomic_private_settings(self):
        storage.save("test.json", {"value": "á"})
        self.assertEqual(storage.load("test.json", {}), {"value": "á"})
        self.assertEqual(
            (storage.config_dir() / "test.json").stat().st_mode & 0o777, 0o600
        )

    def test_bad_json_recovers(self):
        (storage.config_dir() / "bad.json").write_text("{")
        self.assertEqual(storage.load("bad.json", []), [])

    def test_history_is_bounded(self):
        for n in range(105):
            storage.history({"n": n})
        self.assertEqual(len(storage.histories()), 100)
        self.assertEqual(storage.histories()[-1]["n"], 104)

    def test_startup_override_and_restore(self):
        with tempfile.TemporaryDirectory() as root:
            p = pathlib.Path(root) / "autostart"
            p.mkdir()
            (p / "test.desktop").write_text(
                "[Desktop Entry]\nType=Application\nName=Test\nExec=echo hello\n"
            )
            with patch.dict(os.environ, {"XDG_CONFIG_DIRS": root}):
                integrations.set_startup("test.desktop", False)
                self.assertFalse(integrations.startups()[0]["enabled"])
                integrations.restore_startups()
                self.assertTrue(integrations.startups()[0]["enabled"])
                self.assertFalse(
                    (
                        pathlib.Path(os.environ["XDG_CONFIG_HOME"])
                        / "autostart/test.desktop"
                    ).exists()
                )

    def test_shell_is_never_used_in_game_arguments(self):
        p = pathlib.Path(self.temp.name) / "app"
        p.write_text("")
        argv, cwd, env = integrations.game_command(
            {
                "id": "test",
                "kind": "file",
                "target": str(p),
                "args": '"a b" ";" "$(touch /tmp/never-created)"',
            }
        )
        self.assertEqual(argv[1:], ["a b", ";", "$(touch /tmp/never-created)"])
        self.assertEqual(cwd, p.parent)

    def test_package_allowlist(self):
        with self.assertRaises(ValueError):
            integrations.install_command("arbitrary; command")

    def test_game_id_cannot_escape_state_directory(self):
        value = integrations.safe_game_id("../../outside")
        self.assertNotIn("/", value)
        self.assertEqual(len(value), 24)

    def test_steam_options(self):
        self.assertEqual(
            integrations.steam_options(
                {"overlay": True, "gamemode": True, "args": "-windowed"}
            ),
            "gamemoderun mangohud %command% -windowed",
        )


class Workers(unittest.TestCase):
    def test_memory_copy_is_verified(self):
        self.assertTrue(worker.memory_bench()["verificado"])

    def test_ping_parser(self):
        fake = type(
            "Result",
            (),
            {"stdout": "time=10.0 ms\ntime=12.0 ms\n0% packet loss", "returncode": 0},
        )()
        with patch("subprocess.run", return_value=fake):
            r = worker.ping("test")
            self.assertEqual(r["latência_média_ms"], 11)
            self.assertEqual(r["jitter_desvio_ms"], 1)
            self.assertEqual(r["perda_percentual"], 0)


class Reports(unittest.TestCase):
    def test_html_escapes_device_data(self):
        page = report.html_report(
            {"device": "<script>alert(1)</script>", "sensor": None}
        )
        self.assertNotIn("<script>", page)
        self.assertIn("&lt;script&gt;", page)
        self.assertIn("Indisponível", page)


if __name__ == "__main__":
    unittest.main()
