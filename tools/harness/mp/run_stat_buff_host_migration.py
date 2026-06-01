#!/usr/bin/env python3
from __future__ import annotations

from battle_progression_common import add_common_args, run_battle_case


def main() -> int:
    import argparse

    parser = argparse.ArgumentParser(description="Apply an active stat buff at a battle checkpoint, then verify it survives Host Migration.")
    add_common_args(parser)
    args = parser.parse_args()
    return run_battle_case(
        args,
        case_name="stat-buff-host-migration",
        require_spawn=True,
        require_scroll=False,
        require_any_battle_command=True,
        migrate_after_battle=True,
        apply_stat_buff_before_migration=True,
    )


if __name__ == "__main__":
    raise SystemExit(main())
