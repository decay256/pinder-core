#!/usr/bin/env python3
"""Convert rules-v3-enriched.yaml → rules-v3.md.

Thin wrapper around generate.py's generate_markdown(). Can be used
standalone or imported by round_trip_check.py.

Usage:
    python3 rules/tools/yaml_to_md.py [input.yaml] [output.md]

Defaults:
    input  = rules/extracted/rules-v3-enriched.yaml
    output = stdout (pass - or omit)
"""
import sys
import yaml
from pathlib import Path

# Allow importing generate.py from same directory
sys.path.insert(0, str(Path(__file__).parent))
from generate import generate_markdown


def yaml_to_md(yaml_path: str) -> str:
    """Load YAML rules and return generated Markdown string."""
    with open(yaml_path, 'r', encoding='utf-8') as f:
        rules = yaml.safe_load(f)
    if not rules:
        raise ValueError(f"No rules found in {yaml_path}")
    return generate_markdown(rules)


def main():
    root = Path(__file__).parent.parent.parent
    yaml_path = sys.argv[1] if len(sys.argv) > 1 else str(
        root / 'rules' / 'extracted' / 'rules-v3-enriched.yaml')
    out_path = sys.argv[2] if len(sys.argv) > 2 else None

    md = yaml_to_md(yaml_path)

    if out_path and out_path != '-':
        Path(out_path).write_text(md + '\n', encoding='utf-8')
        print(f"Written: {out_path}", file=sys.stderr)
    else:
        print(md)


if __name__ == '__main__':
    main()
