"""GUI construction smoke test. No benchmarks, root actions or network are started."""

from gi.repository import GLib
from reset_explode_linux.app import Application

app = Application()
app.set_application_id("io.github.reset_explode_linux.Smoke")
errors = []


def check():
    try:
        assert len(app.pages) == 19, f"Expected 19 pages, got {len(app.pages)}"
        assert app.sample.get("ram_total", 0) > 0, "No real memory sample"
        assert app.process is None, "Opening the GUI must not start a workload"
        for dark in (False, True):
            app.dark = dark
            app.apply_theme()
        assert set(app.window_controls) == {"minimize", "maximize", "close"}
        for control in app.window_controls.values():
            assert control.get_visible() and control.get_mapped()
            assert control.get_allocated_width() >= 24
        assert app.games is not None
        app.refresh_processes()
        assert len(app.proc_model) > 0
        for title in app.pages:
            app.stack.set_visible_child_name(title)
        print("GUI OK: 19 pages, real sample, no implicit workload", flush=True)
    except Exception as e:
        errors.append(str(e))
    finally:
        app.quit()
    return False


GLib.timeout_add_seconds(4, check)
app.run([])
if errors:
    raise SystemExit("; ".join(errors))
