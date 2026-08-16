using System;
using System.Collections.Generic;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    public static class GameRunOneShotJournalTaxonomy
    {
        public const string DialogueOptions = "game.dialogue-options";
        public const string DramaticArcSetup = "game.setup.dramatic-arc";
        public const string SuccessImprovement = "game.delivery.success-improvement";
        public const string SteeringQuestion = "game.delivery.steering-question";
        public const string HorninessQuestion = "game.delivery.horniness-question";

        public const string GameRunOneShotRecord = "game_run_one_shot_record";
        public const string GameRunSetupOneShotRecord = "game_run_setup_one_shot_record";
        public const string GameRunDeliveryOneShotRecord = "game_run_delivery_one_shot_record";
        public const string GameRunDeliveryAppendOneShotRecord = "game_run_delivery_append_one_shot_record";
    }

    public sealed class GameRunOneShotJournalRequest
    {
        public GameRunOneShotJournalRequest(
            string operationId,
            string executionClass,
            string journalDestination,
            string? turnId,
            string outputLinkId,
            string requestId,
            IReadOnlyDictionary<string, string>? context = null,
            string? invocationIdPrefix = null)
        {
            OperationId = operationId;
            ExecutionClass = executionClass;
            JournalDestination = journalDestination;
            TurnId = turnId;
            OutputLinkId = outputLinkId;
            RequestId = requestId;
            Context = context;
            InvocationIdPrefix = invocationIdPrefix;
        }

        public string OperationId { get; }
        public string ExecutionClass { get; }
        public string JournalDestination { get; }
        public string? TurnId { get; }
        public string OutputLinkId { get; }
        public string RequestId { get; }
        public IReadOnlyDictionary<string, string>? Context { get; }
        public string? InvocationIdPrefix { get; }
    }

    public interface IAgentJournalOneShotContextFactory
    {
        AgentJournalOneShotContext Create(GameRunOneShotJournalRequest request);
    }

    public sealed class GameRunOneShotJournalContextFactory : IAgentJournalOneShotContextFactory
    {
        private readonly string _gameRunId;
        private readonly string _modelId;

        public GameRunOneShotJournalContextFactory(string gameRunId, string modelId)
        {
            _gameRunId = gameRunId;
            _modelId = modelId;
        }

        public AgentJournalOneShotContext Create(GameRunOneShotJournalRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return new AgentJournalOneShotContext(
                _gameRunId,
                request.OperationId,
                request.ExecutionClass,
                request.JournalDestination,
                _modelId,
                request.TurnId,
                request.OutputLinkId,
                request.Context,
                request.RequestId,
                request.InvocationIdPrefix);
        }
    }
}
