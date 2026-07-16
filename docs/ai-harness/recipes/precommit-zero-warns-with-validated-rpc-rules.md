## precommit-zero-warns-with-validated-rpc-rules: Reduce WARNs without hiding authority checks

Status: active
Pinned: true
Category: cleanup
Created: 2026-05-06
Last used: 2026-07-12
Last verified: 2026-05-06
Use count: 3
Review after: 2026-08-04
Triggers: `client_trust`, `rpc_all`, `rpc_persistent_state`, `tick_debug_log`, warning cleanup
Applies to: `tools/harness/precommit.py`, command/RPC/tick-log WARN reduction
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
File-wide regular expressions can keep reporting WARNs after authority hardening is already in place. The common false positives were commented-out logs, wire-format deserialization in `CommandProcessor`, guard log text such as "client peer", validated `RpcSources.All` request RPCs with `RpcInfo`, and migration diagnostics inside tick methods.

Recipe:
- Strip comments before warning scans so commented diagnostic logs do not count as active tick/debug or client-trust risks.
- Inspect RPC methods by body instead of file-wide text. Keep warning on `RpcSources.All` unless the method signature includes `RpcInfo` and the body validates `info.Source`, `Object.InputAuthority`, `IsRpcSourceAuthorizedForPlayer`, or a dedicated request validator.
- Skip persistent-state RPC warnings for `RpcSources.StateAuthority` broadcasts and manually validated request RPCs; those are the expected authority-to-peer sync path.
- Inspect only the actual `Update`, `FixedUpdateNetwork`, and `Render` method bodies for direct `Debug.Log` calls. Move intentional diagnostics into helper methods outside tick bodies when the log is bounded and deliberate.
- Avoid client-trust false positives in guard strings by saying `non-authority peer` instead of `client peer`, and avoid `requested` in neutral trace labels such as shop initialization.
- Rename private wire-format arrays in `CommandProcessor.DeserializeCommand` away from `intParams`/`stringParams` when the arrays are already server-validated before broadcast.

Verification:
- `python tools/harness/precommit.py --self-test` passed, including explicit cases that an unvalidated `RpcSources.All` still warns while a source-validated one does not.
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings` after the cleanup.
- `unity-cli --project Mdfproject editor refresh --compile` completed, `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`, and EditMode passed `8/8`.
- Rebuilt Development player at `artifacts/builds/20260505-230726/MDF-MPTest.exe`; launch smoke exited `0`.
- `artifacts/mp/20260505-230828-human-bot-prepare` passed with `SelectAugment`, `accepted_command`, durable selected augment hash delta, and same-player random outcome matches.

Pitfalls:
- Do not replace WARNs with blanket file allowlists. If a warning is suppressed by smarter logic, add or keep a self-test proving the unsafe shape still warns.
- Do not remove Host Migration, command, or VFX diagnostics just to silence the hook; move bounded diagnostics behind helpers only when they remain intentional and verifiable.
