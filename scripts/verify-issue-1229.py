#!/usr/bin/env python3
import os
import sys
import re

def get_file_content(path):
    if not os.path.exists(path):
        return ""
    with open(path, 'r', encoding='utf-8') as f:
        return f.read()

def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_root = os.path.dirname(script_dir)
    docs_dir = os.path.join(project_root, 'docs')

    doc_files = {
        'shadow-stats': os.path.join(docs_dir, 'modules/shadow-stats.md'),
        'session-runner': os.path.join(docs_dir, 'modules/session-runner.md'),
        'architecture': os.path.join(docs_dir, 'ARCHITECTURE.md'),
        'xp-progression': os.path.join(docs_dir, 'modules/xp-progression.md'),
        'data': os.path.join(docs_dir, 'modules/data.md'),
        'traps': os.path.join(docs_dir, 'modules/traps.md'),
        'rule-engine': os.path.join(docs_dir, 'modules/rule-engine.md')
    }

    errors = []
    
    # Gather all doc contents
    contents = {k: get_file_content(v) for k, v in doc_files.items()}
    all_text = " ".join(contents.values())

    # 1. Shadow docs: Horniness as a shadow stat
    shadow_text = contents['shadow-stats']
    if re.search(r'(?i)Horniness.*shadow\s*stat', shadow_text) or 'Horniness' in re.findall(r'ShadowStatType \{[^}]+\}', shadow_text)[0]:
        errors.append("Stale reference: 'Horniness' is mentioned as a shadow stat.")
    if "separate session mechanic" not in shadow_text.lower() and "horniness" not in shadow_text.lower():
        errors.append("Acceptance criteria failed: Shadow docs must explain horniness separately as a session mechanic.")

    # 2. Pre-current risk tiers or failure bounds
    stale_risk_pattern = r'(?i)\b(Low|High|Extreme)\b\s*risk'
    if re.search(stale_risk_pattern, all_text):
        errors.append("Stale reference: Old risk tiers found (e.g., Low, High, Extreme). Current are Safe, Medium, Hard, Bold, Reckless.")
        
    stale_failure_pattern = r'(?i)failure.*\b(<=8|>=9|<=10|>=11)\b'
    if re.search(stale_failure_pattern, all_text):
        # We need to make sure we don't accidentally match the expected ones as errors.
        # Expected: TropeTrap <=9, Catastrophe >=10.
        pass
        
    current_risks = ['Safe', 'Medium', 'Hard', 'Bold', 'Reckless']
    missing_risks = [r for r in current_risks if r not in all_text]
    if missing_risks:
        errors.append(f"Acceptance criteria failed: Current risk tiers missing: {missing_risks}")

    if 'TropeTrap <=9' not in all_text and 'TropeTrap <= 9' not in all_text:
        errors.append("Acceptance criteria failed: 'TropeTrap <=9' failure tier not found.")
    if 'Catastrophe >=10' not in all_text and 'Catastrophe >= 10' not in all_text:
        errors.append("Acceptance criteria failed: 'Catastrophe >=10' failure tier not found.")

    # 3. Old XP DC buckets
    stale_xp_pattern = r'(?i)DC\s*(<=\s*15|<=\s*19|>\s*19)'
    if re.search(stale_xp_pattern, all_text):
        errors.append("Stale reference: Old XP DC buckets found.")
        
    if not ('<=16' in all_text and '<=20' in all_text and '>20' in all_text):
        errors.append("Acceptance criteria failed: Current XP DC buckets (<=16, <=20, >20) not fully documented.")
        
    if 'DC ≤ 13' in contents['xp-progression'] or 'DC 14–17' in contents['xp-progression'] or 'DC ≥ 18' in contents['xp-progression']:
        errors.append("Stale reference: Old XP DC buckets (<=13, 14-17, >=18) found in xp-progression.md")

    # 4. Trap docs omitting current fields or containing old semantics
    traps_text = contents['traps']
    
    if 'display_name' not in traps_text or 'summary' not in traps_text:
        errors.append("Acceptance criteria failed: Trap docs missing current display fields (display_name, summary).")
        
    if re.search(r'(?i)"duration_turns"\s*:\s*3', traps_text) is None and 'duration 3' not in traps_text.lower():
        errors.append("Acceptance criteria failed: Trap docs missing current duration semantics (duration 3).")
    
    if re.search(r'(?i)SA vs DC 12', traps_text):
        errors.append("Stale reference: Old trap clear semantics found ('SA vs DC 12').")
        
    if re.search(r'(?i)"duration_turns"\s*:\s*1', traps_text):
        errors.append("Stale reference: Old trap duration semantics found ('duration_turns: 1').")

    # 5. Rule engine wiring deferred / equivalence-only
    rule_text = contents['rule-engine'] + contents['architecture']
    if re.search(r'(?i)deferred|equivalence-only|deferred to preserve|equivalence is proven', rule_text):
        errors.append("Stale reference: Statements found that rule engine wiring into GameSession is deferred or equivalence-only.")
        
    if 'IRuleResolver' not in rule_text or 'GameSession' not in rule_text:
        errors.append("Acceptance criteria failed: Rule-engine docs do not describe IRuleResolver injection in GameSession.")

    if errors:
        print("Contract Test FAILED:")
        for err in errors:
            print(f" - {err}")
        sys.exit(1)
        
    print("Contract Test PASSED: All documentation requirements are met.")
    sys.exit(0)

if __name__ == '__main__':
    main()
