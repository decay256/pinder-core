import re
from typing import Any, Dict, List

STAT_NAMES = {'Charm', 'Rizz', 'Honesty', 'Chaos', 'Wit', 'Self-Awareness'}
SHADOW_NAMES = {'Overthinking', 'Denial', 'Fixation', 'Dread', 'Madness', 'Horniness'}


def load_yaml(path: str) -> List[Dict[str, Any]]:
    import yaml
    with open(path, 'r') as f:
        return yaml.safe_load(f) or []


def save_yaml(path: str, data: List[Dict[str, Any]]) -> None:
    import yaml
    with open(path, 'w') as f:
        yaml.dump(data, f, default_flow_style=False, allow_unicode=True, width=200, sort_keys=False)


def parse_stat_modifiers(text: str) -> Dict[str, int]:
    """Parse stat modifiers from text like 'Charm +1, Rizz -1'."""
    mods = {}
    for m in re.finditer(r'(Charm|Rizz|Honesty|Chaos|Wit|Self-Awareness)\s*([+\-−])\s*(\d+)', text):
        stat = m.group(1)
        sign = -1 if m.group(2) in ('-', '−') else 1
        val = int(m.group(3)) * sign
        mods[stat.lower().replace('-', '_')] = val
    return mods
