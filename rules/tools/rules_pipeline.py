#!/usr/bin/env python3
"""Pinder rules pipeline — single entry point for all rules operations.

Commands:
  md-to-yaml    Convert rules-v3.md → rules-v3-enriched.yaml
  yaml-to-md    Convert rules-v3-enriched.yaml → rules-v3.md
  check         Run round-trip verification and report diff count
  check-diff    Run round-trip + LLM classification (FORMATTING_ONLY / CONTENT_LOSS)
  enrich        Run enrichment pass on extracted YAML
  extract       Extract YAML from raw Markdown
  game-def      Generate game-definition.md from game-definition.yaml
  test          Run all pipeline tests

Usage:
  python3 rules/tools/rules_pipeline.py check
  python3 rules/tools/rules_pipeline.py yaml-to-md
  python3 rules/tools/rules_pipeline.py test
"""

import sys
import os
from pathlib import Path

TOOLS_DIR = Path(__file__).parent
sys.path.insert(0, str(TOOLS_DIR))


def cmd_extract():
    from _extract import main as _main
    _main()


def cmd_enrich():
    from _enrich import main as _main
    _main()


def cmd_yaml_to_md():
    from _yaml_to_md import main as _main
    _main()


def cmd_md_to_yaml():
    from _md_to_yaml import main as _main
    _main()


def cmd_check():
    from _round_trip_check import main as _main
    sys.exit(_main())


def cmd_check_diff():
    from _check_diff import main as _main
    sys.exit(_main())


def cmd_game_def():
    from _game_definition import main as _main
    _main()


def cmd_test():
    import subprocess
    test_file = TOOLS_DIR / 'test_rules_pipeline.py'
    result = subprocess.run(
        [sys.executable, '-m', 'pytest', str(test_file), '-v'],
        cwd=str(TOOLS_DIR),
    )
    sys.exit(result.returncode)


COMMANDS = {
    'extract': cmd_extract,
    'enrich': cmd_enrich,
    'yaml-to-md': cmd_yaml_to_md,
    'md-to-yaml': cmd_md_to_yaml,
    'check': cmd_check,
    'check-diff': cmd_check_diff,
    'game-def': cmd_game_def,
    'test': cmd_test,
}


def main():
    if len(sys.argv) < 2 or sys.argv[1] in ('-h', '--help'):
        print(__doc__.strip())
        print(f"\nAvailable commands: {', '.join(sorted(COMMANDS))}")
        sys.exit(0 if len(sys.argv) >= 2 else 1)

    command = sys.argv[1]
    # Remove the command from argv so downstream scripts see their own args
    sys.argv = [sys.argv[0]] + sys.argv[2:]

    if command not in COMMANDS:
        print(f"Unknown command: {command}", file=sys.stderr)
        print(f"Available: {', '.join(sorted(COMMANDS))}", file=sys.stderr)
        sys.exit(1)

    COMMANDS[command]()


if __name__ == '__main__':
    main()
