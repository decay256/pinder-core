#!/usr/bin/env python3
"""
Enrichment script for Pinder rules YAML files.

Decomposed and modularized to respect separation of concerns and file size limits.
Reads each extracted YAML file, adds structured condition/outcome fields
to entries that contain numeric thresholds, ranges, or named mechanical effects.
Produces *-enriched.yaml files in rules/extracted/.

Usage:
    python3 rules/tools/_enrich.py
"""

import copy
import os
import sys
from typing import Any, Dict, List, Tuple

# Import shared structures and helpers
from _enrich_shared import load_yaml, save_yaml, parse_stat_modifiers

# Import sub-enrichers
from _enrich_rules_v3_part1 import enrich_rules_v3_part1
from _enrich_rules_v3_part2 import enrich_rules_v3_part2
from _enrich_risk_reward import enrich_risk_reward
from _enrich_async_time import enrich_async_time
from _enrich_traps_archetypes import enrich_traps, enrich_archetypes
from _enrich_character_items import enrich_character_construction, enrich_items_pool
from _enrich_anatomy_extensibility import enrich_anatomy_parameters, enrich_extensibility
from _enrich_validation import validate_vocabulary


def enrich_rules_v3(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich rules-v3.yaml entries by delegating to decomposed part helper modules."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        desc = str(e.get('description', ''))
        blocks = e.get('blocks', [])

        # Try part 1 (Sections 4-6)
        res = enrich_rules_v3_part1(e, eid, desc, blocks)
        if res is not None:
            result.extend(res)
            continue

        # Try part 2 (Sections 7-14)
        res = enrich_rules_v3_part2(e, eid, desc, blocks)
        if res is not None:
            result.extend(res)
            continue

        result.append(e)

    # Inject archetypes into rules-v3
    archetypes_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'extracted', 'archetypes.yaml')
    if not os.path.exists(archetypes_path):
        raise FileNotFoundError(f"Missing {archetypes_path} required for rules-v3 injection")
    
    arch_entries = load_yaml(archetypes_path)
        
    for a in arch_entries:
        if a.get('type') == 'archetype_definition':
            a['section'] = '§3'
            slug = a.get('title', '').lower().replace(' ', '-').replace('the-', '')
            a['id'] = f"archetype.{slug}"
            result.append(a)

    return result


def count_enriched(entries: List[Dict[str, Any]]) -> Tuple[int, int]:
    """Returns (total_entries, enriched_count)."""
    total = len(entries)
    enriched = sum(1 for e in entries if 'condition' in e or 'outcome' in e)
    return total, enriched


ENRICHERS = {
    'rules-v3': enrich_rules_v3,
    'risk-reward-and-hidden-depth': enrich_risk_reward,
    'async-time': enrich_async_time,
    'traps': enrich_traps,
    'archetypes': enrich_archetypes,
    'character-construction': enrich_character_construction,
    'items-pool': enrich_items_pool,
    'anatomy-parameters': enrich_anatomy_parameters,
    'extensibility': enrich_extensibility,
}


def main():
    base_dir = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'extracted')

    summary = []
    all_issues = []
    grand_total = 0
    grand_enriched = 0

    for name, enricher in ENRICHERS.items():
        src_path = os.path.join(base_dir, f'{name}.yaml')
        dst_path = os.path.join(base_dir, f'{name}-enriched.yaml')

        if not os.path.exists(src_path):
            print(f"SKIP: {src_path} not found")
            continue

        entries = load_yaml(src_path)
        enriched_entries = enricher(entries)
        save_yaml(dst_path, enriched_entries)

        total, enriched = count_enriched(enriched_entries)
        grand_total += total
        grand_enriched += enriched
        summary.append((f'{name}-enriched.yaml', total, enriched))

        issues = validate_vocabulary(enriched_entries, name)
        all_issues.extend(issues)

    # Print summary
    print("\nEnrichment Summary")
    print("=" * 60)
    for fname, total, enriched in summary:
        print(f"  {fname:<50} {total:>3} entries, {enriched:>3} enriched")
    print("=" * 60)
    print(f"  Total: {grand_total} entries, {grand_enriched} enriched")
    print()

    if all_issues:
        print(f"Vocabulary issues ({len(all_issues)}):")
        for issue in all_issues:
            print(f"  WARNING: {issue}")
    else:
        print("Vocabulary check: PASS (0 issues)")

    # Write summary to file
    summary_path = os.path.join(base_dir, 'enrichment-summary.txt')
    with open(summary_path, 'w') as f:
        f.write("Enrichment Summary\n")
        f.write("=" * 60 + "\n")
        for fname, total, enriched in summary:
            f.write(f"  {fname:<50} {total:>3} entries, {enriched:>3} enriched\n")
        f.write("=" * 60 + "\n")
        f.write(f"  Total: {grand_total} entries, {grand_enriched} enriched\n")

    return 0 if not all_issues else 1


if __name__ == '__main__':
    sys.exit(main())
