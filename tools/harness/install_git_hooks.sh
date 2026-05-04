#!/usr/bin/env bash
set -euo pipefail
ROOT="$(git rev-parse --show-toplevel)"
mkdir -p "$ROOT/.git/hooks"
cat > "$ROOT/.git/hooks/pre-commit" <<'HOOK'
#!/usr/bin/env bash
set -euo pipefail
ROOT="$(git rev-parse --show-toplevel)"
python "$ROOT/tools/harness/precommit.py"
HOOK
chmod +x "$ROOT/.git/hooks/pre-commit"
echo "Installed MDF harness pre-commit hook."
