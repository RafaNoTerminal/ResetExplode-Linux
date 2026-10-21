#!/bin/sh
set -eu
APP_ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
export PYTHONPATH="$APP_ROOT/src${PYTHONPATH:+:$PYTHONPATH}"
if ! /usr/bin/python3 -c 'import gi, cairo, psutil; gi.require_version("Gtk", "3.0"); gi.require_foreign("cairo"); from gi.repository import Gtk' >/dev/null 2>&1; then
    echo "Faltam dependências gráficas. No Debian/Ubuntu, execute: $APP_ROOT/install.sh" >&2
    echo 'Pacotes: python3-gi python3-cairo python3-gi-cairo python3-psutil gir1.2-gtk-3.0' >&2
    exit 1
fi
exec /usr/bin/python3 -m reset_explode_linux.app "$@"
