## clone-portability-bootstrap-v1: Bootstrap a fresh MDF clone before feature work

Status: active
Pinned: false
Category: harness, unity-cli
Created: 2026-05-09
Last used: 2026-05-10
Last verified: 2026-05-09
Use count: 4
Review after: 2026-08-07
Triggers: fresh clone, developer onboarding, unity-cli unavailable, pre-commit hook, packages-lock, bootstrap_dev_env
Applies to: `SETUP_MDF_HARNESS.bat`, `docs/ai-harness/developer-onboarding.md`, `tools/harness/bootstrap_harness_windows.py`, `tools/harness/bootstrap_dev_env.py`, `tools/harness/install_git_hooks.py`, `.gitignore`
Verified by: `python -m py_compile tools/harness/install_git_hooks.py tools/harness/bootstrap_dev_env.py`; `python tools/harness/bootstrap_dev_env.py --no-unity-if-unavailable`; `python tools/harness/install_git_hooks.py --dry-run`; `python tools/harness/validate_overlay.py`; `python tools/harness/precommit.py --self-test`; `python tools/harness/precommit.py --all`; artifacts/bootstrap/20260509-111508-harness-bootstrap/bootstrap-summary.json
Replacement: none
Archive policy: archive only after explicit review when clone bootstrap moves to a different repo tool or onboarding source-of-truth

Recipe:
- Keep clone setup in `developer-onboarding.md`, not in long AGENTS instructions.
- For Windows clones where Unity and unity-cli are installed and `Mdfproject` is open, use `SETUP_MDF_HARNESS.bat` as the one-command bootstrap entrypoint.
- The BAT must derive the repo root from its own path so clone location differences do not matter.
- The BAT may install Python 3.10+ through `winget` when Python is missing, then delegate hook install, validation, Unity checks, and Development player build to `tools/harness/bootstrap_harness_windows.py`.
- The stable player path for repeated local MP setup is `artifacts/builds/mptest-current/MDF-MPTest.exe`; timestamped evidence builds remain available for normal feature verification.
- Use a Python hook installer as the cross-platform path; keep the Bash wrapper as a convenience delegate.
- Bootstrap should check source prerequisites, `packages-lock.json` connector hash, generated-path ignores, overlay validation, and precommit self-test without building players or running heavy E2E.
- Treat missing `unity-cli` or a closed Unity Editor as an exact next action under `--no-unity-if-unavailable`, not as a gameplay failure.

Lifecycle notes:
- 2026-05-09: Review tightened bootstrap portability: missing unity-cli now remains FAIL, git hook path uses git --git-path, and .gitignore checks reject Assets .meta ignore patterns; verified py_compile, bootstrap_dev_env, install_git_hooks dry-run, validate_overlay, precommit self-test, and precommit --all.
- 2026-05-09: Added `SETUP_MDF_HARNESS.bat` and `bootstrap_harness_windows.py` for one-command Windows setup after Unity is open; verified with py_compile, BAT dry-run, helper dry-run, validate_overlay, and precommit.
- 2026-05-09: Rechecked after merge/code drift; BAT dry-run, bootstrap_dev_env, validate_overlay, precommit self-test, and precommit --all pass. Hardened installed hook to choose python/python3/py -3.
- 2026-05-09: verified artifact `artifacts/bootstrap/20260509-111508-harness-bootstrap/bootstrap-summary.json`
- 2026-05-10: Used while tightening hook setup documentation and install_git_hooks dry-run reporting.
