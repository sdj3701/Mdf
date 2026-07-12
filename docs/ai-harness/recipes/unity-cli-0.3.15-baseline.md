## unity-cli-0.3.15-baseline: Verified local CLI syntax

Status: active
Pinned: false
Category: unity-cli
Created: 2026-05-05
Last used: 2026-07-12
Last verified: 2026-05-22
Use count: 16
Review after: 2026-08-03
Triggers: unity-cli, status, list, editor refresh, console, test, screenshot
Applies to: unity-cli v0.3.15, connector 0.3.15, Unity 2021.3.45f1
Verified by: see Verification section below; migrated from old Status: verified-local; Mdfproject; artifacts/singleplayer-breakwall-addressables-latest.log; unity-cli editor refresh --compile, unity-cli console --type error --stacktrace user; artifacts/mp/20260522-021002-matrix/matrix-summary.json; artifacts/mp/20260522-021953-matrix/matrix-summary.json; unity-cli --project Mdfproject editor refresh --compile; unity-cli --project Mdfproject console --type error --stacktrace user
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
`unity-cli list` reports connector tool schema names such as `run_tests`, `refresh_unity`, and `manage_editor`, while the CLI uses shorthand commands such as `test`, `editor refresh`, and `console`.

Recipe:
- Use `unity-cli --project Mdfproject status` to confirm the active Editor and connector version.
- Use `unity-cli --project Mdfproject list` to inspect registered tools and future `mp_*` custom tools.
- Verified syntax:
  - `unity-cli --project Mdfproject editor refresh --compile`
  - `unity-cli --project Mdfproject console --type error --stacktrace user`
  - `unity-cli --project Mdfproject test --mode EditMode`
  - `unity-cli --project Mdfproject test --mode PlayMode`
  - `unity-cli --project Mdfproject screenshot --view game --output_path artifacts/<case>/editor.png`

Verification:
- `unity-cli --help`, `unity-cli editor --help`, `unity-cli test --help`, `unity-cli console --help`, and `unity-cli screenshot --help` all confirmed this syntax.
- `unity-cli --project Mdfproject list` returned built-ins only; before Phase 4 no `mp_*` tools are expected.

Pitfalls:
- v0.3.15 prints an available update to v0.3.18. Do not update mid-verification unless the task explicitly asks for a CLI upgrade.

Lifecycle notes:
- 2026-05-10: Used for editor status, compile, console, and EditMode test syntax during wall remove UI verification.
- 2026-05-10: Verified by unity-cli 0.3.15 status, editor refresh --compile, console --type error, and EditMode class-filter commands completing on 2026-05-10.
- 2026-05-10: Verified unity-cli status, refresh compile, console errors, and EditMode tests on 2026-05-10.
- 2026-05-10: verified artifact `Mdfproject`
- 2026-05-16: verified artifact `artifacts/singleplayer-breakwall-addressables-latest.log`
- 2026-05-19: Verified UI Toolkit cover change with editor refresh compile, console errors [], and MPTestHarnessEditModeTests 77/77.
- 2026-05-19: verified artifact `unity-cli editor refresh --compile`
- 2026-05-19: verified artifact `unity-cli console --type error --stacktrace user`
- 2026-05-19: Verified shop star-based UI image mapping with editor refresh compile and empty Unity error console.
- 2026-05-22: verified artifact `artifacts/mp/20260522-021002-matrix/matrix-summary.json`
- 2026-05-22: verified artifact `artifacts/mp/20260522-021953-matrix/matrix-summary.json`
- 2026-05-22: verified artifact `unity-cli --project Mdfproject editor refresh --compile; unity-cli --project Mdfproject console --type error --stacktrace user`
