## mp-client-permanent-wall-sync-race: Queue and fallback permanent wall RPCs

Status: active
Pinned: false
Category: harness
Created: 2026-05-05
Last used: 2026-07-13
Last verified: 2026-07-13
Use count: 3
Review after: 2026-08-03
Triggers: matrix-only `build-host-editor-client` wallHash mismatch, `state_ready_timeout`, client permanentWallCount 0/1
Applies to: `PlayerManager.RPC_ApplyPermanentWalls`, `FieldManager.ApplyPermanentWallsFromServer`, `run_matrix.py`
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260713-041007-permanent-wall-placement/result.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
`build-host-editor-client` could pass alone but fail when run after `editor-host-build-client` in the matrix. The build host broadcast permanent wall coordinates while the Editor client was still initializing, and `RPC_ApplyPermanentWalls` dropped the message when `fieldManager` was null. In another timing path, the client had authoritative wall cells but never matched enough network wall objects, leaving `field.ready=false` and `wallHash=unknown`. A later standalone failure showed a stricter variant where the Editor client missed the one-time permanent wall coordinate RPC entirely and stayed at `permanentWallCount` 0/1 until timeout.

Recipe:
- Queue `RPC_ApplyPermanentWalls` payloads when `fieldManager` is not ready and drain them after `Rpc_InitializePlayer` rebinds runtime references.
- On the State Authority, rebuild a deterministic permanent wall coordinate payload from authoritative cells and rebroadcast it for a short window after `Rpc_InitializePlayer`; one-shot RPC state is not enough for late or slow peers.
- For clients only, if network permanent wall objects do not arrive by the sync timeout, create local non-authoritative placeholder walls from the authoritative cell list and rebuild wall maps.
- Keep server generation authoritative; the fallback is only a client recovery path for missed/delayed network wall visuals/maps.
- In `run_matrix.py`, clean the Editor between cases with `mp_stop`, `editor stop`, `unity-cli status`, and a short inter-case delay.

Verification:
- Before the fix, `artifacts/mp/20260505-120203-matrix` and `artifacts/mp/20260505-122021-matrix` failed only `build-host-editor-client` with `state_ready_timeout` and `player.0.field.wallHash` mismatch.
- `artifacts/mp/20260505-122826-matrix/matrix-summary.json` passed all matrix cases: Editor Host + Build Client, Build Host + Editor Client, Build Host + Build Client, AI fill, disconnect AI takeover, same-token reconnect, and 4-player smoke.
- `artifacts/mp/20260505-125052-build-host-editor-client` reproduced the standalone miss with `state_ready_timeout` and `snapshot_mismatch`.
- `artifacts/mp/20260505-125705-build-host-editor-client` passed after adding the authority rebroadcast payload and rebuilding the Development player at `artifacts/builds/20260505-125616/MDF-MPTest.exe`.

Lifecycle notes:
- 2026-07-13: verified artifact `artifacts/mp/20260713-041007-permanent-wall-placement/result.json`
