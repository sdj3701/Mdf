## mp-host-migration-probe: Kill the host process, then prove Fusion callback/token/resume

Status: active
Pinned: true
Category: host-migration, security
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: Host Migration feasibility, process kill, `HostMigrationToken`
Applies to: `tools/harness/mp/run_host_migration_probe.py`, Host Migration Phase 15
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Host Migration cannot be proven with `/quit` or code inspection. The surviving peer must show Fusion callback, non-null token, new runner start, `HostMigrationResume`, and recovery completion.

Recipe:
- Build a Development player after any Host Migration instrumentation changes.
- Launch build host and build client with `--mpTest`.
- Wait for both peers to reach `Game`.
- Kill only the host process with `process.kill`.
- Poll the surviving client's `/dumpState` until `hostMigration.completeCount > 0` or timeout.
- Require `onHostMigrationCount > 0`, `nonNullTokenCount > 0`, `startGameSuccessCount > 0`, `resumeCount > 0`, `recoverySucceeded=true`, and `failureCount=0`.

Verification:
- `artifacts/mp/20260504-210930-host-migration-feasibility/host-migration-result.json` reported `hostWasKilled=true`, `EnableAutoUpdate=true`, `onHostMigrationCount=1`, `nonNullTokenCount=4`, `resumeCount=2`, `startGameSuccessCount=1`, `completeCount=1`, `recoverySucceeded=true`, and `failures=[]`.

Pitfalls:
- The callback proof is only feasibility. It does not prove durable player control or gameplay state preservation.
