using System;
using System.Collections.Generic;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Carries all the mid-session state needed to restore a GameSession for resimulation.
    /// Populated from a TurnSnapshot by the session runner. All fields use plain types
    /// so that Pinder.Core does not reference Pinder.SessionRunner.
    /// </summary>
    public sealed class ResimulateData
    {
        public const int IdentityBackedLegacySchemaVersion = 1;
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion { get; set; }
        public Guid PlayerCharacterId { get; set; }
        public Guid DateeCharacterId { get; set; }

        public ResimulateData()
        {
        }

        public ResimulateData(Guid playerCharacterId, Guid dateeCharacterId)
        {
            SchemaVersion = CurrentSchemaVersion;
            PlayerCharacterId = playerCharacterId;
            DateeCharacterId = dateeCharacterId;
        }

        public void ValidateForRestore(Guid expectedPlayerCharacterId, Guid expectedDateeCharacterId)
        {
            if (SchemaVersion == 0)
                throw new InvalidOperationException("restore.schema_version.required");
            if (SchemaVersion != IdentityBackedLegacySchemaVersion && SchemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException("restore.schema_version.unsupported");
            if (PlayerCharacterId == Guid.Empty || DateeCharacterId == Guid.Empty)
                throw new InvalidOperationException("restore.character_identity.required");
            if (PlayerCharacterId != expectedPlayerCharacterId || DateeCharacterId != expectedDateeCharacterId)
                throw new InvalidOperationException("restore.character_identity.mismatch");
            if (SchemaVersion == IdentityBackedLegacySchemaVersion
                && (CurrentResolvedTarget != null
                    || CurrentAvatarRevelationTarget != null
                    || CurrentDateeReactionTarget != null))
                throw new InvalidOperationException("restore.schema_version.legacy_active_target_forbidden");
            if (DateeResponsePlanReplaySelection != null
                && (LastAcceptedDateeResponsePlanState == null
                    || LastDateeResponseReplayState == null
                    || !DateeResponsePlanReplaySelection.Selects(
                        LastAcceptedDateeResponsePlanState,
                        LastAcceptedDateeResponsePlanState.VisibleMessageText)))
            {
                throw new InvalidOperationException("restore.datee_response_plan_replay.identity.mismatch");
            }
            if (DateeResponsePlanReplaySelection != null)
                LastDateeResponseReplayState!.ValidateAgainst(LastAcceptedDateeResponsePlanState!);
        }
        /// <summary>Interest to restore (absolute value, not a delta).</summary>
        public int TargetInterest { get; set; }

        /// <summary>Turn number at the time of the snapshot.</summary>
        public int TurnNumber { get; set; }

        /// <summary>Momentum streak to restore.</summary>
        public int MomentumStreak { get; set; }

        /// <summary>
        /// Effective shadow values by shadow stat name.
        /// Key = ShadowStatType.ToString(), Value = effective total (base + in-session growth).
        /// </summary>
        public Dictionary<string, int> ShadowValues { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Active traps: (stat name as StatType.ToString(), turns remaining).
        /// </summary>
        public List<(string TrapStat, int TurnsRemaining)> ActiveTraps { get; set; } = new List<(string, int)>();

        /// <summary>Full conversation history: (sender, text) pairs in chronological order.</summary>
        public List<(string Sender, string Text)> ConversationHistory { get; set; } = new List<(string, string)>();

        /// <summary>
        /// Combo history window (last up-to-3 turns): (stat name as StatType.ToString(), succeeded).
        /// </summary>
        public List<(string StatName, bool Succeeded)> ComboHistory { get; set; } = new List<(string, bool)>();

        /// <summary>Whether The Triple bonus is pending for the next roll.</summary>
        public bool PendingTripleBonus { get; set; }

        /// <summary>Cumulative Rizz failure count for Despair shadow tracking.</summary>
        public int RizzCumulativeFailureCount { get; set; }
        public List<CallbackOpportunity> Topics { get; set; } = new List<CallbackOpportunity>();
        public int PendingMomentumBonus { get; set; }

        /// <summary>
        /// Engine-owned datee LLM conversation history (#788). Each entry is
        /// a (role, content) pair where role is <c>"user"</c> or
        /// <c>"assistant"</c>. Survives snapshot/restore so a replayed session
        /// can reproduce the same multi-turn datee context the original ran
        /// with. Empty list = no prior turns.
        /// </summary>
        public List<(string Role, string Content)> DateeHistory { get; set; } = new List<(string, string)>();

        public List<CharacterEmotionalDirectionSummary> DateeEmotionalDirectionHistory { get; set; } = new List<CharacterEmotionalDirectionSummary>();
        public DateeResponsePlan? LastAcceptedDateeResponsePlan { get; set; }
        public AcceptedDateeResponsePlanState? LastAcceptedDateeResponsePlanState { get; set; }
        public DateeResponseReplayState? LastDateeResponseReplayState { get; set; }
        public DateeResponsePlanReplaySelection? DateeResponsePlanReplaySelection { get; set; }

        /// <summary>
        /// Engine-owned avatar LLM conversation history (#1123) — the symmetric
        /// sibling of <see cref="DateeHistory"/>. Each entry is a (role, content)
        /// pair where role is <c>"user"</c> or <c>"assistant"</c>. Survives
        /// snapshot/restore so a replayed session reproduces the same multi-turn
        /// avatar context the original ran with. Empty list = no prior turns.
        ///
        /// <para>
        /// Deploy-ordering note (#1129): this is a NEW persisted field added with
        /// NO back-compat, on the same convention #1121 used for the renamed
        /// persisted keys — the data wipe is owned by #1129. Pre-#1123 snapshots
        /// deserialise this as an empty list (the replay then starts the avatar
        /// session statelessly from turn 1, which is the correct degraded
        /// behaviour for old snapshots).
        /// </para>
        /// </summary>
        public List<(string Role, string Content)> AvatarHistory { get; set; } = new List<(string, string)>();

        public LlmConversationSessionSnapshot? DateeSessionSnapshot { get; set; }

        public LlmConversationSessionSnapshot? AvatarSessionSnapshot { get; set; }

        public string DateeOutfitDescription { get; set; } = string.Empty;
        public HashSet<int>? AvatarSpentBackstoryIndices { get; set; }
        public HashSet<int>? AvatarSpentStakeIndices { get; set; }
        public string? AvatarPreviousPhase { get; set; }
        public int AvatarPreviousResolvedIndex { get; set; }
        public AvatarRevelationTarget? CurrentAvatarRevelationTarget { get; set; }
        public string? CurrentAvatarCognitiveSubtext { get; set; }
        public OwnedPromptFactV1? CurrentAvatarCognitiveSubtextFact { get; set; }
        public HashSet<int>? DateeSpentBackstoryIndices { get; set; }
        public HashSet<int>? DateeSpentStakeIndices { get; set; }
        public string? DateePreviousPhase { get; set; }
        public int DateePreviousResolvedIndex { get; set; }
        public DateeReactionTarget? CurrentDateeReactionTarget { get; set; }
        public string? CurrentDateeCognitiveSubtext { get; set; }
        public OwnedPromptFactV1? CurrentDateeCognitiveSubtextFact { get; set; }
        public HashSet<int> SpentBackstoryIndices { get; set; } = new HashSet<int>();
        public HashSet<int> SpentStakeIndices { get; set; } = new HashSet<int>();
        public string? PreviousPhase { get; set; }
        public int PreviousResolvedIndex { get; set; }
        public ResolvedRevelationTarget? CurrentResolvedTarget { get; set; }
        public string? CurrentCognitiveSubtext { get; set; }
        public List<(string Source, int Amount)> XpEvents { get; set; } = new List<(string, int)>();
        public int SessionHorniness { get; set; }
        public int HorninessRoll { get; set; }
        public int HorninessTimeModifier { get; set; }
        public bool PendingCritAdvantage { get; set; }
        public StatType? LastStatUsed { get; set; }
        public HashSet<StatType>? ShadowDisadvantagedStats { get; set; }
        public Dictionary<ShadowStatType, int>? CurrentShadowThresholds { get; set; }
        public Dictionary<string, int> DateeShadowValues { get; set; } = new Dictionary<string, int>();
        public WeaknessWindow? ActiveWeakness { get; set; }
        public Tell? ActiveTell { get; set; }
    }
}
