using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals.Recording
{
    public sealed class GameRunOneShotJournalWiringTests
    {
        [Fact]
        public async Task dialogue_options_records_no_session_owner_and_output()
        {
            var sink = new RecordingJournalSink();
            var adapter = Adapter(new QueueTransport(DialogueOptionsText()), sink);

            DialogueOption[] options = await adapter.GetDialogueOptionsAsync(DialogueContext("game.dialogue-options", "dialogue_options"));

            Assert.Equal(3, options.Length);
            AssertInvocationAndResult(sink, "game.dialogue-options", "game_run_one_shot_record", "dialogue_options", "turn-7", AgentJournalTerminalStatus.Succeeded);
            Assert.Null(sink.Invocation.Correlation.AgentSessionId);
            Assert.Contains("OPTION_1", sink.Result.OutputText);
            Assert.Contains(sink.Records, record => record.RecordId.Contains("/game_run_bundle/game.dialogue-options/", StringComparison.Ordinal));
            AssertUsage(sink.Result);
        }

        [Fact]
        public async Task dramatic_arc_setup_mirrors_runtime_and_durable_call_identity()
        {
            var sink = new RecordingJournalSink();
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var generator = new LlmDramaticArcGenerator(
                new QueueTransport("One sentence lands. Two sentence turns. Three sentence closes."),
                new LlmDramaticArcGenerator.Options
                {
                    AgentJournalHostSink = sink,
                    AgentJournal = Journal("game.setup.dramatic-arc", "game_run_setup_one_shot_record", turnId: null, output: "setup.dramatic_arc"),
                    AgentJournalClock = FixedClock,
                    OnDiagnostic = diagnostics.Add,
                });

            string arc = await generator.GenerateAsync("Ari", "truth", "bio", "Bea", "risk", "bio");

            Assert.Contains("Three sentence closes.", arc);
            AssertInvocationAndResult(sink, "game.setup.dramatic-arc", "game_run_setup_one_shot_record", "dramatic_arc", null, AgentJournalTerminalStatus.Succeeded);
            AssertUsage(sink.Result);
            OperationalDiagnosticEvent[] mirroredDiagnostics = diagnostics
                .Where(diagnostic => diagnostic.PhaseCode == LlmPhase.DramaticArc)
                .ToArray();
            Assert.Equal(2, mirroredDiagnostics.Length);
            Assert.Contains(mirroredDiagnostics, diagnostic => diagnostic.Lifecycle == OperationalDiagnosticLifecycle.Start);
            Assert.Contains(mirroredDiagnostics, diagnostic => diagnostic.Lifecycle == OperationalDiagnosticLifecycle.Terminal);
            Assert.All(
                mirroredDiagnostics,
                diagnostic => Assert.Equal(sink.Invocation.Correlation.InvocationId, diagnostic.CallId));
        }

        [Fact]
        public async Task dialogue_options_preserves_cache_token_usage_and_diagnostic_call_id()
        {
            var sink = new RecordingJournalSink();
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var transport = new QueueTransport(DialogueOptionsText())
            {
                CacheReadInputTokensPerCall = 5,
                CacheCreationInputTokensPerCall = 4,
            };
            var adapter = Adapter(transport, sink, diagnostics);

            DialogueOption[] options = await adapter.GetDialogueOptionsAsync(DialogueContext("game.dialogue-options.cache", "dialogue_options_cache"));

            Assert.Equal(3, options.Length);
            AssertUsage(sink.Result, cacheCreationInputTokens: 4, cacheReadInputTokens: 5);
            OperationalDiagnosticEvent terminal = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Lifecycle == OperationalDiagnosticLifecycle.Terminal
                && diagnostic.PhaseCode == LlmPhase.DialogueOptions));
            Assert.Equal(sink.Invocation.Correlation.InvocationId, terminal.CallId);
        }

        [Fact]
        public async Task successful_call_without_usage_provider_marks_usage_unavailable()
        {
            var sink = new RecordingJournalSink();
            var adapter = Adapter(new PlainTransport(DialogueOptionsText()), sink);

            DialogueOption[] options = await adapter.GetDialogueOptionsAsync(
                DialogueContext("game.dialogue-options.no-usage", "dialogue_options_no_usage"));

            Assert.Equal(3, options.Length);
            Assert.Equal(AgentJournalTerminalStatus.Succeeded, sink.Result.TerminalStatus);
            Assert.Equal(AgentJournalUsageStatus.Unavailable, sink.Result.UsageStatus);
            Assert.Null(sink.Result.Usage);
        }

        [Fact]
        public async Task successful_call_with_throwing_usage_provider_marks_usage_unavailable()
        {
            var sink = new RecordingJournalSink();
            var adapter = Adapter(new ThrowingUsageTransport(DialogueOptionsText()), sink);

            DialogueOption[] options = await adapter.GetDialogueOptionsAsync(
                DialogueContext("game.dialogue-options.throwing-usage", "dialogue_options_throwing_usage"));

            Assert.Equal(3, options.Length);
            Assert.Equal(AgentJournalTerminalStatus.Succeeded, sink.Result.TerminalStatus);
            Assert.Equal(AgentJournalUsageStatus.Unavailable, sink.Result.UsageStatus);
            Assert.Null(sink.Result.Usage);
        }

        [Fact]
        public async Task successful_call_with_zero_measured_calls_marks_usage_unavailable()
        {
            var sink = new RecordingJournalSink();
            var transport = new QueueTransport(DialogueOptionsText()) { UsageCallCountPerSend = 0 };
            var adapter = Adapter(transport, sink);

            DialogueOption[] options = await adapter.GetDialogueOptionsAsync(
                DialogueContext("game.dialogue-options.zero-call-usage", "dialogue_options_zero_call_usage"));

            Assert.Equal(3, options.Length);
            Assert.Equal(1, transport.CallCount);
            Assert.Equal(AgentJournalUsageStatus.Unavailable, sink.Result.UsageStatus);
            Assert.Null(sink.Result.Usage);
        }

        [Fact]
        public async Task successful_call_with_multi_call_delta_marks_usage_incomplete()
        {
            var sink = new RecordingJournalSink();
            var transport = new QueueTransport(DialogueOptionsText())
            {
                UsageCallCountPerSend = 2,
                CacheCreationInputTokensPerCall = 3,
                CacheReadInputTokensPerCall = 4,
            };
            var adapter = Adapter(transport, sink);

            DialogueOption[] options = await adapter.GetDialogueOptionsAsync(
                DialogueContext("game.dialogue-options.multi-call-usage", "dialogue_options_multi_call_usage"));

            Assert.Equal(3, options.Length);
            Assert.Equal(AgentJournalUsageStatus.Incomplete, sink.Result.UsageStatus);
            Assert.NotNull(sink.Result.Usage);
            Assert.Equal(22, sink.Result.Usage!.InputTokens);
            Assert.Equal(14, sink.Result.Usage.OutputTokens);
            Assert.Equal(6, sink.Result.Usage.CacheCreationInputTokens);
            Assert.Equal(8, sink.Result.Usage.CacheReadInputTokens);
        }

        [Fact]
        public async Task overlapping_calls_on_shared_transport_are_distinct_but_usage_is_incomplete()
        {
            var sink = new RecordingJournalSink();
            var transport = new OverlappingUsageTransport(DialogueOptionsText());
            var adapter = Adapter(transport, sink);

            await Task.WhenAll(
                adapter.GetDialogueOptionsAsync(DialogueContext(
                    "game.dialogue-options.overlap-a",
                    "dialogue_options_overlap_a")),
                adapter.GetDialogueOptionsAsync(DialogueContext(
                    "game.dialogue-options.overlap-b",
                    "dialogue_options_overlap_b")));

            Assert.Equal(2, sink.Invocations.Count);
            Assert.Equal(2, sink.Results.Count);
            Assert.Equal(2, sink.Invocations
                .Select(record => record.Correlation.InvocationId)
                .Distinct(StringComparer.Ordinal)
                .Count());
            Assert.All(sink.Results, result =>
            {
                Assert.Equal(AgentJournalUsageStatus.Incomplete, result.UsageStatus);
                Assert.NotNull(result.Usage);
                Assert.Equal(22, result.Usage!.InputTokens);
                Assert.Equal(14, result.Usage.OutputTokens);
            });
        }

        [Fact]
        public async Task success_improvement_records_replacement()
        {
            var sink = new RecordingJournalSink();
            var adapter = Adapter(new QueueTransport("you noticed the exact thing"), sink);
            var context = new SuccessImprovementContext(
                "player prompt",
                "Bea",
                "Ari",
                "nice detail",
                StatType.Charm,
                "strong",
                History(),
                Journal("game.delivery.success-improvement", "game_run_delivery_one_shot_record", output: "turn-7.delivery.replacement"));

            string result = await adapter.GetSuccessImprovementAsync(context);

            Assert.Equal("you noticed the exact thing", result);
            AssertInvocationAndResult(sink, "game.delivery.success-improvement", "game_run_delivery_one_shot_record", "delivery", "turn-7", AgentJournalTerminalStatus.Succeeded);
            Assert.Equal("turn-7.delivery.replacement", sink.Invocation.Correlation.OutputLinkId);
            AssertUsage(sink.Result);
        }

        [Fact]
        public async Task steering_question_records_append_check_context()
        {
            var sink = new RecordingJournalSink();
            var adapter = Adapter(new QueueTransport("want to get coffee?"), sink);
            var context = new SteeringContext(
                "player prompt",
                "Bea",
                "Ari",
                "nice detail",
                History(),
                Journal(
                    "game.delivery.steering-question",
                    "game_run_delivery_append_one_shot_record",
                    output: "turn-7.delivery.steering_append",
                    extra: new Dictionary<string, string>
                    {
                        ["check_kind"] = "steering",
                        ["check_total"] = "19",
                    }));

            string result = await adapter.GetSteeringQuestionAsync(context);

            Assert.Equal("want to get coffee?", result);
            AssertInvocationAndResult(sink, "game.delivery.steering-question", "game_run_delivery_append_one_shot_record", "steering", "turn-7", AgentJournalTerminalStatus.Succeeded);
            Assert.Equal("steering", sink.Invocation.Correlation.Context["check_kind"]);
            AssertUsage(sink.Result);
        }

        [Fact]
        public async Task horniness_question_records_append_check_context()
        {
            var sink = new RecordingJournalSink();
            var adapter = Adapter(new QueueTransport("or are you busy thinking about me?"), sink);
            var context = new HorninessQuestionContext(
                "player prompt",
                "Bea",
                "Ari",
                "nice detail",
                History(),
                Journal(
                    "game.delivery.horniness-question",
                    "game_run_delivery_append_one_shot_record",
                    output: "turn-7.delivery.horniness_append",
                    extra: new Dictionary<string, string>
                    {
                        ["check_kind"] = "horniness",
                        ["check_tier"] = "misfire",
                    }));

            string result = await adapter.GetHorninessQuestionAsync(context);

            Assert.Equal("or are you busy thinking about me?", result);
            AssertInvocationAndResult(sink, "game.delivery.horniness-question", "game_run_delivery_append_one_shot_record", "horniness_overlay", "turn-7", AgentJournalTerminalStatus.Succeeded);
            Assert.Equal("horniness", sink.Invocation.Correlation.Context["check_kind"]);
            AssertUsage(sink.Result);
        }

        [Fact]
        public async Task fail_closed_when_context_is_supplied_without_sink()
        {
            var adapter = new PinderLlmAdapter(new QueueTransport("want to get coffee?"), new PinderLlmAdapterOptions
            {
                GameDefinition = ConfiguredGameDefinition(),
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                adapter.GetSteeringQuestionAsync(new SteeringContext(
                    "p", "d", "p", "m", History(),
                    Journal("game.delivery.steering-question.missing-sink", "game_run_delivery_append_one_shot_record"))));

            var generator = new LlmDramaticArcGenerator(
                new QueueTransport("One sentence lands. Two sentence turns. Three sentence closes."),
                new LlmDramaticArcGenerator.Options
                {
                    AgentJournal = Journal(
                        "game.setup.dramatic-arc.missing-sink",
                        "game_run_setup_one_shot_record",
                        turnId: null),
                });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                generator.GenerateAsync("Ari", "truth", "bio", "Bea", "risk", "bio"));
        }

        [Fact]
        public void production_call_site_wiring_guard()
        {
            string turn = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Pinder.Core", "Conversation", "TurnOrchestrator.cs"));
            string delivery = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Pinder.Core", "Conversation", "DeliveryStage.cs"));
            string steering = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Pinder.Core", "Conversation", "SteeringEngine.cs"));
            string setup = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Pinder.SessionSetup", "LlmDramaticArcGenerator.cs"));

            Assert.Contains("GameRunOneShotJournalTaxonomy.DialogueOptions", turn, StringComparison.Ordinal);
            Assert.Contains("agentJournal: CreateAgentJournalContext(", turn, StringComparison.Ordinal);
            Assert.Contains("GameRunOneShotJournalTaxonomy.SuccessImprovement", delivery, StringComparison.Ordinal);
            Assert.Contains("GameRunOneShotJournalTaxonomy.HorninessQuestion", delivery, StringComparison.Ordinal);
            Assert.True(delivery.Split(new[] { "agentJournal: CreateAgentJournalContext(" }, StringSplitOptions.None).Length >= 3);
            Assert.Contains("GameRunOneShotJournalTaxonomy.SteeringQuestion", steering, StringComparison.Ordinal);
            Assert.Contains("agentJournal: CreateAgentJournalContext(", steering, StringComparison.Ordinal);
            Assert.Contains("ResolveAgentJournalContext", setup, StringComparison.Ordinal);
            Assert.Contains("GameRunOneShotJournalTaxonomy.DramaticArcSetup", setup, StringComparison.Ordinal);

            var factory = new GameRunOneShotJournalContextFactory("game-run-production", "test-model");
            AgentJournalCorrelationIds correlation = factory.Create(new GameRunOneShotJournalRequest(
                "game.dialogue-options.turn-9",
                GameRunOneShotJournalTaxonomy.DialogueOptions,
                GameRunOneShotJournalTaxonomy.GameRunOneShotRecord,
                "turn-9",
                "turn-9.dialogue-options.output",
                "game.dialogue-options.turn-9.request")).ToCorrelation(2);
            Assert.Equal("game-run-production", correlation.GameRunId);
            Assert.Equal("turn-9", correlation.TurnId);
            Assert.Equal(2, correlation.AttemptOrdinal);
            Assert.Null(correlation.AgentSessionId);
        }

        [Fact]
        public void excluded_owner_guard_rejects_unsafe_host_correlation_identifiers()
        {
            const string unsafeValue = "https://host/private";
            var correlation = new AgentJournalCorrelationIds(
                unsafeValue,
                agentSessionId: null,
                invocationId: unsafeValue,
                operationId: unsafeValue,
                attemptOrdinal: 1,
                attemptId: unsafeValue,
                requestId: unsafeValue,
                turnId: unsafeValue,
                branchId: unsafeValue,
                owner: AgentJournalOneShotContext.GameRunBundleOwner,
                journalDestination: "game_run_one_shot_record",
                executionClass: GameRunOneShotJournalTaxonomy.DialogueOptions,
                outputLinkId: unsafeValue);
            var record = new LlmInvocationRecord(correlation, "test-model", "dialogue_options", new[] { TestDocument() });
            string[] forbiddenPaths = AgentJournalValidator.Validate(record).Errors
                .Where(error => error.Code == AgentJournalValidator.ForbiddenSourceLink)
                .Select(error => error.Path)
                .ToArray();

            Assert.Contains("$.correlation.game_run_id", forbiddenPaths);
            Assert.Contains("$.correlation.invocation_id", forbiddenPaths);
            Assert.Contains("$.correlation.operation_id", forbiddenPaths);
            Assert.Contains("$.correlation.attempt_id", forbiddenPaths);
            Assert.Contains("$.correlation.request_id", forbiddenPaths);
            Assert.Contains("$.correlation.turn_id", forbiddenPaths);
            Assert.Contains("$.correlation.branch_id", forbiddenPaths);
            Assert.Contains("$.correlation.output_link_id", forbiddenPaths);
            Assert.Throws<ArgumentException>(() => new AgentJournalOneShotContext(
                unsafeValue,
                "operation",
                GameRunOneShotJournalTaxonomy.DialogueOptions,
                GameRunOneShotJournalTaxonomy.GameRunOneShotRecord,
                "test-model"));
        }

        [Fact]
        public async Task retry_records_distinct_attempts()
        {
            var sink = new RecordingJournalSink();
            var transport = new QueueTransport("not parseable", DialogueOptionsText());
            var adapter = Adapter(transport, sink);

            DialogueOption[] options = await adapter.GetDialogueOptionsAsync(DialogueContext("game.dialogue-options.retry", "dialogue_options_retry"));

            Assert.Equal(3, options.Length);
            Assert.Equal(4, sink.Records.Count);
            Assert.Contains(sink.Results, result => result.TerminalStatus == AgentJournalTerminalStatus.Rejected);
            Assert.Contains(sink.Results, result => result.TerminalStatus == AgentJournalTerminalStatus.Succeeded);
            Assert.Equal(new[] { 1, 2 }, sink.Invocations.Select(invocation => invocation.Correlation.AttemptOrdinal).ToArray());
        }

        [Fact]
        public async Task provider_failure_records_failed_terminal()
        {
            var sink = new RecordingJournalSink();
            var adapter = Adapter(new QueueTransport(new LlmTransportException("boom")), sink);

            await Assert.ThrowsAsync<LlmTransportException>(() =>
                adapter.GetSteeringQuestionAsync(new SteeringContext("p", "d", "p", "m", History(), Journal("game.delivery.steering-question.failure", "game_run_delivery_append_one_shot_record"))));

            Assert.Equal(AgentJournalTerminalStatus.Failed, sink.Result.TerminalStatus);
            Assert.Equal(nameof(LlmTransportException), sink.Result.ErrorCode);

            foreach (Func<PinderLlmAdapter, Task> call in FailingAdapterCalls())
            {
                var pathSink = new RecordingJournalSink();
                var pathAdapter = Adapter(new QueueTransport(new InvalidOperationException("runtime bug")), pathSink);
                await Assert.ThrowsAsync<InvalidOperationException>(() => call(pathAdapter));
                Assert.Single(pathSink.Invocations);
                Assert.Single(pathSink.Results);
                Assert.Equal(AgentJournalTerminalStatus.Failed, pathSink.Result.TerminalStatus);
                Assert.Equal(nameof(InvalidOperationException), pathSink.Result.ErrorCode);
                AssertUsage(pathSink.Result);
            }

            var setupSink = new RecordingJournalSink();
            var generator = new LlmDramaticArcGenerator(
                new QueueTransport(new InvalidOperationException("serializer bug")),
                new LlmDramaticArcGenerator.Options
                {
                    AgentJournalHostSink = setupSink,
                    AgentJournal = Journal(
                        "game.setup.dramatic-arc.failure",
                        "game_run_setup_one_shot_record",
                        turnId: null),
                });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                generator.GenerateAsync("Ari", "truth", "bio", "Bea", "risk", "bio"));
            Assert.Single(setupSink.Results);
            Assert.Equal(AgentJournalTerminalStatus.Failed, setupSink.Result.TerminalStatus);
            Assert.Equal(nameof(InvalidOperationException), setupSink.Result.ErrorCode);
        }

        [Fact]
        public async Task validation_or_skipped_output_records_rejection_or_skips_without_provider()
        {
            var sink = new RecordingJournalSink();
            var adapter = Adapter(new QueueTransport("   "), sink);

            string result = await adapter.GetSteeringQuestionAsync(new SteeringContext("p", "d", "p", "m", History(), Journal("game.delivery.steering-question.empty", "game_run_delivery_append_one_shot_record")));

            Assert.Equal(string.Empty, result);
            Assert.Equal(AgentJournalTerminalStatus.Rejected, sink.Result.TerminalStatus);
            Assert.Equal("empty_output", sink.Result.ValidationCode);
        }

        [Fact]
        public async Task validation_or_skipped_output_success_improvement_without_template_records_unavailable_usage_skip()
        {
            var sink = new RecordingJournalSink();
            var transport = new QueueTransport("provider must not be called");
            var adapter = Adapter(transport, sink);
            var context = new SuccessImprovementContext(
                "player prompt",
                "Bea",
                "Ari",
                "original delivery",
                StatType.Charm,
                "not_configured",
                History(),
                Journal(
                    "game.delivery.success-improvement.turn-7.skip",
                    "game_run_delivery_one_shot_record",
                    output: "turn-7.delivery.replacement"));

            string result = await adapter.GetSuccessImprovementAsync(context);

            Assert.Equal("original delivery", result);
            Assert.Single(sink.Invocations);
            Assert.Single(sink.Results);
            Assert.Equal("game.delivery.success-improvement.turn-7.skip", sink.Invocation.Correlation.OperationId);
            Assert.Equal(AgentJournalTerminalStatus.Rejected, sink.Result.TerminalStatus);
            Assert.Equal("skipped_no_template", sink.Result.ValidationCode);
            Assert.Equal(AgentJournalUsageStatus.Unavailable, sink.Result.UsageStatus);
            Assert.Null(sink.Result.Usage);
            Assert.Equal(0, transport.CallCount);
        }

        [Fact]
        public async Task cancellation_records_cancelled_terminal()
        {
            var sink = new RecordingJournalSink();
            var adapter = Adapter(new QueueTransport(new OperationCanceledException()), sink);

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                adapter.GetHorninessQuestionAsync(new HorninessQuestionContext("p", "d", "p", "m", History(), Journal("game.delivery.horniness-question.cancel", "game_run_delivery_append_one_shot_record"))));

            Assert.Equal(AgentJournalTerminalStatus.Cancelled, sink.Result.TerminalStatus);
            Assert.Equal(AgentJournalTerminalCodes.Cancelled, sink.Result.ErrorCode);

            foreach (Func<PinderLlmAdapter, Task> call in FailingAdapterCalls())
            {
                var pathSink = new RecordingJournalSink();
                var pathAdapter = Adapter(new QueueTransport(new OperationCanceledException("cancelled")), pathSink);
                await Assert.ThrowsAsync<OperationCanceledException>(() => call(pathAdapter));
                Assert.Single(pathSink.Invocations);
                Assert.Single(pathSink.Results);
                Assert.Equal(AgentJournalTerminalStatus.Cancelled, pathSink.Result.TerminalStatus);
                AssertUsage(pathSink.Result);
            }

            var setupSink = new RecordingJournalSink();
            var generator = new LlmDramaticArcGenerator(
                new QueueTransport(new OperationCanceledException("cancelled")),
                new LlmDramaticArcGenerator.Options
                {
                    AgentJournalHostSink = setupSink,
                    AgentJournal = Journal(
                        "game.setup.dramatic-arc.cancel",
                        "game_run_setup_one_shot_record",
                        turnId: null),
                });
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                generator.GenerateAsync("Ari", "truth", "bio", "Bea", "risk", "bio"));
            Assert.Single(setupSink.Invocations);
            Assert.Single(setupSink.Results);
            Assert.Equal(AgentJournalTerminalStatus.Cancelled, setupSink.Result.TerminalStatus);
            AssertUsage(setupSink.Result);
        }

        [Fact]
        public async Task abandoned_or_disposal_records_abandoned_no_session()
        {
            var sink = new RecordingJournalSink();
            var recorder = new AgentJournalRecorder(new AgentJournalRecorderContext(
                Journal("game.delivery.success-improvement.abandoned", "game_run_delivery_one_shot_record").ToCorrelation(1),
                "test-model",
                "delivery",
                new[] { TestDocument() })
            {
                HostSink = sink,
            });

            AgentJournalAttempt attempt = await recorder.StartAsync();
            await attempt.DisposeAsync();

            Assert.Equal(AgentJournalTerminalStatus.Failed, sink.Result.TerminalStatus);
            Assert.Equal(AgentJournalTerminalCodes.Abandoned, sink.Result.ErrorCode);
            Assert.Null(sink.Invocation.Correlation.AgentSessionId);
        }

        [Fact]
        public void excluded_owner_guard_rejects_forbidden_agent_session_in_no_session_context()
        {
            var record = new LlmInvocationRecord(
                new AgentJournalCorrelationIds(
                    "game-run-1376",
                    "fake-pi-session",
                    "invocation",
                    "operation",
                    1,
                    attemptId: "attempt-1",
                    owner: AgentJournalOneShotContext.GameRunBundleOwner,
                    journalDestination: "game_run_one_shot_record",
                    executionClass: "game.dialogue-options"),
                "test-model",
                "dialogue_options",
                new[] { TestDocument() });

            Assert.Contains(AgentJournalValidator.Validate(record).Errors, error => error.Code == AgentJournalValidator.ForbiddenOwnerId);
        }

        [Fact]
        public void dormant_interest_guard_static_no_production_callers()
        {
            string[] allowed =
            {
                "src/Pinder.Core/Interfaces/ILlmAdapter.cs",
                "src/Pinder.Core/Conversation/NullLlmAdapter.cs",
                "src/Pinder.LlmAdapters/PinderLlmAdapter.cs",
            };
            string[] matches = Directory.GetFiles(RepoRoot(), "*.cs", SearchOption.AllDirectories)
                .Where(path => path.Contains(Path.Combine("src", ""), StringComparison.Ordinal))
                .Where(path => File.ReadAllText(path).Contains("GetInterestChangeBeatAsync(", StringComparison.Ordinal))
                .Select(path => Relative(path))
                .Where(path => !allowed.Contains(path, StringComparer.Ordinal))
                .ToArray();

            Assert.Empty(matches);
        }

        [Fact]
        public void final_document_builder_guard_remains()
        {
            string adapter = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Pinder.LlmAdapters", "AgentJournals", "GameRunPromptDocumentBuilder.cs"));

            Assert.Contains("BuildSuccessImprovementDocuments", adapter, StringComparison.Ordinal);
            Assert.Contains("BuildSteeringQuestionDocuments", adapter, StringComparison.Ordinal);
            Assert.Contains("BuildHorninessQuestionDocuments", adapter, StringComparison.Ordinal);
        }

        private static PinderLlmAdapter Adapter(
            ILlmTransport transport,
            RecordingJournalSink sink,
            List<OperationalDiagnosticEvent>? diagnostics = null)
            => new PinderLlmAdapter(transport, new PinderLlmAdapterOptions
            {
                GameDefinition = ConfiguredGameDefinition(),
                StatDeliveryInstructions = StatDeliveryInstructions.LoadFrom(File.ReadAllText(Path.Combine(RepoRoot(), "data", "delivery-instructions.yaml"))),
                MaxTokens = 256,
                AgentJournalHostSink = sink,
                AgentJournalClock = FixedClock,
                OnDiagnostic = diagnostics == null ? (Action<OperationalDiagnosticEvent>?)null : diagnostics.Add,
            });

        private static GameDefinition ConfiguredGameDefinition()
            => new GameDefinition(
                GameDefinition.PinderDefaults.Name,
                GameDefinition.PinderDefaults.GameMasterPrompt,
                GameDefinition.PinderDefaults.PlayerAvatarRoleDescription,
                GameDefinition.PinderDefaults.DateeRoleDescription,
                improvementPrompt: GameDefinition.PinderDefaults.ImprovementPrompt,
                steeringPrompt: "Write one steering question after {delivered_message}. Context: {conversation_history}",
                horninessPrompt: "Write one horniness question after {delivered_message}. Context: {conversation_history}");

        private static DialogueContext DialogueContext(string operationId, string output)
            => new DialogueContext(
                "player prompt",
                "datee prompt",
                History(),
                "last",
                Array.Empty<string>(),
                10,
                playerName: "Ari",
                dateeName: "Bea",
                currentTurn: 7,
                availableStats: new[] { StatType.Charm, StatType.Wit, StatType.Honesty },
                agentJournal: Journal(operationId, "game_run_one_shot_record", output: output));

        private static AgentJournalOneShotContext Journal(
            string operationId,
            string destination,
            string? turnId = "turn-7",
            string? output = null,
            IReadOnlyDictionary<string, string>? extra = null)
            => new AgentJournalOneShotContext(
                "game-run-1376",
                operationId,
                operationId,
                destination,
                "test-model",
                turnId: turnId,
                outputLinkId: output,
                context: extra,
                requestId: "request-1376");

        private static AgentJournalInputDocument TestDocument()
            => new AgentJournalInputDocument(
                "test.user",
                AgentJournalInputRole.User,
                "hello",
                new[]
                {
                    new AgentJournalProvenanceRange(
                        "test.user",
                        0,
                        5,
                        AgentJournalRangeKind.RuntimeGenerated,
                        AgentJournalRedactionClass.None,
                        new AgentJournalSourceIdentity(AgentJournalSourceKind.RuntimeGenerated, "runtime", "test")),
                });

        private static IReadOnlyList<(string Sender, string Text)> History()
            => new[] { ("Bea", "I like exact details."), ("Ari", "I noticed.") };

        private static string DialogueOptionsText()
            => "OPTION_1\n[STAT: Charm]\n\"Hey, you come here often?\"\n\n"
               + "OPTION_2\n[STAT: Wit]\n\"Penguins propose with pebbles.\"\n\n"
               + "OPTION_3\n[STAT: Honesty]\n\"I have to be real.\"\n";

        private static IReadOnlyList<Func<PinderLlmAdapter, Task>> FailingAdapterCalls()
            => new Func<PinderLlmAdapter, Task>[]
            {
                async adapter => { await adapter.GetDialogueOptionsAsync(DialogueContext("game.dialogue-options.failure", "dialogue-failure")); },
                async adapter => { await adapter.GetSuccessImprovementAsync(new SuccessImprovementContext(
                    "p", "d", "p", "m", StatType.Charm, "strong", History(),
                    Journal("game.delivery.success-improvement.failure", "game_run_delivery_one_shot_record"))); },
                async adapter => { await adapter.GetSteeringQuestionAsync(new SteeringContext(
                    "p", "d", "p", "m", History(),
                    Journal("game.delivery.steering-question.failure-broad", "game_run_delivery_append_one_shot_record"))); },
                async adapter => { await adapter.GetHorninessQuestionAsync(new HorninessQuestionContext(
                    "p", "d", "p", "m", History(),
                    Journal("game.delivery.horniness-question.failure", "game_run_delivery_append_one_shot_record"))); },
            };

        private static DateTimeOffset FixedClock()
            => new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

        private static string RepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "Pinder.Core.sln")))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repo root.");
        }

        private static string Relative(string path)
            => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');

        private sealed class PlainTransport : ILlmTransport
        {
            private readonly string _response;

            public PlainTransport(string response)
            {
                _response = response;
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(_response);
            }
        }

        private sealed class ThrowingUsageTransport : ILlmTransport, ITokenUsageProvider
        {
            private readonly PlainTransport _inner;

            public ThrowingUsageTransport(string response)
            {
                _inner = new PlainTransport(response);
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
                => _inner.SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase, ct);

            public SessionTokenUsage GetSessionUsage()
                => throw new InvalidOperationException("usage diagnostics unavailable");
        }

        private sealed class QueueTransport : ILlmTransport, ITokenUsageProvider
        {
            private readonly Queue<object> _responses = new Queue<object>();
            private int _sendCount;
            private int _usageCallCount;

            public int CallCount => _sendCount;
            public int UsageCallCountPerSend { get; set; } = 1;
            public int CacheReadInputTokensPerCall { get; set; }
            public int CacheCreationInputTokensPerCall { get; set; }

            public QueueTransport(params object[] responses)
            {
                foreach (object response in responses)
                {
                    _responses.Enqueue(response);
                }
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                _sendCount++;
                _usageCallCount += UsageCallCountPerSend;
                object next = _responses.Count == 0 ? string.Empty : _responses.Dequeue();
                if (next is Exception ex)
                {
                    throw ex;
                }

                return Task.FromResult((string)next);
            }

            public SessionTokenUsage GetSessionUsage()
                => new SessionTokenUsage
                {
                    InputTokens = _usageCallCount * 11,
                    OutputTokens = _usageCallCount * 7,
                    CacheReadInputTokens = _usageCallCount * CacheReadInputTokensPerCall,
                    CacheCreationInputTokens = _usageCallCount * CacheCreationInputTokensPerCall,
                    CallCount = _usageCallCount,
                };
        }

        private sealed class OverlappingUsageTransport : ILlmTransport, ITokenUsageProvider
        {
            private readonly string _response;
            private readonly TaskCompletionSource<bool> _bothEntered =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _bothAccounted =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _entered;
            private int _accounted;
            private int _inputTokens;
            private int _outputTokens;
            private int _callCount;

            public OverlappingUsageTransport(string response) { _response = response; }

            public async Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                if (Interlocked.Increment(ref _entered) == 2) _bothEntered.TrySetResult(true);
                await _bothEntered.Task.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                Interlocked.Add(ref _inputTokens, 11);
                Interlocked.Add(ref _outputTokens, 7);
                Interlocked.Increment(ref _callCount);
                if (Interlocked.Increment(ref _accounted) == 2) _bothAccounted.TrySetResult(true);
                await _bothAccounted.Task.ConfigureAwait(false);
                return _response;
            }

            public SessionTokenUsage GetSessionUsage()
                => new SessionTokenUsage
                {
                    InputTokens = Volatile.Read(ref _inputTokens),
                    OutputTokens = Volatile.Read(ref _outputTokens),
                    CacheCreationInputTokens = 0,
                    CacheReadInputTokens = 0,
                    CallCount = Volatile.Read(ref _callCount),
                };
        }

        private sealed class RecordingJournalSink : IAgentJournalSink
        {
            public readonly List<AgentJournalSinkRecord> Records = new List<AgentJournalSinkRecord>();

            public IReadOnlyList<LlmInvocationRecord> Invocations => Records
                .Where(record => record.Record is LlmInvocationRecord)
                .Select(record => (LlmInvocationRecord)record.Record)
                .ToArray();

            public IReadOnlyList<LlmResultRecord> Results => Records
                .Where(record => record.Record is LlmResultRecord)
                .Select(record => (LlmResultRecord)record.Record)
                .ToArray();

            public LlmInvocationRecord Invocation => Invocations.Last();
            public LlmResultRecord Result => Results.Last();

            public Task PersistAsync(AgentJournalSinkRecord record, CancellationToken cancellationToken)
            {
                if (record.Record is LlmResultRecord result
                    && result.UsageStatus == AgentJournalUsageStatus.Unknown)
                {
                    throw new InvalidOperationException(
                        "Production one-shot records must declare usage availability; unknown is historical compatibility only.");
                }

                lock (Records)
                {
                    Records.Add(record);
                }
                return Task.CompletedTask;
            }
        }

        private static void AssertInvocationAndResult(
            RecordingJournalSink sink,
            string executionClass,
            string destination,
            string phase,
            string? turnId,
            AgentJournalTerminalStatus status)
        {
            Assert.Equal(2, sink.Records.Count);
            Assert.Equal(executionClass, sink.Invocation.Correlation.ExecutionClass);
            Assert.Equal(destination, sink.Invocation.Correlation.JournalDestination);
            Assert.Equal(AgentJournalOneShotContext.GameRunBundleOwner, sink.Invocation.Correlation.Owner);
            Assert.Equal(phase, sink.Invocation.Phase);
            Assert.Equal(turnId, sink.Invocation.Correlation.TurnId);
            Assert.Null(sink.Invocation.Correlation.AgentSessionId);
            Assert.Equal(status, sink.Result.TerminalStatus);
            Assert.Equal(sink.Invocation.Correlation.InvocationId, sink.Result.Correlation.InvocationId);
            Assert.NotEqual(AgentJournalUsageStatus.Unknown, sink.Result.UsageStatus);
        }

        private static void AssertUsage(
            LlmResultRecord result,
            int cacheCreationInputTokens = 0,
            int cacheReadInputTokens = 0)
        {
            Assert.Equal(AgentJournalUsageStatus.Complete, result.UsageStatus);
            Assert.NotNull(result.Usage);
            Assert.Equal(11, result.Usage!.InputTokens);
            Assert.Equal(7, result.Usage.OutputTokens);
            Assert.Equal(18, result.Usage.TotalTokens);
            Assert.Equal(cacheCreationInputTokens, result.Usage.CacheCreationInputTokens);
            Assert.Equal(cacheReadInputTokens, result.Usage.CacheReadInputTokens);
        }
    }
}
