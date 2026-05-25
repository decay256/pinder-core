using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.I18n;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Progression;
using Pinder.Core.Traps;
using Pinder.Core.Text;

namespace Pinder.Core.Conversation
{
    public sealed partial class GameSession
    {
        /// <summary>
        /// Register a conversation topic for future callback opportunities.
        /// Called by the host or LLM adapter after each turn to seed topics.
        /// </summary>
        /// <param name="topic">The topic to register. Must not be null.</param>
        /// <exception cref="ArgumentNullException">If topic is null.</exception>
        public void AddTopic(CallbackOpportunity topic)
        {
            if (topic == null)
                throw new ArgumentNullException(nameof(topic));
            _topics.Add(topic);
        }

        /// <summary>Total XP earned during this session.</summary>
        public int TotalXpEarned => _xpLedger.TotalXp;

        /// <summary>Current 0-based turn number. Incremented by ResolveTurnAsync.</summary>
        public int TurnNumber => _turnNumber;

        /// <summary>True after the session has reached a terminal <see cref="GameOutcome"/>.</summary>
        public bool IsEnded => _ended;

        /// <summary>Terminal outcome, or null while the session is still running.</summary>
        public GameOutcome? Outcome => _outcome;

        /// <summary>
        /// Restore an already-ended session from persisted state. Sets the
        /// terminal flags so subsequent <see cref="StartTurnAsync"/> throws
        /// <see cref="GameEndedException"/> with the right outcome.
        ///
        /// Intended for post-game replay/rehydration paths (e.g. loading a
        /// finished session back from storage). <see cref="RestoreState"/>
        /// targets mid-game resimulation and deliberately does not touch the
        /// terminal flags; callers reviving an ended session must call this
        /// in addition.
        /// </summary>
        /// <param name="outcome">The terminal <see cref="GameOutcome"/> the session ended with.</param>
        public void MarkEnded(GameOutcome outcome)
        {
            _ended = true;
            _outcome = outcome;
        }

        /// <summary>
        /// Conversation history as (sender, text) tuples, in emission order.
        /// Read-only snapshot view; safe to enumerate concurrently with session mutation
        /// since the underlying list is only appended during ResolveTurnAsync.
        /// </summary>
        /// <remarks>
        /// Includes any turn-0 scene-setting entries (issue #333) tagged with
        /// <see cref="Senders.Scene"/>. Callers that feed the history back
        /// into an LLM should use <see cref="BuildHistoryForLlmContext"/>
        /// instead so the analyzer/delivery LLM does not see the scene
        /// entries.
        /// </remarks>
        public System.Collections.Generic.IReadOnlyList<(string Sender, string Text)> ConversationHistory
            => _history;

        /// <summary>
        /// #788: opponent-LLM conversation history owned by the engine. Each
        /// entry's role is <c>"user"</c> or <c>"assistant"</c>. Read-only view
        /// over the live mutable list so callers see updates as turns resolve.
        /// Survives snapshot/restore via
        /// <see cref="ResimulateData.OpponentHistory"/>.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<ConversationMessage> OpponentHistory
            => _opponentHistory;

        /// <summary>
        /// Build the conversation history view fed to subsequent LLM calls.
        /// Excludes synthetic scene-setting entries (issue #333) so the
        /// matchup analyser / delivery LLM / opponent-response LLM never
        /// sees its own scene-description output as prior conversation.
        /// </summary>
        private System.Collections.Generic.IReadOnlyList<(string Sender, string Text)> BuildHistoryForLlmContext()
        {
            // Hot path: when there are no scene entries, return the full
            // list as-is so we don’t allocate a copy on every turn.
            bool anyScene = false;
            for (int i = 0; i < _history.Count; i++)
            {
                if (Senders.IsScene(_history[i].Sender)) { anyScene = true; break; }
            }
            if (!anyScene) return _history.AsReadOnly();

            var view = new List<(string Sender, string Text)>(_history.Count);
            for (int i = 0; i < _history.Count; i++)
            {
                var entry = _history[i];
                if (Senders.IsScene(entry.Sender)) continue;
                view.Add(entry);
            }
            return view.AsReadOnly();
        }

        /// <summary>
        /// Issue #333: append the three turn-0 scene-setting entries
        /// (player bio, opponent bio, LLM-generated outfit description) to
        /// the conversation log BEFORE the first player turn. Sender for
        /// each entry is <see cref="Senders.Scene"/>; the frontend renders
        /// these distinctly from player/opponent dialogue.
        /// </summary>
        /// <param name="playerBio">Player bio text. Empty entries are skipped.</param>
        /// <param name="opponentBio">Opponent bio text. Empty entries are skipped.</param>
        /// <param name="outfitDescription">LLM-generated outfit description. Empty entries are skipped.</param>
        /// <exception cref="InvalidOperationException">If any turn has already been resolved.</exception>
        public void SeedSceneEntries(string? playerBio, string? opponentBio, string? outfitDescription)
        {
            if (_turnNumber > 0)
            {
                throw new InvalidOperationException(
                    "SeedSceneEntries must be called before the first turn is resolved.");
            }
            if (!string.IsNullOrWhiteSpace(playerBio))
                _history.Add(($"{Senders.Scene}:{_player.DisplayName}", playerBio!.Trim()));
            if (!string.IsNullOrWhiteSpace(opponentBio))
                _history.Add(($"{Senders.Scene}:{_opponent.DisplayName}", opponentBio!.Trim()));
            if (!string.IsNullOrWhiteSpace(outfitDescription))
            {
                string trimmed = outfitDescription!.Trim();
                _history.Add((Senders.Scene, trimmed));
                // #562: also retain on the session so
                // BuildOpponentVisibleProfile can surface it on every
                // dialogue-options call. Scene-history entries are
                // excluded from the LLM context view, so without this
                // field the player-LLM never sees the outfit.
                _opponentOutfitDescription = trimmed;
            }
        }

        /// <summary>Session horniness value (d10 + clock modifier). Used for display.</summary>
        public int SessionHorniness => _sessionHorniness;

        /// <summary>Raw d10 roll used for session horniness.</summary>
        public int HorninessRoll => _horninessRoll;

        /// <summary>Time-of-day modifier applied to the horniness roll.</summary>
        public int HorninessTimeModifier => _horninessTimeModifier;

        /// <summary>The full XP ledger for this session.</summary>
        public XpLedger XpLedger => _xpLedger;

        /// <summary>
        /// Restores all mutable session state from a <see cref="ResimulateData"/> snapshot.
        /// Call this immediately after constructing a GameSession with the correct initial snapshot;
        /// the session must not have had any turns played.
        /// </summary>
        /// <param name="data">State data to restore.</param>
        /// <param name="trapRegistry">Used to look up trap definitions by stat.</param>
        public void RestoreState(ResimulateData data, ITrapRegistry trapRegistry)
        {
            _state.RestoreFromSnapshot(data, trapRegistry);
        }

        /// <summary>
        /// Get momentum bonus for the current streak length.
        /// Uses rule resolver if available, falls back to hardcoded values.
        /// 3-streak → +2, 4-streak → +2, 5+ → +3.
        /// </summary>
        private int GetMomentumBonus(int streak)
        {
            if (_rules != null)
            {
                var resolved = _rules.GetMomentumBonus(streak);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            if (streak >= 5) return 3;
            if (streak >= 3) return 2;
            return 0;
        }

        /// <summary>
        /// Get failure interest delta, using rule resolver if available.
        /// </summary>
        private int ResolveFailureInterestDelta(RollResult rollResult)
        {
            if (_rules != null)
            {
                var resolved = _rules.GetFailureInterestDelta(rollResult.MissMargin, rollResult.UsedDieRoll);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return FailureScale.GetInterestDelta(rollResult);
        }

        /// <summary>
        /// Get success interest delta, using rule resolver if available.
        /// </summary>
        private int ResolveSuccessInterestDelta(RollResult rollResult)
        {
            if (_rules != null)
            {
                int beatMargin = rollResult.FinalTotal - rollResult.DC;
                var resolved = _rules.GetSuccessInterestDelta(beatMargin, rollResult.UsedDieRoll);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return SuccessScale.GetInterestDelta(rollResult);
        }

        /// <summary>
        /// Get interest state, using rule resolver if available.
        /// </summary>
        private InterestState ResolveInterestState()
        {
            if (_rules != null)
            {
                var resolved = _rules.GetInterestState(_interest.Current);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return _interest.GetState();
        }

        /// <summary>
        /// Get shadow threshold level, using rule resolver if available.
        /// </summary>
        private int ResolveThresholdLevel(int shadowValue)
        {
            if (_rules != null)
            {
                var resolved = _rules.GetShadowThresholdLevel(shadowValue);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return ShadowThresholdEvaluator.GetThresholdLevel(shadowValue);
        }

        /// <summary>
        /// Build a fresh <see cref="GameStateSnapshot"/> for the current session state.
        /// Public so test/debug code can observe restored or mid-flight state without
        /// running a turn (e.g. the W2a #371 RestoreState round-trip tests).
        /// </summary>
        public GameStateSnapshot CreateSnapshot()
        {
            return GameSessionHelpers.CreateSnapshot(
                _interest,
                ResolveInterestState(),
                _momentumStreak,
                _traps,
                _turnNumber,
                _comboTracker.HasTripleBonus,
                _opponentHistory);
        }

        /// <summary>
        /// Returns the shadow stat paired with the given positive stat, or null if unrecognised.
        /// </summary>
        private static ShadowStatType? GetPairedShadow(StatType stat)
        {
            switch (stat)
            {
                case StatType.Charm:         return ShadowStatType.Madness;
                case StatType.Rizz:          return ShadowStatType.Despair;
                case StatType.Honesty:       return ShadowStatType.Denial;
                case StatType.Chaos:         return ShadowStatType.Fixation;
                case StatType.Wit:           return ShadowStatType.Dread;
                case StatType.SelfAwareness: return ShadowStatType.Overthinking;
                default:                     return null;
            }
        }

        /// <summary>
        /// Creates a synthetic RollResult that represents a forced failure at the given tier.
        /// Used by the shadow check to compute the failure interest delta when overriding a success.
        /// </summary>
        /// <remarks>
        /// #920: explicitly synthesises a <see cref="Pinder.Core.Rolls.RollCheckResult"/> via
        /// <see cref="Pinder.Core.Rolls.RollCheckResult.Synthesise"/> so the returned
        /// <see cref="Pinder.Core.Rolls.RollResult.Check"/> is never null — prerequisite for the
        /// Phase 2 wire-DTO serializer which will read <c>Check.*</c>.
        /// </remarks>
        private static RollResult CreateForcedFailResult(RollResult original, FailureTier shadowTier)
        {
            // Build a result that looks like a miss at the given tier.
            // We derive a miss margin that maps to the tier, then compute a die roll that misses DC.
            int fakeDie = original.DC > 1 ? original.DC - 1 : 1; // just below DC
            var check = Pinder.Core.Rolls.RollCheckResult.Synthesise(
                dieRoll:       fakeDie,
                secondDieRoll: null,
                usedDieRoll:   fakeDie,
                statModifier:  0,
                levelBonus:    0,
                dc:            original.DC);
            return new RollResult(
                dieRoll:        fakeDie,
                secondDieRoll:  null,
                usedDieRoll:    fakeDie,
                stat:           original.Stat,
                statModifier:   0,
                levelBonus:     0,
                dc:             original.DC,
                tier:           shadowTier,
                activatedTrap:  null,
                externalBonus:  0,
                check:          check,
                defendingStat:  Pinder.Core.Stats.StatBlock.DefenceTable[original.Stat]);
        }

        /// <summary>
        /// Builds a compact opponent context string for the horniness overlay system prompt.
        /// Format: Opponent: [DisplayName] | Bio: "[bio]" | Wearing: [items]
        /// </summary>
        private static string BuildOpponentContext(CharacterProfile opponent)
        {
            if (opponent == null) return string.Empty;
            string bio = string.IsNullOrWhiteSpace(opponent.Bio) ? "(no bio)" : opponent.Bio;
            string items = opponent.EquippedItemDisplayNames != null && opponent.EquippedItemDisplayNames.Count > 0
                ? string.Join(", ", opponent.EquippedItemDisplayNames)
                : "(none)";
            return $"Opponent: {opponent.DisplayName} | Bio: \"{bio}\" | Wearing: {items}";
        }

        // ── #314: text-layer no-op breadcrumb ─────────────────────────────

        /// <summary>
        /// Issue #314: emit a structured event when a text-transform layer
        /// (Horniness / Shadow / Trap overlay) ran an LLM call but produced
        /// byte-identical output. The diff is silently dropped from
        /// <c>TextDiffs</c> in that case (correctly — there's nothing to
        /// render), but without this breadcrumb the audit cannot tell
        /// "layer ran and produced no delta" apart from "layer didn't run
        /// at all". Hosts that wire <c>OnTextLayerNoop</c> can log a
        /// structured INFO line with <c>{turn, layer, before_hash,
        /// after_hash}</c>.
        /// </summary>
        private void EmitTextLayerNoop(string layer, string beforeText, string afterText)
        {
            if (_onTextLayerNoop == null) return;
            try
            {
                string beforeHash = ComputeStableHash(beforeText);
                string afterHash = ComputeStableHash(afterText);
                _onTextLayerNoop(new TextLayerNoopEvent(_turnNumber, layer, beforeHash, afterHash));
            }
            catch
            {
                // Diagnostic-only path — never let a logging failure break
                // the turn. Swallow and move on.
            }
        }

        /// <summary>
        /// Stable, non-cryptographic, run-independent hash for the layer-noop
        /// breadcrumb. Uses SHA-256 truncated to 16 hex chars; the value is
        /// an audit identifier, not a security primitive.
        /// </summary>
        private static string ComputeStableHash(string? text)
        {
            if (text == null) return "";
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                var sb = new System.Text.StringBuilder(16);
                for (int i = 0; i < 8 && i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}