## addressables-lifecycle-bundle-boundaries-v1: Keep loads owned and bundles evidence-based

Status: active
Pinned: false
Category: unity, performance
Created: 2026-07-13
Last used: 2026-07-13
Last verified: 2026-07-13
Use count: 1
Review after: 2026-08-03
Triggers: Addressables, AssetLoader, single-flight, bundle size, PackSeparately, Fusion prefab
Applies to: MDF Addressables startup, runtime leases, category groups, Development player builds
Verified by: `artifacts/builds/20260713-performance-memory-final-v2-win64/build-metadata.json`; `artifacts/mp/20260712-234101-matrix`; `artifacts/mp/20260712-234205-matrix`; `artifacts/mp/20260712-234340-matrix`; `artifacts/mp/20260712-234448-matrix`
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Boot-time locator scans and broad prewarm increase startup memory. Unowned handles leak, concurrent requests can duplicate loads, and indiscriminate `PackSeparately` can duplicate large shared dependencies. Removing a scene dependency from Addressables can also break Fusion when `SpawnAsync` resolves the prefab by GUID.

Recipe:
- Initialize only the Addressables catalog at boot. Load match content on demand.
- Use a typed address/type cache with explicit owner leases. Coalesce pending requests through a multi-subscriber completion source and release the handle after the final lease.
- Do not use one pending `UniTask` or `Preserve()` as the concurrent fan-out primitive; this UniTask version can throw `Already continuation registered` for simultaneous awaiters.
- Split groups by update cadence and ownership first. Measure both maximum bundle size and total bytes before selecting packing mode.
- Keep Unit, Monster, Portrait, and Projectile entries `PackSeparately` when their measured dependency duplication is small. Keep the two VFX groups `PackTogether` when shared VFX dependencies make separate bundles materially larger.
- A direct scene reference is not automatically safe to remove from Addressables. Keep prefabs such as `Player_Root` and `GameManagers` registered when Fusion or an `AssetReference` resolves them by GUID.
- Build Addressables for the requested player target and run both three-direction smoke and a real battle profile.

Verification:
- Final Windows content: 45 bundles, 160,643,025 bytes total (153.20 MiB), 38,393,076-byte maximum (36.61 MiB).
- The maximum bundle fell from 55.73 MiB to 36.61 MiB while total content grew about 1.8% from the category-split baseline.
- Development build succeeded with Addressables packed mode.
- Editor Host/Build Client, Build Host/Editor Client, and Build Host/Build Client passed with cleanup PASS and no orphaned PIDs.
- Monster spawn command, magic scroll command, and HumanBot battle progression passed with cleanup PASS and no orphaned PIDs.

Pitfalls:
- Verify serialized Fusion prefab GUIDs after deduplicating build-scene dependencies. A missing `GameManagers` entry presents as `InvalidKeyException` followed by `NetworkObjectSpawnException` only after entering `03_Game`.
- Compare total bytes, not only bundle count or largest bundle. Making both VFX groups separate increased Windows Addressables content by 17.4% because of shared dependency duplication.
- A player launch smoke proves startup only. It does not prove the Fusion prefab catalog or lazy VFX paths; use multiplayer and battle cases too.

Lifecycle notes:
- 2026-07-13: Captured after implementing catalog-only boot, lease-backed single-flight loading, category groups, and measured mixed packing modes.
