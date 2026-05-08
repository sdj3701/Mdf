#!/usr/bin/env python3
from __future__ import annotations

import argparse

from long_lifecycle_common import (
    EVENT_DISCONNECT,
    add_long_lifecycle_args,
    run_long_lifecycle_case,
)


CASE_NAME = "3round-disconnect-ai-takeover"


def main() -> int:
    parser = argparse.ArgumentParser()
    add_long_lifecycle_args(parser, default_seed=8203)
    args = parser.parse_args()
    return run_long_lifecycle_case(args, case_name=CASE_NAME, event_type=EVENT_DISCONNECT)


if __name__ == "__main__":
    raise SystemExit(main())
