import pytest
import os
import yaml
import sys
from unittest.mock import patch

sys.path.append(os.path.dirname(__file__))

from extract import parse_archetype_blocks
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
    assert "By message 3, mention one achievement or capability as context" in res['behavior']
    assert "Frame it as information." in res['behavior']
    assert res['interference'] == {}

# What: Ensures missing stats/shadows ("-") and open-ended level ranges ("8+") are parsed correctly.
# Mutation: Fails if parse_archetype_blocks crashes on missing data ("—" or "-") or fails to map "8+" to [8, 99].
def test_parse_archetype_blocks_edge_cases():
    title = "The Edge Case"
    blocks = [
        {"kind": "paragraph", "text": "**Stats:** High \u2014 | Low \u2014 | **Shadow:** High \u2014\n**Level range:** 8+\n\nBehavior"},
    ]
    
    res = parse_archetype_blocks(title, blocks, 3)
    
    assert res['stats']['high'] == []
    assert res['stats']['low'] == []
    assert res['shadows']['high'] == []
    assert res['shadows']['low'] == []
    assert res['level_range'] == [8, 99]

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
def test_enrich_rules_v3(monkeypatch):
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
    
    def mock_exists(path):
        if "archetypes.yaml" in path:
            return True
        return os.path.exists(path)

    def mock_load_yaml(path):
        if "archetypes.yaml" in path:
            return mock_arch
        # Fallback to standard open for other cases, though load_yaml in enrich just loads it
        with open(path, 'r', encoding='utf-8') as f:
            return yaml.safe_load(f)
            
    import enrich
    monkeypatch.setattr(enrich.os.path, "exists", mock_exists)
    monkeypatch.setattr(enrich, "load_yaml", mock_load_yaml)

    base_entries = [{"id": "base_rule", "type": "rule"}]
    result = enrich_rules_v3(base_entries)
    
    assert len(result) == 2
    arch_entry = result[1]
    assert arch_entry['type'] == 'archetype_definition'
    assert arch_entry['id'] == 'archetype.peacock'
    assert arch_entry['section'] == '§3'

# What: If archetypes.yaml cannot be located by enrich.py during injection, it logs a critical error or raises FileNotFoundError.
# Mutation: Fails if enrich_rules_v3 silently ignores a missing archetypes.yaml file.
def test_enrich_rules_v3_missing_file(monkeypatch):
    def mock_exists(path):
        if "archetypes.yaml" in path:
            return False
        return os.path.exists(path)
        
    import enrich
    monkeypatch.setattr(enrich.os.path, "exists", mock_exists)
    
    base_entries = [{"id": "base_rule", "type": "rule"}]
    with pytest.raises((FileNotFoundError, SystemExit, Exception)) as excinfo:
        enrich_rules_v3(base_entries)
        
    # Validates an exception was raised due to missing file

# --- 4. Accuracy Checker Updates (accuracy_check.py) ---

# What: Recognizes archetype_definition as valid and validates required keys.
# Mutation: Fails if accuracy_check does not recognize archetype_definition, or does not report missing required fields (tier, level_range, behavior, interference).
def test_accuracy_check_archetype_definition(tmp_path):
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
    assert any("tier" in err["message"].lower() or "missing" in err["message"].lower() for err in errors)

# What: If accuracy_check.py finds an archetype with missing or malformed interference structure, it logs INACCURATE with specific entry ID and field name.
# Mutation: Fails if accuracy_check doesn't report 'INACCURATE' for malformed 'interference'.
def test_accuracy_check_malformed_interference(tmp_path):
    invalid_arch = [
        {
            "id": "archetype.bad_interference",
            "type": "archetype_definition",
            "tier": 3,
            "level_range": [3, 8],
            "behavior": "Valid behavior",
            "interference": []  # Should be a dict
        }
    ]
    invalid_path = tmp_path / "invalid_interference.yaml"
    invalid_path.write_text(yaml.dump(invalid_arch))
    
    errors = check_file(str(invalid_path))
    assert len(errors) > 0, "Expected an error for malformed 'interference' field"
    error_msg = " ".join(err["message"] for err in errors).lower()
    assert "inaccurate" in error_msg
    assert "archetype.bad_interference" in error_msg
    assert "interference" in error_msg


# What: Ensures tier tracking correctly assigns the current tier to the block without an off-by-one error on block finalization.
# Mutation: Fails if extract_rules updates current_tier before calling finalize_rule on the previous block (which causes the previous block to receive the new tier).
def test_extract_rules_tier_assignment(tmp_path):
    from extract import extract_rules
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
