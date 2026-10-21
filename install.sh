#!/bin/sh
set -eu
APP_ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ "$(id -u)" -eq 0 ]; then
    echo 'Execute ./install.sh como seu usuário; sudo será usado somente para instalar as dependências.' >&2
    exit 1
fi
if ! command -v apt-get >/dev/null 2>&1; then
    echo 'Instalação automática disponível para Debian/Ubuntu. Consulte o README para outras distribuições.' >&2
    exit 1
fi
sudo apt-get update
sudo apt-get install -y python3 python3-gi python3-cairo python3-gi-cairo python3-psutil gir1.2-gtk-3.0 pciutils usbutils iproute2 iputils-ping
exec "$APP_ROOT/install-user.sh"
