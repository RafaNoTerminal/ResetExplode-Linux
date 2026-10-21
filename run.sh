#!/bin/sh
set -eu
APP_ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
export PYTHONPATH="$APP_ROOT/src${PYTHONPATH:+:$PYTHONPATH}"
exec /usr/bin/python3 -m reset_explode_linux.app "$@"
