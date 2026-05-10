# MDF Context Packer

Root is kept clean: only `MDF_PACK_CONTEXT.bat` is placed at the project root. All helper files and generated bundles live in `_context_packer/`.

The default `scripts-plus-context` profile includes the required root launchers (`SETUP_MDF_HARNESS.bat`, `MDF_PACK_CONTEXT.bat`) and `.gitignore`. Generated context outputs remain excluded.

## Default

Double-click:

```bat
MDF_PACK_CONTEXT.bat
```

This creates the recommended `scripts-plus-context` bundle in:

```text
_context_packer/output/
```

## Other profiles

From cmd.exe or PowerShell:

```bat
MDF_PACK_CONTEXT.bat --profile scripts
MDF_PACK_CONTEXT.bat --profile trimmed-project
MDF_PACK_CONTEXT.bat --profile harness-only
MDF_PACK_CONTEXT.bat --profile failure-artifacts --artifact-path artifacts\mp\case-folder
MDF_PACK_CONTEXT.bat --list-profiles
MDF_PACK_CONTEXT.bat --dry-run
MDF_PACK_CONTEXT.bat --menu
```

## Notes

- Requires Python 3 on PATH, or the Windows `py` launcher.
- Files that look sensitive are excluded by default.
- Unity cache/build folders are excluded by default.
- Included Unity `.meta` sidecar files are added automatically when present.
