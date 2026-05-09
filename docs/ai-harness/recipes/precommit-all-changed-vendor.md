## precommit-all-changed-vendor: Check modified vendor paths without scanning all vendor files

Status: active
Pinned: false
Category: harness
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: precommit, vendor, Photon, TMP, Toon Shader
Applies to: tools/harness/precommit.py --all
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
`--all` should not scan every existing vendor file, but it still must catch modified protected vendor paths.

Recipe:
- Exclude vendor folders during normal full-tree scanning.
- Add changed vendor files from `git status --porcelain` back into the check set so edits are blocked.

Verification:
- `python tools/harness/precommit.py --self-test` includes a vendor edit path case.
- `python tools/harness/precommit.py --all` completed with `0 errors`.

Pitfalls:
- Fully scanning vendor folders can create noisy warnings from third-party sample code.
