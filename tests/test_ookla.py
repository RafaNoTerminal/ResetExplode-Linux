import io
import unittest
from unittest.mock import patch
from reset_explode_linux import ookla


class OoklaAdapter(unittest.TestCase):
    def test_conversion_and_private_fields_are_omitted(self):
        source = {
            "ping": {"latency": 0.64, "jitter": 0.84},
            "download": {
                "bandwidth": 115926250,
                "bytes": 1000,
                "latency": {"iqm": 417.7},
            },
            "upload": {"bandwidth": 114073750, "bytes": 2000},
            "packetLoss": 5.6,
            "server": {
                "id": 123,
                "name": "Fixture",
                "location": "Nearby",
                "country": "BR",
                "host": "private.example",
            },
            "interface": {"externalIp": "203.0.113.20"},
            "isp": "PRIVATE_ISP",
            "result": {"url": "SECRET_RESULT"},
        }
        result = ookla.normalize(source)
        self.assertEqual(result["Download (Mbps)"], 927.41)
        self.assertEqual(result["Upload (Mbps)"], 912.59)
        self.assertEqual(result["Latência (ms)"], 0.64)
        self.assertEqual(result["Perda de pacotes (%)"], 5.6)
        for private in (
            "203.0.113.20",
            "PRIVATE_ISP",
            "SECRET_RESULT",
            "private.example",
        ):
            self.assertNotIn(private, str(result))

    def test_missing_or_invalid_is_not_fake_zero(self):
        result = ookla.normalize(
            {"download": {"bandwidth": float("nan")}, "upload": {"bandwidth": -1}}
        )
        self.assertNotIn("Download (Mbps)", result)
        self.assertNotIn("Upload (Mbps)", result)
        self.assertNotIn("Perda de pacotes (%)", result)

    def test_python_client_is_not_mistaken_for_official(self):
        with (
            patch.object(ookla.shutil, "which", return_value="/test/speedtest"),
            patch.object(ookla.subprocess, "run") as run,
        ):
            run.return_value.returncode = 0
            run.return_value.stdout = "speedtest-cli 2.1.3"
            self.assertIsNone(ookla.find_cli())

    def test_progress_and_final_result_without_accept_flags(self):
        import json

        data = [
            {"type": "download", "download": {"bandwidth": 12500000}},
            {
                "type": "result",
                "download": {"bandwidth": 12500000},
                "upload": {"bandwidth": 1000000},
                "ping": {"latency": 1},
            },
        ]
        events = []
        with patch.object(ookla.subprocess, "Popen") as spawn:
            proc = spawn.return_value
            proc.stdout = io.StringIO("\n".join(json.dumps(frame) for frame in data))
            proc.wait.return_value = 0
            proc.poll.return_value = 0
            result = ookla.measure(progress=events.append, executable="/test/speedtest")
            command = spawn.call_args.args[0]
            self.assertNotIn("--accept-license", command)
            self.assertNotIn("--accept-gdpr", command)
            self.assertNotIn("start_new_session", spawn.call_args.kwargs)
        self.assertEqual(result["Download (Mbps)"], 100)
        self.assertEqual(result["Upload (Mbps)"], 8)
        self.assertEqual(events[0]["phase"], "Download")
        self.assertEqual(events[-1]["phase"], "Concluído")

    def test_failure_is_explicit_not_silent_https_fallback(self):
        with patch.object(ookla.subprocess, "Popen") as spawn:
            proc = spawn.return_value
            proc.stdout = io.StringIO("Please review license terms")
            proc.wait.return_value = 1
            proc.poll.return_value = 1
            with self.assertRaisesRegex(ValueError, "termos"):
                ookla.measure(executable="/test/speedtest")
