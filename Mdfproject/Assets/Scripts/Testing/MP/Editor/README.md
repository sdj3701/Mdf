# MDF MP Test Editor Tools

Put Editor-only `[UnityCliTool]` custom commands here.

Planned files:

- `MPTestUnityCliTools.cs`
- `BuildAutomation.cs`

Required tools:

- `mp_start_host`
- `mp_join_client`
- `mp_load_game`
- `mp_dump_state`
- `mp_assert_state`
- `mp_command`
- `mp_screenshot`
- `mp_stop`
- `mp_build_player`

Rules:

- This folder must remain Editor-only.
- Verify with `unity-cli --project Mdfproject list` after compile.
- If a tool is not implemented, return explicit JSON failure instead of fake success.
