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
        /// Issue #333: append the two turn-0 bio scene-setting entries
        /// (player bio, datee bio) to the conversation log BEFORE the
        /// first player turn. Sender for each entry is
        /// <see cref="Senders.Scene"/>; the frontend renders these
        /// distinctly from player/datee dialogue.
        /// </summary>
        /// <param name="playerBio">Player bio text. Empty entries are skipped.</param>
        /// <param name="dateeBio">Datee bio text. Empty entries are skipped.</param>
        /// <param name="outfitDescription">Legacy compatibility parameter. Ignored; equipped-items fallback now provides outfit context.</param>
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
            if (data == null) throw new ArgumentNullException(nameof(data));
            data.ValidateForRestore(_player.CharacterId, _datee.CharacterId);
            _state.RestoreFromSnapshot(data, trapRegistry);
        }

        /// <summary>
        /// Captures all committed engine state required to continue an active
        /// session in a fresh process. Prepared turn options and dice pools are
        /// intentionally excluded because they are uncommitted work.
        /// </summary>
        public ResimulateData CreateResimulateData()
        {
            var data = new ResimulateData
            {
                SchemaVersion = ResimulateData.CurrentSchemaVersion,
                PlayerCharacterId = _player.CharacterId,
                DateeCharacterId = _datee.CharacterId,
                TargetInterest = _interest.Current,
                TurnNumber = _turnNumber,
                MomentumStreak = _momentumStreak,
                ConversationHistory = new List<(string Sender, string Text)>(_history),
                ComboHistory = _comboTracker.CreateSnapshot().ToList(),
                PendingTripleBonus = _comboTracker.HasTripleBonus,
                RizzCumulativeFailureCount = _rizzCumulativeFailureCount,
                Topics = new List<CallbackOpportunity>(_topics),
                PendingMomentumBonus = _pendingMomentumBonus,
                DateeHistory = _dateeHistory.Select(message => (message.Role, message.Content)).ToList(),
                AvatarHistory = _avatarHistory.Select(message => (message.Role, message.Content)).ToList(),
                DateeSessionSnapshot = _state.DateeSessionSnapshot,
                AvatarSessionSnapshot = _state.AvatarSessionSnapshot,
                DateeOutfitDescription = _state.DateeOutfitDescription,
                AvatarSpentBackstoryIndices = new HashSet<int>(_state.AvatarSpentBackstoryIndices),
                AvatarSpentStakeIndices = new HashSet<int>(_state.AvatarSpentStakeIndices),
                AvatarPreviousPhase = _state.AvatarPreviousPhase,
                AvatarPreviousResolvedIndex = _state.AvatarPreviousResolvedIndex,
                CurrentAvatarRevelationTarget = _state.CurrentAvatarRevelationTarget,
                CurrentAvatarCognitiveSubtext = _state.CurrentAvatarCognitiveSubtext,
                CurrentAvatarCognitiveSubtextFact = _state.CurrentAvatarCognitiveSubtextFact,
                DateeSpentBackstoryIndices = new HashSet<int>(_state.DateeSpentBackstoryIndices),
                DateeSpentStakeIndices = new HashSet<int>(_state.DateeSpentStakeIndices),
                DateePreviousPhase = _state.DateePreviousPhase,
                DateePreviousResolvedIndex = _state.DateePreviousResolvedIndex,
                CurrentDateeReactionTarget = _state.CurrentDateeReactionTarget,
                CurrentDateeCognitiveSubtext = _state.CurrentDateeCognitiveSubtext,
                CurrentDateeCognitiveSubtextFact = _state.CurrentDateeCognitiveSubtextFact,
                SpentBackstoryIndices = new HashSet<int>(_state.AvatarSpentBackstoryIndices),
                SpentStakeIndices = new HashSet<int>(_state.AvatarSpentStakeIndices),
                PreviousPhase = _state.AvatarPreviousPhase,
                PreviousResolvedIndex = _state.AvatarPreviousResolvedIndex,
                CurrentResolvedTarget = null,
                CurrentCognitiveSubtext = _state.CurrentDateeCognitiveSubtext,
                XpEvents = _xpLedger.Events.Select(entry => (entry.Source, entry.Amount)).ToList(),
                SessionHorniness = _sessionHorniness,
                HorninessRoll = _horninessRoll,
                HorninessTimeModifier = _horninessTimeModifier,
                PendingCritAdvantage = _pendingCritAdvantage,
                LastStatUsed = _lastStatUsed,
                ActiveWeakness = _activeWeakness,
                ActiveTell = _activeTell,
            };

            foreach (var trap in _traps.AllActive)
                data.ActiveTraps.Add((trap.Definition.Stat.ToString(), trap.TurnsRemaining));
            if (_playerShadows != null)
            {
                foreach (ShadowStatType shadow in System.Enum.GetValues(typeof(ShadowStatType)))
                    data.ShadowValues[shadow.ToString()] = _playerShadows.GetEffectiveShadow(shadow);
            }
            if (_dateeShadows != null)
            {
                foreach (ShadowStatType shadow in System.Enum.GetValues(typeof(ShadowStatType)))
                    data.DateeShadowValues[shadow.ToString()] = _dateeShadows.GetEffectiveShadow(shadow);
            }
            return data;
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
                _dateeHistory,
                _avatarHistory,
                _playerShadows,
                _state.DateeSessionSnapshot,
                _state.AvatarSessionSnapshot,
                _state);
        }
    }
}
