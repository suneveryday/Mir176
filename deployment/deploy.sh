#!/bin/sh
set -eu
command -v python3 >/dev/null 2>&1 || { echo 'Python 3.9+ is required.' >&2; exit 1; }
task_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec python3 "$task_directory/deploy.py" "$@"
