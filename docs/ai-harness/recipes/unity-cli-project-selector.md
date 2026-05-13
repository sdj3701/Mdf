## unity-cli-project-selector: Always target Mdfproject

Status: active
Pinned: false
Category: unity-cli
Created: 2026-05-05
Last used: 2026-05-13
Last verified: 2026-05-10
Use count: 8
Review after: 2026-08-03
Triggers: unity-cli, status, console, test, build
Applies to: MDF repo layout, unity-cli
Verified by: see Verification section below; migrated from old Status: verified-from-repo; Mdfproject
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
The repository root is not the Unity project root. The Unity project is under `Mdfproject`.

Recipe:
Use `unity-cli --project Mdfproject ...` or an absolute path to `Mdfproject` for all Editor automation.

Verification:
- `ProjectSettings/ProjectVersion.txt` exists under `Mdfproject`.
- `Packages/manifest.json` exists under `Mdfproject`.
- `unity-cli --project Mdfproject status` found the active Editor at `E:/UnityProjects/mdf/Mdfproject`.

Pitfalls:
- Running unity-cli from the repo root without `--project` may target the wrong Editor if multiple projects are open.

Lifecycle notes:
- 2026-05-10: Used for targeted UI compile/test verification with unity-cli --project Mdfproject.
- 2026-05-10: Verified by unity-cli --project Mdfproject status selecting E:/UnityProjects/mdf/Mdfproject on 2026-05-10.
- 2026-05-10: Verified unity-cli --project Mdfproject status selected E:/UnityProjects/mdf/Mdfproject on 2026-05-10.
- 2026-05-10: verified artifact `Mdfproject`
