from __future__ import annotations
import collections, datetime, json, os, pathlib, shlex, shutil, signal, subprocess, sys, threading, time, uuid, re
import gi

gi.require_version("Gtk", "3.0")
gi.require_foreign("cairo")
from gi.repository import Gtk, Gdk, GLib
from . import __version__, system, storage, integrations, report, worker

CSS = b"""
window {background:#111317;color:#e9edf2;}
.sidebar {background:#181b20; padding:12px;}
.sidebar row {border-radius:7px;padding:6px; margin-bottom:2px;}
.sidebar row:selected {background:#343a43;}
.title {font-size:25px;font-weight:700;}
.subtitle {color:#a3adb9;}
.card {background:#1c2027;border:1px solid #303640;border-radius:10px;padding:16px;}
.metric {font-size:27px;font-weight:700;}
button {border-radius:7px;padding:7px 13px;}
button.suggested-action {background:#dce4ef;color:#111317;}
textview, textview text {background:#15191f;color:#dce4ef;}
entry, combobox {margin:3px;}
headerbar {background:#181b20;color:#e9edf2;}
.warning {color:#f2c879;}
progressbar trough {min-height:8px;border-radius:5px;background:#303640;}
progressbar progress {background:#c0d4e8;border-radius:5px;}
"""


class Details(Gtk.Box):
    """Readable native fields; nested collector data never appears as JSON."""

    def __init__(self):
        super().__init__(orientation=Gtk.Orientation.VERTICAL, spacing=10)
        self.previous = None

    def render(self, data):
        if data == self.previous:
            return
        self.previous = data
        for child in self.get_children():
            self.remove(child)

        def add(value, parent, title=None):
            if isinstance(value, (dict, list)):
                card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
                card.get_style_context().add_class("card")
                parent.pack_start(card, False, False, 0)
                if title:
                    heading = Gtk.Label(label=str(title).replace("_", " "), xalign=0)
                    heading.get_style_context().add_class("heading")
                    card.pack_start(heading, False, False, 0)
                items = (
                    value.items() if isinstance(value, dict) else enumerate(value, 1)
                )
                if not value:
                    add("Nenhum registro disponível", card)
                for key, item in items:
                    add(item, card, key)
            else:
                if value is None:
                    value = "Não disponível"
                elif isinstance(value, bool):
                    value = "Sim" if value else "Não"
                label = Gtk.Label(xalign=0)
                label.set_line_wrap(True)
                label.set_selectable(True)
                label.set_text(
                    (str(title).replace("_", " ") + "  ·  " if title else "")
                    + str(value)
                )
                parent.pack_start(label, False, False, 0)

        add(data, self)
        self.show_all()


class HistoryGraph(Gtk.DrawingArea):
    def __init__(self):
        super().__init__()
        self.set_size_request(400, 175)
        self.values = collections.deque(maxlen=180)
        self.connect("draw", self.draw)

    def add(self, cpu, gpu, ram):
        self.values.append((cpu, gpu or 0, ram))
        self.queue_draw()

    def draw(self, w, cr):
        width = self.get_allocated_width()
        height = self.get_allocated_height()
        dark = Gtk.Settings.get_default().get_property(
            "gtk-application-prefer-dark-theme"
        )
        cr.set_source_rgb(*((0.10, 0.12, 0.15) if dark else (0.97, 0.98, 1)))
        cr.paint()
        for y in [0, 25, 50, 75, 100]:
            yy = height - 15 - (height - 30) * y / 100
            cr.set_source_rgba(*((1, 1, 1, 0.25) if dark else (0.1, 0.2, 0.3, 0.35)))
            cr.move_to(30, yy)
            cr.line_to(width - 5, yy)
            cr.stroke()
            cr.set_font_size(10)
            cr.move_to(3, yy)
            cr.show_text(str(y))
        colors = [(0.15, 0.55, 0.95), (0.65, 0.3, 0.85), (0.15, 0.65, 0.35)]
        for i, color in enumerate(colors):
            cr.set_source_rgb(*color)
            cr.set_line_width(2)
            for j, row in enumerate(self.values):
                x = 30 + (width - 40) * j / 179
                y = height - 15 - (height - 30) * max(0, min(100, row[i])) / 100
                if j == 0:
                    cr.move_to(x, y)
                else:
                    cr.line_to(x, y)
            cr.stroke()
        return False


class SpeedGraph(Gtk.DrawingArea):
    def __init__(self):
        super().__init__()
        self.set_size_request(400, 150)
        self.points = collections.deque(maxlen=240)
        self.connect("draw", self.draw)

    def add_rate(self, phase, rate):
        if rate is not None and phase in ("Download", "Upload"):
            self.points.append((time.monotonic(), phase, max(0, float(rate))))
            self.queue_draw()

    def draw(self, widget, cr):
        dark = Gtk.Settings.get_default().get_property(
            "gtk-application-prefer-dark-theme"
        )
        width, height = self.get_allocated_width(), self.get_allocated_height()
        cr.set_source_rgb(*((0.10, 0.12, 0.15) if dark else (0.97, 0.98, 1)))
        cr.paint()
        maximum = max(1, max((p[2] for p in self.points), default=1) * 1.1)
        cr.set_source_rgba(*((1, 1, 1, 0.4) if dark else (0.1, 0.2, 0.3, 0.5)))
        cr.set_font_size(10)
        for step in range(4):
            y = 15 + (height - 35) * step / 3
            cr.move_to(48, y)
            cr.line_to(width - 8, y)
            cr.stroke()
            cr.move_to(3, y + 4)
            cr.show_text(
                format(maximum * (3 - step) / 3, ".1f" if maximum < 10 else ".0f")
            )
        if not self.points:
            return False
        start = self.points[0][0]
        duration = max(1, self.points[-1][0] - start)
        for phase, color in (
            ("Download", (1, 0.48, 0.12)),
            ("Upload", (0.2, 0.55, 0.95)),
        ):
            cr.set_source_rgb(*color)
            cr.set_line_width(2)
            active = False
            for timestamp, direction, value in self.points:
                if direction != phase:
                    active = False
                    continue
                x = 48 + (width - 60) * (timestamp - start) / duration
                y = height - 20 - (height - 35) * value / maximum
                if active:
                    cr.line_to(x, y)
                else:
                    cr.move_to(x, y)
                active = True
            cr.stroke()
            for timestamp, direction, value in self.points:
                if direction == phase:
                    x = 48 + (width - 60) * (timestamp - start) / duration
                    y = height - 20 - (height - 35) * value / maximum
                    cr.arc(x, y, 2, 0, 6.283185)
                    cr.fill()
        return False


class Application(Gtk.Application):
    def __init__(self):
        super().__init__(application_id="io.github.reset_explode_linux.Community")
        self.process = None
        self.job_is_admin = False
        self.cancelled = False
        self.sample = {}
        self.max_temp = 90
        self.last_job = None
        self._closing = False
        self.connect("activate", self.activate)

    def activate(self, *_):
        if hasattr(self, "win"):
            self.win.present()
            return
        self.dark = storage.load("ui.json", {"dark": True}).get("dark", True)
        provider = Gtk.CssProvider()
        self.theme_provider = provider
        self.apply_theme()
        Gtk.StyleContext.add_provider_for_screen(
            Gdk.Screen.get_default(), provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION
        )
        self.win = Gtk.ApplicationWindow(application=self)
        self.win.set_title("RESET EXPLODE · Linux")
        self.win.set_default_size(1190, 790)
        self.win.connect("delete-event", self.close)
        header = Gtk.HeaderBar(
            title="RESET EXPLODE · LINUX", subtitle=f"Comunidade · {__version__}"
        )
        header.set_show_close_button(False)
        self.window_controls = {}
        for action, tooltip, callback in [
            ("close", "Fechar", self.win.close),
            ("maximize", "Maximizar / restaurar", self.toggle_maximized),
            ("minimize", "Minimizar", self.win.iconify),
        ]:
            control = Gtk.Button(
                label={"close": "×", "maximize": "□", "minimize": "−"}[action]
            )
            control.set_valign(Gtk.Align.CENTER)
            control.set_size_request(32, 32)
            self.window_controls[action] = control
            control.set_tooltip_text(tooltip)
            control.get_style_context().add_class("window-control")
            if action == "close":
                control.get_style_context().add_class("close-control")
            control.connect("clicked", lambda _, fn=callback: fn())
            header.pack_end(control)
        self.win.connect(
            "window-state-event",
            lambda *_: self.window_controls["maximize"].set_label(
                "▣" if self.win.is_maximized() else "□"
            ),
        )
        self.win.set_titlebar(header)
        report = self.button("Exportar relatório", self.export_report)
        header.pack_end(report)
        header.pack_end(self.button("Claro / escuro", self.toggle_theme))
        outer = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        self.win.add(outer)
        body = Gtk.Box()
        outer.pack_start(body, True, True, 0)
        self.nav = Gtk.ListBox()
        self.nav.set_selection_mode(Gtk.SelectionMode.SINGLE)
        self.nav.get_style_context().add_class("sidebar")
        self.nav.set_size_request(225, -1)
        self.nav.connect("row-selected", self.selected)
        nav_scroll = Gtk.ScrolledWindow()
        nav_scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        nav_scroll.add(self.nav)
        body.pack_start(nav_scroll, False, False, 0)
        self.stack = Gtk.Stack()
        self.stack.set_transition_type(Gtk.StackTransitionType.CROSSFADE)
        body.pack_start(self.stack, True, True, 0)
        self.footer = Gtk.Label(label="Lendo hardware…")
        self.footer.set_xalign(0)
        self.footer.set_margin_start(18)
        self.footer.set_margin_top(8)
        self.footer.set_margin_bottom(8)
        outer.pack_end(self.footer, False, False, 0)
        self.pages = {}
        self.live = {}
        builders = [
            ("VISÃO GERAL", self.dashboard),
            ("Sistema", self.info_page),
            ("Sensores", self.sensor_page),
            ("Placa de vídeo", self.gpu_page),
            ("Discos", self.disk_page),
            ("Energia", self.energy_page),
            ("Rede", self.network_page),
            ("JOGOS · Biblioteca", self.game_page),
            ("Gargalo", self.bottleneck_page),
            ("FPS e overlay", self.overlay_page),
            ("DESEMPENHO · Benchmarks", self.bench_page),
            ("Stress test", self.stress_page),
            ("FERRAMENTAS · Processos", self.process_page),
            ("Inicialização", self.startup_page),
            ("Instalar ferramentas", self.packages_page),
            ("Drivers e USB/serial", self.driver_page),
            ("Integração RGB", self.rgb_page),
            ("Histórico", self.history_page),
            ("Sobre e compatibilidade", self.about_page),
        ]
        for title, builder in builders:
            page = self.page(title)
            builder(page)
            self.pages[title] = page
        self.nav.select_row(self.nav.get_row_at_index(0))
        self.win.show_all()
        self.refresh()
        GLib.timeout_add_seconds(1, self.refresh)

    def toggle_maximized(self):
        if self.win.is_maximized():
            self.win.unmaximize()
        else:
            self.win.maximize()

    def apply_theme(self):
        bg, panel, card, fg, muted, border, accent = (
            (
                "#111820",
                "#18222d",
                "#202e3c",
                "#edf3fa",
                "#b4c2d2",
                "#3a4b5e",
                "#305e87",
            )
            if self.dark
            else (
                "#f2f5f9",
                "#e5ebf3",
                "#ffffff",
                "#182739",
                "#4d6076",
                "#bdcbdc",
                "#d2e6ff",
            )
        )
        css = f"""
        window, dialog {{background:{bg};color:{fg};}}
        label {{color:{fg};}}
        headerbar, .sidebar {{background:{panel};color:{fg};}}
        .sidebar {{padding:10px;}}
        row {{padding:8px;border-radius:6px;}}
        .sidebar row {{background-image:none;background-color:{panel};color:{fg};}}
        .sidebar row:selected {{background-image:none;background-color:{accent};color:{fg};}}
        .sidebar row label {{color:{fg};}}
        row:selected, treeview:selected {{background:{accent};color:{fg};}}
        .title {{font-size:25px;font-weight:700;}}
        .heading {{font-size:16px;font-weight:700;}}
        .metric {{font-size:27px;font-weight:700;}}
        .subtitle {{color:{muted};}}
        .warning {{color:{"#f5cc85" if self.dark else "#825000"};}}
        .card {{background:{card};border:1px solid {border};border-radius:10px;padding:15px;}}
        button, entry, spinbutton, combobox, textview, textview text, treeview, list, viewport {{background:{card};color:{fg};}}
        button {{background-image:none;border:1px solid {border};border-radius:7px;padding:8px 12px;}}
        button:hover {{background:{accent};}}
        headerbar button.window-control {{font-family:sans-serif;font-size:19px;font-weight:normal;padding:4px;min-width:24px;min-height:24px;margin:0 1px;border:0;box-shadow:none;background-image:none;color:{fg};}}
        headerbar button.close-control:hover {{background:#c43d47;color:#ffffff;}}
        headerbar button.close-control:hover label {{color:#ffffff;}}
        button:disabled {{color:{muted};}}
        button.speed-start {{background:#f6821f;color:#17202c;}}
        button.speed-start label {{color:#17202c;}}
        entry selection, textview text selection {{background:{accent};color:{fg};}}
        treeview header button {{background:{panel};color:{fg};}}
        progressbar trough {{min-height:8px;background:{border};border-radius:5px;}}
        progressbar progress {{background:#4285ce;border-radius:5px;}}
        """
        self.theme_provider.load_from_data(css.encode())
        Gtk.Settings.get_default().set_property(
            "gtk-application-prefer-dark-theme", self.dark
        )
        for name in ("graph", "bottleneck_graph"):
            if hasattr(self, name):
                getattr(self, name).queue_draw()

    def toggle_theme(self):
        self.dark = not self.dark
        storage.save("ui.json", {"dark": self.dark})
        self.apply_theme()

    def page(self, title):
        row = Gtk.ListBoxRow()
        label = Gtk.Label(label=title)
        label.set_xalign(0)
        label.set_margin_start(5)
        row.add(label)
        row.page_title = title
        self.nav.add(row)
        scroll = Gtk.ScrolledWindow()
        scroll.set_policy(Gtk.PolicyType.AUTOMATIC, Gtk.PolicyType.AUTOMATIC)
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
        box.set_border_width(22)
        scroll.add(box)
        self.stack.add_named(scroll, title)
        t = Gtk.Label(label=title)
        t.set_xalign(0)
        t.get_style_context().add_class("title")
        box.pack_start(t, False, False, 0)
        return box

    def selected(self, _, row):
        if row:
            self.stack.set_visible_child_name(row.page_title)

    def label(self, box, text, style=None):
        l = Gtk.Label(label=text)
        l.set_xalign(0)
        l.set_line_wrap(True)
        l.set_selectable(True)
        if style:
            l.get_style_context().add_class(style)
        box.pack_start(l, False, False, 0)
        return l

    def button(self, label, callback):
        b = Gtk.Button(label=label)
        b.connect("clicked", lambda *_: callback())
        return b

    def buttons(self, box, items):
        row = Gtk.FlowBox()
        row.set_selection_mode(Gtk.SelectionMode.NONE)
        row.set_max_children_per_line(5)
        for title, fn in items:
            row.add(self.button(title, fn))
        box.pack_start(row, False, False, 0)
        return row

    def text(self, box, text="", technical=False):
        if not technical:
            view = Details()
            view.render(text)
            box.pack_start(view, False, False, 0)
            return view
        view = Gtk.TextView()
        view.set_editable(False)
        view.set_cursor_visible(False)
        view.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
        view.set_monospace(True)
        view.set_left_margin(12)
        view.set_right_margin(12)
        view.set_top_margin(12)
        view.get_buffer().set_text(str(text))
        scroll = Gtk.ScrolledWindow()
        scroll.set_min_content_height(240)
        scroll.add(view)
        box.pack_start(scroll, True, True, 0)
        return view

    def put(self, view, text):
        if isinstance(view, Details):
            view.render(text)
            return
        view.get_buffer().set_text(
            text
            if isinstance(text, str)
            else json.dumps(text, ensure_ascii=False, indent=2)
        )

    def table(self, box, columns):
        model = Gtk.ListStore(*([str] * len(columns)))
        view = Gtk.TreeView(model=model)
        for i, title in enumerate(columns):
            renderer = Gtk.CellRendererText()
            column = Gtk.TreeViewColumn(title, renderer, text=i)
            column.set_resizable(True)
            column.set_sort_column_id(i)
            view.append_column(column)
        scroll = Gtk.ScrolledWindow()
        scroll.set_min_content_height(260)
        scroll.add(view)
        box.pack_start(scroll, True, True, 0)
        return model, view

    def fill(self, model, rows):
        model.clear()
        for row in rows:
            model.append([str(x) for x in row])

    def metric(self, box, name):
        card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=7)
        card.get_style_context().add_class("card")
        title = Gtk.Label(label=name)
        title.set_xalign(0)
        card.pack_start(title, False, False, 0)
        label = Gtk.Label(label="—")
        label.set_xalign(0)
        label.get_style_context().add_class("metric")
        card.pack_start(label, False, False, 0)
        bar = Gtk.ProgressBar()
        card.pack_start(bar, False, False, 0)
        box.pack_start(card, True, True, 0)
        return label, bar

    def dashboard(self, box):
        self.label(
            box,
            "Hardware real, sensores do Linux e ferramentas por demanda. Nenhum teste de carga roda ao abrir.",
            "subtitle",
        )
        cards = Gtk.Box(spacing=12)
        box.pack_start(cards, False, False, 0)
        self.cpu_card = self.metric(cards, "CPU")
        self.gpu_card = self.metric(cards, "GPU")
        self.ram_card = self.metric(cards, "MEMÓRIA")
        self.graph = HistoryGraph()
        box.pack_start(self.graph, False, False, 0)
        self.label(
            box, "CPU azul · GPU lilás · RAM verde — últimos 3 minutos", "subtitle"
        )
        self.dash_details = self.label(box, "")
        self.dash_bottleneck = self.label(box, "")
        self.buttons(
            box,
            [
                ("Ver sensores", lambda: self.stack.set_visible_child_name("Sensores")),
                ("Histórico", lambda: self.stack.set_visible_child_name("Histórico")),
                (
                    "Quem usa a RAM?",
                    lambda: self.stack.set_visible_child_name(
                        "FERRAMENTAS · Processos"
                    ),
                ),
            ],
        )
        self.label(
            box,
            "Bem-vindo à implementação Linux comunitária. Benchmarks usam cargas próprias identificadas e não reproduzem as notas da versão Windows.",
            "subtitle",
        )

    def info_page(self, box):
        self.info_text = self.text(box, system.inventory())
        self.put(self.info_text, system.inventory())
        self.buttons(
            box, [("Atualizar", lambda: self.put(self.info_text, system.inventory()))]
        )

    def sensor_page(self, box):
        self.label(
            box,
            "Os sensores disponíveis dependem do driver e do equipamento. Campo ausente significa que o kernel não expõe aquela medição.",
            "subtitle",
        )
        self.sensor_filter = Gtk.SearchEntry()
        self.sensor_filter.set_placeholder_text("Buscar dispositivo, sensor ou unidade")
        box.pack_start(self.sensor_filter, False, False, 0)
        self.sensor_model, _ = self.table(
            box, ["Dispositivo", "Sensor", "Valor", "Unidade", "Mínimo", "Máximo"]
        )
        self.extrema = {}

    def gpu_page(self, box):
        self.gpu_text = self.text(box)
        self.put(self.gpu_text, system.gpu_info())
        self.gpu_live = self.label(box, "")
        self.buttons(
            box,
            [
                (
                    "Informações Vulkan",
                    lambda: self.run_job(["vulkaninfo", "--summary"], "Vulkan"),
                ),
                (
                    "Informações OpenGL",
                    lambda: self.run_job(["glxinfo", "-B"], "OpenGL"),
                ),
            ],
        )

    def disk_page(self, box):
        self.disk_model, _ = self.table(
            box, ["Dispositivo", "Montagem", "Tipo", "Capacidade", "Livre", "Uso"]
        )
        self.disk_selector = Gtk.ComboBoxText()
        for d in system.physical_disks():
            self.disk_selector.append(
                d["path"], f"{d['path']} — {d.get('model') or ''} {d.get('size') or ''}"
            )
        self.disk_selector.set_active(0)
        box.pack_start(self.disk_selector, False, False, 0)
        self.disk_health_text = self.text(
            box,
            "S.M.A.R.T. é consultado somente quando solicitado; sem executar testes ou escrever no disco.",
        )
        self.buttons(
            box,
            [
                ("Ler saúde S.M.A.R.T.", self.read_smart),
                ("Ler com permissão do sistema", self.read_smart_admin),
                ("Instalar smartmontools", lambda: self.install("Saúde dos discos")),
            ],
        )
        self.refresh_disks()

    def refresh_disks(self):
        self.fill(
            self.disk_model,
            [
                [
                    d[k]
                    for k in [
                        "Dispositivo",
                        "Montagem",
                        "Sistema de arquivos",
                        "Capacidade",
                        "Livre",
                        "Uso",
                    ]
                ]
                for d in system.disks()
            ],
        )

    def read_smart(self):
        dev = self.disk_selector.get_active_id()
        if dev:
            self.put(self.disk_health_text, system.disk_health(dev))

    def read_smart_admin(self):
        dev = self.disk_selector.get_active_id()
        exe = shutil.which("smartctl")
        if not exe:
            return self.message("Instale smartmontools primeiro.")
        if dev not in [d["path"] for d in system.physical_disks()]:
            return self.message("Disco inválido.")
        self.run_job(
            ["pkexec", exe, "--json", "--all", dev],
            "Saúde S.M.A.R.T.",
            confirm="Autorizar somente a leitura S.M.A.R.T. de "
            + dev
            + "? Não inicia teste nem grava no disco.",
        )

    def energy_page(self, box):
        self.energy_text = self.text(box)
        self.buttons(
            box,
            [
                ("Atualizar", lambda: self.put(self.energy_text, system.energy())),
                ("Economia", lambda: self.set_power("power-saver")),
                ("Equilibrado", lambda: self.set_power("balanced")),
                ("Desempenho", lambda: self.set_power("performance")),
            ],
        )
        self.label(
            box,
            "Perfis dependem do serviço power-profiles-daemon e do suporte do hardware. Desempenho pode elevar consumo e temperatura.",
            "subtitle",
        )

    def set_power(self, value):
        self.run_job(
            ["powerprofilesctl", "set", value],
            "Perfil de energia",
            confirm=f"Aplicar o perfil {value} ao sistema?",
        )

    def network_page(self, box):
        self.label(
            box,
            "Meça download, upload e estabilidade da conexão. Acompanhe as etapas e a transferência em tempo real.",
            "subtitle",
        )
        self.label(box, "Sua conexão", "heading")
        self.speed_metrics = {}
        for pair in (
            ("Download (Mbps)", "Upload (Mbps)"),
            ("Latência HTTPS (ms)", "Variação da latência (ms)"),
        ):
            row = Gtk.Box(spacing=12)
            box.pack_start(row, False, False, 0)
            for key in pair:
                display = {
                    "Download (Mbps)": "DOWNLOAD · Mbps",
                    "Upload (Mbps)": "UPLOAD · Mbps",
                    "Latência HTTPS (ms)": "LATÊNCIA · ms",
                    "Variação da latência (ms)": "JITTER · ms",
                }.get(key, key.upper())
                label, bar = self.metric(row, display)
                bar.set_no_show_all(True)
                bar.hide()
                self.speed_metrics[key] = label
        self.speed_status = self.label(box, "Pronto para medir")
        self.speed_progress = Gtk.ProgressBar()
        box.pack_start(self.speed_progress, False, False, 0)
        self.speed_graph = SpeedGraph()
        box.pack_start(self.speed_graph, False, False, 0)
        self.label(
            box,
            "Download laranja · Upload azul · Mb/s durante o teste. Jitter representa a variação das requisições HTTPS.",
            "subtitle",
        )
        speed_actions = self.buttons(
            box,
            [
                (
                    "Iniciar / testar novamente",
                    lambda: self.run_worker("speed", "Velocidade da internet"),
                ),
                ("Parar", self.stop_job),
            ],
        )
        speed_actions.get_child_at_index(0).get_child().get_style_context().add_class(
            "speed-start"
        )
        details = Gtk.Expander(label="Servidor, método e detalhes do resultado")
        contents = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        details.add(contents)
        box.pack_start(details, False, False, 0)
        self.speed_result = self.text(
            contents,
            {
                "Servidores": "Cloudflare, com alternativa automática LibreSpeed",
                "Tráfego máximo": "214 MiB incluindo tentativas e blocos recusados",
                "Método": "Uma conexão HTTPS por vez; inclui o custo de conexão e resposta. Não mede bufferbloat. O servidor usado aparece no resultado.",
            },
        )
        interfaces = Gtk.Expander(label="Interfaces de rede")
        interface_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        interfaces.add(interface_box)
        self.put(self.text(interface_box), system.network())
        box.pack_start(interfaces, False, False, 0)
        self.label(
            box,
            "O diagnóstico envia 5 pings ao gateway e a 1.1.1.1/8.8.8.8. Sem envio de inventário, arquivos ou credenciais.",
            "subtitle",
        )
        self.buttons(
            box,
            [
                (
                    "Medir latência / jitter / perda",
                    lambda: self.run_worker(
                        "network",
                        "Diagnóstico de rede",
                        confirm="Enviar pings para medir a conexão?",
                    ),
                )
            ],
        )

    def speed_event(self, event):
        phase = event.get("phase", "Medindo")
        self.speed_status.set_text(phase)
        self.speed_progress.set_fraction(
            {
                "Conectando": 0.02,
                "Trocando servidor": 0.03,
                "Latência": 0.08,
                "Download": 0.25,
                "Upload": 0.7,
                "Reduzindo bloco de upload": 0.7,
                "Concluído": 1,
            }.get(phase, 0)
        )
        for key, value in event.get("metrics", {}).items():
            if key in self.speed_metrics:
                self.speed_metrics[key].set_text(
                    f"{value:.2f}"
                    if isinstance(value, (int, float))
                    else "Indisponível"
                )
        self.speed_graph.add_rate(phase, event.get("rate"))
        return False

    def game_page(self, box):
        self.games = storage.load("games.json", [])
        self.game_model, self.game_view = self.table(
            box, ["ID", "Jogo", "Origem", "Executável / AppID"]
        )
        self.buttons(
            box,
            [
                ("Detectar jogos", self.discover_games),
                ("Adicionar jogo", self.add_game),
                ("Editar perfil", self.edit_game),
                ("Jogar", self.launch_game),
                ("Opções para Steam", self.copy_steam_options),
                ("Remover da lista", self.remove_game),
            ],
        )
        self.label(
            box,
            "Proton usa um prefixo separado por jogo. Para jogos Steam, GameMode/overlay devem ser incluídos nas opções de inicialização copiadas acima. Ajustes não ficam aplicados globalmente.",
            "subtitle",
        )
        self.game_status = self.label(box, "Selecione um jogo e clique em Jogar.")
        self.game_view.connect("row-activated", lambda *_: self.launch_game())
        self.discover_games()

    def refresh_games(self):
        self.fill(
            self.game_model,
            [[g["id"], g["name"], g["kind"], g["target"]] for g in self.games],
        )

    def discover_games(self):
        known = {g["id"] for g in self.games}
        self.games.extend(g for g in integrations.scan_games() if g["id"] not in known)
        self.save_games()

    def save_games(self):
        storage.save("games.json", self.games)
        self.refresh_games()

    def selected_game(self):
        model, it = self.game_view.get_selection().get_selected()
        return (
            next((g for g in self.games if g["id"] == model[it][0]), None)
            if it
            else None
        )

    def add_game(self):
        dialog = Gtk.FileChooserDialog(
            title="Escolher executável Linux ou Windows",
            parent=self.win,
            action=Gtk.FileChooserAction.OPEN,
        )
        dialog.add_buttons(
            "Cancelar", Gtk.ResponseType.CANCEL, "Escolher", Gtk.ResponseType.OK
        )
        if dialog.run() == Gtk.ResponseType.OK:
            target = dialog.get_filename()
            self.games.append(
                {
                    "id": uuid.uuid4().hex,
                    "name": pathlib.Path(target).stem,
                    "kind": "file",
                    "target": target,
                    "args": "",
                    "overlay": False,
                    "gamemode": False,
                }
            )
            self.save_games()
        dialog.destroy()

    def edit_game(self):
        g = self.selected_game()
        if not g:
            return self.message("Selecione um jogo primeiro.")
        dialog = Gtk.Dialog(title="Perfil do jogo", transient_for=self.win, modal=True)
        dialog.add_buttons(
            "Cancelar", Gtk.ResponseType.CANCEL, "Salvar", Gtk.ResponseType.OK
        )
        b = dialog.get_content_area()
        b.set_border_width(18)
        entries = {}
        for key, title in [
            ("name", "Nome"),
            ("args", "Argumentos (sem comandos de shell)"),
            ("proton", "Caminho do Proton (opcional)"),
        ]:
            self.label(b, title)
            e = Gtk.Entry()
            e.set_text(g.get(key, ""))
            b.pack_start(e, False, False, 3)
            entries[key] = e
        checks = {}
        for key, title in [
            ("gamemode", "GameMode durante a execução"),
            ("overlay", "Overlay MangoHud"),
        ]:
            c = Gtk.CheckButton(label=title)
            c.set_active(g.get(key, False))
            b.pack_start(c, False, False, 3)
            checks[key] = c
        dialog.show_all()
        if dialog.run() == Gtk.ResponseType.OK:
            try:
                shlex.split(entries["args"].get_text())
            except ValueError:
                dialog.destroy()
                return self.message("Argumentos com aspas inválidas.")
            g.update({k: e.get_text() for k, e in entries.items()})
            g.update({k: c.get_active() for k, c in checks.items()})
            self.save_games()
        dialog.destroy()

    def remove_game(self):
        g = self.selected_game()
        if g and self.confirm(
            "Remover apenas o perfil da lista? Arquivos e saves ficam preservados."
        ):
            self.games.remove(g)
            self.save_games()

    def launch_game(self):
        g = self.selected_game()
        if not g:
            return self.message("Selecione um jogo primeiro.")
        try:
            argv, cwd, env = integrations.game_command(g)
            log = storage.state_dir() / f"game-{integrations.safe_game_id(g['id'])}.log"
            with log.open("a") as stream:
                proc = subprocess.Popen(
                    argv,
                    cwd=cwd,
                    env=env,
                    stdout=stream,
                    stderr=stream,
                    start_new_session=True,
                )
            self.game_status.set_text("Abrindo " + g["name"] + "…")

            def monitor():
                code = proc.wait()

                def done():
                    self.game_status.set_text(
                        (
                            "Inicialização enviada ao launcher"
                            if g["kind"] in ("steam", "desktop")
                            else "Jogo encerrado"
                        )
                        + f" · código {code}"
                    )
                    if code != 0:
                        self.message(
                            "Não foi possível abrir "
                            + g["name"]
                            + ".\n\n"
                            + log.read_text(errors="replace")[-2500:]
                        )
                    return False

                GLib.idle_add(done)

            threading.Thread(target=monitor, daemon=True).start()
        except (ValueError, OSError) as e:
            self.message(str(e))

    def copy_steam_options(self):
        g = self.selected_game()
        if g:
            value = integrations.steam_options(g)
            Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD).set_text(value, -1)
            self.message(
                "Cole em Steam → Propriedades → Opções de inicialização:\n\n" + value
            )

    def bottleneck_page(self, box):
        self.bottleneck_label = self.label(box, "")
        self.label(
            box,
            "Interpretação de carga observada, não porcentagem teórica de compatibilidade CPU × GPU. Pode haver limites de FPS, sincronização, I/O ou fases sem carga.",
            "subtitle",
        )
        self.bottleneck_graph = HistoryGraph()
        box.pack_start(self.bottleneck_graph, False, False, 0)

    def overlay_page(self, box):
        self.label(
            box,
            "FPS real e frametime nos jogos Linux/Proton via MangoHud. Ative Overlay no perfil do jogo e inicie pela Biblioteca. Na Steam, copie as opções de inicialização.",
            "subtitle",
        )
        self.buttons(
            box,
            [
                ("Instalar MangoHud", lambda: self.install("Overlay FPS")),
                (
                    "Abrir documentação",
                    lambda: self.open_url(
                        "https://github.com/flightlessmango/MangoHud"
                    ),
                ),
            ],
        )
        self.label(
            box,
            "O perfil inclui FPS, frametime, gráfico, CPU, GPU, RAM/VRAM e temperaturas disponíveis. Shift direito + F12 alterna o HUD. O driver ou anticheat do jogo pode restringir overlays.",
            "subtitle",
        )

    def bench_page(self, box):
        self.label(
            box,
            "Cargas identificadas e resultados locais. Notas não são comparáveis às do ResetExplode Windows. Os testes só começam pelos botões abaixo.",
            "subtitle",
        )
        self.buttons(
            box,
            [
                (
                    "CPU · 1 worker / 5 s",
                    lambda: self.run_worker(
                        "cpu",
                        "CPU single",
                        extra=["--seconds", "5", "--threads", "1"],
                        confirm="Executar benchmark de CPU por 5 segundos?",
                    ),
                ),
                (
                    "CPU · multi / 5 s",
                    lambda: self.run_worker(
                        "cpu",
                        "CPU multi",
                        extra=[
                            "--seconds",
                            "5",
                            "--threads",
                            str(min(8, os.cpu_count() or 1)),
                        ],
                        confirm="Executar benchmark de CPU com até 8 workers?",
                    ),
                ),
                (
                    "RAM · cópia/verificação",
                    lambda: self.run_worker(
                        "memory",
                        "Memória",
                        confirm="Executar cópia e verificação de 32 MiB de RAM?",
                    ),
                ),
                ("Disco · temporário", self.disk_bench),
                (
                    "GPU · glmark2",
                    lambda: self.run_job(
                        [system.gpu_benchmark()],
                        "Benchmark GPU",
                        confirm="Abrir benchmark gráfico? A carga pode ser alta.",
                    ),
                ),
                ("Instalar glmark2", lambda: self.install("Benchmark GPU")),
            ],
        )

        self.result_details = self.text(
            box, "Escolha um teste para medir o desempenho."
        )
        expander = Gtk.Expander(label="Detalhes técnicos da tarefa")
        logs = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        expander.add(logs)
        box.pack_start(expander, False, False, 0)
        self.bench_output = self.text(logs, "", technical=True)
        self.job_output = self.bench_output
        self.buttons(box, [("Parar tarefa em execução", self.stop_job)])

    def disk_bench(self):
        dialog = Gtk.FileChooserDialog(
            title="Pasta para arquivo temporário de benchmark",
            parent=self.win,
            action=Gtk.FileChooserAction.SELECT_FOLDER,
        )
        dialog.add_buttons(
            "Cancelar", Gtk.ResponseType.CANCEL, "Escolher", Gtk.ResponseType.OK
        )
        if dialog.run() == Gtk.ResponseType.OK:
            directory = dialog.get_filename()
            dialog.destroy()
            self.run_worker(
                "disk",
                "Disco",
                extra=["--directory", directory],
                confirm="Escrever, ler e apagar um arquivo temporário de 64 MiB nessa pasta? A leitura pode usar cache do Linux.",
            )
            return
        dialog.destroy()

    def stress_page(self, box):
        self.label(
            box,
            "Testes de carga explícitos com stress-ng/glmark2. Parada por botão, tempo e temperatura de CPU quando um sensor está disponível. Não altere limites de energia durante o teste.",
            "subtitle",
        )
        self.duration = Gtk.SpinButton.new_with_range(5, 300, 5)
        self.duration.set_value(30)
        self.label(box, "Duração em segundos")
        box.pack_start(self.duration, False, False, 0)
        self.temp = Gtk.SpinButton.new_with_range(60, 100, 1)
        self.temp.set_value(90)
        self.label(box, "Parar quando a CPU atingir (°C)")
        box.pack_start(self.temp, False, False, 0)
        self.buttons(
            box,
            [
                ("Stress CPU", lambda: self.start_stress("cpu")),
                ("Stress RAM", lambda: self.start_stress("ram")),
                ("Stress GPU", lambda: self.start_stress("gpu")),
                ("Parar", self.stop_job),
                ("Instalar stress-ng", lambda: self.install("Stress CPU/RAM")),
            ],
        )
        self.stress_status = self.label(box, "Nenhum teste em execução.")

    def start_stress(self, kind):
        seconds = int(self.duration.get_value())
        self.max_temp = self.temp.get_value()
        if kind == "cpu":
            argv = [
                "stress-ng",
                "--cpu",
                str(min(8, os.cpu_count() or 1)),
                "--timeout",
                str(seconds) + "s",
                "--verify",
                "--metrics-brief",
            ]
        elif kind == "ram":
            mib = max(
                16,
                min(
                    256,
                    int(__import__("psutil").virtual_memory().available / 4 / 1024**2),
                ),
            )
            argv = [
                "stress-ng",
                "--vm",
                "1",
                "--vm-bytes",
                str(mib) + "M",
                "--vm-keep",
                "--timeout",
                str(seconds) + "s",
                "--verify",
                "--metrics-brief",
            ]
        else:
            argv = [system.gpu_benchmark(), "--run-forever"]
        sensor = (
            "O sensor da CPU está indisponível: não haverá parada térmica por temperatura."
            if self.sample.get("cpu_temp") is None
            else f"Parada térmica da CPU configurada em {self.max_temp:.0f} °C."
        )
        self.run_job(
            argv,
            "Stress " + kind,
            confirm=f"Executar carga de {kind} por até {seconds} segundos?\n\n{sensor}\nA GPU pode não expor sensor térmico; observe o equipamento.",
            seconds=seconds,
            thermal=True,
        )

    def process_page(self, box):
        self.label(
            box,
            "Processos por uso de RAM. Fechar envia SIGTERM apenas ao processo selecionado do seu usuário. Não limpa caches do kernel ou encerra programas automaticamente.",
            "subtitle",
        )
        self.ram_summary = self.label(box, "")
        self.label(box, "Memória por programa", "heading")
        self.app_memory_model, _ = self.table(
            box, ["Programa", "Processos", "RAM (RSS)", "% da RAM"]
        )
        self.label(box, "Processos individuais", "heading")
        self.proc_model, self.proc_view = self.table(
            box, ["PID", "Programa", "CPU %", "RAM %", "Memória usada"]
        )
        self.buttons(
            box,
            [
                ("Atualizar", self.refresh_processes),
                ("Fechar selecionado", self.close_process),
                ("Instalar GameMode", lambda: self.install("GameMode")),
            ],
        )
        self.refresh_processes()

    def refresh_processes(self):
        import psutil

        ram = psutil.virtual_memory()
        self.ram_summary.set_text(
            f"Em uso: {system.size(ram.used)} · Disponível: {system.size(ram.available)} · Total: {system.size(ram.total)} · Maiores consumidores primeiro. A memória RSS de processos pode compartilhar páginas; a soma não equivale ao total do sistema."
        )
        groups = {}
        import collections

        groups = collections.defaultdict(lambda: [0, 0])
        processes = system.processes(limit=10000)
        for process in processes:
            group = groups[process.get("name") or "Desconhecido"]
            group[0] += 1
            group[1] += process.get("rss", 0)
        self.fill(
            self.app_memory_model,
            [
                [
                    name,
                    values[0],
                    system.size(values[1]),
                    f"{100 * values[1] / ram.total:.1f}%",
                ]
                for name, values in sorted(
                    groups.items(), key=lambda pair: pair[1][1], reverse=True
                )[:20]
            ],
        )
        model, selected = self.proc_view.get_selection().get_selected()
        selected_pid = model[selected][0] if selected else None
        self.fill(
            self.proc_model,
            [
                [
                    p["pid"],
                    p.get("name"),
                    p.get("cpu_percent") or 0,
                    round(p.get("memory_percent") or 0, 2),
                    system.size(p.get("rss", 0)),
                ]
                for p in processes[:100]
            ],
        )
        if selected_pid:
            for row in self.proc_model:
                if row[0] == selected_pid:
                    self.proc_view.get_selection().select_iter(row.iter)
                    break

    def close_process(self):
        model, it = self.proc_view.get_selection().get_selected()
        if not it:
            return
        pid = int(model[it][0])
        name = model[it][1]
        try:
            import psutil

            p = psutil.Process(pid)
            if p.name() != name:
                return self.message(
                    "O processo mudou desde a seleção. Atualize a lista e escolha novamente."
                )
            if (
                p.uids().real != os.getuid()
                or pid in [1, os.getpid()]
                or p.name()
                in [
                    "kwin_wayland",
                    "plasmashell",
                    "systemd",
                    "dbus-daemon",
                    "pipewire",
                    "wireplumber",
                ]
            ):
                return self.message("Processo do sistema ou sessão protegido.")
            if self.confirm(
                f"Pedir para {name} (PID {pid}) encerrar? Salve seu trabalho antes."
            ):
                p.terminate()
        except (psutil.Error, OSError) as e:
            self.message(str(e))

    def startup_page(self, box):
        self.label(
            box,
            "Aplicativos XDG de início da sessão. Alterações são feitas no seu usuário, com backup para restaurar. Serviços systemd não são removidos.",
            "subtitle",
        )
        self.startup_model, self.startup_view = self.table(
            box, ["Arquivo", "Programa", "Ativo"]
        )
        self.buttons(
            box,
            [
                ("Atualizar", self.refresh_startups),
                ("Ativar", lambda: self.toggle_startup(True)),
                ("Desativar", lambda: self.toggle_startup(False)),
                ("Restaurar alterações", self.restore_startups),
            ],
        )
        self.refresh_startups()

    def refresh_startups(self):
        self.fill(
            self.startup_model,
            [
                [s["id"], s["name"], "Sim" if s["enabled"] else "Não"]
                for s in integrations.startups()
            ],
        )

    def toggle_startup(self, value):
        model, it = self.startup_view.get_selection().get_selected()
        if not it:
            return
        if not self.confirm(
            ("Ativar" if value else "Desativar")
            + " "
            + model[it][1]
            + " ao iniciar a sessão?"
        ):
            return
        try:
            integrations.set_startup(model[it][0], value)
            self.refresh_startups()
        except (ValueError, OSError) as e:
            self.message(str(e))

    def restore_startups(self):
        if self.confirm(
            "Restaurar todos os arquivos de inicialização alterados por este aplicativo?"
        ):
            integrations.restore_startups()
            self.refresh_startups()

    def packages_page(self, box):
        self.label(
            box,
            "Instalação pelos repositórios da distribuição, após confirmação e autenticação. Disponibilidade e versões variam; esta versão foi verificada no Debian 13.",
            "subtitle",
        )
        self.package_combo = Gtk.ComboBoxText()
        for name in integrations.PACKAGES:
            self.package_combo.append_text(name)
        self.package_combo.set_active(0)
        box.pack_start(self.package_combo, False, False, 0)
        self.buttons(
            box,
            [
                (
                    "Instalar selecionado",
                    lambda: self.install(self.package_combo.get_active_text()),
                )
            ],
        )
        self.tools_text = self.text(box)
        self.refresh_tools()

    def refresh_tools(self):
        self.put(
            self.tools_text,
            {
                k: ("Disponível" if v else "Não instalado")
                for k, v in system.tools().items()
            },
        )

    def install(self, name):
        try:
            argv = integrations.install_command(name)
        except ValueError as e:
            return self.message(str(e))
        self.run_job(
            argv,
            "Instalar " + name,
            confirm="Instalar pelo gerenciador de pacotes?\n\n"
            + shlex.join(argv)
            + "\n\nO sistema pedirá autenticação.",
        )

    def driver_page(self, box):
        self.label(
            box,
            "Dispositivos PCI, USB, módulos do kernel e portas seriais detectados no Linux.",
            "subtitle",
        )
        self.driver_text = self.text(box)
        self.buttons(
            box,
            [
                (
                    "Atualizar diagnóstico",
                    lambda: self.put(self.driver_text, system.drivers()),
                )
            ],
        )
        self.put(self.driver_text, system.drivers())

    def rgb_page(self, box):
        self.label(
            box,
            "Iluminação pelo OpenRGB, quando o dispositivo é suportado. O servidor SDK pode ser usado para efeitos externos. O suporte precisa ser verificado antes de acessar hardware.",
            "subtitle",
        )
        self.buttons(
            box,
            [
                ("Abrir OpenRGB", self.open_rgb),
                (
                    "Listar dispositivos",
                    lambda: self.run_job(
                        ["openrgb", "--list-devices"], "Dispositivos RGB"
                    ),
                ),
                ("Instalar OpenRGB", lambda: self.install("Iluminação OpenRGB")),
            ],
        )
        self.label(
            box,
            "Atalhos de iluminação já configurados no sistema continuam independentes deste projeto.",
            "subtitle",
        )

    def open_rgb(self):
        exe = shutil.which("openrgb") or shutil.which("OpenRGB")
        if not exe:
            return self.message("Instale OpenRGB pela aba Instalar ferramentas.")
        subprocess.Popen([exe], start_new_session=True)

    def history_page(self, box):
        self.history_text = self.text(box)
        self.buttons(box, [("Atualizar", self.refresh_history)])
        self.refresh_history()

    def refresh_history(self):
        rows = []
        for item in reversed(storage.histories()):
            try:
                result = item.get("result") or worker.decode_result(
                    item.get("output", "")
                )
            except ValueError:
                result = item.get("output", "").strip()
            rows.append(
                {
                    "Tarefa": item.get("task"),
                    "Data": datetime.datetime.fromtimestamp(item["time"]).strftime(
                        "%d/%m/%Y %H:%M"
                    ),
                    "Estado": "Interrompido"
                    if item.get("cancelled")
                    else ("Concluído" if item.get("exit_code") == 0 else "Falhou"),
                    "Resultado": result,
                }
            )
        self.put(self.history_text, rows)

    def about_page(self, box):
        self.label(
            box,
            "RESET EXPLODE Linux — desenvolvido com Codex para Linux. Implementação comunitária em Python/GTK 3. Referência de organização e funcionalidades: RESET EXPLODE, de GustavoReset (MIT). Não é uma versão oficial do autor original.",
            "subtitle",
        )
        self.label(
            box,
            "Implementado: hardware/sensores, gráficos, energia, discos e S.M.A.R.T., rede, biblioteca Steam/manual/Heroic, perfis Proton/GameMode/MangoHud, benchmarks identificados, stress controlado, processos, inicialização, instalação de ferramentas, diagnóstico USB/serial, OpenRGB e relatório.",
            "subtitle",
        )

        self.buttons(
            box,
            [
                (
                    "Projeto original",
                    lambda: self.open_url(
                        "https://github.com/GustavoReset/ResetExplode"
                    ),
                ),
                ("Abrir dados locais", lambda: self.open_url(str(storage.state_dir()))),
            ],
        )

    def message(self, text):
        d = Gtk.MessageDialog(
            transient_for=self.win,
            modal=True,
            message_type=Gtk.MessageType.INFO,
            buttons=Gtk.ButtonsType.OK,
            text=str(text),
        )
        d.run()
        d.destroy()

    def confirm(self, text):
        d = Gtk.MessageDialog(
            transient_for=self.win,
            modal=True,
            message_type=Gtk.MessageType.QUESTION,
            buttons=Gtk.ButtonsType.OK_CANCEL,
            text=str(text),
        )
        answer = d.run()
        d.destroy()
        return answer == Gtk.ResponseType.OK

    def open_url(self, url):
        subprocess.Popen(
            ["xdg-open", url],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            start_new_session=True,
        )

    def refresh(self):
        if self._closing:
            return False
        try:
            self.sample = system.snapshot()
            if self.stack.get_visible_child_name() == "FERRAMENTAS · Processos":
                self.refresh_processes()
            s = self.sample
            for pair, value in [
                (self.cpu_card, s["cpu"]),
                (self.gpu_card, s["gpu"]),
                (self.ram_card, s["ram"]),
            ]:
                pair[0].set_text(f"{value:.1f}%" if value is not None else "N/D")
                pair[1].set_fraction(max(0, min(1, (value or 0) / 100)))
            self.graph.add(s["cpu"], s["gpu"], s["ram"])
            self.bottleneck_graph.add(s["cpu"], s["gpu"], s["ram"])
            temp = (
                f"{s['cpu_temp']:.1f} °C"
                if s["cpu_temp"] is not None
                else "sensor indisponível"
            )
            self.footer.set_text(
                f"CPU {s['cpu']:.0f}% · {temp} · RAM {s['ram']:.0f}% · {system.size(s['ram_used'])} / {system.size(s['ram_total'])}"
            )
            self.dash_details.set_text(
                f"CPU: {s['mhz'] or 0:.0f} MHz · Temperatura: {temp} · Swap: {s['swap']:.0f}%"
            )
            verdict = system.bottleneck(s)
            self.dash_bottleneck.set_text(verdict)
            self.bottleneck_label.set_text(verdict)
            self.gpu_live.set_text(
                "Uso da GPU: "
                + (
                    f"{s['gpu']:.0f}%"
                    if s["gpu"] is not None
                    else "Não exposto pelo driver"
                )
            )
            rows = []
            for r in s["sensors"]:
                query = self.sensor_filter.get_text().strip().lower()
                if (
                    query
                    and query
                    not in (r["chip"] + " " + r["name"] + " " + r["unit"]).lower()
                ):
                    continue
                low, high = self.extrema.get(r["id"], (r["value"], r["value"]))
                low = min(low, r["value"])
                high = max(high, r["value"])
                self.extrema[r["id"]] = (low, high)
                rows.append(
                    [
                        r["chip"],
                        r["name"],
                        f"{r['value']:.1f}",
                        r["unit"],
                        f"{low:.1f}",
                        f"{high:.1f}",
                    ]
                )
            self.fill(self.sensor_model, rows)
            if self.stack.get_visible_child_name() == "Energia":
                self.put(self.energy_text, system.energy())
        except Exception as e:
            self.footer.set_text("Coleta parcial: " + str(e))
        return True

    def run_worker(self, task, title, extra=None, confirm=None):
        self.run_job(
            [sys.executable, "-m", "reset_explode_linux.worker", task, *(extra or [])],
            title,
            confirm=confirm,
        )

    def run_job(self, argv, title, confirm=None, seconds=0, thermal=False):
        if self.process is not None:
            return self.message(
                "Há outra tarefa em execução. Pare-a antes de iniciar uma nova."
            )
        if not shutil.which(argv[0]) and not pathlib.Path(argv[0]).is_file():
            return self.message(
                "Ferramenta não instalada: " + argv[0] + ". Use Instalar ferramentas."
            )
        if confirm and not self.confirm(confirm):
            return
        self.job_is_admin = argv[0] == "pkexec"
        self.cancelled = False
        self.put(self.job_output, title + "\n\n" + shlex.join(argv) + "\n")
        self.stack.set_visible_child_name(
            "Rede" if title == "Velocidade da internet" else "DESEMPENHO · Benchmarks"
        )
        self.put(self.result_details, {"Tarefa": title, "Estado": "Em execução"})
        if title == "Velocidade da internet":
            self.speed_graph.points.clear()
            self.speed_graph.queue_draw()
            for label in self.speed_metrics.values():
                label.set_text("—")
            self.speed_event({"phase": "Conectando"})
            self.put(self.speed_result, "Medindo latência, download e upload…")
        self.stress_status.set_text("Executando " + title)
        self._job_start = time.monotonic()
        # Spawn before launching the reader thread so two clicks cannot start two tasks.
        try:
            self.process = subprocess.Popen(
                argv,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                errors="replace",
                start_new_session=True,
                bufsize=1,
            )
        except OSError as e:
            self.process = None
            return self.message(str(e))
        proc = self.process
        self.last_job = title

        def reader():
            chunks = []
            length = 0
            try:
                for line in proc.stdout:
                    if title == "Velocidade da internet":
                        try:
                            event = json.loads(line)
                            if event.get("event") == "progress":
                                GLib.idle_add(self.speed_event, event)
                        except (ValueError, AttributeError):
                            pass
                    chunks.append(line)
                    length += len(line)
                    while length > 200000 and len(chunks) > 1:
                        length -= len(chunks.pop(0))
                    GLib.idle_add(self.put, self.job_output, "".join(chunks))
                code = proc.wait()
                text = "".join(chunks)
            except Exception as exc:
                code = -1
                text = str(exc)
            GLib.idle_add(self.job_done, proc, title, code, text)

        threading.Thread(target=reader, daemon=True).start()
        if seconds or thermal:
            GLib.timeout_add(500, self.check_job, proc, seconds, thermal)

    def check_job(self, proc, seconds, thermal):
        if proc is not self.process or proc.poll() is not None:
            return False
        if seconds and time.monotonic() - self._job_start >= seconds:
            self.stop_job()
            return False
        if (
            thermal
            and self.sample.get("cpu_temp") is not None
            and self.sample["cpu_temp"] >= self.max_temp
        ):
            self.stop_job()
            self.message("Teste interrompido pelo limite de temperatura da CPU.")
            return False
        return True

    def stop_job(self):
        if self.job_is_admin and self.process is not None:
            self.message(
                "Uma operação autorizada do sistema está em andamento. Aguarde a conclusão ou cancele na janela de autenticação."
            )
            return
        proc = self.process
        if proc and proc.poll() is None:
            self.cancelled = True
            try:
                os.killpg(proc.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass

            def kill():
                if proc.poll() is None:
                    try:
                        os.killpg(proc.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                return False

            GLib.timeout_add_seconds(3, kill)

    def job_done(self, proc, title, code, text):
        if self.process is proc:
            self.process = None
            self.job_is_admin = False
        cancelled = self.cancelled
        record = {
            "task": title,
            "exit_code": code,
            "cancelled": cancelled,
            "seconds": round(time.monotonic() - self._job_start, 2),
            "output": text[-30000:],
        }
        try:
            result = worker.decode_result(text)
        except ValueError:
            result = {
                "Tarefa": title,
                "Estado": "Interrompido"
                if cancelled
                else ("Concluído" if code == 0 else "Falhou"),
                "Duração (s)": record["seconds"],
                "Detalhes": "Consulte o registro técnico abaixo."
                if text.strip()
                else "Tarefa concluída sem saída adicional.",
            }
        record["result"] = result
        storage.history(record)
        score = re.search(r"glmark2 Score:\s*(\d+)", text)
        if score:
            result["Pontuação glmark2"] = int(score[1])
        self.put(self.result_details, result)
        if title == "Velocidade da internet":
            self.put(self.speed_result, result)
            self.speed_event(
                {
                    "phase": "Interrompido"
                    if cancelled
                    else ("Falhou" if code else "Concluído"),
                    "metrics": result,
                }
            )
            if result.get("Aviso"):
                self.speed_status.set_text("Resultado parcial · " + result["Aviso"])
            elif result.get("erro"):
                self.speed_status.set_text(
                    "Não foi possível concluir · " + result["erro"]
                )
        self.put(
            self.job_output,
            text
            + f"\n\nFinalizado (código {code})"
            + (" — interrompido" if cancelled else ""),
        )
        self.stress_status.set_text(
            "Interrompido" if cancelled else f"Concluído: {title}"
        )
        self.refresh_history()
        self.refresh_tools()
        return False

    def export_report(self):
        d = Gtk.FileChooserDialog(
            title="Exportar relatório local",
            parent=self.win,
            action=Gtk.FileChooserAction.SAVE,
        )
        d.add_buttons(
            "Cancelar", Gtk.ResponseType.CANCEL, "Salvar", Gtk.ResponseType.OK
        )
        d.set_current_name("reset-explode-linux.html")
        d.set_do_overwrite_confirmation(True)
        if d.run() == Gtk.ResponseType.OK:
            data = {
                "app_version": __version__,
                "generated_at": datetime.datetime.now(
                    datetime.timezone.utc
                ).isoformat(),
                "inventory": system.inventory(),
                "sample": self.sample,
                "disks": system.disks(),
                "energy": system.energy(),
            }
            try:
                pathlib.Path(d.get_filename()).write_text(
                    report.serialize(
                        data,
                        "html"
                        if d.get_filename().lower().endswith(".html")
                        else "json",
                    )
                )
            except OSError as e:
                self.message(str(e))
        d.destroy()

    def close(self, *_):
        if self.process is not None:
            if self.job_is_admin:
                self.message("Aguarde a operação do sistema terminar antes de fechar.")
                return True
            if not self.confirm(
                "Uma tarefa ainda está em execução. Interromper e fechar?"
            ):
                return True
            proc = self.process
            self.stop_job()
            try:
                proc.wait(timeout=2)
            except subprocess.TimeoutExpired:
                try:
                    os.killpg(proc.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
        self._closing = True
        return False


def main():
    return Application().run(sys.argv)


if __name__ == "__main__":
    raise SystemExit(main())
