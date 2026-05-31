#!/usr/bin/env python3
from __future__ import annotations

from battle_progression_common import add_common_args, run_battle_case


def main() -> int:
    import argparse

    parser = argparse.ArgumentParser(
        description="Inject synthetic pending fire/hit load at a frozen battle checkpoint and verify it survives Host Migration."
    )
    add_common_args(parser)
    parser.add_argument("--pending-fire-count", type=int, default=48)
    parser.add_argument("--pending-hit-count", type=int, default=72)
    parser.add_argument("--pending-delay-ticks", type=int, default=3600)
    args = parser.parse_args()
    return run_battle_case(
        args,
        case_name="network-budget-pending-stress",
        require_spawn=True,
        require_scroll=False,
        require_any_battle_command=True,
        migrate_after_battle=True,
        inject_pending_load_before_migration=True,
    )


if __name__ == "__main__":
    raise SystemExit(main())
