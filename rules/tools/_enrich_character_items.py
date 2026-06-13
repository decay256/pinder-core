#!/usr/bin/env python3
# AUTO-GENERATED
import copy
import os
import re
from typing import Any, Dict, List, Tuple, Optional, Set
from _enrich_shared import SHADOW_NAMES, STAT_NAMES, parse_stat_modifiers, load_yaml, save_yaml

def enrich_character_construction(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich character-construction.yaml entries."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        desc = str(e.get('description', ''))
        blocks = e.get('blocks', [])

        if eid == '§2.slot-structure':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        slot = row.get('Slot', '')
                        occupancy = row.get('Occupancy', row.get('Count', ''))
                        effect_scope = row.get('Effect scope', row.get('Effect', ''))
                        if not slot:
                            continue
                        slug = slot.lower().replace(' ', '-').replace('×', 'x')[:30]
                        outcome = {}
                        c_match = re.search(r'(\d+)', str(occupancy))
                        if c_match:
                            outcome['slot_count'] = int(c_match.group(1))
                        if effect_scope:
                            outcome['stat_focus'] = effect_scope
                        if outcome:
                            sub = {
                                'id': f'§2.slot.{slug}',
                                'section': '§2',
                                'title': f'Slot — {slot}',
                                'type': 'definition',
                                'description': f'{slot}: occupancy={occupancy}, scope={effect_scope}.',
                                'condition': {'slot': slot},
                                'outcome': outcome,
                            }
                            result.append(sub)
            result.append(e)
            continue

        elif eid == '§2.4-archetype-tendencies':
            e['condition'] = {'formula': 'archetype_tendency'}
            e['outcome'] = {'archetype_stat_weight': 'dominant_stat'}

        elif eid == '§3.stat-descriptors-dynamic-scaled-to-effective-value':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        eff_mod = row.get('Effective modifier', '')
                        descriptor = row.get('Descriptor', '')
                        if not eff_mod:
                            continue
                        # Parse ranges like "+4 or higher", "+2 to +3", "0 to +1", etc.
                        m = re.search(r'([+\-−]?\d+)\s*(?:to|–|-)\s*([+\-−]?\d+)', str(eff_mod))
                        m_single = re.search(r'([+\-−]?\d+)\s*or\s*(higher|lower)', str(eff_mod))
                        cond = {}
                        if m:
                            lo = int(m.group(1).replace('+', ''))
                            hi = int(m.group(2).replace('+', ''))
                            cond['stat_range'] = [lo, hi]
                        elif m_single:
                            val = int(m_single.group(1).replace('+', ''))
                            if 'higher' in m_single.group(2):
                                cond['stat_range'] = [val, 99]
                            else:
                                cond['stat_range'] = [-99, val]
                        if cond:
                            slug = eff_mod.replace('+', 'p').replace('-', 'm').replace(' ', '').replace('−', 'm')[:20]
                            sub = {
                                'id': f'§3.stat-descriptor.{slug}',
                                'section': '§3',
                                'title': f'Stat Descriptor — {eff_mod}',
                                'type': 'definition',
                                'description': f'Effective modifier {eff_mod}: {descriptor}.',
                                'condition': cond,
                                'outcome': {'descriptor': descriptor.strip('"')},
                            }
                            result.append(sub)
            result.append(e)
            continue

        elif eid == '§3.35-datee-message-generation':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        interest = row.get('Interest', '')
                        block_text = row.get('Block text', '')
                        if not interest:
                            continue
                        m = re.search(r'(\d+)\s*[–-]\s*(\d+)', str(interest))
                        if m:
                            slug = f'{m.group(1)}-{m.group(2)}'
                            sub = {
                                'id': f'§3.datee-tone.{slug}',
                                'section': '§3',
                                'title': f'Datee Tone — Interest {interest}',
                                'type': 'template',
                                'description': f'Interest {interest}: {block_text}',
                                'condition': {'interest_range': [int(m.group(1)), int(m.group(2))]},
                                'outcome': {'quality_boost': block_text.strip('"')[:80]},
                            }
                            result.append(sub)
            result.append(e)
            continue

        elif eid == '§3.dynamic-assembly':
            # Has references to fragment assembly with numeric thresholds
            all_text = desc
            for b in blocks:
                all_text += ' ' + str(b.get('text', ''))
            if any(c.isdigit() for c in all_text):
                e['condition'] = {'formula': 'dynamic_assembly'}
                e['outcome'] = {'effect': 'fragment_concatenation'}

        result.append(e)
    return result

def enrich_items_pool(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich items-pool.yaml entries."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        desc = str(e.get('description', ''))
        blocks = e.get('blocks', [])

        # Item entries (§12.*) have stat modifiers
        if eid.startswith('§12.'):
            # Parse stat modifiers from description
            mods = parse_stat_modifiers(desc)
            if mods:
                # Parse tier
                tier_m = re.search(r'\*\*Tier:\*\*\s*(\w+)', desc)
                tier = tier_m.group(1) if tier_m else 'Unknown'
                e['condition'] = {'item': e.get('title', ''), 'tier': tier}
                e['outcome'] = {'stat_modifiers': mods}

        # Slot/placement tables
        elif eid == '§2.anatomical-placement':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        slot = row.get('Slot', '')
                        placement = row.get('Placement', row.get('Location', ''))
                        stat = row.get('Primary Stat', row.get('Stat Focus', ''))
                        if not slot:
                            continue
                        if stat:
                            slug = slot.lower().replace(' ', '-')[:30]
                            sub = {
                                'id': f'§2.placement.{slug}',
                                'section': '§2',
                                'title': f'Placement — {slot}',
                                'type': 'definition',
                                'description': f'{slot}: {placement}. Primary stat: {stat}.',
                                'condition': {'slot': slot},
                                'outcome': {'primary_stat': stat},
                            }
                            result.append(sub)
            result.append(e)
            continue

        # Fragment types
        elif eid == '§4.fragment-types':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        frag_type = row.get('Fragment', row.get('Type', ''))
                        purpose = row.get('Purpose', row.get('Description', ''))
                        if not frag_type:
                            continue
                        slug = frag_type.lower().replace(' ', '-')[:30]
                        sub = {
                            'id': f'§4.fragment.{slug}',
                            'section': '§4',
                            'title': f'Fragment Type — {frag_type}',
                            'type': 'definition',
                            'description': f'{frag_type}: {purpose}.',
                            'condition': {'fragment_type': frag_type},
                            'outcome': {'purpose': purpose},
                        }
                        result.append(sub)
            result.append(e)
            continue

        # Quick reference tables (hats, shirts, etc.)
        elif 'quick-reference' in eid:
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        item_name = row.get('Item', row.get('Name', ''))
                        tier = row.get('Tier', '')
                        stats = row.get('Stats', '')
                        if not item_name:
                            continue
                        mods = parse_stat_modifiers(str(stats))
                        if mods:
                            slug = item_name.lower().replace(' ', '-').replace("'", '').replace('"', '')[:30]
                            sub = {
                                'id': f'{eid}.{slug}',
                                'section': e.get('section', ''),
                                'title': f'{item_name}',
                                'type': 'definition',
                                'description': f'{item_name}: Tier {tier}, Stats: {stats}.',
                                'condition': {'item': item_name, 'tier': tier},
                                'outcome': {'stat_modifiers': mods},
                            }
                            result.append(sub)
            result.append(e)
            continue

        result.append(e)
    return result

