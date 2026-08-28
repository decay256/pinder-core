using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Versioned immutable inputs required to replay only an accepted DATEE
    /// performance. Roll, delivery, interest, XP, trap advancement, and turn
    /// resolution have already committed and are never executed by this path.
    /// </summary>
    public sealed class DateeResponseReplayState
    {
        public const int CurrentSchemaVersion = 1;

        public DateeResponseReplayState(
            int responseTurn,
            int postTurnNumber,
            string deliveredMessage,
            string acceptedDateeMessage,
            double responseDelayMinutes,
            int interestBefore,
            InterestState interestBeforeState,
            InterestState interestAfterState,
            FailureTier deliveryTier,
            StatType rollStat,
            RollOutcomeIntensity outcomeIntensity,
            bool horninessOverlayApplied,
            FailureTier horninessTier,
            CharacterEmotionalDirectionSummary acceptedEmotionalDirection,
            IReadOnlyList<string> activeTrapIds,
            IReadOnlyList<string> activeTrapInstructions,
            int schemaVersion = CurrentSchemaVersion)
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException("datee_response_replay.schema_version.unsupported");
            if (responseTurn < 0 || postTurnNumber != responseTurn + 1)
                throw new InvalidOperationException("datee_response_replay.turn_identity.invalid");
            if (responseDelayMinutes < 0 || double.IsNaN(responseDelayMinutes) || double.IsInfinity(responseDelayMinutes))
                throw new ArgumentOutOfRangeException(nameof(responseDelayMinutes));
            if (interestBefore < InterestMeter.Min || interestBefore > InterestMeter.Max)
                throw new ArgumentOutOfRangeException(nameof(interestBefore));
            RequireEnum(interestBeforeState, nameof(interestBeforeState));
            RequireEnum(interestAfterState, nameof(interestAfterState));
            RequireEnum(deliveryTier, nameof(deliveryTier));
            RequireEnum(rollStat, nameof(rollStat));
            RequireEnum(outcomeIntensity, nameof(outcomeIntensity));
            RequireEnum(horninessTier, nameof(horninessTier));

            SchemaVersion = schemaVersion;
            ResponseTurn = responseTurn;
            PostTurnNumber = postTurnNumber;
            DeliveredMessage = Required(deliveredMessage, nameof(deliveredMessage));
            AcceptedDateeMessage = Required(acceptedDateeMessage, nameof(acceptedDateeMessage));
            ResponseDelayMinutes = responseDelayMinutes;
            InterestBefore = interestBefore;
            InterestBeforeState = interestBeforeState;
            InterestAfterState = interestAfterState;
            DeliveryTier = deliveryTier;
            RollStat = rollStat;
            OutcomeIntensity = outcomeIntensity;
            HorninessOverlayApplied = horninessOverlayApplied;
            HorninessTier = horninessTier;
            AcceptedEmotionalDirection = acceptedEmotionalDirection
                ?? throw new ArgumentNullException(nameof(acceptedEmotionalDirection));
            if (AcceptedEmotionalDirection.Turn != responseTurn)
                throw new InvalidOperationException("datee_response_replay.emotional_direction.turn_identity.invalid");
            ActiveTrapIds = Copy(activeTrapIds, nameof(activeTrapIds));
            ActiveTrapInstructions = Copy(activeTrapInstructions, nameof(activeTrapInstructions));
        }

        public int SchemaVersion { get; }
        public int ResponseTurn { get; }
        public int PostTurnNumber { get; }
        public string DeliveredMessage { get; }
        public string AcceptedDateeMessage { get; }
        public double ResponseDelayMinutes { get; }
        public int InterestBefore { get; }
        public InterestState InterestBeforeState { get; }
        public InterestState InterestAfterState { get; }
        public FailureTier DeliveryTier { get; }
        public StatType RollStat { get; }
        public RollOutcomeIntensity OutcomeIntensity { get; }
        public bool HorninessOverlayApplied { get; }
        public FailureTier HorninessTier { get; }
        public CharacterEmotionalDirectionSummary AcceptedEmotionalDirection { get; }
        public IReadOnlyList<string> ActiveTrapIds { get; }
        public IReadOnlyList<string> ActiveTrapInstructions { get; }

        public void ValidateAgainst(AcceptedDateeResponsePlanState acceptedPlan)
        {
            if (acceptedPlan == null) throw new ArgumentNullException(nameof(acceptedPlan));
            if (ResponseTurn != acceptedPlan.OriginatingTurn
                || !string.Equals(DeliveredMessage, acceptedPlan.VisibleMessageText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("datee_response_replay.plan_identity.mismatch");
            }
        }

        public DateeResponseReplayState WithAcceptedDateeMessage(string acceptedDateeMessage)
            => new DateeResponseReplayState(
                ResponseTurn,
                PostTurnNumber,
                DeliveredMessage,
                acceptedDateeMessage,
                ResponseDelayMinutes,
                InterestBefore,
                InterestBeforeState,
                InterestAfterState,
                DeliveryTier,
                RollStat,
                OutcomeIntensity,
                HorninessOverlayApplied,
                HorninessTier,
                AcceptedEmotionalDirection,
                ActiveTrapIds,
                ActiveTrapInstructions,
                SchemaVersion);

        private static IReadOnlyList<string> Copy(IReadOnlyList<string> values, string name)
        {
            if (values == null) throw new ArgumentNullException(name);
            string[] copy = values.Select(value => Required(value, name)).ToArray();
            return Array.AsReadOnly(copy);
        }

        private static string Required(string value, string name)
            => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("A non-empty value is required.", name)
                : value;

        private static void RequireEnum<T>(T value, string name) where T : struct
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
