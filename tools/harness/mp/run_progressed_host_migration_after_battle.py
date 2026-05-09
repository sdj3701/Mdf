#!/usr/bin/env python3
from __future__ import annotations

import argparse

from battle_progression_common import add_common_args, run_battle_case


def main() -> int:
    parser = argparse.ArgumentParser()
    add_common_args(parser)
    parser.set_defaults(seed=6103)
    args = parser.parse_args()
    return run_battle_case(
        args,
        case_name="progressed-host-migration-after-battle",
        require_spawn=True,
        require_scroll=False,
        require_any_battle_command=True,
        migrate_after_battle=True,
    )


if __name__ == "__main__":
    raise SystemExit(main())
