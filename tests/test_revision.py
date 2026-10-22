import unittest
from unittest.mock import patch
from reset_explode_linux import integrations, system


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
