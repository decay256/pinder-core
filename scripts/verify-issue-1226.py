#!/usr/bin/env python3
import os
import sys
import re

def check_file_for_stale_patterns(filepath):
    if not os.path.exists(filepath):
        print(f"Warning: File not found: {filepath}")
        return False, False

    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    errors_found = False

    def report_error(msg):
        nonlocal errors_found
        print(f"[{os.path.basename(filepath)}] ERROR: {msg}")
        errors_found = True

    def check_pattern(pattern, error_msg, ignore_case=False):
        flags = re.IGNORECASE if ignore_case else 0
        matches = list(re.finditer(pattern, content, flags))
        for match in matches:
            context_start = max(0, match.start() - 200)
            context_end = min(len(content), match.end() + 200)
            context = content[context_start:context_end].lower()
            if 'historical' not in context and 'archived' not in context:
                report_error(error_msg)
                return

    # Check for stale mapping parameterId to tierId
    check_pattern(r'parameterId\s*(?:[^\w\s]*|to|→|->)\s*tierId', "Found stale mapping of parameterId to tierId.", ignore_case=True)

    # Check for discrete tier-based anatomy
    check_pattern(r'\bdiscrete tiers?\b', "Found mention of discrete tier-based anatomy instead of normalized [0..1] scalar bands.", ignore_case=True)
    check_pattern(r'\btierId\b', "Found mention of tierId instead of scalar bands.")
    check_pattern(r'\btier IDs?\b', "Found mention of tier ID instead of scalar bands.", ignore_case=True)
    check_pattern(r'\btier migration\b', "Found mention of tier migration.", ignore_case=True)
    check_pattern(r'\bAnatomyTierDefinition\b', "Found mention of AnatomyTierDefinition.", ignore_case=True)
    check_pattern(r'\banatomy tier\b', "Found mention of anatomy tier instead of scalar bands.", ignore_case=True)

    # Check for top-level build_points or shadows in JSON schema context
    check_pattern(r'(?<!allocation\.)\bbuild_points\b', "Found mention of top-level `build_points` instead of `allocation.spent` or `allocation.total`.")
    check_pattern(r'(?<!allocation\.)\bshadows\s*\{', "Found mention of top-level `shadows{}` instead of `allocation.shadows`.")
    check_pattern(r'"shadows"\s*:', "Found top-level JSON key `\"shadows\"` instead of `allocation.shadows`.")
    
    # Check for CharacterAssembler.Assemble with string, string
    check_pattern(r'CharacterAssembler\.Assemble.*?IReadOnlyDictionary<string,\s*string>', 
                 "Found stale signature `CharacterAssembler.Assemble` taking `IReadOnlyDictionary<string, string>`.")
    check_pattern(r'IReadOnlyDictionary<string,\s*string>\s*anatomySelections', 
                 "Found stale signature `CharacterAssembler.Assemble` taking `IReadOnlyDictionary<string, string>`.")

    return errors_found, content

def main():
    files_to_check = [
        'docs/data-architecture.md',
        'docs/unity-integration.md',
        'docs/modules/characters.md'
    ]

    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    
    any_errors = False
    all_content = ""
    for filename in files_to_check:
        filepath = os.path.join(base_dir, filename)
        errs, text = check_file_for_stale_patterns(filepath)
        if errs:
            any_errors = True
        if text:
            all_content += text + "\n"

    # Also verify positive presence of the new documentation criteria
    # "schema v2 documented with normalized values, allocation blocks, and current band thresholds"
    def check_presence(pattern, error_msg):
        nonlocal any_errors
        if not re.search(pattern, all_content, re.IGNORECASE):
            print(f"[Missing Requirement] ERROR: {error_msg}")
            any_errors = True

    check_presence(r'\[0\.\.1\]', "Normalized [0..1] scalar bands not documented.")
    check_presence(r'allocation\.spent', "`allocation.spent` not documented.")
    check_presence(r'allocation\.shadows', "`allocation.shadows` not documented.")
    check_presence(r'allocation\.total', "`allocation.total` not documented.")
    check_presence(r'IReadOnlyDictionary<string,\s*float>', "`CharacterAssembler.Assemble` with `IReadOnlyDictionary<string, float>` not documented.")
    
    # Check for something resembling bands or thresholds
    check_presence(r'bands?|thresholds?', "Scalar bands or thresholds not documented.")

    if any_errors:
        print("\nFAILURE: Contract test did not pass. Stale documentation references found or new requirements missing.")
        sys.exit(1)
    else:
        print("\nSUCCESS: No stale documentation references found and new requirements met.")
        sys.exit(0)

if __name__ == '__main__':
    main()
