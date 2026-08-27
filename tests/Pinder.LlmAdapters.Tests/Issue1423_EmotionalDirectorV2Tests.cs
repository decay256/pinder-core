using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.Core.Text;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class Issue1423_EmotionalDirectorV2Tests
    {
        static Issue1423_EmotionalDirectorV2Tests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        public void Contract_AcceptsExactV2ShapeAndRejectsV1OrDrift()
        {
            var catalog = BuiltInCatalog();
            var emotions = CharacterEmotionCatalog.Load(catalog);

            Assert.True(CharacterEmotionalDirectionContract.TryParse(ValidDirectionJson(), true, emotions, out var direction, out var errorCode));
            Assert.Equal(string.Empty, errorCode);
            Assert.NotNull(direction);
            Assert.Equal("relief", direction!.PrimaryEmotion);
            Assert.Equal(CharacterEmotionalDirection.NoneSecondaryEmotion, direction.SecondaryEmotion);
            Assert.Equal("controlled", direction.RegulatoryState);
            Assert.Equal(4, direction.Activation);
            Assert.Equal("escalating", direction.Trajectory);
            Assert.Equal("fear of being dismissed", direction.CoreThreatOrDesire);

            AssertRejects(V1Json(), "invalid_schema_version", emotions);
            AssertRejects(RemoveField("trajectory"), "missing_field", emotions);
            AssertRejects(AddField("unexpected", "value"), "unexpected_field", emotions);
            AssertRejects(Replace("activation", "4"), "invalid_activation", emotions);
            AssertRejects(Replace("activation", 6), "invalid_activation", emotions);
            AssertRejects(Replace("primary_emotion", "anxiety"), "unsupported_primary_emotion", emotions);
            AssertRejects(Replace("primary_emotion", "ambivalence"), "unsupported_primary_emotion", emotions);
            AssertRejects(Replace("primary_emotion", "numbness"), "unsupported_primary_emotion", emotions);
            AssertRejects(Replace("secondary_emotion", "relief"), "duplicate_primary_secondary_emotion", emotions);
            AssertRejects(Replace("secondary_emotion", "anxiety"), "unsupported_secondary_emotion", emotions);
            AssertRejects(Replace("regulatory_state", "alert"), "unsupported_regulatory_state", emotions);
            AssertRejects(Replace("trajectory", "looping"), "unsupported_trajectory", emotions);
            AssertRejects(ReplaceMany(("regulatory_state", "conflicted"), ("secondary_emotion", CharacterEmotionalDirection.NoneSecondaryEmotion)), "conflicted_requires_secondary_emotion", emotions);
        }

        [Theory]
        [InlineData("anxious")]
        [InlineData("guarded")]
        [InlineData("overwhelmed")]
        [InlineData("conflicted")]
        [InlineData("numb")]
        [InlineData("dissociated")]
        public void Contract_UsesRegulatoryStatesWithoutAdmittingThemAsPrimaryEmotion(string regulatoryState)
        {
            var emotions = CharacterEmotionCatalog.Load(BuiltInCatalog());
            string json = regulatoryState == "conflicted"
                ? ReplaceMany(("regulatory_state", regulatoryState), ("secondary_emotion", "fear"))
                : Replace("regulatory_state", regulatoryState);

            Assert.True(CharacterEmotionalDirectionContract.TryParse(json, true, emotions, out var direction, out var errorCode));
            Assert.Equal(string.Empty, errorCode);
            Assert.Equal(regulatoryState, direction!.RegulatoryState);
        }

        [Fact]
        public async Task DateeDuplicateVisibleMessage_ReusesDirectorAndRetriesWithRepairPrompt()
        {
            var transport = new RecordingTransport(
                ValidDirectionJson(impulse: "wants to answer with a precise invitation"),
                "that lands softer than i expected.",
                "A new visible reply with a different move.");
            var violations = new List<LlmContractViolation>();
            var adapter = CreateAdapter(transport, retries: 1, onViolation: violations.Add);
            var priorHistory = new[]
            {
                ConversationMessage.User("older player line"),
                ConversationMessage.Assistant("That lands softer than I expected."),
            };

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(MakeContext(), priorHistory);

            Assert.Equal(new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse, LlmPhase.OpponentResponse }, transport.Phases.ToArray());
            Assert.Equal(1, transport.Phases.Count(phase => phase == LlmPhase.EmotionalDirector));
            Assert.Single(violations);
            Assert.Equal("repeated_visible_message", violations[0].Reason);
            Assert.Equal("StrictDateeResponseParser", violations[0].ParserName);
            Assert.DoesNotContain("REPETITION REPAIR", transport.UserMessages[1], StringComparison.Ordinal);
            Assert.Contains("REPETITION REPAIR", transport.UserMessages[2], StringComparison.Ordinal);
            Assert.Equal(2, transport.UserMessages.Count(message => message.Contains("Impulse: wants to answer with a precise invitation", StringComparison.Ordinal)));
            Assert.Equal("A new visible reply with a different move.", result.Response.MessageText);
            Assert.DoesNotContain(result.NewHistoryEntries, entry => entry.Content.Contains("that lands softer", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task DateeNovelVisibleMessage_StaysOnHealthyTwoCallPath()
        {
            var transport = new RecordingTransport(ValidDirectionJson(), "A fresh visible answer.");
            var adapter = CreateAdapter(transport, retries: 1);
            var priorHistory = new[] { ConversationMessage.Assistant("Older accepted answer.") };

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(MakeContext(), priorHistory);

            Assert.Equal(new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse }, transport.Phases.ToArray());
            Assert.Equal("A fresh visible answer.", result.Response.MessageText);
        }

        [Fact]
        public void DuplicateGuard_NormalizesOnlyExactVisibleDuplicates()
        {
            var history = new[]
            {
                ConversationMessage.User("That lands softer than I expected."),
                ConversationMessage.Assistant("That lands softer than I expected."),
            };

            Assert.True(DateeVisibleMessageDuplicateGuard.IsDuplicateAcceptedVisibleMessage("  !! THAT   LANDS  SOFTER THAN I EXPECTED. ?? ", history));
            Assert.True(DateeVisibleMessageDuplicateGuard.IsDuplicateAcceptedVisibleMessage("\u2605 \uff34\uff48\uff41\uff54 lands softer than I expected. \u2605", history));
            Assert.True(DateeVisibleMessageDuplicateGuard.IsDuplicateAcceptedVisibleMessage("\U0001F680 That lands softer than I expected. \U0001F9ED", history));
            Assert.False(DateeVisibleMessageDuplicateGuard.IsDuplicateAcceptedVisibleMessage("That lands softer than I expected, actually.", history));
            Assert.False(DateeVisibleMessageDuplicateGuard.IsDuplicateAcceptedVisibleMessage("That lands softer than I expected.", new[] { ConversationMessage.User("That lands softer than I expected.") }));
        }

        [Fact]
        public void PromptCompiler_RendersBoundedContinuityAndRepairWithYamlProvenance()
        {
            PromptCatalog catalog = BuiltInCatalog();
            var compiler = new EmotionalPromptCompiler(catalog);
            DateeContext context = MakeContext(previousDirections: new[]
            {
                Summary(5, "fear", "hope", "conflicted", 5, "volatile", "almost asks for reassurance"),
                Summary(6, "relief", "none", "controlled", 4, "easing", "lets the reply soften"),
            });

            var director = compiler.CompileDirector(context);

            Assert.Contains("schema_version", director.SystemPrompt.Text, StringComparison.Ordinal);
            Assert.Contains(CharacterEmotionalDirectionContract.SchemaVersion, director.SystemPrompt.Text, StringComparison.Ordinal);
            Assert.Contains("Previous accepted emotional directions", director.SystemPrompt.Text, StringComparison.Ordinal);
            Assert.Contains("Turn 5", director.SystemPrompt.Text, StringComparison.Ordinal);
            Assert.Contains("Turn 6", director.SystemPrompt.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("{previous_accepted_directions}", director.SystemPrompt.Text, StringComparison.Ordinal);
            Assert.Contains(director.SystemPrompt.Spans, span => span.SourceFile == "data/prompts/emotional-reactions.yaml" && span.Key == "emotional-reaction-previous-direction-line");

            var direction = Direction();
            PromptTraceResult performance = SessionDocumentBuilder.BuildDateePerformancePromptEx(context, direction, catalog);
            Assert.Contains("Secondary emotion: none", performance.Text, StringComparison.Ordinal);
            Assert.Contains("Regulatory state: controlled", performance.Text, StringComparison.Ordinal);
            Assert.Contains("Activation: 4", performance.Text, StringComparison.Ordinal);
            Assert.Contains("Trajectory: escalating", performance.Text, StringComparison.Ordinal);
            Assert.Contains("Core threat/desire: fear of being dismissed", performance.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Intensity:", performance.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Underlying feeling:", performance.Text, StringComparison.Ordinal);
            foreach (string key in new[]
            {
                "CharacterEmotionalDirection.SecondaryEmotion",
                "CharacterEmotionalDirection.RegulatoryState",
                "CharacterEmotionalDirection.Activation",
                "CharacterEmotionalDirection.Trajectory",
                "CharacterEmotionalDirection.CoreThreatOrDesire",
            })
            {
                Assert.Contains(performance.Spans, span => span.SourceFile == SessionDocumentBuilder.CharacterEmotionalDirectionRuntimeSource && span.Key == key);
            }

            PromptTraceResult repaired = compiler.CompilePerformanceRepetitionRepairPrompt(performance);
            Assert.Contains("REPETITION REPAIR", repaired.Text, StringComparison.Ordinal);
            Assert.Contains(repaired.Spans, span => span.SourceFile == "data/prompts/templates.yaml" && span.Key == "datee-response-repetition-repair");
        }

        [Fact]
        public async Task DateePerformanceDiagnostics_RetainAllV2RuntimeProvenanceKeys()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var adapter = CreateAdapter(
                new RecordingTransport(ValidDirectionJson(), "A fresh visible answer."),
                onDiagnostic: diagnostics.Add);

            await adapter.GetDateeResponseAsync(MakeContext(), Array.Empty<ConversationMessage>());

            OperationalDiagnosticEvent terminal = diagnostics.Last(diagnostic =>
                diagnostic.EventName == "LlmTransportSucceeded"
                && diagnostic.PhaseCode == LlmPhase.OpponentResponse);
            string retainedKeys = terminal.CorrelationHints["prompt_trace_keys"];
            foreach (string key in new[]
            {
                "CharacterEmotionalDirection.PrimaryEmotion",
                "CharacterEmotionalDirection.SecondaryEmotion",
                "CharacterEmotionalDirection.RegulatoryState",
                "CharacterEmotionalDirection.Activation",
                "CharacterEmotionalDirection.Trajectory",
                "CharacterEmotionalDirection.CoreThreatOrDesire",
                "CharacterEmotionalDirection.Interpretation",
                "CharacterEmotionalDirection.Impulse",
                "CharacterEmotionalDirection.Restraint",
                "CharacterEmotionalDirection.ResponsePosture",
            })
            {
                Assert.Contains(key, retainedKeys.Split(','));
                Assert.True(PromptTraceDiagnosticContract.IsSafeTraceKey(key));
            }

            Assert.DoesNotContain("EmotionalDirector.Intensity", retainedKeys, StringComparison.Ordinal);
            Assert.DoesNotContain("EmotionalDirector.UnderlyingFeeling", retainedKeys, StringComparison.Ordinal);
            Assert.False(PromptTraceDiagnosticContract.IsSafeTraceKey("EmotionalDirector.Intensity"));
            Assert.False(PromptTraceDiagnosticContract.IsSafeTraceKey("EmotionalDirector.UnderlyingFeeling"));
        }

        [Fact]
        public async Task ContinuityRemainsAbsentFromVisibleHistoriesAndAvatarPlayerPrompts()
        {
            const string privateSentinel = "PRIVATE-CONTINUITY-SENTINEL-1423";
            var transport = new PhaseRecordingTransport(
                (LlmPhase.AvatarEmotionalDirector, ValidDirectionJson(
                    responsePosture: "Writing from relief, turns warmer while still checking sincerity")));
            var adapter = CreateAdapter(transport);
            var trapRegistry = new NullTrapRegistry();
            var session = new GameSession(
                MakeProfile("Player"),
                MakeProfile("Datee"),
                adapter,
                new FixedDice(10),
                trapRegistry,
                new GameSessionConfig(
                    clock: TestHelpers.MakeClock(),
                    startingInterest: 12,
                    maxDialogueOptions: 2));
            session.RestoreState(new ResimulateData
            {
                TargetInterest = 12,
                TurnNumber = 4,
                DateeEmotionalDirectionHistory = new List<CharacterEmotionalDirectionSummary>
                {
                    new CharacterEmotionalDirectionSummary(
                        3,
                        "fear",
                        "hope",
                        "conflicted",
                        5,
                        "volatile",
                        privateSentinel),
                },
            }, trapRegistry);

            await session.StartTurnAsync();

            Assert.DoesNotContain(session.ConversationHistory, entry => entry.Text.Contains(privateSentinel, StringComparison.Ordinal));
            Assert.DoesNotContain(session.AvatarHistory, entry => entry.Content.Contains(privateSentinel, StringComparison.Ordinal));
            for (int index = 0; index < transport.Phases.Count; index++)
            {
                if (transport.Phases[index] == LlmPhase.AvatarEmotionalDirector
                    || transport.Phases[index] == LlmPhase.DialogueOptions)
                {
                    Assert.DoesNotContain(privateSentinel, transport.SystemMessages[index], StringComparison.Ordinal);
                    Assert.DoesNotContain(privateSentinel, transport.UserMessages[index], StringComparison.Ordinal);
                }
            }
            Assert.Contains(LlmPhase.AvatarEmotionalDirector, transport.Phases);
            Assert.Contains(LlmPhase.DialogueOptions, transport.Phases);
        }

        private static void AssertRejects(string json, string expectedReason, IReadOnlyList<string> emotions)
        {
            Assert.False(CharacterEmotionalDirectionContract.TryParse(json, true, emotions, out _, out var errorCode));
            Assert.Equal(expectedReason, errorCode);
        }

        private static CharacterEmotionalDirection Direction() => new CharacterEmotionalDirection(
            "relief",
            CharacterEmotionalDirection.NoneSecondaryEmotion,
            "controlled",
            4,
            "escalating",
            "fear of being dismissed",
            "reads the message as specific warmth that is probably meant for them",
            "leans in with a careful question",
            "keeps the reply tentative but available",
            "turns warmer while still checking sincerity");

        private static CharacterEmotionalDirectionSummary Summary(
            int turn,
            string primary,
            string secondary,
            string regulatoryState,
            int activation,
            string trajectory,
            string impulse)
        {
            return new CharacterEmotionalDirectionSummary(turn, primary, secondary, regulatoryState, activation, trajectory, impulse);
        }

        private static string ValidDirectionJson(
            string? primaryEmotion = null,
            string? secondaryEmotion = null,
            string? regulatoryState = null,
            int activation = 4,
            string? trajectory = null,
            string? coreThreatOrDesire = null,
            string? interpretation = null,
            string? impulse = null,
            string? restraint = null,
            string? responsePosture = null)
        {
            var direction = new JObject
            {
                ["schema_version"] = CharacterEmotionalDirectionContract.SchemaVersion,
                ["primary_emotion"] = primaryEmotion ?? "relief",
                ["secondary_emotion"] = secondaryEmotion ?? CharacterEmotionalDirection.NoneSecondaryEmotion,
                ["regulatory_state"] = regulatoryState ?? "controlled",
                ["activation"] = activation,
                ["trajectory"] = trajectory ?? "escalating",
                ["core_threat_or_desire"] = coreThreatOrDesire ?? "fear of being dismissed",
                ["interpretation"] = interpretation ?? "reads the message as specific warmth that is probably meant for them",
                ["impulse"] = impulse ?? "leans in with a careful question",
                ["restraint"] = restraint ?? "keeps the reply tentative but available",
                ["response_posture"] = responsePosture ?? "turns warmer while still checking sincerity",
            };
            return direction.ToString(Formatting.None);
        }

        private static string V1Json()
        {
            return new JObject
            {
                ["schema_version"] = "emotional_director.v1",
                ["primary_emotion"] = "relief",
                ["intensity"] = "moderate and steadily rising",
                ["underlying_feeling"] = "fear of being dismissed",
                ["interpretation"] = "reads the message as specific warmth that is probably meant for them",
                ["impulse"] = "leans in with a careful question",
                ["restraint"] = "keeps the reply tentative but available",
                ["response_posture"] = "turns warmer while still checking sincerity",
            }.ToString(Formatting.None);
        }

        private static string RemoveField(string name)
        {
            var json = JObject.Parse(ValidDirectionJson());
            json.Remove(name);
            return json.ToString(Formatting.None);
        }

        private static string AddField(string name, JToken value)
        {
            var json = JObject.Parse(ValidDirectionJson());
            json[name] = value;
            return json.ToString(Formatting.None);
        }

        private static string Replace(string name, JToken value)
        {
            var json = JObject.Parse(ValidDirectionJson());
            json[name] = value;
            return json.ToString(Formatting.None);
        }

        private static string ReplaceMany(params (string Name, JToken Value)[] values)
        {
            var json = JObject.Parse(ValidDirectionJson());
            foreach (var (name, value) in values)
            {
                json[name] = value;
            }

            return json.ToString(Formatting.None);
        }

        private static PinderLlmAdapter CreateAdapter(
            ILlmTransport transport,
            int retries = 0,
            Action<LlmContractViolation>? onViolation = null,
            Action<OperationalDiagnosticEvent>? onDiagnostic = null)
        {
            return new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    PromptCatalog = BuiltInCatalog(),
                    MaxContractViolationRetries = retries,
                    ContractViolationBackoffMs = 1,
                    OnLlmContractViolation = onViolation,
                    OnDiagnostic = onDiagnostic,
                });
        }

        private static DateeContext MakeContext(IReadOnlyList<CharacterEmotionalDirectionSummary>? previousDirections = null)
        {
            return new DateeContext(
                dateePrompt: "datee prompt",
                conversationHistory: new[]
                {
                    ("Player", "older visible player line"),
                    ("Datee", "older visible datee line"),
                },
                dateeLastMessage: "older visible datee line",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: "visible delivered line",
                interestBefore: 8,
                interestAfter: 12,
                responseDelayMinutes: 0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 7,
                interestBeforeState: InterestState.Lukewarm,
                interestAfterState: InterestState.Interested,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    RollOutcomeIntensity.Strong,
                    TestHelpers.MakePsychiatricDiagnosis()),
                previousAcceptedEmotionalDirections: previousDirections);
        }

        private static string ValidDialogueOptionsJson()
        {
            return new JObject
            {
                ["schema_version"] = DialogueOptionsStructuredContract.SchemaVersion,
                ["options"] = new JArray
                {
                    new JObject
                    {
                        ["stat"] = "CHARM",
                        ["text"] = "A warm opening line.",
                        ["callback"] = null,
                        ["combo"] = null,
                    },
                    new JObject
                    {
                        ["stat"] = "HONESTY",
                        ["text"] = "A direct honest opening line.",
                        ["callback"] = null,
                        ["combo"] = null,
                    },
                },
            }.ToString(Formatting.None);
        }

        private static CharacterProfile MakeProfile(string name)
        {
            return TestHelpers.MakeCharacterProfile(
                TestHelpers.MakeStatBlock(2),
                "You are " + name + ".",
                name,
                new TimingProfile(10, 0.2f, 0.0f, "neutral"),
                1);
        }

        private static PromptCatalog BuiltInCatalog()
        {
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            catalog.ValidateRuntimeCatalog();
            return catalog;
        }

        private static string FindPromptsRoot()
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "data", "prompts");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Could not locate bundled data/prompts.");
        }

        private sealed class FixedDice : IDiceRoller
        {
            private readonly int _value;

            public FixedDice(int value)
            {
                _value = value;
            }

            public int Roll(int sides) => _value;
        }

        private sealed class PhaseRecordingTransport : ILlmTransport, IConversationLlmTransport
        {
            private readonly Dictionary<string, Queue<string>> _responses =
                new Dictionary<string, Queue<string>>(StringComparer.Ordinal);

            public PhaseRecordingTransport(params (string Phase, string Response)[] responses)
            {
                foreach (var response in responses)
                {
                    if (!_responses.TryGetValue(response.Phase, out Queue<string>? queue))
                    {
                        queue = new Queue<string>();
                        _responses[response.Phase] = queue;
                    }
                    queue.Enqueue(response.Response);
                }
            }

            public List<string?> Phases { get; } = new List<string?>();
            public List<string> SystemMessages { get; } = new List<string>();
            public List<string> UserMessages { get; } = new List<string>();
            public bool SupportsConversationMessages => true;

            public Task<string> SendConversationAsync(
                string systemPrompt,
                IReadOnlyList<ConversationMessage> priorMessages,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken cancellationToken = default)
                => SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase, cancellationToken);

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (phase == null)
                    throw new InvalidOperationException("A phase is required for the privacy transport.");

                string response;
                if (phase == LlmPhase.DialogueOptions)
                {
                    response = BuildDialogueOptionsForPrompt(userMessage);
                }
                else if (_responses.TryGetValue(phase, out Queue<string>? queue)
                    && queue.Count > 0)
                {
                    response = queue.Dequeue();
                }
                else
                {
                    throw new InvalidOperationException("No scripted response for phase '" + phase + "'.");
                }

                Phases.Add(phase);
                SystemMessages.Add(systemPrompt);
                UserMessages.Add(userMessage);
                return Task.FromResult(response);
            }

            private static string BuildDialogueOptionsForPrompt(string prompt)
            {
                System.Text.RegularExpressions.Match match =
                    System.Text.RegularExpressions.Regex.Match(
                        prompt,
                        @"Each stat must be one of: (?<stats>[A-Z_, ]+)\.",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                if (!match.Success)
                    throw new InvalidOperationException("Available stat list was not present in the dialogue-options prompt.");

                string[] stats = match.Groups["stats"].Value
                    .Split(',')
                    .Select(stat => stat.Trim())
                    .Where(stat => stat.Length > 0)
                    .Take(2)
                    .ToArray();
                if (stats.Length != 2)
                    throw new InvalidOperationException("Expected at least two available dialogue-option stats.");

                return new JObject
                {
                    ["schema_version"] = DialogueOptionsStructuredContract.SchemaVersion,
                    ["options"] = new JArray
                    {
                        new JObject
                        {
                            ["stat"] = stats[0],
                            ["text"] = "A first opening line.",
                            ["callback"] = null,
                            ["combo"] = null,
                        },
                        new JObject
                        {
                            ["stat"] = stats[1],
                            ["text"] = "A second opening line.",
                            ["callback"] = null,
                            ["combo"] = null,
                        },
                    },
                }.ToString(Formatting.None);
            }
        }

        private sealed class RecordingTransport : ILlmTransport
        {
            private readonly Queue<string> _responses;

            public RecordingTransport(params string[] responses)
            {
                _responses = new Queue<string>(responses);
            }

            public List<string?> Phases { get; } = new List<string?>();
            public List<string> SystemMessages { get; } = new List<string>();
            public List<string> UserMessages { get; } = new List<string>();

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Phases.Add(phase);
                SystemMessages.Add(systemPrompt);
                UserMessages.Add(userMessage);
                return Task.FromResult(_responses.Dequeue());
            }
        }
    }
}
