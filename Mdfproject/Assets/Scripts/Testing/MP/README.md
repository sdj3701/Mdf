# MDF MP Test Runtime Folder

Codex should create runtime harness C# files here during the phase prompts.

Planned runtime files:

- `MPTestCommandLine.cs` — parse `--mpTest` and other runtime args.
- `MPTestBootstrap.cs` — auto-start host/client test flow and set `Application.runInBackground`.
- `MPTestLogger.cs` — emit structured `[MPTEST]` timeline logs.
- `MPTestStateSnapshot.cs` — collect stable comparable MDF game state.
- `MPTestAssertions.cs` — assert session, scene, player, field, command, AI, and migration invariants.
- `MPTestCommands.cs` — test-only deterministic commands if needed.
- `MPTestAutomationServer.cs` — build-side loopback/token test server; must be gated.
- `MPTestMainThreadDispatcher.cs` — marshal automation endpoint work to Unity main thread.
- `MPTestAutomationClient.cs` — optional local client helper.

Rules:

- Runtime automation activates only with `--mpTest`.
- Server code must be `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.
- Never log raw connection tokens, automation tokens, or Photon AppId.
- Normal production builds must not expose this harness.
