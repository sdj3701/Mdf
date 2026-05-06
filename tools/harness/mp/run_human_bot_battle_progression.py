#!/usr/bin/env python3
from __future__ import annotations

import argparse

from battle_progression_common import add_common_args, run_battle_case


def main() -> int:
    parser = argparse.ArgumentParser()
    add_common_args(parser)
    args = parser.parse_args()
    return run_battle_case(
        args,
        case_name="human-bot-battle-progression",
        require_spawn=False,
        require_scroll=False,
        require_any_battle_command=True,
    )


if __name__ == "__main__":
    raise SystemExit(main())
