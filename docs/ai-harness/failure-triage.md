# MDF Harness Failure Triage

## First split

1. Compile/console failure.
2. Editor control failure.
3. Build launch failure.
4. Automation server failure.
5. Fusion session/join failure.
6. Scene/GameManagers failure.
7. Snapshot mismatch.
8. Visual/UI-only issue.
9. Host Migration/reconnect issue.

## Required failure artifact

Every failed E2E case must produce:

- command transcript
- host/client stdout/stderr
- `[MPTEST]` timeline
- latest Unity console errors
- state snapshots from every responsive peer
- screenshot if available
- `failure-summary.md`

## Debug order

- Read `failure-summary.md`.
- Check Unity console errors.
- Check `[MPTEST]` first failing phase.
- Compare pre/post snapshots.
- Check authority mismatch: `playerId`, StateAuthority, InputAuthority, `PlayerRef`.
- Check whether failure is deterministic by re-running the same case once.

## Do not

- Do not claim PASS with missing artifacts.
- Do not hide console errors as unrelated unless a verifier agrees.
- Do not rewrite giant files to fix a small failure.
- Do not treat Host Migration as passed unless migration callbacks/resume are visible.
