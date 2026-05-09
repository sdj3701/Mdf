#!/usr/bin/env bash
set -euo pipefail
ROOT="$(git rev-parse --show-toplevel)"
python "$ROOT/tools/harness/install_git_hooks.py" "$@"
