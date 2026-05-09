# MDF Developer Onboarding

Use this checklist after cloning MDF on a new machine. The Unity project root is `Mdfproject`, not the repository root.

## Required Tools

- Unity `2021.3.45f1`.
- Python `3.10` or newer on `PATH`.
- The `unity-cli` CLI binary on `PATH`.
- Git with hooks enabled for the clone.

Open `Mdfproject` in Unity before running `unity-cli`. The Unity package connector is restored by Unity Package Manager from `Mdfproject/Packages/manifest.json`; `Mdfproject/Packages/packages-lock.json` must stay committed because it pins the resolved git package hashes, including `com.youngwoocho02.unity-cli-connector`.

The manifest currently references the unity-cli connector by git URL. Do not change it to a tag or commit hash without team approval; if stronger reproducibility is needed, update the manifest and lock file together in a dedicated change.

## Editor Setup

1. Install Unity `2021.3.45f1`.
2. Open the Unity project at `Mdfproject`.
3. Let Unity restore packages and compile.
4. Set Editor Preferences > General > Interaction Mode to `No Throttling` if the option exists.
5. Keep the Editor open while running `unity-cli`.

## Codex Setup

Codex users must trust the repo-local `.codex` config so project hooks load. The hooks are part of the MDF harness contract; without them Codex will miss prompt routing, command policy reminders, knowledge capture, and final report checks.

## Git Hook Setup

Install the local pre-commit hook with either command:

```bash
python tools/harness/install_git_hooks.py
bash tools/harness/install_git_hooks.sh
```

The installed hook runs:

```bash
python tools/harness/precommit.py
```

`.gitignore` should ignore generated Unity/Python/harness outputs, but it must not ignore `Mdfproject/Assets/`, `Mdfproject/ProjectSettings/`, `Mdfproject/Packages/`, or Unity `.meta` files under `Mdfproject/Assets`.

## First Verification

On Windows, when Unity `2021.3.45f1` and `unity-cli` are already installed and `Mdfproject` is open in Unity, run the one-command harness setup from the repo root:

```bat
SETUP_MDF_HARNESS.bat
```

This BAT is path-portable. It uses its own location as the repo root, installs Python 3.10+ with `winget` when Python is missing, installs Git hooks, runs overlay/precommit/Unity verification, builds a stable Development player at `artifacts/builds/mptest-current/MDF-MPTest.exe`, and writes a bootstrap summary under `artifacts/bootstrap/`.

Useful options:

```bat
SETUP_MDF_HARNESS.bat --dry-run
SETUP_MDF_HARNESS.bat --skip-build
SETUP_MDF_HARNESS.bat --no-firewall
```

Firewall setup is applied only when the BAT is run from an Administrator shell. Without Administrator rights, the setup still completes and reports the exact firewall command to run later.

After opening `Mdfproject` in Unity:

```bash
unity-cli --project Mdfproject status
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
unity-cli --project Mdfproject test --mode EditMode
```

To check clone readiness without running heavy multiplayer E2E:

```bash
python tools/harness/bootstrap_dev_env.py --no-unity-if-unavailable
python tools/harness/validate_overlay.py
python tools/harness/precommit.py --self-test
python tools/harness/precommit.py --all
```

## Multiplayer E2E

E2E requires a Development player build. Use existing harness build scripts or matrix profiles after the first verification passes.

Heavy E2E should use `--headless-player` unless screenshots or visual artifacts are required. Do not run endurance or broad profiles just to validate a clone.

On Windows, graphical MP runs can trigger a Windows Defender Firewall prompt for `MDF-MPTest.exe`. For repeated local visual checks, build to a stable ignored path such as `artifacts/builds/mptest-current` and use `tools/harness/mp/configure_windows_firewall.py` from an elevated shell to create a block-inbound rule for that exact exe. This suppresses repeated prompts without allowing public inbound access.
