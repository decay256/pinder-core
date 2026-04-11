#!/usr/bin/env python3
"""Round-trip verification: YAML → MD → YAML.

1. Load rules-v3-enriched.yaml
2. Generate Markdown via yaml_to_md
3. Parse the generated MD back to YAML via md_to_yaml
4. Compare original vs round-tripped YAML (structural fields only)
5. Report differences

Usage:
    python3 rules/tools/rules_pipeline.py check
"""
import sys
import yaml
import difflib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _yaml_to_md import yaml_to_md
from _md_to_yaml import md_to_rules


# Fields that only exist in the enriched YAML and cannot be recovered from MD
ENRICHMENT_ONLY_FIELDS = {
    'type', 'formula', 'condition', 'outcome', 'trigger_condition',
    'applies_to', 'tier', 'overlay_tiers', 'time_bands',
    # Archetype-specific enrichment fields
    'stats', 'shadows', 'level_range', 'behavior', 'interference', 'has_hr',
}

# Fields whose values differ due to inference vs enrichment, not content
INFERRED_FIELDS = {'id', 'section'}

SKIP_FIELDS = ENRICHMENT_ONLY_FIELDS | INFERRED_FIELDS


def clean_rule(r: dict) -> dict:
    """Strip non-content fields for comparison."""
    clean = {k: v for k, v in r.items() if k not in SKIP_FIELDS}
    if clean.get('blocks') == []:
        del clean['blocks']
    return clean


def to_yaml(obj) -> str:
    return yaml.dump(obj, default_flow_style=False, allow_unicode=True,
                     sort_keys=True, width=200)


def run_check(yaml_path: str | None = None) -> tuple[int, list[tuple], int]:
    """Run round-trip check. Returns (total_diff_lines, diffs_found, missing_count).
    
    diffs_found is a list of (title, diff_lines_list, changed_count).
    """
    root = Path(__file__).parent.parent.parent
    if yaml_path is None:
        yaml_path = str(root / 'rules' / 'extracted' / 'rules-v3-enriched.yaml')

    with open(yaml_path, 'r', encoding='utf-8') as f:
        original_rules = yaml.safe_load(f)

    heading_rules = [r for r in original_rules if 'heading_level' in r]

    md_text = yaml_to_md(yaml_path)
    round_tripped = md_to_rules(md_text)

    orig_by_title = {r['title']: r for r in heading_rules}
    rt_by_title = {r['title']: r for r in round_tripped}

    orig_titles = set(orig_by_title)
    rt_titles = set(rt_by_title)
    missing = orig_titles - rt_titles
    matched = orig_titles & rt_titles

    archetype_titles = {r['title'] for r in heading_rules if r.get('type') == 'archetype_definition'}

    total_diff_lines = 0
    diffs_found = []

    for title in sorted(matched):
        if title in archetype_titles:
            continue

        o = clean_rule(orig_by_title[title])
        r = clean_rule(rt_by_title[title])
        oy = to_yaml([o])
        ry = to_yaml([r])
        d = list(difflib.unified_diff(
            oy.splitlines(keepends=True), ry.splitlines(keepends=True),
            fromfile='orig', tofile='rt', n=1))
        changed = [l for l in d
                   if (l.startswith('+') or l.startswith('-'))
                   and not l.startswith('+++') and not l.startswith('---')]
        if changed:
            total_diff_lines += len(changed)
            diffs_found.append((title, d, len(changed)))

    return total_diff_lines, diffs_found, len(missing)


def main():
    root = Path(__file__).parent.parent.parent
    yaml_path = sys.argv[1] if len(sys.argv) > 1 else str(
        root / 'rules' / 'extracted' / 'rules-v3-enriched.yaml')

    print(f"=== Round-trip check: {yaml_path} ===")
    print()

    # Step 1: Load original
    with open(yaml_path, 'r', encoding='utf-8') as f:
        original_rules = yaml.safe_load(f)

    heading_rules = [r for r in original_rules if 'heading_level' in r]
    derived_rules = [r for r in original_rules if 'heading_level' not in r]
    print(f"1. Loaded {len(original_rules)} rules ({len(heading_rules)} heading, {len(derived_rules)} enrichment-only)")

    # Step 2: Generate Markdown
    md_text = yaml_to_md(yaml_path)
    print(f"2. Generated Markdown ({md_text.count(chr(10))} lines)")

    # Step 3: Parse back
    round_tripped = md_to_rules(md_text)
    print(f"3. Parsed back to {len(round_tripped)} rules")

    # Step 4: Match by title — compare heading rules
    orig_by_title = {r['title']: r for r in heading_rules}
    rt_by_title = {r['title']: r for r in round_tripped}

    orig_titles = set(orig_by_title)
    rt_titles = set(rt_by_title)
    missing = orig_titles - rt_titles
    extra = rt_titles - orig_titles
    matched = orig_titles & rt_titles

    archetype_titles = {r['title'] for r in heading_rules if r.get('type') == 'archetype_definition'}

    total_diff_lines = 0
    diffs_found = []
    block_mismatches = 0
    archetypes_skipped = 0

    for title in sorted(matched):
        if title in archetype_titles:
            archetypes_skipped += 1
            continue

        o = clean_rule(orig_by_title[title])
        r = clean_rule(rt_by_title[title])
        oy = to_yaml([o])
        ry = to_yaml([r])
        d = list(difflib.unified_diff(
            oy.splitlines(keepends=True), ry.splitlines(keepends=True),
            fromfile='orig', tofile='rt', n=0))
        changed = [l for l in d
                   if (l.startswith('+') or l.startswith('-'))
                   and not l.startswith('+++') and not l.startswith('---')]
        if changed:
            total_diff_lines += len(changed)
            diffs_found.append((title, d, len(changed)))

        ob = len(orig_by_title[title].get('blocks', []))
        rb = len(rt_by_title[title].get('blocks', []))
        if ob != rb:
            block_mismatches += 1

    print(f"4. Matched {len(matched)}/{len(heading_rules)} rules  |  diff lines: {total_diff_lines}  |  archetypes skipped: {archetypes_skipped}")
    print()

    if missing:
        print(f"⚠️  MISSING titles ({len(missing)}):")
        for t in sorted(missing):
            print(f"  - {t}")
        print()

    if extra:
        derived_titles = {r['title'] for r in derived_rules}
        expected_extras = extra & derived_titles
        unexpected_extras = extra - derived_titles
        if expected_extras:
            print(f"ℹ️  {len(expected_extras)} extra rules from enrichment-derived headings (expected)")
        if unexpected_extras:
            print(f"⚠️  {len(unexpected_extras)} unexpected extra titles:")
            for t in sorted(unexpected_extras):
                print(f"  + {t}")
        print()

    if diffs_found:
        print(f"=== RULE DIFFS ({len(diffs_found)} rules) ===")
        for i, (title, d, count) in enumerate(diffs_found):
            if i >= 15:
                print(f"  ... and {len(diffs_found) - i} more rules with diffs")
                break
            print(f"\n  [{title}] ({count} lines):")
            for line in d[:12]:
                print(f"    {line}", end='')
            if len(d) > 12:
                print(f"    ... ({len(d) - 12} more)")
        print()

    if block_mismatches:
        print(f"Block count mismatches (excl. archetypes): {block_mismatches}")
        for title in sorted(matched):
            if title in archetype_titles:
                continue
            ob = len(orig_by_title[title].get('blocks', []))
            rb = len(rt_by_title[title].get('blocks', []))
            if ob != rb:
                print(f"  '{title}': orig={ob}, rt={rb}")
        print()

    print("=== SUMMARY ===")
    print(f"  Heading rules:       {len(heading_rules)}")
    print(f"  Round-tripped total: {len(round_tripped)}")
    print(f"  Enrichment extras:   {len(extra)}")
    print(f"  Missing titles:      {len(missing)}")
    print(f"  Content diff lines:  {total_diff_lines}")
    print(f"  Block mismatches:    {block_mismatches}")

    if total_diff_lines <= 50:
        print(f"\n✅ Round-trip diff is acceptable ({total_diff_lines} content diff lines)")
    else:
        print(f"\n⚠️  Round-trip diff is {total_diff_lines} lines — investigate for content loss")

    return 0 if not missing else 1


if __name__ == '__main__':
    sys.exit(main())
