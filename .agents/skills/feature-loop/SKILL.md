---
name: feature-loop
description: Use when implementing or modifying MDF gameplay, UI, networking, AI, commands, or harness features that require compile/build/E2E verification.
---

# MDF Feature Loop

Read `docs/ai-harness/feature-implementation-loop.md`.

For short gameplay/content/AI/UI/network feature requests, use `mdf-content-feature` first. It expands the short request into the MDF content workflow and selects the smallest relevant verification profile.

Required steps:

1. Map real code path before editing.
2. Identify State Authority, command serialization, UI success events, data loading, and snapshot/test impact.
3. Implement minimally.
4. Run `python tools/harness/precommit.py --all`.
5. Run unity-cli status/compile/console.
6. Run relevant EditMode/PlayMode tests.
7. Run the smallest relevant multiplayer E2E matrix for multiplayer-visible changes.
8. Inspect artifacts and fix/retry if needed.
9. Update learned recipes when a reusable method is discovered.

Never claim PASS without command output and artifacts.
