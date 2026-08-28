using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Pinder.Core.Conversation
{
    public enum DateeResponsePlanCompilationOutcome { Accepted, CreativeAmbiguity, Rejected }

    public sealed class DateeResponsePlanInput
    {
        public DateeResponsePlanInput(
            DateeVisibleEvidence visibleEvidence,
            int interestBefore,
            int interestAfter,
            InterestState interestBeforeState,
            InterestState interestAfterState,
            CharacterEmotionalDirection emotionalDirection,
            DateeReactionTarget? reactionTarget,
            OwnedPromptFactV1? cognitivePressure,
            IReadOnlyList<string>? activeTrapIds,
            string? archetypeId,
            string? dramaticArcSourceId,
            IReadOnlyList<RoleFactAccessDecision>? accessDecisions)
        {
            VisibleEvidence = visibleEvidence ?? throw new ArgumentNullException(nameof(visibleEvidence));
            InterestBefore = interestBefore;
            InterestAfter = interestAfter;
            if (!Enum.IsDefined(typeof(InterestState), interestBeforeState) || !Enum.IsDefined(typeof(InterestState), interestAfterState))
                throw new ArgumentException("Unknown interest state.");
            InterestBeforeState = interestBeforeState;
            InterestAfterState = interestAfterState;
            EmotionalDirection = emotionalDirection ?? throw new ArgumentNullException(nameof(emotionalDirection));
            ReactionTarget = reactionTarget;
            CognitivePressure = cognitivePressure;
            ActiveTrapIds = Snapshot(activeTrapIds);
            ArchetypeId = string.IsNullOrWhiteSpace(archetypeId) ? null : archetypeId;
            DramaticArcSourceId = string.IsNullOrWhiteSpace(dramaticArcSourceId) ? null : dramaticArcSourceId;
            AccessDecisions = Snapshot(accessDecisions);
        }

        public DateeVisibleEvidence VisibleEvidence { get; }
        public int InterestBefore { get; }
        public int InterestAfter { get; }
        public InterestState InterestBeforeState { get; }
        public InterestState InterestAfterState { get; }
        public CharacterEmotionalDirection EmotionalDirection { get; }
        public DateeReactionTarget? ReactionTarget { get; }
        public OwnedPromptFactV1? CognitivePressure { get; }
        public IReadOnlyList<string> ActiveTrapIds { get; }
        public string? ArchetypeId { get; }
        public string? DramaticArcSourceId { get; }
        public IReadOnlyList<RoleFactAccessDecision> AccessDecisions { get; }

        public static DateeResponsePlanInput From(DateeContext context, CharacterEmotionalDirection direction)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return new DateeResponsePlanInput(
                new DateeVisibleEvidence(
                    context.PlayerDeliveredMessage,
                    ConversationMessageReference.Create(context.CurrentTurn, ConversationParticipantRole.PlayerAvatar)),
                context.InterestBefore,
                context.InterestAfter,
                context.InterestBeforeState,
                context.InterestAfterState,
                direction,
                context.DateeReactionTarget,
                context.CognitiveSubtextFact,
                context.ActiveTraps,
                string.IsNullOrWhiteSpace(context.ActiveArchetypeDirective) ? null : "active-archetype:turn:" + context.CurrentTurn,
                context.DramaticArcSourceId,
                context.PromptFactAccessDecisions);
        }

        private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T>? values)
            => new ReadOnlyCollection<T>((values ?? Array.Empty<T>()).ToArray());
    }

    public sealed class DateeResponsePlanCompilationResult
    {
        internal DateeResponsePlanCompilationResult(
            DateeResponsePlanCompilationOutcome outcome,
            DateeResponsePlan? plan,
            DateeResponsePlanContractException? rejection,
            IReadOnlyList<DateeResponseMovement>? allowedMovements,
            IReadOnlyList<DateeConversationalMove>? allowedMoves,
            IReadOnlyList<IReadOnlyList<DateeResponseMovementStage>>? allowedStageOrders)
        {
            Outcome = outcome;
            Plan = plan;
            Rejection = rejection;
            AllowedMovements = Snapshot(allowedMovements);
            AllowedConversationalMoves = Snapshot(allowedMoves);
            AllowedStageOrders = new ReadOnlyCollection<IReadOnlyList<DateeResponseMovementStage>>(
                (allowedStageOrders ?? Array.Empty<IReadOnlyList<DateeResponseMovementStage>>())
                    .Select(order => (IReadOnlyList<DateeResponseMovementStage>)new ReadOnlyCollection<DateeResponseMovementStage>(order.ToArray()))
                    .ToArray());
        }

        public DateeResponsePlanCompilationOutcome Outcome { get; }
        public DateeResponsePlan? Plan { get; }
        public DateeResponsePlanContractException? Rejection { get; }
        public IReadOnlyList<DateeResponseMovement> AllowedMovements { get; }
        public IReadOnlyList<DateeConversationalMove> AllowedConversationalMoves { get; }
        public IReadOnlyList<IReadOnlyList<DateeResponseMovementStage>> AllowedStageOrders { get; }

        private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T>? values)
            => new ReadOnlyCollection<T>((values ?? Array.Empty<T>()).ToArray());
    }

    public sealed class DateeResponsePlanCompiler
    {
        private const string EmotionalSourcePrefix = "emotional-director:turn:";
        private const string RelationshipSourcePrefix = "relationship-state:turn:";

        public DateeResponsePlanCompilationResult Compile(DateeResponsePlanInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            try
            {
                ValidateAdmittedFacts(input);
                var sources = BuildSources(input);
                var constraints = BuildConstraints(input, sources);
                DateeResponseDisclosure disclosure = ResolveDisclosure(input.ReactionTarget);
                if (IsTerminal(input.InterestAfterState)
                    && (input.InterestAfter > input.InterestBefore
                        || string.Equals(
                            input.ReactionTarget?.ResolvedTarget.Manner,
                            "INTIMATE_BREAKTHROUGH",
                            StringComparison.Ordinal)))
                    return Rejected(
                        "terminal_state_open_forbidden",
                        "Terminal relationship state cannot produce Open movement.",
                        RelationshipSource(input));
                MovementResolution movement = ResolveMovement(input, disclosure);

                if (IsTerminal(input.InterestAfterState) && movement.Allowed.Contains(DateeResponseMovement.Open))
                {
                    if (movement.Allowed.Count == 1)
                        return Rejected("terminal_state_open_forbidden", "Terminal relationship state cannot produce Open movement.", RelationshipSource(input));
                    movement = movement.Without(DateeResponseMovement.Open);
                }

                string interpretation = "Possibly, " + input.EmotionalDirection.Interpretation.Trim();
                IReadOnlyList<DateeConversationalMove> allowedMoves = ResolveMoves(
                    movement.Allowed[0],
                    disclosure,
                    input.EmotionalDirection,
                    EmotionalSource(input));
                bool hasCreativeAmbiguity = movement.Allowed.Count > 1
                    || movement.StageOrders.Count > 1
                    || allowedMoves.Count > 1;
                if (!hasCreativeAmbiguity)
                {
                    DateeResponseMovement selected = movement.Allowed[0];
                    IReadOnlyList<DateeResponseMovementStage> stages = selected == DateeResponseMovement.Mixed
                        ? movement.StageOrders.Single()
                        : Array.Empty<DateeResponseMovementStage>();
                    DateeConversationalMove move = allowedMoves[0];
                    DateeResponsePlan plan = BuildPlan(input, sources, constraints, interpretation, selected, stages, disclosure, move);
                    return new DateeResponsePlanCompilationResult(DateeResponsePlanCompilationOutcome.Accepted, plan, null, null, null, null);
                }

                DateeResponsePlan template = BuildPlan(
                    input,
                    sources,
                    constraints,
                    interpretation,
                    movement.Allowed[0],
                    movement.Allowed[0] == DateeResponseMovement.Mixed ? movement.StageOrders[0] : Array.Empty<DateeResponseMovementStage>(),
                    disclosure,
                    allowedMoves[0]);
                return new DateeResponsePlanCompilationResult(
                    DateeResponsePlanCompilationOutcome.CreativeAmbiguity,
                    template,
                    null,
                    movement.Allowed,
                    allowedMoves,
                    movement.StageOrders);
            }
            catch (DateeResponsePlanContractException ex)
            {
                return new DateeResponsePlanCompilationResult(DateeResponsePlanCompilationOutcome.Rejected, null, ex, null, null, null);
            }
        }

        public DateeResponsePlan AcceptReconciled(DateeResponsePlanCompilationResult compilation, DateeResponsePlan candidate)
        {
            if (compilation == null) throw new ArgumentNullException(nameof(compilation));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (compilation.Outcome != DateeResponsePlanCompilationOutcome.CreativeAmbiguity || compilation.Plan == null)
                throw Incompatible("reconciliation.not_allowed", "Reconciliation is allowed only for creative ambiguity.");
            DateeResponsePlan baseline = compilation.Plan;
            RequireSameFixedFields(baseline, candidate);
            if (!compilation.AllowedMovements.Contains(candidate.Movement))
                throw Incompatible("reconciliation.movement_out_of_set", "Reconciler selected a movement outside the compiler set.");
            if (!compilation.AllowedConversationalMoves.Contains(candidate.ConversationalMove))
                throw Incompatible("reconciliation.move_out_of_set", "Reconciler selected a conversational move outside the compiler set.");
            if (candidate.Movement == DateeResponseMovement.Mixed
                && !compilation.AllowedStageOrders.Any(order => StagesEqual(order, candidate.MovementStages)))
                throw Incompatible("reconciliation.stage_order_out_of_set", "Reconciler selected a stage order outside the compiler set.");
            return candidate;
        }

        public DateeResponsePlan AttachReconciliationSource(DateeResponsePlan accepted, string sourceId)
        {
            if (accepted == null) throw new ArgumentNullException(nameof(accepted));
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("Reconciliation source id is required.", nameof(sourceId));
            if (accepted.Sources.Any(source => string.Equals(source.Id, sourceId, StringComparison.Ordinal)))
                return accepted;
            DateeResponsePlanSource[] sources = accepted.Sources
                .Concat(new[] { new DateeResponsePlanSource(sourceId, DateePlanSourceKind.Reconciliation) })
                .ToArray();
            return new DateeResponsePlan(
                accepted.VisibleEvidence,
                accepted.DateeInterpretation,
                accepted.Movement,
                accepted.MovementStages,
                accepted.PrimaryEmotion,
                accepted.SecondaryEmotion,
                accepted.RegulatoryState,
                accepted.Activation,
                accepted.Trajectory,
                accepted.Disclosure,
                accepted.ConversationalMove,
                accepted.DramaticArcSourceId,
                accepted.Constraints,
                sources);
        }

        private static DateeResponsePlan BuildPlan(
            DateeResponsePlanInput input,
            IReadOnlyList<DateeResponsePlanSource> sources,
            IReadOnlyList<DateeResponsePlanConstraint> constraints,
            string interpretation,
            DateeResponseMovement movement,
            IReadOnlyList<DateeResponseMovementStage> stages,
            DateeResponseDisclosure disclosure,
            DateeConversationalMove move)
            => new DateeResponsePlan(
                input.VisibleEvidence,
                interpretation,
                movement,
                stages,
                input.EmotionalDirection.PrimaryEmotion,
                input.EmotionalDirection.SecondaryEmotion,
                input.EmotionalDirection.RegulatoryState,
                input.EmotionalDirection.Activation,
                input.EmotionalDirection.Trajectory,
                disclosure,
                move,
                input.DramaticArcSourceId,
                constraints,
                sources);

        private static void ValidateAdmittedFacts(DateeResponsePlanInput input)
        {
            foreach (OwnedPromptFactV1 fact in new[] { input.ReactionTarget?.Fact, input.CognitivePressure }.Where(f => f != null).Cast<OwnedPromptFactV1>())
            {
                RoleFactAccessDecision? decision = input.AccessDecisions.FirstOrDefault(d => string.Equals(d.FactSourceId, fact.SourceId, StringComparison.Ordinal));
                if (decision == null || !decision.Admitted)
                    throw Incompatible("role_fact.not_admitted", "Plan source fact was not admitted by role policy.", fact.SourceId);
                if (fact.SubjectRole != ConversationParticipantRole.Datee)
                    throw Incompatible("role_fact.wrong_owner", "DATEE plan source is owned by another role.", fact.SourceId);
            }
        }

        private static IReadOnlyList<DateeResponsePlanSource> BuildSources(DateeResponsePlanInput input)
        {
            var sources = new List<DateeResponsePlanSource>
            {
                new DateeResponsePlanSource(input.VisibleEvidence.MessageReference.Value, DateePlanSourceKind.VisibleMessage),
                new DateeResponsePlanSource(RelationshipSource(input), DateePlanSourceKind.RelationshipState),
                new DateeResponsePlanSource(EmotionalSource(input), DateePlanSourceKind.EmotionalDirection),
            };
            if (input.ReactionTarget != null) sources.Add(new DateeResponsePlanSource(input.ReactionTarget.SourceId, DateePlanSourceKind.RevelationTarget));
            if (input.CognitivePressure != null) sources.Add(new DateeResponsePlanSource(input.CognitivePressure.SourceId, DateePlanSourceKind.CognitivePressure));
            foreach (string trap in input.ActiveTrapIds.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.Ordinal))
                sources.Add(new DateeResponsePlanSource("trap:" + trap, DateePlanSourceKind.Trap));
            if (input.ArchetypeId != null) sources.Add(new DateeResponsePlanSource(input.ArchetypeId, DateePlanSourceKind.Archetype));
            if (input.DramaticArcSourceId != null) sources.Add(new DateeResponsePlanSource(input.DramaticArcSourceId, DateePlanSourceKind.DramaticArc));
            return sources;
        }

        private static IReadOnlyList<DateeResponsePlanConstraint> BuildConstraints(DateeResponsePlanInput input, IReadOnlyList<DateeResponsePlanSource> sources)
        {
            var constraints = new List<DateeResponsePlanConstraint>
            {
                new DateeResponsePlanConstraint("visible_evidence.canonical", DateePlanConstraintSeverity.Hard, input.VisibleEvidence.MessageReference.Value),
                new DateeResponsePlanConstraint("relationship.resolved", DateePlanConstraintSeverity.Hard, RelationshipSource(input), input.InterestBefore + "->" + input.InterestAfter),
            };
            if (IsTerminal(input.InterestAfterState))
                constraints.Add(new DateeResponsePlanConstraint("relationship.terminal_no_open", DateePlanConstraintSeverity.Hard, RelationshipSource(input)));
            if (input.ReactionTarget != null)
                constraints.Add(new DateeResponsePlanConstraint("disclosure." + MannerToken(input.ReactionTarget.ResolvedTarget.Manner), DateePlanConstraintSeverity.Hard, input.ReactionTarget.SourceId, input.ReactionTarget.Text));
            if (input.CognitivePressure != null)
                constraints.Add(new DateeResponsePlanConstraint("cognitive_pressure.private", DateePlanConstraintSeverity.Soft, input.CognitivePressure.SourceId, input.CognitivePressure.Text));
            foreach (DateeResponsePlanSource source in sources.Where(s => s.Kind == DateePlanSourceKind.Trap))
                constraints.Add(new DateeResponsePlanConstraint("trap.active." + source.Id.Substring("trap:".Length), DateePlanConstraintSeverity.Soft, source.Id));
            if (input.ArchetypeId != null)
                constraints.Add(new DateeResponsePlanConstraint("archetype.active", DateePlanConstraintSeverity.Soft, input.ArchetypeId));
            if (input.DramaticArcSourceId != null)
                constraints.Add(new DateeResponsePlanConstraint("dramatic_arc.soft_tiebreaker", DateePlanConstraintSeverity.Soft, input.DramaticArcSourceId));
            return constraints;
        }

        private static DateeResponseDisclosure ResolveDisclosure(DateeReactionTarget? target)
        {
            if (target == null) return DateeResponseDisclosure.None;
            switch (target.ResolvedTarget.Manner)
            {
                case "CURATED_BUFFER":
                case "DEFENSIVE_EVASION":
                case "INTIMATE_BREAKTHROUGH":
                    return DateeResponseDisclosure.Voluntary;
                case "TRAUMATIC_LEAKAGE":
                    return DateeResponseDisclosure.Involuntary;
                default:
                    throw Incompatible("transition_manner.unknown", "Unknown transition manner.", target.SourceId);
            }
        }

        private static MovementResolution ResolveMovement(DateeResponsePlanInput input, DateeResponseDisclosure disclosure)
        {
            DateeResponseMovement resolvedMovement = input.InterestAfter > input.InterestBefore
                ? DateeResponseMovement.Open
                : input.InterestAfter < input.InterestBefore
                    ? DateeResponseMovement.Withdraw
                    : DateeResponseMovement.Hold;
            string? manner = input.ReactionTarget?.ResolvedTarget.Manner;
            if (manner != null && manner != "TRAUMATIC_LEAKAGE")
            {
                DateeResponseMovement requiredByManner;
                switch (manner)
                {
                    case "CURATED_BUFFER": requiredByManner = DateeResponseMovement.Hold; break;
                    case "DEFENSIVE_EVASION": requiredByManner = DateeResponseMovement.Withdraw; break;
                    case "INTIMATE_BREAKTHROUGH": requiredByManner = DateeResponseMovement.Open; break;
                    default:
                        throw Incompatible("transition_manner.unknown", "Unknown transition manner.", input.ReactionTarget?.SourceId);
                }
                if (requiredByManner != resolvedMovement)
                    throw Incompatible(
                        "transition_manner.conflicts_with_interest_movement",
                        "Transition manner contradicts the authoritative resolved interest movement.",
                        input.ReactionTarget?.SourceId);
            }
            return MovementResolution.One(resolvedMovement);
        }

        private static IReadOnlyList<DateeConversationalMove> ResolveMoves(
            DateeResponseMovement movement,
            DateeResponseDisclosure disclosure,
            CharacterEmotionalDirection direction,
            string emotionalSourceId)
        {
            if (disclosure != DateeResponseDisclosure.None)
                return new[] { DateeConversationalMove.Reveal };
            string regulatory = direction.RegulatoryState.Trim().ToLowerInvariant();
            switch (regulatory)
            {
                case "open":
                case "controlled":
                case "guarded":
                case "numb":
                case "dissociated":
                    return new[] { ResolveMove(movement, disclosure, direction.Trajectory) };
                case "anxious":
                case "overwhelmed":
                case "conflicted":
                    return AllowedMoves(movement).ToArray();
                default:
                    throw Incompatible("emotional_direction.regulatory_state_unknown", "Unknown regulatory state.", emotionalSourceId);
            }
        }

        private static DateeConversationalMove ResolveMove(DateeResponseMovement movement, DateeResponseDisclosure disclosure, string trajectory)
        {
            if (disclosure != DateeResponseDisclosure.None) return DateeConversationalMove.Reveal;
            if (movement == DateeResponseMovement.Open)
                return string.Equals(trajectory, "escalating", StringComparison.OrdinalIgnoreCase) ? DateeConversationalMove.Escalate : DateeConversationalMove.Tease;
            if (movement == DateeResponseMovement.Hold) return DateeConversationalMove.Challenge;
            if (movement == DateeResponseMovement.Withdraw) return DateeConversationalMove.Redirect;
            return DateeConversationalMove.Reverse;
        }

        private static IEnumerable<DateeConversationalMove> AllowedMoves(DateeResponseMovement movement)
        {
            switch (movement)
            {
                case DateeResponseMovement.Open: return new[] { DateeConversationalMove.Escalate, DateeConversationalMove.Tease };
                case DateeResponseMovement.Hold: return new[] { DateeConversationalMove.Challenge, DateeConversationalMove.Misunderstand };
                case DateeResponseMovement.Withdraw: return new[] { DateeConversationalMove.Redirect, DateeConversationalMove.Reverse };
                default: return new[] { DateeConversationalMove.Reverse };
            }
        }

        private static void RequireSameFixedFields(DateeResponsePlan baseline, DateeResponsePlan candidate)
        {
            if (!string.Equals(baseline.VisibleEvidence.Text, candidate.VisibleEvidence.Text, StringComparison.Ordinal)
                || !baseline.VisibleEvidence.MessageReference.Equals(candidate.VisibleEvidence.MessageReference)
                || !string.Equals(baseline.DateeInterpretation, candidate.DateeInterpretation, StringComparison.Ordinal)
                || !string.Equals(baseline.PrimaryEmotion, candidate.PrimaryEmotion, StringComparison.Ordinal)
                || !string.Equals(baseline.SecondaryEmotion, candidate.SecondaryEmotion, StringComparison.Ordinal)
                || !string.Equals(baseline.RegulatoryState, candidate.RegulatoryState, StringComparison.Ordinal)
                || baseline.Activation != candidate.Activation
                || !string.Equals(baseline.Trajectory, candidate.Trajectory, StringComparison.Ordinal)
                || baseline.Disclosure != candidate.Disclosure
                || !string.Equals(baseline.DramaticArcSourceId, candidate.DramaticArcSourceId, StringComparison.Ordinal)
                || !ConstraintsEqual(baseline.Constraints, candidate.Constraints)
                || !SourcesEqual(baseline.Sources, candidate.Sources))
                throw Incompatible("reconciliation.fixed_fields_changed", "Reconciler changed compiler-owned fields.");
        }

        private static bool ConstraintsEqual(IReadOnlyList<DateeResponsePlanConstraint> left, IReadOnlyList<DateeResponsePlanConstraint> right)
            => left.Count == right.Count && left.Zip(right, (a, b) => a.Id == b.Id && a.Severity == b.Severity && a.SourceId == b.SourceId && a.Value == b.Value).All(x => x);
        private static bool SourcesEqual(IReadOnlyList<DateeResponsePlanSource> left, IReadOnlyList<DateeResponsePlanSource> right)
            => left.Count == right.Count && left.Zip(right, (a, b) => a.Id == b.Id && a.Kind == b.Kind).All(x => x);
        private static bool StagesEqual(IReadOnlyList<DateeResponseMovementStage> left, IReadOnlyList<DateeResponseMovementStage> right)
            => left.Count == right.Count && left.Zip(right, (a, b) => a.Movement == b.Movement && a.OwnsDisclosure == b.OwnsDisclosure).All(x => x);
        private static bool IsTerminal(InterestState state) => state == InterestState.Unmatched || state == InterestState.DateSecured;
        private static string EmotionalSource(DateeResponsePlanInput input) => EmotionalSourcePrefix + input.VisibleEvidence.MessageReference.Turn;
        private static string RelationshipSource(DateeResponsePlanInput input) => RelationshipSourcePrefix + input.VisibleEvidence.MessageReference.Turn;
        private static string MannerToken(string manner) => manner.ToLowerInvariant();
        private static DateeResponsePlanCompilationResult Rejected(string code, string message, string? sourceId)
            => new DateeResponsePlanCompilationResult(DateeResponsePlanCompilationOutcome.Rejected, null, Incompatible(code, message, sourceId), null, null, null);
        private static DateeResponsePlanContractException Incompatible(string code, string message, string? sourceId = null)
            => new DateeResponsePlanContractException("datee_response_plan_incompatible." + code, message, sourceId);

        private sealed class MovementResolution
        {
            public MovementResolution(IReadOnlyList<DateeResponseMovement> allowed, IReadOnlyList<IReadOnlyList<DateeResponseMovementStage>> stageOrders)
            {
                Allowed = allowed;
                StageOrders = stageOrders;
            }
            public IReadOnlyList<DateeResponseMovement> Allowed { get; }
            public IReadOnlyList<IReadOnlyList<DateeResponseMovementStage>> StageOrders { get; }
            public static MovementResolution One(DateeResponseMovement movement) => new MovementResolution(new[] { movement }, Array.Empty<IReadOnlyList<DateeResponseMovementStage>>());
            public MovementResolution Without(DateeResponseMovement movement) => new MovementResolution(Allowed.Where(value => value != movement).ToArray(), StageOrders);
        }
    }
}
