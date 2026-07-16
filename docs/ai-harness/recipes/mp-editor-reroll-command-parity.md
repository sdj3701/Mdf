## mp-editor-reroll-command-parity: Editor mp_command must preserve runtime authority paths

Status: active
Pinned: false
Category: unity-cli
Created: 2026-05-05
Last used: 2026-07-13
Last verified: 2026-07-13
Use count: 5
Review after: 2026-08-03
Triggers: Phase 4 hardening, Editor-side command parity
Applies to: `unity-cli --project Mdfproject mp_command --command <reroll_shop|select_king> --player_id <id> [--king_key <canonical-key>]`
Verified by: see Verification section below; migrated from old Status: verified-compile; artifacts/mp/20260713-055637-editor-host-build-client/result.json; artifacts/mp/20260713-063902-editor-host-build-client/result.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Editor-side `mp_command` should match the build-side `/command` validation path without allowing accidental command execution from Edit Mode or bypassing the command's required authority.

Recipe:
- Register `mp_command` parameters as `command`, `player_id`, and command-specific fields such as `king_key`.
- Keep the supported command allow-list explicit; currently it includes `reroll_shop` and `select_king`.
- Before queueing `RerollShopCommand`, require Editor Play Mode, `GameManagers` runner running, server/host peer, `GameManagers` State Authority, `CommandProcessor`, valid `playerId`, `ShopManager`, shop database readiness, and enough gold for the reroll cost.
- For `select_king`, require Editor Play Mode, the JoinLobby scene, an active player slot, an allow-listed canonical key, and the peer's owned `NetworkPlayer`; call `RequestKingSelection` so the production input-authority RPC remains the only mutation path.
- Return the same practical JSON shape as build-side `/command`: `success`, `message`, `timestampUtc`, and either `data` or `error.code/error.details`.
- Do not run `mp_command` as verification unless the Editor is already in a valid host Play Mode state.

Verification:
- `unity-cli --project Mdfproject editor refresh --compile` completed compilation.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject list` showed `mp_command` with `command` and `player_id` parameters.
- Editor state probe reported `isPlaying=False`, `hasGameManagers=False`, so live command execution was skipped as `NEEDS_ENVIRONMENT`.
- The added `king_key` schema and `select_king` route require fresh compile/list/E2E evidence before updating `Last verified`.

Lifecycle notes:
- 2026-07-13: verified artifact `artifacts/mp/20260713-055637-editor-host-build-client/result.json`
- 2026-07-13: verified artifact `artifacts/mp/20260713-063902-editor-host-build-client/result.json`
