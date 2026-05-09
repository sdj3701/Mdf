---
name: entropy-gc
description: Scan MDF code for drift, oversized files, stale harness docs, and repeated warnings.
---

# Entropy GC

Check:

- docs reference missing files
- AGENTS.md > 70 lines
- missing learned recipe after repeated command failures
- new giant files or broad rewrites
- stale SimpleRTS references
- recurring WARNs from precommit
- changed command classes without serialization/assertion coverage

Report cleanup candidates; do not rewrite code without approval.
