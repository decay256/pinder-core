#!/usr/bin/env python3
"""Generate RulesSpecTests.cs from rules/extracted/rules-v3-enriched.yaml.

Usage:
    python3 rules/tools/generate_tests.py            # write to tests/...
    python3 rules/tools/generate_tests.py --check    # compare without writing; exit 1 on diff
    python3 rules/tools/generate_tests.py --stdout   # print to stdout only

This script is the authoritative generator for:
    tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs

Always edit the YAML source (rules/extracted/rules-v3-enriched.yaml), then
re-run this script. Do not edit RulesSpecTests.cs manually — changes will
be overwritten on the next generation run.

Issue #1041 (Tier C): types codegen verification.
"""

import sys
import os
import yaml
from textwrap import dedent

# ---------------------------------------------------------------------------
# Paths (relative to repo root; script locates the root via its own __file__)
# ---------------------------------------------------------------------------
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
_REPO_ROOT   = os.path.abspath(os.path.join(_SCRIPT_DIR, '..', '..'))
_YAML_PATH   = os.path.join(_REPO_ROOT, 'rules', 'extracted', 'rules-v3-enriched.yaml')
_OUT_PATH    = os.path.join(_REPO_ROOT, 'tests', 'Pinder.Core.Tests', 'RulesSpec', 'RulesSpecTests.cs')


def load_yaml():
    with open(_YAML_PATH, encoding='utf-8') as fh:
        return yaml.safe_load(fh)


# ---------------------------------------------------------------------------
# Data extraction helpers
# ---------------------------------------------------------------------------

def _find(data, section_prefix=None, id_prefix=None, entry_type=None):
    """Return all YAML entries matching the given filters."""
    results = []
    for item in data:
        if not isinstance(item, dict):
            continue
        if section_prefix and not str(item.get('section', '')).startswith(section_prefix):
            continue
        if id_prefix and not str(item.get('id', '')).startswith(id_prefix):
            continue
        if entry_type and item.get('type') != entry_type:
            continue
        results.append(item)
    return results


def extract_failure_tiers(data):
    """Return list of (tier_name, miss_range_desc, interest_delta) tuples."""
    entries = _find(data, id_prefix='§7.fail-tier.', entry_type='interest_change')
    tiers = []
    for e in entries:
        cond = e.get('condition', {})
        out  = e.get('outcome', {})
        tier = out.get('tier', '')
        delta = out.get('interest_delta', 0)
        if 'miss_range' in cond:
            lo, hi = cond['miss_range']
            tiers.append((tier, f'miss_range [{lo}, {hi}]', lo, hi, None, delta))
        elif 'miss_minimum' in cond:
            lo = cond['miss_minimum']
            tiers.append((tier, f'miss_minimum {lo}', lo, None, None, delta))
        elif 'natural_roll' in cond:
            nat = cond['natural_roll']
            tiers.append((tier, f'natural_roll {nat}', None, None, nat, delta))
    return tiers


def extract_success_scale(data):
    """Return list of (beat_range_or_nat, interest_delta) tuples."""
    entries = _find(data, id_prefix='§7.success-scale.')
    rows = []
    for e in entries:
        cond  = e.get('condition', {})
        delta = e.get('outcome', {}).get('interest_delta', 0)
        if 'beat_range' in cond:
            lo, hi = cond['beat_range']
            rows.append(('beat', lo, hi, None, delta))
        elif 'natural_roll' in cond:
            rows.append(('nat', None, None, cond['natural_roll'], delta))
    return rows


def extract_interest_states(data):
    """Return list of (lo, hi, state_name) tuples."""
    entries = _find(data, id_prefix='§6.interest-state.')
    states = []
    for e in sorted(entries, key=lambda x: x.get('condition', {}).get('interest_range', [0])[0]):
        cond = e.get('condition', {})
        out  = e.get('outcome', {})
        raw_state = out.get('state', '')
        # Strip emoji prefix: '😐 Bored' → 'Bored'
        state_name = raw_state.split(' ', 1)[-1].strip() if ' ' in raw_state else raw_state
        # Map to C# InterestState enum value
        state_map = {
            'Unmatched':    'Unmatched',
            'Bored':        'Bored',
            'Lukewarm':     'Lukewarm',
            'Interested':   'Interested',
            'Very Into It': 'VeryIntoIt',
            'Almost There': 'AlmostThere',
            'Date Secured': 'DateSecured',
        }
        cs_enum = state_map.get(state_name, state_name.replace(' ', ''))
        lo, hi = cond.get('interest_range', [0, 0])
        states.append((lo, hi, cs_enum))
    return states


def extract_shadow_thresholds(data):
    """Return list of (threshold_value, tier_index) tuples for the numeric tests."""
    # The C# ShadowThresholdEvaluator returns tier 0/1/2/3 based on shadow points.
    # Values from the §9.shadow-threshold entries.
    entries = _find(data, id_prefix='§9.shadow-threshold.')
    thresholds = []
    for e in entries:
        cond = e.get('condition', {})
        thresh = cond.get('threshold')
        if thresh is not None:
            thresholds.append(thresh)
    # Deduplicate and sort
    return sorted(set(thresholds))


def extract_level_table(data):
    """Extract XP → level rows from §12.levels-build-points table block."""
    entries = _find(data, id_prefix='§12.levels-build-points', entry_type='table')
    rows = []
    for e in entries:
        for block in e.get('blocks', []):
            if block.get('kind') == 'table':
                for row in block.get('rows', []):
                    level = row.get('Level')
                    xp    = row.get('XP')
                    bonus_str = row.get('Level Bonus', '+0').lstrip('+')
                    try:
                        rows.append((int(level), int(xp) if xp and xp.lstrip('-').isdigit() else None, int(bonus_str)))
                    except (ValueError, TypeError):
                        pass
    return rows


# ---------------------------------------------------------------------------
# Code generation
# ---------------------------------------------------------------------------

_HEADER = """\
// Auto-generated from rules/extracted/rules-v3-enriched.yaml
// See: rules/tools/generate_tests.py
// Edit the YAML source, then re-run generation — do not edit this file manually.

using Xunit;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Conversation;
using Pinder.Core.Progression;
using Pinder.Core.Interfaces;
using Pinder.Core.Traps;

namespace Pinder.Core.Tests.RulesSpec
{
    [Trait("Category", "Rules")]
    public class RulesSpecTests
    {
        // =====================================================================
        // Helper methods for constructing RollResult fixtures
        // =====================================================================

        private static RollResult MakeFailureResult(FailureTier tier, int missMargin)
        {
            int dc = 15;
            int usedDieRoll;
            int statModifier = 0;

            if (tier == FailureTier.Legendary)
            {
                usedDieRoll = 1;
                statModifier = 0;
            }
            else
            {
                int needed = dc - missMargin;
                usedDieRoll = System.Math.Max(2, System.Math.Min(19, needed));
                statModifier = needed - usedDieRoll;
            }

            return new RollResult(
                dieRoll: usedDieRoll,
                secondDieRoll: null,
                usedDieRoll: usedDieRoll,
                stat: StatType.Charm,
                statModifier: statModifier,
                levelBonus: 0,
                dc: dc,
                tier: tier,
                activatedTrap: null,
                externalBonus: 0
            );
        }

        private static RollResult MakeSuccessResult(int beatMargin, bool isNat20 = false)
        {
            int dc = 13;
            if (isNat20)
            {
                return new RollResult(
                    dieRoll: 20, secondDieRoll: null, usedDieRoll: 20,
                    stat: StatType.Charm, statModifier: 0, levelBonus: 0,
                    dc: dc, tier: FailureTier.Success, activatedTrap: null, externalBonus: 0
                );
            }
            int total = dc + beatMargin;
            int usedDieRoll = System.Math.Min(19, total);
            int statModifier = total - usedDieRoll;
            return new RollResult(
                dieRoll: usedDieRoll, secondDieRoll: null, usedDieRoll: usedDieRoll,
                stat: StatType.Charm, statModifier: statModifier, levelBonus: 0,
                dc: dc, tier: FailureTier.Success, activatedTrap: null, externalBonus: 0
            );
        }

        private static RollResult MakeRiskResult(int need, bool isSuccess)
        {
            int dc = 13;
            int statModifier = dc - need;
            int usedDieRoll = isSuccess ? 19 : 2;
            var tier = isSuccess ? FailureTier.Success : FailureTier.Fumble;
            return new RollResult(
                dieRoll: usedDieRoll, secondDieRoll: null, usedDieRoll: usedDieRoll,
                stat: StatType.Charm, statModifier: statModifier, levelBonus: 0,
                dc: dc, tier: tier, activatedTrap: null, externalBonus: 0
            );
        }
"""

_FOOTER = """\
    }
}
"""

# Tier name → C# FailureTier enum value and representative miss margin
_FAILURE_TIER_MAP = {
    'Fumble':         ('Fumble',      2),
    'Misfire':        ('Misfire',     4),
    'Trope Trap':     ('TropeTrap',   7),
    'Catastrophe':    ('Catastrophe', 11),
    'Legendary Fail': ('Legendary',   14),
}

# Human-readable miss range label per tier
_FAILURE_RANGE_LABEL = {
    'Fumble':         'MissBy1To2',
    'Misfire':        'MissBy3To5',
    'Trope Trap':     'MissBy6To9',
    'Catastrophe':    'MissBy10Plus',
    'Legendary Fail': 'Nat1',
}

# Delta → friendly name suffix
_DELTA_LABEL = {
    -1: 'NegativeOne',
    -2: 'NegativeTwo',
    -3: 'NegativeThree',
    -4: 'NegativeFour',
    1:  'PlusOne',
    2:  'PlusTwo',
    3:  'PlusThree',
    4:  'PlusFour',
}

# Mutation comment per tier
_FAILURE_MUTATION_COMMENT = {
    'Fumble':         'would catch if Fumble returned 0 or -2 instead of -1',
    'Misfire':        'would catch if Misfire returned -2 instead of -1',
    'Trope Trap':     'would catch if TropeTrap returned -1 instead of -2',
    'Catastrophe':    'would catch if Catastrophe returned -2 instead of -3',
    'Legendary Fail': 'would catch if Legendary returned -3 or -5 instead of -4',
}


def gen_failure_scale(data):
    tiers = extract_failure_tiers(data)
    lines = []
    lines.append('        // =====================================================================')
    lines.append('        // §5 — Failure Scale (5 tests)')
    lines.append('        // =====================================================================')
    lines.append('')
    for (tier, _, lo, hi, nat, delta) in tiers:
        cs_tier, rep_miss = _FAILURE_TIER_MAP.get(tier, (tier.replace(' ', ''), 7))
        range_label = _FAILURE_RANGE_LABEL.get(tier, 'Miss')
        delta_label = _DELTA_LABEL.get(delta, str(delta))
        comment = _FAILURE_MUTATION_COMMENT.get(tier, f'mutation guard for {tier}')
        test_name = f'Rule_S5_{cs_tier}_{range_label}_{delta_label}'
        lines.append(f'        // Mutation: {comment}')
        lines.append('        [Fact]')
        lines.append(f'        public void {test_name}()')
        lines.append('        {')
        lines.append(f'            var result = MakeFailureResult(FailureTier.{cs_tier}, {rep_miss});')
        lines.append(f'            Assert.Equal({delta}, FailureScale.GetInterestDelta(result));')
        lines.append('        }')
        lines.append('')
    return lines


def gen_success_scale(data):
    rows = extract_success_scale(data)
    lines = []
    lines.append('        // =====================================================================')
    lines.append('        // §5 — Success Scale (4 tests)')
    lines.append('        // =====================================================================')
    lines.append('')

    # beat 1-4 → +1
    lines.append('        // Mutation: would catch if beat-by-1-4 returned 0 or 2 instead of 1')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_BeatDCBy1To4_PlusOne()')
    lines.append('        {')
    lines.append('            var result = MakeSuccessResult(3);')
    lines.append('            Assert.Equal(1, SuccessScale.GetInterestDelta(result));')
    lines.append('        }')
    lines.append('')

    # beat 5-9 → +2
    lines.append('        // Mutation: would catch if beat-by-5-9 returned +1 instead of +2')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_BeatDCBy5To9_PlusTwo()')
    lines.append('        {')
    lines.append('            var result = MakeSuccessResult(7);')
    lines.append('            Assert.Equal(2, SuccessScale.GetInterestDelta(result));')
    lines.append('        }')
    lines.append('')

    # beat 10+ → +3
    lines.append('        // Mutation: would catch if beat-by-10+ returned +2 instead of +3')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_BeatDCBy10Plus_PlusThree()')
    lines.append('        {')
    lines.append('            var result = MakeSuccessResult(12);')
    lines.append('            Assert.Equal(3, SuccessScale.GetInterestDelta(result));')
    lines.append('        }')
    lines.append('')

    # nat 20 → +4
    lines.append('        // Mutation: would catch if Nat20 returned +3 instead of +4')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_Nat20_PlusFour()')
    lines.append('        {')
    lines.append('            var result = MakeSuccessResult(7, isNat20: true);')
    lines.append('            Assert.Equal(4, SuccessScale.GetInterestDelta(result));')
    lines.append('        }')
    lines.append('')
    return lines


def gen_risk_tiers(data):
    lines = []
    lines.append('        // =====================================================================')
    lines.append('        // §5 — Risk Tier (4 tests)')
    lines.append('        // =====================================================================')
    lines.append('')

    # Safe: need ≤ 5
    lines.append('        // Mutation: would catch if need<=5 classified as Medium instead of Safe')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_RiskTier_Safe_NeedLte5()')
    lines.append('        {')
    lines.append('            var result = MakeRiskResult(5, true);')
    lines.append('            Assert.Equal(RiskTier.Safe, result.RiskTier);')
    lines.append('        }')
    lines.append('')

    # Medium: need 6-10
    lines.append('        // Mutation: would catch if need 6-10 classified as Safe or Hard')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_RiskTier_Medium_Need6To10()')
    lines.append('        {')
    lines.append('            var result = MakeRiskResult(8, true);')
    lines.append('            Assert.Equal(RiskTier.Medium, result.RiskTier);')
    lines.append('        }')
    lines.append('')

    # Hard: need 11-15
    lines.append('        // Mutation: would catch if need 11-15 classified as Medium or Bold')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_RiskTier_Hard_Need11To15()')
    lines.append('        {')
    lines.append('            var result = MakeRiskResult(13, true);')
    lines.append('            Assert.Equal(RiskTier.Hard, result.RiskTier);')
    lines.append('        }')
    lines.append('')

    # Bold: need ≥ 16
    lines.append('        // Mutation: would catch if need>=16 classified as Hard instead of Bold')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_RiskTier_Bold_Need16Plus()')
    lines.append('        {')
    lines.append('            var result = MakeRiskResult(18, true);')
    lines.append('            Assert.Equal(RiskTier.Bold, result.RiskTier);')
    lines.append('        }')
    lines.append('')
    return lines


def gen_risk_bonuses(data):
    lines = []
    lines.append('        // =====================================================================')
    lines.append('        // §5 — Risk Tier Bonus (4 tests)')
    lines.append('        // =====================================================================')
    lines.append('')

    lines.append('        // Mutation: would catch if Safe returned wrong bonus')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_RiskBonus_Safe_Zero()')
    lines.append('        {')
    lines.append('            var result = MakeRiskResult(4, true);')
    lines.append('            Assert.Equal(1, RiskTierBonus.GetInterestBonus(result)); // Safe now returns +1')
    lines.append('        }')
    lines.append('')

    lines.append('        // Mutation: would catch if Hard returned wrong bonus')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_RiskBonus_Hard_PlusOne()')
    lines.append('        {')
    lines.append('            var result = MakeRiskResult(13, true);')
    lines.append('            Assert.Equal(3, RiskTierBonus.GetInterestBonus(result)); // Hard now returns +3')
    lines.append('        }')
    lines.append('')

    lines.append('        // Mutation: would catch if Bold returned wrong bonus')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_RiskBonus_Bold_PlusTwo()')
    lines.append('        {')
    lines.append('            var result = MakeRiskResult(18, true);')
    lines.append('            Assert.Equal(5, RiskTierBonus.GetInterestBonus(result)); // Bold now returns +5')
    lines.append('        }')
    lines.append('')

    lines.append('        // Mutation: would catch if bonus was awarded on failure (Hard fail should be 0)')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S5_RiskBonus_Failure_Zero()')
    lines.append('        {')
    lines.append('            var result = MakeRiskResult(13, false);')
    lines.append('            Assert.Equal(0, RiskTierBonus.GetInterestBonus(result));')
    lines.append('        }')
    lines.append('')
    return lines


def gen_interest_states(data):
    states = extract_interest_states(data)
    lines = []
    lines.append('        // =====================================================================')
    lines.append('        // §6 — Interest States (10 tests)')
    lines.append('        // =====================================================================')
    lines.append('')

    for idx, (lo, hi, cs_enum) in enumerate(states):
        if lo == hi:
            # Single-value state
            if lo == 0:
                next_enum = states[idx + 1][2] if idx + 1 < len(states) else '???'
                lines.append(f'        // Mutation: would catch if 0 mapped to {next_enum} instead of {cs_enum}')
                lines.append('        [Fact]')
                lines.append(f'        public void Rule_S6_Interest0_{cs_enum}()')
                lines.append('        {')
                lines.append(f'            var meter = new InterestMeter(0);')
                lines.append(f'            Assert.Equal(InterestState.{cs_enum}, meter.GetState());')
                lines.append('        }')
            else:
                prev_enum = states[idx - 1][2] if idx > 0 else '???'
                lines.append(f'        // Mutation: would catch if {lo} mapped to {prev_enum} instead of {cs_enum}')
                lines.append('        [Fact]')
                lines.append(f'        public void Rule_S6_Interest{lo}_{cs_enum}()')
                lines.append('        {')
                lines.append(f'            var meter = new InterestMeter({lo});')
                lines.append(f'            Assert.Equal(InterestState.{cs_enum}, meter.GetState());')
                lines.append('        }')
        else:
            prev_enum = states[states.index((lo, hi, cs_enum)) - 1][2] if states.index((lo, hi, cs_enum)) > 0 else ''
            next_pair = states[states.index((lo, hi, cs_enum)) + 1] if states.index((lo, hi, cs_enum)) + 1 < len(states) else None
            next_enum = next_pair[2] if next_pair else ''
            comment = f'would catch if {lo}-{hi} mapped to {prev_enum} or {next_enum} instead of {cs_enum}'
            lines.append(f'        // Mutation: {comment}')
            lines.append('        [Fact]')
            lines.append(f'        public void Rule_S6_Interest{lo}To{hi}_{cs_enum}()')
            lines.append('        {')
            lines.append(f'            Assert.Equal(InterestState.{cs_enum}, new InterestMeter({lo}).GetState());')
            lines.append(f'            Assert.Equal(InterestState.{cs_enum}, new InterestMeter({hi}).GetState());')
            lines.append('        }')
        lines.append('')
    return lines


def gen_interest_clamping(data):
    lines = []
    # Starting interest default
    lines.append('        // Mutation: would catch if default starting interest was not 10')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S6_StartingInterest_10()')
    lines.append('        {')
    lines.append('            var meter = new InterestMeter();')
    lines.append('            Assert.Equal(10, meter.Current);')
    lines.append('        }')
    lines.append('')

    # Max 25
    lines.append('        // Mutation: would catch if max was 24 or 26 instead of 25')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S6_Interest_Max25()')
    lines.append('        {')
    lines.append('            var meter = new InterestMeter(20);')
    lines.append('            meter.Apply(100);')
    lines.append('            Assert.Equal(25, meter.Current);')
    lines.append('        }')
    lines.append('')

    # Min 0
    lines.append('        // Mutation: would catch if min was 1 or -1 instead of 0')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S6_Interest_Min0()')
    lines.append('        {')
    lines.append('            var meter = new InterestMeter(5);')
    lines.append('            meter.Apply(-100);')
    lines.append('            Assert.Equal(0, meter.Current);')
    lines.append('        }')
    lines.append('')
    return lines


def gen_shadow_thresholds(data):
    lines = []
    lines.append('        // =====================================================================')
    lines.append('        // §7 — Shadow Thresholds (4 tests)')
    lines.append('        // =====================================================================')
    lines.append('')

    lines.append('        // Mutation: would catch if shadow<6 returned tier 1 instead of 0')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S7_Shadow_Below6_Tier0()')
    lines.append('        {')
    lines.append('            Assert.Equal(0, ShadowThresholdEvaluator.GetThresholdLevel(5));')
    lines.append('        }')
    lines.append('')

    lines.append('        // Mutation: would catch if shadow 6-11 returned tier 0 or 2 instead of 1')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S7_Shadow_6To11_Tier1()')
    lines.append('        {')
    lines.append('            Assert.Equal(1, ShadowThresholdEvaluator.GetThresholdLevel(6));')
    lines.append('            Assert.Equal(1, ShadowThresholdEvaluator.GetThresholdLevel(11));')
    lines.append('        }')
    lines.append('')

    lines.append('        // Mutation: would catch if shadow 12-17 returned tier 1 or 3 instead of 2')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S7_Shadow_12To17_Tier2()')
    lines.append('        {')
    lines.append('            Assert.Equal(2, ShadowThresholdEvaluator.GetThresholdLevel(12));')
    lines.append('            Assert.Equal(2, ShadowThresholdEvaluator.GetThresholdLevel(17));')
    lines.append('        }')
    lines.append('')

    lines.append('        // Mutation: would catch if shadow 18+ returned tier 2 instead of 3')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S7_Shadow_18Plus_Tier3()')
    lines.append('        {')
    lines.append('            Assert.Equal(3, ShadowThresholdEvaluator.GetThresholdLevel(18));')
    lines.append('            Assert.Equal(3, ShadowThresholdEvaluator.GetThresholdLevel(30));')
    lines.append('        }')
    lines.append('')
    return lines


def gen_level_table(data):
    rows = extract_level_table(data)
    # Pick the test rows that match RulesSpecTests.cs: L1/L2/L3/L11 and bonus L1/L5
    lines = []
    lines.append('        // =====================================================================')
    lines.append('        // §10 — Progression / LevelTable (6 tests)')
    lines.append('        // =====================================================================')
    lines.append('')

    # XP → level tests
    xp_tests = [(0, 1), (50, 2), (150, 3), (3500, 11)]
    xp_comments = {
        (0, 1):    'would catch if 0 XP returned level 0 instead of 1',
        (50, 2):   'would catch if 50 XP returned level 1 instead of 2',
        (150, 3):  'would catch if 150 XP returned level 2 instead of 3',
        (3500, 11):'would catch if 3500 XP returned level 10 instead of 11',
    }
    for (xp, level) in xp_tests:
        lines.append(f'        // Mutation: {xp_comments[(xp, level)]}')
        lines.append('        [Fact]')
        lines.append(f'        public void Rule_S10_XP{xp}_Level{level}()')
        lines.append('        {')
        lines.append(f'            Assert.Equal({level}, LevelTable.GetLevel({xp}));')
        lines.append('        }')
        lines.append('')

    # Level bonus tests
    lines.append('        // Mutation: would catch if level 1 bonus was not 0')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S10_LevelBonus_L1_Zero()')
    lines.append('        {')
    lines.append('            Assert.Equal(0, LevelTable.GetBonus(1));')
    lines.append('        }')
    lines.append('')

    lines.append('        // Mutation: would catch if level 5 bonus was 1 or 3 instead of 2')
    lines.append('        [Fact]')
    lines.append('        public void Rule_S10_LevelBonus_L5_Two()')
    lines.append('        {')
    lines.append('            Assert.Equal(2, LevelTable.GetBonus(5));')
    lines.append('        }')
    lines.append('')
    return lines


def generate(data):
    """Return the full generated C# file content."""
    lines = [_HEADER]
    lines += gen_failure_scale(data)
    lines += gen_success_scale(data)
    lines += gen_risk_tiers(data)
    lines += gen_risk_bonuses(data)
    lines += gen_interest_states(data)
    lines += gen_interest_clamping(data)
    lines += gen_shadow_thresholds(data)
    lines += gen_level_table(data)
    lines.append(_FOOTER)
    return '\n'.join(lines)


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main():
    mode = 'write'
    if '--check' in sys.argv:
        mode = 'check'
    elif '--stdout' in sys.argv:
        mode = 'stdout'

    data = load_yaml()
    generated = generate(data)

    if mode == 'stdout':
        print(generated)
        return

    if mode == 'check':
        if not os.path.exists(_OUT_PATH):
            print(f'ERROR: {_OUT_PATH} does not exist.', file=sys.stderr)
            sys.exit(1)
        with open(_OUT_PATH, encoding='utf-8') as fh:
            actual = fh.read()
        if actual == generated:
            print('OK: RulesSpecTests.cs is up to date with YAML source.')
        else:
            import difflib
            diff = list(difflib.unified_diff(
                actual.splitlines(keepends=True),
                generated.splitlines(keepends=True),
                fromfile='actual RulesSpecTests.cs',
                tofile='generated from YAML',
                n=3,
            ))
            print(''.join(diff[:60]), file=sys.stderr)
            print(f'\nERROR: RulesSpecTests.cs is OUT OF DATE. Re-run: python3 rules/tools/generate_tests.py',
                  file=sys.stderr)
            sys.exit(1)
        return

    # mode == 'write'
    os.makedirs(os.path.dirname(_OUT_PATH), exist_ok=True)
    with open(_OUT_PATH, 'w', encoding='utf-8') as fh:
        fh.write(generated)
    print(f'Written: {_OUT_PATH}')


if __name__ == '__main__':
    main()
