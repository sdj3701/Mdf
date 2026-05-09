#!/usr/bin/env python3
from __future__ import annotations

import argparse

from battle_progression_common import add_common_args, run_battle_client_lifecycle_case


def main() -> int:
    parser = argparse.ArgumentParser()
    add_common_args(parser)
    parser.set_defaults(seed=6105)
    args = parser.parse_args()
    return run_battle_client_lifecycle_case(
        args,
        case_name="progressed-disconnect-after-battle",
        reconnect=False,
    )


if __name__ == "__main__":
    raise SystemExit(main())
