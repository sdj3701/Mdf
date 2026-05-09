## historical-harness-doc-archive-v1: Keep old phase prompts out of default context

Status: active
Pinned: false
Category: harness
Created: 2026-05-08
Last used: 2026-05-08
Last verified: 2026-05-08
Use count: 1
Review after: 2026-08-06
Triggers: stale phase prompt docs, historical bootstrap prompts, context bundle bloat
Applies to: `docs/ai-harness/archive`, SessionStart, context packer, overlay validation
Verified by: see preserved recipe notes below; migrated from old Status: documented
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Recipe:
- Move obsolete phase prompt docs to `docs/ai-harness/archive/` instead of deleting them when they still explain harness history.
- Active docs should name `content-development-routine.md`, `verification-profile-selector.md`, `feature-implementation-loop.md`, `mp-test-protocol.md`, `state-snapshot-schema.md`, and `learned-recipes.md` as current source-of-truth.
- SessionStart may mention archived full phase prompts only for explicit historical harness-bootstrap or full overlay rebuild audit work.
- Exclude `docs/ai-harness/archive/**` from default context bundles so historical prompts do not compete with current workflow docs.
