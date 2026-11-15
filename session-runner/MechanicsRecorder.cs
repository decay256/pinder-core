using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Collects what the engine decided on each turn and writes it as JSON
    /// (<c>--mechanics-out</c>). The tone lab reads this file so that the dice in a
    /// simulated chat are the game's dice, not a second implementation of the rules.
    /// </summary>
    public sealed class MechanicsRecorder
    {
        private readonly Dictionary<string, object?> _header;
        private readonly List<Dictionary<string, object?>> _turns = new List<Dictionary<string, object?>>();

        private MechanicsRecorder(Dictionary<string, object?> header) { _header = header; }

        internal static MechanicsRecorder FromSetup(GameSetupResult setup, int? seed)
        {
            var header = new Dictionary<string, object?>
            {
                ["schema"] = "pinder-mechanics/1",
                ["mechanics_only"] = setup.MechanicsOnly,
                ["seed"] = seed,
                ["clock"] = setup.Clock?.Now.ToString("o"),
                ["time_of_day"] = setup.Clock?.GetTimeOfDay().ToString(),
                ["max_turns"] = setup.MaxTurns,
                ["starting_interest"] = setup.Interest,
                ["traps_disabled"] = setup.TrapsDisabled,
                ["session_horniness"] = setup.Session.SessionHorniness,
                ["horniness_roll_1d10"] = setup.Session.HorninessRoll,
                ["horniness_time_modifier"] = setup.Session.HorninessTimeModifier,
                ["player"] = Character(setup.Sable, setup.SableStats, setup.P1LevelBonus),
                ["datee"] = Character(setup.Brick, setup.BrickStats, setup.P2LevelBonus),
            };
            return new MechanicsRecorder(header);
        }

        private static Dictionary<string, object?> Character(CharacterProfile c, StatBlock stats, int levelBonus)
        {
            var effective = new Dictionary<string, int>();
            var defenceDc = new Dictionary<string, int>();
            foreach (StatType s in Enum.GetValues(typeof(StatType)))
            {
                effective[s.ToString()] = stats.GetEffective(s);
                defenceDc[s.ToString()] = stats.GetDefenceDC(s);
            }
            return new Dictionary<string, object?>
            {
                ["display_name"] = c.DisplayName,
                ["level"] = c.Level,
                ["level_bonus"] = levelBonus,
                ["effective_stats"] = effective,
                ["defence_dc_against"] = defenceDc,
            };
        }

        internal void RecordTurn(int turn, TurnStart start, DialogueOption chosen, TurnResult r)
        {
            var roll = r.Roll;
            var h = r.HorninessCheck;
            var sh = r.ShadowCheck;
            var st = r.Steering;
            bool hornPerformed = h != null && !ReferenceEquals(h, HorninessCheckResult.NotPerformed);
            bool shadowPerformed = sh != null && sh.CheckPerformed;

            _turns.Add(new Dictionary<string, object?>
            {
                ["turn"] = turn,
                ["interest_before"] = start.State.Interest,
                ["state_before"] = start.State.State.ToString(),
                ["active_traps_before"] = start.State.ActiveTrapNames,
                ["options"] = start.Options.Select(o => o.Stat.ToString()).ToArray(),
                ["chosen_stat"] = chosen.Stat.ToString(),
                ["roll"] = roll == null ? null : new Dictionary<string, object?>
                {
                    ["dice"] = roll.SecondDieRoll.HasValue ? new[] { roll.DieRoll, roll.SecondDieRoll.Value } : new[] { roll.DieRoll },
                    ["used_die"] = roll.UsedDieRoll,
                    ["stat_modifier"] = roll.StatModifier,
                    ["level_bonus"] = roll.LevelBonus,
                    ["external_bonus"] = roll.ExternalBonus,
                    ["total"] = roll.FinalTotal,
                    ["dc"] = roll.DC,
                    ["defending_stat"] = roll.DefendingStat.ToString(),
                    ["success"] = roll.IsSuccess,
                    ["nat1"] = roll.IsNatOne,
                    ["nat20"] = roll.IsNatTwenty,
                    ["tier"] = roll.Tier.ToString(),
                    ["final_tier"] = roll.Check?.FinalTier.ToString(),
                    ["miss_margin"] = roll.MissMargin,
                    ["risk_tier"] = roll.RiskTier.ToString(),
                    ["activated_trap"] = roll.ActivatedTrap?.DisplayName,
                    ["consequence"] = roll.Check?.Consequence,
                },
                ["horniness"] = !hornPerformed ? null : new Dictionary<string, object?>
                {
                    ["roll"] = h!.Roll,
                    ["modifier"] = h.Modifier,
                    ["total"] = h.Total,
                    ["dc"] = h.DC,
                    ["miss"] = h.IsMiss,
                    ["tier"] = h.Tier.ToString(),
                    ["overlay_applied"] = h.OverlayApplied,
                    ["consequence"] = h.Consequence,
                },
                ["shadow"] = !shadowPerformed ? null : new Dictionary<string, object?>
                {
                    ["shadow"] = sh!.Shadow.ToString(),
                    ["roll"] = sh.Roll,
                    ["dc"] = sh.DC,
                    ["miss"] = sh.IsMiss,
                    ["tier"] = sh.Tier.ToString(),
                    ["overlay_applied"] = sh.OverlayApplied,
                    ["consequence"] = sh.Consequence,
                },
                ["steering"] = st == null || !st.SteeringAttempted ? null : new Dictionary<string, object?>
                {
                    ["succeeded"] = st.SteeringSucceeded,
                    ["roll"] = st.SteeringRoll,
                    ["modifier"] = st.SteeringMod,
                    ["dc"] = st.SteeringDC,
                },
                ["interest_delta"] = r.InterestDelta,
                ["base_interest_delta"] = r.BaseInterestDelta,
                ["breakdown"] = r.InterestBreakdown?.Select(b => new Dictionary<string, object?>
                {
                    ["source"] = b.Source,
                    ["label"] = b.Label,
                    ["delta"] = b.Delta,
                }).ToList(),
                ["combo"] = r.ComboTriggered,
                ["trap_cleared"] = r.TrapClearedDisplayName,
                ["shadow_growth"] = r.ShadowGrowthEvents,
                ["interest_after"] = r.StateAfter.Interest,
                ["state_after"] = r.StateAfter.State.ToString(),
                ["momentum_after"] = r.StateAfter.MomentumStreak,
                ["active_traps_after"] = r.StateAfter.ActiveTrapNames,
                ["game_over"] = r.IsGameOver,
                ["outcome"] = r.Outcome?.ToString(),
            });
        }

        internal void Write(string path, GameLoopResult loop)
        {
            string? lastOutcome = _turns.Count > 0 ? _turns[_turns.Count - 1]["outcome"] as string : null;
            var doc = new Dictionary<string, object?>(_header)
            {
                ["turns"] = _turns,
                ["turns_played"] = _turns.Count,
                ["final_interest"] = loop.Interest,
                // null outcome = the turn cap was reached with no terminal state; the
                // engine has no "lost interest at the cap" outcome.
                ["outcome"] = loop.FinalOutcome?.ToString() ?? lastOutcome,
            };
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine("Mechanics written to " + path);
        }
    }
}
