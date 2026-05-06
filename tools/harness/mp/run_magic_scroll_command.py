#!/usr/bin/env python3
from __future__ import annotations

import argparse

from battle_progression_common import add_common_args, run_battle_case


def main() -> int:
    parser = argparse.ArgumentParser()
    add_common_args(parser)
    parser.set_defaults(seed=6201)
    args = parser.parse_args()
    args.prefer_scroll_augment = True
    return run_battle_case(
        args,
        case_name="magic-scroll-command",
        require_spawn=False,
        require_scroll=True,
        require_any_battle_command=True,
    )


if __name__ == "__main__":
    raise SystemExit(main())
