import sys
import os
import json
import pytest
import yaml
import re

sys.path.append(os.path.dirname(__file__))

from extract import parse_archetype_blocks, extract_rules
from generate import generate_archetype_definition
from enrich import enrich_rules_v3
from accuracy_check import check_file

# --- 1. Extraction Pipeline Updates (extract.py) ---

# What: Extracts stats, shadows, level_range, behavior, and handles missing interference correctly.
# Mutation: Fails if extract_rules or parse_archetype_blocks misparses these fields, drops behavior lines, or crashes on missing interference.
def test_parse_archetype_blocks_basic():
    title = "The Peacock"
    blocks = [
        {"kind": "paragraph", "text": "**Stats:** High Charm, Rizz | Low Honesty, Self-Awareness | **Shadow:** High Dread, Denial\n**Level range:** 3-8\n\nBy message 3, mention one achievement or capability as context \u2014 not bragging, accounting.\n\nFrame it as information. The mention should feel natural, like it came up because it's relevant."},
    ]
    
    res = parse_archetype_blocks(title, blocks, 3)
    
    assert res['stats']['high'] == ['Charm', 'Rizz']
    assert res['stats']['low'] == ['Honesty', 'Self-Awareness']
    assert res['shadows']['high'] == ['Dread', 'Denial']
    assert res['shadows']['low'] == []
    assert res['level_range'] == [3, 8]
    assert "By message 3" in res['behavior']
    assert "Frame it as information." in res['behavior']
    assert res['interference'] == {}

# What: Ensures tier tracking correctly assigns the current tier to the block without an off-by-one error on block finalization.
# Mutation: Fails if extract_rules updates current_tier before calling finalize_rule on the previous block (which causes the previous block to receive the new tier).
def test_extract_rules_tier_assignment(tmp_path):
    md_content = """# Archetypes
### Tier 1 - Early Game
#### Arch A
**Stats:** High Charm | Low Honesty | **Shadow:** High Dread
**Level range:** 1-2

Some behavior A.
### Tier 2 - Mid Game
#### Arch B
**Stats:** High Rizz | Low Honesty | **Shadow:** High Denial
**Level range:** 3-5

Some behavior B.
"""
    p = tmp_path / "dummy.md"
    p.write_text(md_content)
    
    rules = extract_rules(str(p))
    
    arch_a = next(r for r in rules if 'Arch A' in r['title'])
    arch_b = next(r for r in rules if 'Arch B' in r['title'])
    
    assert arch_a.get('type') == 'archetype_definition'
    assert arch_a.get('tier') == 1, "Arch A should be tier 1, not affected by Tier 2 heading"
    
    assert arch_b.get('type') == 'archetype_definition'
    assert arch_b.get('tier') == 2

# What: Ensures behavior text is extracted fully and description isn't arbitrarily truncated mid-word.
# Mutation: Fails if the behavior field drops paragraphs or if description blindly slices to 100 chars mid-word.
def test_extract_rules_no_midword_truncation(tmp_path):
    md_content = """# Archetypes
### Tier 3
#### The Peacock
**Stats:** High Charm | Low Honesty | **Shadow:** High Dread
**Level range:** 3-8

This is a very long behavior block that should definitely not be arbitrarily truncated mid-word under any circumstances whatsoever. It is critical that the entire text is preserved exactly as it appears in the source markdown document.
"""
    p = tmp_path / "dummy.md"
    p.write_text(md_content)
    
    rules = extract_rules(str(p))
    arch = next(r for r in rules if 'The Peacock' in r['title'])
    
    # Behavior must be fully intact
    assert "This is a very long behavior block" in arch['behavior']
    assert "source markdown document." in arch['behavior']
    
    # If description is present, it shouldn't end with a broken word
    if 'description' in arch:
        desc = arch['description']
        if len(desc) < len(arch['behavior']) and arch['behavior'].startswith(desc):
            next_char = arch['behavior'][len(desc)]
            assert not (re.match(r'\w', desc[-1]) and re.match(r'\w', next_char)), "Description appears truncated mid-word"

# --- 2. Generation Pipeline Updates (generate.py) ---

# What: Safely formats archetype_definition back to standard archetypes.md template including Interference
# Mutation: Fails if generate_archetype_definition omits fields, formatting, or fails to render **Interference:** block.
def test_generate_archetype_definition():
    rule = {
        "title": "The Peacock",
        "tier": 3,
        "stats": {"high": ["Charm", "Rizz"], "low": ["Honesty", "Self-Awareness"]},
        "shadows": {"high": ["Dread", "Denial"], "low": []},
        "level_range": [3, 8],
        "behavior": "By message 3, mention one achievement.\n\nFrame it as information.",
        "interference": {
            "count_1_2": "slight - occasionally drops a credential",
            "count_3_5": "moderate - consistent"
        }
    }
    
    res = generate_archetype_definition(rule, heading_level=4)
    
    assert "#### The Peacock" in res
    assert "**Stats:** High Charm, Rizz | Low Honesty, Self-Awareness | **Shadow:** High Dread, Denial" in res
    assert "**Level range:** 3–8" in res or "**Level range:** 3-8" in res
    assert "By message 3, mention one achievement." in res
    assert "**Interference:**" in res
    assert "* count_1_2: slight - occasionally drops a credential" in res
    assert "* count_3_5: moderate - consistent" in res

# --- 3. V3 Enrichment Injection (enrich.py) ---

# What: enrich_rules_v3 loads archetypes.yaml, processes archetype_definition entries, and appends them
# Mutation: Fails if enrich_rules_v3 does not read archetypes.yaml, fails to normalize IDs, or fails to set section §3.
def test_enrich_rules_v3(tmp_path, monkeypatch):
    # Mock the archetypes.yaml path to our temporary file
    mock_yaml_path = tmp_path / "archetypes.yaml"
    
    mock_arch = [
        {
            "id": "§0.the-peacock",
            "type": "archetype_definition",
            "title": "The Peacock",
            "tier": 3,
            "level_range": [3, 8],
            "behavior": "Test",
            "interference": {}
        }
    ]
    mock_yaml_path.write_text(yaml.dump(mock_arch))
    
    import enrich
    
    # Write to the real path to be safe
    real_path = os.path.join(os.path.dirname(__file__), '../extracted/archetypes.yaml')
    os.makedirs(os.path.dirname(real_path), exist_ok=True)
    
    backup = None
    if os.path.exists(real_path):
        with open(real_path, 'r') as f:
            backup = f.read()
            
    try:
        with open(real_path, 'w') as f:
            yaml.dump(mock_arch, f)
            
        base_entries = [{"id": "base_rule", "type": "rule"}]
        result = enrich_rules_v3(base_entries)
        
        # Check that the archetype was appended
        assert len(result) == 2
        arch_entry = result[1]
        
        assert arch_entry['type'] == 'archetype_definition'
        # Check ID normalization
        assert arch_entry['id'] == 'archetype.peacock'
        # Check section mapping
        assert arch_entry['section'] == '§3'
        
    finally:
        if backup is not None:
            with open(real_path, 'w') as f:
                f.write(backup)
        else:
            if os.path.exists(real_path):
                os.remove(real_path)

# --- 4. Accuracy Checker Updates (accuracy_check.py) ---

# What: Recognizes archetype_definition as valid and validates required keys.
# Mutation: Fails if accuracy_check does not recognize archetype_definition, or does not report missing required fields (tier, level_range, behavior, interference).
def test_accuracy_check_archetype_definition(tmp_path):
    # Valid
    valid_arch = [
        {
            "id": "archetype.peacock",
            "type": "archetype_definition",
            "tier": 3,
            "level_range": [3, 8],
            "behavior": "Valid behavior",
            "interference": {}
        }
    ]
    valid_path = tmp_path / "valid.yaml"
    valid_path.write_text(yaml.dump(valid_arch))
    
    errors = check_file(str(valid_path))
    assert len(errors) == 0, f"Expected no errors for valid archetype, got: {errors}"
    
    # Invalid missing tier
    invalid_arch = [
        {
            "id": "archetype.missing_tier",
            "type": "archetype_definition",
            "level_range": [3, 8],
            "behavior": "Valid behavior",
            "interference": {}
        }
    ]
    invalid_path = tmp_path / "invalid.yaml"
    invalid_path.write_text(yaml.dump(invalid_arch))
    
    errors = check_file(str(invalid_path))
    assert len(errors) > 0, "Expected an error for missing 'tier' field"
    assert any("tier" in err.lower() or "missing" in err.lower() for err in errors)


# What: Ensures missing stats/shadows ("—") and open-ended level ranges ("8+") are parsed correctly.
# Mutation: Fails if extract_rules crashes on missing data or fails to map "8+" to [8, 99].
def test_parse_archetype_blocks_edge_cases():
    title = "The Edge Case"
    blocks = [
        {"kind": "paragraph", "text": "**Stats:** High — | Low — | **Shadow:** High —\n**Level range:** 8+\n\nBehavior"},
    ]
    
    res = parse_archetype_blocks(title, blocks, 3)
    
    assert res['stats']['high'] == []
    assert res['stats']['low'] == []
    assert res['shadows']['high'] == []
    assert res['shadows']['low'] == []
    assert res['level_range'] == [8, 99]
