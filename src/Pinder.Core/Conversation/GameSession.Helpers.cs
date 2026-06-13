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
        /// #788: datee-LLM conversation history owned by the engine. Each
        /// entry's role is <c>"user"</c> or <c>"assistant"</c>. Read-only view
        /// over the live mutable list so callers see updates as turns resolve.
        /// Survives snapshot/restore via
        /// <see cref="ResimulateData.DateeHistory"/>.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<ConversationMessage> DateeHistory
            => _dateeHistory;

        /// <summary>
        /// #1123: avatar-LLM conversation history owned by the engine, the
        /// symmetric sibling of <see cref="DateeHistory"/>. Each entry's role is
        /// <c>"user"</c> or <c>"assistant"</c>. Read-only view over the live
        /// mutable list so callers see updates as turns resolve. Survives
        /// snapshot/restore via <see cref="ResimulateData.AvatarHistory"/>.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<ConversationMessage> AvatarHistory
            => _avatarHistory;

        /// <summary>
        /// Build the conversation history view fed to subsequent LLM calls.
        /// Excludes synthetic scene-setting entries (issue #333) so the
        /// matchup analyser / delivery LLM / datee-response LLM never
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
        /// (player bio, datee bio, LLM-generated outfit description) to
        /// the conversation log BEFORE the first player turn. Sender for
        /// each entry is <see cref="Senders.Scene"/>; the frontend renders
        /// these distinctly from player/datee dialogue.
        /// </summary>
        /// <param name="playerBio">Player bio text. Empty entries are skipped.</param>
        /// <param name="dateeBio">Datee bio text. Empty entries are skipped.</param>
        /// <param name="outfitDescription">LLM-generated outfit description. Empty entries are skipped.</param>
        /// <exception cref="InvalidOperationException">If any turn has already been resolved.</exception>
        public void SeedSceneEntries(string? playerBio, string? dateeBio, string? outfitDescription)
        {
            if (_turnNumber > 0)
            {
                throw new InvalidOperationException(
                    "SeedSceneEntries must be called before the first turn is resolved.");
            }
            if (!string.IsNullOrWhiteSpace(playerBio))
                _history.Add(($"{Senders.Scene}:{_player.DisplayName}", playerBio!.Trim()));
            if (!string.IsNullOrWhiteSpace(dateeBio))
                _history.Add(($"{Senders.Scene}:{_datee.DisplayName}", dateeBio!.Trim()));
            if (!string.IsNullOrWhiteSpace(outfitDescription))
            {
                string trimmed = outfitDescription!.Trim();
                _history.Add((Senders.Scene, trimmed));
                // #562: also retain on the session so
                // BuildDateeVisibleProfile can surface it on every
                // dialogue-options call. Scene-history entries are
                // excluded from the LLM context view, so without this
                // field the player-LLM never sees the outfit.
                _dateeOutfitDescription = trimmed;
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
                _interest.GetState(),
                _momentumStreak,
                _traps,
                _turnNumber,
                _comboTracker.HasTripleBonus,
                _dateeHistory);
        }
    }
}
