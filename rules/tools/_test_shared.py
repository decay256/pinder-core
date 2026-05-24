#!/usr/bin/env python3
import os
import re
import subprocess
import sys
import tempfile
from collections import Counter
from typing import Any, Dict, List, Optional, Set

import pytest
import yaml

# Path setup
TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS_DIR)

import _extract as extract
import _generate as generate
from _extract import parse_archetype_blocks, extract_rules
from _generate import generate_archetype_definition, generate_table, generate_markdown, render_blocks, rule_to_markdown
from _enrich import enrich_rules_v3
from _accuracy_check import check_file
from _md_to_yaml import md_to_rules, _slugify, _parse_table as md_parse_table
from _yaml_to_md import yaml_to_md
from _round_trip_check import main as round_trip_main

# Design docs live outside the repo in the agent workspace
DESIGN_DIR = os.environ.get(
    'PINDER_DESIGN_DIR',
    os.path.join(os.path.expanduser('~'), '.openclaw', 'agents-extra', 'pinder', 'design')
)
EXTRACTED_DIR = os.path.join(TOOLS_DIR, '..', 'extracted')

ENRICHED_FILES = [
    'rules-v3-enriched.yaml',
    'risk-reward-and-hidden-depth-enriched.yaml',
    'async-time-enriched.yaml',
    'traps-enriched.yaml',
    'archetypes-enriched.yaml',
    'character-construction-enriched.yaml',
    'items-pool-enriched.yaml',
    'anatomy-parameters-enriched.yaml',
    'extensibility-enriched.yaml',
]

ORIGINAL_BASENAMES = [
    'risk-reward-and-hidden-depth',
    'async-time',
    'traps',
    'archetypes',
    'character-construction',
    'items-pool',
    'anatomy-parameters',
    'extensibility',
]

EXPECTED_DOCS = [
    'systems/rules-v3.md',
    'systems/risk-reward-and-hidden-depth.md',
    'systems/async-time.md',
    'settings/archetypes.md',
    'settings/character-construction.md',
    'settings/anatomy-parameters.md',
    'settings/items-pool.md',
    'settings/traps.md',
    'settings/extensibility.md',
]

CONDITION_KEYS_SPEC = {
    'miss_range', 'beat_range', 'interest_range', 'need_range', 'level_range',
    'timing_range', 'natural_roll', 'miss_minimum', 'streak', 'streak_minimum',
    'action', 'stat', 'failed_stat', 'shadow', 'threshold', 'conversation_start',
    'formula', 'shadow_points_per_penalty', 'dc', 'time_of_day', 'energy_below',
    'trap_active', 'combo_sequence', 'callback_distance',
}

OUTCOME_KEYS_SPEC = {
    'interest_delta', 'interest_bonus', 'roll_bonus', 'roll_modifier',
    'dc_adjustment', 'xp_multiplier', 'xp_payout', 'risk_tier', 'tier',
    'effect', 'trap', 'trap_name', 'shadow', 'shadow_delta', 'shadow_effect',
    'stat_penalty_per_step', 'level_bonus', 'base_dc', 'addend',
    'starting_interest', 'ghost_chance_percent', 'duration_turns',
    'on_fail_interest_delta', 'modifier', 'energy_cost', 'horniness_modifier',
    'delay_penalty', 'stat_modifier', 'stat_modifiers', 'quality_boost',
}

RANGE_KEYS = {
    'miss_range', 'beat_range', 'interest_range', 'need_range', 'level_range',
    'timing_range',
}

ORIGINAL_FIELDS = {
    'id', 'section', 'title', 'type', 'description', 'table_rows',
    'code_examples', 'designer_notes', 'flavor', 'heading_level',
    'unstructured_prose', 'related_rules', 'examples',
}

def _load(fname: str) -> list:
    path = os.path.join(EXTRACTED_DIR, fname)
    with open(path, 'r') as f:
        return yaml.safe_load(f)

def _find(entries: list, entry_id: str) -> Optional[dict]:
    for e in entries:
        if e.get('id') == entry_id:
            return e
    return None

def _find_prefix(entries: list, prefix: str) -> list:
    return [e for e in entries if e.get('id', '').startswith(prefix)]

def _all_enriched(entries: list) -> list:
    return [e for e in entries if e.get('condition') or e.get('outcome')]

def _get_design_docs():
    docs = []
    for subdir in ['systems', 'settings']:
        path = os.path.join(DESIGN_DIR, subdir)
        if os.path.isdir(path):
            for f in sorted(os.listdir(path)):
                if f.endswith('.md'):
                    docs.append(os.path.join(path, f))
    return docs

def _roundtrip(md_text):
    """Run markdown through extract → YAML → generate and return result + rules."""
    with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
        f.write(md_text)
        f.flush()
        tmp_path = f.name
    try:
        rules = extract.extract_rules(tmp_path)
    finally:
        os.unlink(tmp_path)
    return generate.generate_markdown(rules), rules

def _extract_from_text(md_text):
    with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
        f.write(md_text)
        f.flush()
        tmp_path = f.name
    try:
        rules = extract.extract_rules(tmp_path)
    finally:
        os.unlink(tmp_path)
    return rules

def _normalize_value(v):
    """Normalize a value for semantic comparison."""
    if isinstance(v, str):
        # Collapse whitespace, strip quote artifacts
        return ' '.join(v.split()).strip().strip("'\"")
    if isinstance(v, list):
        return [_normalize_value(x) for x in v]
    if isinstance(v, dict):
        return {k: _normalize_value(val) for k, val in sorted(v.items())}
    return v

def _find_semantic_losses(original, roundtripped, path=""):
    """Recursively find fields where content was lost."""
    losses = []
    if isinstance(original, dict):
        for key, orig_val in original.items():
            if key not in roundtripped:
                losses.append(f"MISSING key '{path}.{key}'")
            else:
                losses.extend(_find_semantic_losses(
                    orig_val, roundtripped[key], f"{path}.{key}"
                ))
    elif isinstance(original, list) and isinstance(roundtripped, list):
        for i, (o, r) in enumerate(zip(original, roundtripped)):
            losses.extend(_find_semantic_losses(o, r, f"{path}[{i}]"))
        # Extra items in original that roundtripped lost
        for i in range(len(roundtripped), len(original)):
            losses.append(f"MISSING list item '{path}[{i}]'")
    elif isinstance(original, str):
        norm_orig = _normalize_value(original)
        norm_rt = _normalize_value(roundtripped) if isinstance(roundtripped, str) else ""
        if norm_orig and norm_rt and norm_orig != norm_rt:
            losses.append(
                f"VALUE CHANGED at {path}: '{norm_orig[:60]}' → '{norm_rt[:60]}'"
            )
    return losses

def _is_acceptable_loss(loss_msg: str) -> bool:
    """Filter out known acceptable differences."""
    # sep_cells — formatting-only separator rows
    if '.sep_cells' in loss_msg:
        return True
    # heading_level — integer, can be regenerated from context
    if '.heading_level' in loss_msg:
        return True
    # Empty string values
    if loss_msg.startswith("VALUE CHANGED") and "'' →" in loss_msg:
        return True
    if loss_msg.startswith("VALUE CHANGED") and "→ ''" in loss_msg:
        return True
    # description field when content moved into blocks
    if '.description' in loss_msg and 'MISSING' not in loss_msg:
        return True
    # Enrichment-only fields that md_to_yaml cannot reconstruct
    enrichment_only = {
        '.condition', '.outcome', '.type', '.formula',
        '.section', '.compact_heading', '.tier',
        '.stats', '.shadows', '.level_range', '.behavior', '.interference',
    }
    for field in enrichment_only:
        if field in loss_msg:
            return True
    return False

# Explicitly export everything (including underscored names and TOOLS_DIR) for wildcard imports
__all__ = [
    'extract', 'generate', 'parse_archetype_blocks', 'extract_rules',
    'generate_archetype_definition', 'generate_table', 'generate_markdown',
    'render_blocks', 'rule_to_markdown', 'enrich_rules_v3', 'check_file',
    'md_to_rules', '_slugify', 'md_parse_table', 'yaml_to_md', 'round_trip_main',
    'DESIGN_DIR', 'EXTRACTED_DIR', 'ENRICHED_FILES', 'ORIGINAL_BASENAMES',
    'EXPECTED_DOCS', 'CONDITION_KEYS_SPEC', 'OUTCOME_KEYS_SPEC', 'RANGE_KEYS',
    'ORIGINAL_FIELDS', '_load', '_find', '_find_prefix', '_all_enriched',
    '_get_design_docs', '_roundtrip', '_extract_from_text', '_normalize_value',
    '_find_semantic_losses', '_is_acceptable_loss', 'TOOLS_DIR'
]
