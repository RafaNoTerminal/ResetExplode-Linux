"""Exercise worker events and displayed results with a fixture child, without internet."""

import json
import os
import sys
import tempfile
from gi.repository import GLib
from reset_explode_linux.app import Application

scratch = tempfile.TemporaryDirectory(prefix="reset-speed-gui-")
os.environ["XDG_STATE_HOME"] = scratch.name
app = Application()
app.set_application_id("io.github.reset_explode_linux.SpeedSmoke")
errors = []
fixture = {
    "Download (Mbps)": 42.5,
    "Upload (Mbps)": 12.25,
    "Latência HTTPS (ms)": 21.0,
    "Variação da latência (ms)": 2.0,
}


def start():
    events = [
        {"event": "progress", "phase": "Download", "metrics": fixture, "rate": 42.5},
        {"event": "result", "result": fixture},
    ]
    script = "import time; " + "; ".join(
        "print(" + repr(json.dumps(e)) + ",flush=True); time.sleep(.1)" for e in events
    )
    app.run_job([sys.executable, "-c", script], "Velocidade da internet")
    return False


def finish():
    try:
        assert app.process is None, "Fixture child did not finish"
        assert app.speed_metrics["Download (Mbps)"].get_text() == "42.50"
        assert app.speed_metrics["Upload (Mbps)"].get_text() == "12.25"
        assert app.speed_progress.get_fraction() == 1
        assert app.speed_result.previous == fixture
        assert app.speed_graph.points
        print("Speed GUI OK: progress, chart and structured result", flush=True)
    except Exception as exc:
        errors.append(str(exc))
    finally:
        app.quit()
    return False


GLib.timeout_add_seconds(1, start)
GLib.timeout_add_seconds(4, finish)
app.run([])
scratch.cleanup()
if errors:
    raise SystemExit("; ".join(errors))
