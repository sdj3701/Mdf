## authority-hardening-command-rpc-gate: Validate client commands on the owned PlayerManager

Status: active
Pinned: true
Category: security, battle
Created: 2026-05-05
Last used: 2026-07-12
Last verified: 2026-05-05
Use count: 4
Review after: 2026-08-03
Triggers: precommit `client_trust`, `rpc_all`, `playerref_durable`, Authority Hardening
Applies to: `PlayerManager.RPC_RequestCommandToServer`, `GameManagers.RPC_RequestSpawnMonster`, `NetworkManager.CacheDisconnectedPlayerData`
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Client command requests previously reached server broadcast with only the owned `PlayerManager` RPC source restriction. The legacy `NetworkManager.RPC_RequestCommandToServer` still contained a trust-all validation stub, monster spawn requests accepted client-supplied battle/spawn data without matching the RPC source to the attacker, and disconnect cache could fall back to transient `PlayerRef`.

Recipe:
- Route gameplay client requests through the `PlayerManager` object owned by the caller and require `RpcInfo.Source == Object.InputAuthority`.
- Normalize RPC arrays before use, require the authoritative `playerId`, and reject sync/notify/server-only commands from clients.
- Validate command-specific authority facts before queueing: Prepare/Battle phase, shop database readiness, gold/cost, shop slot state, augment choice, unit/wall ownership, grid bounds, wall stock, and skill unit ownership.
- Treat client `PlaceUnit` as not authority-safe until an authoritative inventory/bench ownership model exists.
- For battle monster spawn RPCs, require source ownership of the attacker, active attacker status, battle defender mapping, field bounds, matching monster pool entry, and boss origin consistency.
- Never use `PlayerRef` as a durable disconnect/reconnect cache key; skip caching when the durable connection token is missing.
- Keep gameplay readiness strict. If the harness needs to recognize a peer as snapshot-ready after late or duplicate initialization, compute that in `MPTestStateSnapshot`; do not broaden `PlayerManager.IsReadyForPlayerActions`, because Host Migration flow uses it as a resume gate.

Verification:
- `python tools/harness/precommit.py --all` reports `0 errors, 11 warnings` after hardening, down from the previous `0 errors, 17 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed and `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed 8/8.
- Rebuilt Development player at `artifacts/builds/20260505-141419/MDF-MPTest.exe`.
- `artifacts/mp/20260505-142027-durable-command-soak/durable-command-report.json` passed 10/10 `reroll_shop` iterations.
- `artifacts/mp/20260505-141541-matrix/matrix-summary.json` passed all default matrix cases.
- `artifacts/mp/20260505-142056-host-migration-feasibility` and `artifacts/mp/20260505-141502-host-migration-e2e` passed after the token-cache hardening and snapshot readiness separation.

Pitfalls:
- Remaining precommit WARNs are heuristics or broader debt, not BLOCK errors. Keep `CommandProcessor` and command-level validation warnings until broader server-authoritative command coverage is added.
