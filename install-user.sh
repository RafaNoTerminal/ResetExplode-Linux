#!/bin/sh
set -eu
APP_ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PYTHONPATH="$APP_ROOT/src" /usr/bin/python3 -c 'import gi, psutil; gi.require_version("Gtk", "3.0"); from gi.repository import Gtk'
mkdir -p "$HOME/.local/share/applications"
# Quote the executable according to Desktop Entry syntax, including paths with spaces.
APP_ROOT="$APP_ROOT" /usr/bin/python3 - <<'PY'
import os,pathlib
root=pathlib.Path(os.environ['APP_ROOT'])
quoted=str(root/'run.sh').replace('\\','\\\\').replace('"','\\"').replace('`','\\`').replace('$','\\$').replace('%','%%')
p=pathlib.Path.home()/'.local/share/applications/reset-explode-linux.desktop'
p.write_text('[Desktop Entry]\nType=Application\nName=RESET EXPLODE Linux\nComment=Hardware, sensores e ferramentas Linux\nExec="'+quoted+'"\nIcon=utilities-system-monitor\nTerminal=false\nCategories=System;Monitor;\nStartupNotify=true\n')
print('Atalho instalado:',p)
PY
