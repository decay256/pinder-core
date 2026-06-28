#!/usr/bin/env python3
import sys
import re
from pathlib import Path

def get_context(lines, idx, context_lines=2):
    start = max(0, idx - context_lines)
    end = min(len(lines), idx + context_lines + 1)
    return "\n".join(lines[start:end])

def is_marked_historical(text):
    historical_markers = ['historical', 'archived', 'legacy', 'deprecated', 'removed']
    text_lower = text.lower()
    return any(marker in text_lower for marker in historical_markers)

def main():
    files_to_check = [
        'docs/data-architecture.md',
        'docs/modules/llm-adapters.md',
        'docs/unity-integration.md',
        'docs/prompts.md'
    ]

    missing_files = [f for f in files_to_check if not Path(f).exists()]
    if missing_files:
        print(f"ERROR: Missing expected documentation files: {', '.join(missing_files)}")
        sys.exit(1)

    errors = []

    # Read contents once
    contents = {}
    lines_map = {}
    for filepath in files_to_check:
        with open(filepath, 'r', encoding='utf-8') as f:
            contents[filepath] = f.read()
            f.seek(0)
            lines_map[filepath] = f.readlines()

    # Rule 1: Stale keys check
    stale_keys_regex = re.compile(r'\b(vision|world_description|player_role_description)\b', re.IGNORECASE)
    for filepath, lines in lines_map.items():
        for idx, line in enumerate(lines):
            matches = stale_keys_regex.finditer(line)
            for match in matches:
                context = get_context(lines, idx)
                if not is_marked_historical(context):
                    errors.append(f"Stale key reference '{match.group(1)}' found in {filepath}:{idx+1} without historical context.")

    # Rule 2: Legacy fallback keys still accepted
    fallback_regex = re.compile(r'(fallback.*accepted|legacy.*supported|fallback exists only so unmigrated|still accepts the legacy)', re.IGNORECASE)
    for filepath, lines in lines_map.items():
        for idx, line in enumerate(lines):
            if fallback_regex.search(line):
                errors.append(f"Statement that legacy/unsupported fallback keys are still accepted found in {filepath}:{idx+1}.")

    # Rule 3: Phase 5 pending / prompts partially C# constants
    phase5_regex = re.compile(r'(Phase 5.*pending|migration.*pending|prompts remain partially C#|status:\s*partial|status:\s*const|embed.*in C#)', re.IGNORECASE)
    for filepath, lines in lines_map.items():
        if filepath == 'docs/prompts.md':
            for idx, line in enumerate(lines):
                if phase5_regex.search(line):
                    context = get_context(lines, idx)
                    if not is_marked_historical(context):
                        errors.append(f"Statement about Phase 5 pending or partial C# constants found in {filepath}:{idx+1}.")

    # Rule 4: Removed delivery-prompt / old const-migration language without historic context
    delivery_prompt_regex = re.compile(r'\bdelivery-prompt\b', re.IGNORECASE)
    const_migration_regex = re.compile(r'const-migration', re.IGNORECASE)
    for filepath, lines in lines_map.items():
        for idx, line in enumerate(lines):
            if delivery_prompt_regex.search(line):
                context = get_context(lines, idx)
                if not is_marked_historical(context):
                    errors.append(f"Removed 'delivery-prompt' found in {filepath}:{idx+1} without historical context.")
            if const_migration_regex.search(line):
                context = get_context(lines, idx)
                if not is_marked_historical(context):
                    errors.append(f"Old 'const-migration' language found in {filepath}:{idx+1} without historical context.")

    # Acceptance Criteria 1: Game-definition docs list current keys
    current_keys = ['game_master_prompt', 'player_avatar_role_description', 'datee_role_description', 'global_dc_bias']
    all_content = "\n".join(contents.values())
    for key in current_keys:
        if key not in all_content:
            errors.append(f"Current game-definition key '{key}' is missing from the documentation.")

    # Acceptance Criteria 2: Prompt docs describe current YAML catalog SSOT and fail-fast wiring
    prompts_content = contents.get('docs/prompts.md', '')
    ssot_regex = re.compile(r'(Single Source of Truth|SSOT)', re.IGNORECASE)
    fail_fast_regex = re.compile(r'fail-fast', re.IGNORECASE)
    
    if not ssot_regex.search(prompts_content) or 'YAML' not in prompts_content:
        errors.append("docs/prompts.md does not adequately describe the YAML catalog as the Single Source of Truth (SSOT).")
    if not fail_fast_regex.search(prompts_content):
        errors.append("docs/prompts.md does not describe fail-fast wiring.")

    # Acceptance Criteria 3: Lists the current catalog files
    expected_catalog_files = ['background.yaml', 'templates.yaml', 'archetypes.yaml', 'structural.yaml', 'narrative.yaml', 'stake.yaml']
    for cat_file in expected_catalog_files:
        if cat_file not in prompts_content:
            errors.append(f"docs/prompts.md is missing the current catalog file: '{cat_file}'.")

    if errors:
        print("Contract Test FAILED:")
        for error in errors:
            print(f" - {error}")
        sys.exit(1)
    
    print("Contract Test PASSED: All documentation requirements are met.")
    sys.exit(0)

if __name__ == '__main__':
    main()
