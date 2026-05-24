#!/usr/bin/env python3
# AUTO-GENERATED
import copy
import os
import re
from typing import Any, Dict, List, Tuple, Optional, Set
from _enrich_shared import SHADOW_NAMES, STAT_NAMES, parse_stat_modifiers, load_yaml, save_yaml

def enrich_anatomy_parameters(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich anatomy-parameters.yaml entries."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        desc = str(e.get('description', ''))
        blocks = e.get('blocks', [])

        # §1 parameters overview table
        if eid == '§1.parameters':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        param = row.get('Parameter', '')
                        levels = row.get('Levels', '')
                        stat_effect = row.get('Stat effect', '')
                        if not param:
                            continue
                        slug = param.lower().replace(' ', '-')
                        sub = {
                            'id': f'§1.param.{slug}',
                            'section': '§1',
                            'title': f'Parameter — {param}',
                            'type': 'definition',
                            'description': f'{param}: Levels = {levels}. Stat effect: {stat_effect}.',
                            'condition': {'parameter': param, 'levels': levels},
                            'outcome': {'stat_effect': stat_effect},
                        }
                        result.append(sub)
            result.append(e)
            continue

        # Individual anatomy tier entries (§3.short, §3.medium, etc.)
        # These have stat modifiers in their description
        elif any(eid.startswith(f'§{n}.') for n in range(3, 20)):
            # Check if it's a tier entry with stats
            mods = parse_stat_modifiers(desc)
            if mods:
                e['condition'] = {'anatomy_tier': e.get('title', '')}
                e['outcome'] = {'stat_modifiers': mods}
            elif 'none' in desc.lower() and 'baseline' in desc.lower():
                e['condition'] = {'anatomy_tier': e.get('title', '')}
                e['outcome'] = {'stat_modifiers': {}, 'baseline': True}

        result.append(e)
    return result

def enrich_extensibility(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich extensibility.yaml entries.
    
    Extensibility entries are mostly templates and definitions describing
    how to add custom content. Very few contain mechanical thresholds.
    """
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')

        # §4.modding-support has a table with extensibility layers
        if eid == '§4.modding-support':
            blocks = e.get('blocks', [])
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        layer = row.get('Layer', row.get('Content Type', ''))
                        path = row.get('Path', row.get('Directory', ''))
                        if layer and path:
                            slug = layer.lower().replace(' ', '-')[:25]
                            sub = {
                                'id': f'§4.mod-layer.{slug}',
                                'section': '§4',
                                'title': f'Mod Layer — {layer}',
                                'type': 'definition',
                                'description': f'{layer}: {path}.',
                                'condition': {'formula': 'modding'},
                                'outcome': {'mod_path': path},
                            }
                            result.append(sub)
            result.append(e)
            continue

        # §5 has runtime assembly mechanics
        elif eid == '§5.llm-prompt-assembly-runtime':
            e['condition'] = {'formula': 'prompt_assembly'}
            e['outcome'] = {'effect': 'fragment_concatenation'}

        result.append(e)
    return result

