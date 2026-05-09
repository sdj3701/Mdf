## windows-firewall-mptest-prompt-v1: Suppress repeated MDF-MPTest firewall prompts safely

Status: active
Pinned: true
Category: security, multiplayer
Created: 2026-05-09
Last used: 2026-05-09
Last verified: 2026-05-09
Use count: 1
Review after: 2026-08-07
Triggers: Windows Defender Firewall prompt, MDF-MPTest.exe, graphical MP test, repeated timestamped player builds
Applies to: `tools/harness/mp/configure_windows_firewall.py`, `tools/harness/mp/build_player.py`, Windows graphical MP E2E
Verified by: `python -m py_compile tools/harness/mp/configure_windows_firewall.py`; `python tools/harness/mp/configure_windows_firewall.py --player-path artifacts/builds/mptest-current/MDF-MPTest.exe --allow-missing-player`; `python tools/harness/mp/configure_windows_firewall.py --help`
Replacement: none
Archive policy: keep active while Windows graphical MP tests use `MDF-MPTest.exe` and Defender Firewall prompts are possible

Recipe:
- Windows Firewall prompts are path-based for the player exe. Timestamped builds under `artifacts/builds/<timestamp>/MDF-MPTest.exe` can look like a new app every time.
- For repeated local visual checks, build to a stable ignored path such as `artifacts/builds/mptest-current` and pass that exact `--player-path` to MP runners.
- Use `tools/harness/mp/configure_windows_firewall.py` from an elevated Administrator shell to create a rule for that stable exe.
- The helper defaults to `action=block`, which suppresses the inbound prompt without opening public inbound access. Use `--action allow --profile private` only when an explicit LAN/direct-connect test needs inbound traffic.
