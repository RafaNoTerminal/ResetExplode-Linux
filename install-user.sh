#!/bin/sh
set -eu
APP_ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PYTHONPATH="$APP_ROOT/src" /usr/bin/python3 -c 'import gi, cairo, psutil, requests; gi.require_foreign("cairo"); gi.require_version("Gtk", "3.0"); from gi.repository import Gtk'
mkdir -p "$HOME/.local/share/applications"
# Quote the executable according to Desktop Entry syntax, including paths with spaces.
APP_ROOT="$APP_ROOT" /usr/bin/python3 - <<'PY'
import os,pathlib,shlex
root=pathlib.Path(os.environ['APP_ROOT'])
quoted=str(root/'run.sh').replace('\\','\\\\').replace('"','\\"').replace('`','\\`').replace('$','\\$').replace('%','%%')
p=pathlib.Path.home()/'.local/share/applications/reset-explode-linux.desktop'
p.write_text('[Desktop Entry]\nType=Application\nName=RBT Diagnóstico Linux\nComment=Hardware, sensores e ferramentas Linux\nExec="'+quoted+'"\nIcon=utilities-system-monitor\nTerminal=false\nCategories=System;Monitor;\nStartupNotify=true\n')
binary=pathlib.Path.home()/'.local/bin/reset-explode-linux'
binary.parent.mkdir(parents=True,exist_ok=True)
if binary.is_symlink():
    binary.unlink()
binary.write_text('#!/bin/sh\nexec '+shlex.quote(str(root/'run.sh'))+' "$@"\n')
binary.chmod(0o755)
alias=pathlib.Path.home()/'.local/bin/rbt-diagnostico-linux'
alias.write_text(binary.read_text())
alias.chmod(0o755)
print('Atalho instalado:',p)
print('Novo comando:',alias)
print('Comando instalado:',binary)
print('Abra pelo menu RBT Diagnóstico Linux ou execute ~/.local/bin/reset-explode-linux')
print('O comando reset-explode-linux estará disponível quando ~/.local/bin estiver no PATH (abra uma nova sessão se necessário).')
PY
