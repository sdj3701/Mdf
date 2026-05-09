## mp-editor-reroll-command-parity: Editor mp_command must run only on a host Play Mode peer

Status: active
Pinned: false
Category: unity-cli
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: Phase 4 hardening, Editor-side command parity
Applies to: `unity-cli --project Mdfproject mp_command --command reroll_shop --player_id <id>`
Verified by: see Verification section below; migrated from old Status: verified-compile
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Editor-side `mp_command` should match the build-side `/command` validation path without allowing accidental command execution from Edit Mode or a client peer.

Recipe:
- Register `mp_command` parameters as `command` and `player_id`.
- Support only `reroll_shop` until broader gameplay commands are explicitly added.
- Before queueing `RerollShopCommand`, require Editor Play Mode, `GameManagers` runner running, server/host peer, `GameManagers` State Authority, `CommandProcessor`, valid `playerId`, `ShopManager`, shop database readiness, and enough gold for the reroll cost.
- Return the same practical JSON shape as build-side `/command`: `success`, `message`, `timestampUtc`, and either `data` or `error.code/error.details`.
- Do not run `mp_command` as verification unless the Editor is already in a valid host Play Mode state.

Verification:
- `unity-cli --project Mdfproject editor refresh --compile` completed compilation.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject list` showed `mp_command` with `command` and `player_id` parameters.
- Editor state probe reported `isPlaying=False`, `hasGameManagers=False`, so live command execution was skipped as `NEEDS_ENVIRONMENT`.
