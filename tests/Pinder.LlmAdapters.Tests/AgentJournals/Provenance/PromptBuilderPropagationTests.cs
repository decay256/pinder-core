using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pinder.Core.Conversation;
using Pinder.Core.Characters;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.Core.Text;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals.Provenance
{
    public sealed class PromptBuilderPropagationTests
    {
        private static readonly string[] RequiredBuilderIds =
        {
            "session.system",
            "session.user",
            "datee.emotional-director.system",
            "datee.emotional-director.user",
            "datee.performance",
            "dialogue-options.system",
            "dialogue-options.user",
            "game.setup.dramatic-arc",
            "delivery.success-improvement",
            "delivery.steering-question",
            "delivery.horniness-question",
            "datee.interest-change-beat.dormant",
        };

        static PromptBuilderPropagationTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        public void AC1_ManifestContainsExactAmendedTwelveRowsAndSymbolMappings()
        {
            using JsonDocument manifest = LoadManifest();
            JsonElement[] rows = Rows(manifest).ToArray();

            Assert.Equal("agent-journal-provenance-builders.v1", manifest.RootElement.GetProperty("schema_version").GetString());
            Assert.True(manifest.RootElement.GetProperty("closed_inventory").GetBoolean());
            Assert.Equal(12, manifest.RootElement.GetProperty("manifest_count").GetInt32());
            Assert.Equal(11, manifest.RootElement.GetProperty("live_builder_count").GetInt32());
            Assert.Equal(1, manifest.RootElement.GetProperty("dormant_guard_count").GetInt32());
            Assert.Equal(RequiredBuilderIds, rows.Select(row => row.GetProperty("id").GetString()).ToArray());
            Assert.Equal(11, rows.Count(row => row.GetProperty("status").GetString() == "live_production"));
            Assert.Single(rows.Where(row => row.GetProperty("status").GetString() == "provider_capable_dormant"));
            Assert.Equal(
                "game_run_delivery_one_shot_record",
                rows.Single(row => row.GetProperty("id").GetString() == "delivery.success-improvement")
                    .GetProperty("recorder_consumer").GetString());
            Assert.Equal(
                "game_run_delivery_append_one_shot_record",
                rows.Single(row => row.GetProperty("id").GetString() == "delivery.steering-question")
                    .GetProperty("recorder_consumer").GetString());
            Assert.Equal(
                "game_run_delivery_append_one_shot_record",
                rows.Single(row => row.GetProperty("id").GetString() == "delivery.horniness-question")
                    .GetProperty("recorder_consumer").GetString());

            foreach (JsonElement row in rows)
            {
                JsonElement implementation = row.GetProperty("implementation");
                string file = Path.Combine(RepoRoot(), implementation.GetProperty("file").GetString()!);
                string pattern = implementation.GetProperty("symbol_pattern").GetString()!;
                Assert.True(File.Exists(file), row.GetProperty("id").GetString() + " implementation file missing");
                Assert.Matches(new Regex(pattern), File.ReadAllText(file));
                Assert.NotEmpty(row.GetProperty("expected_configured_sources").EnumerateArray());
                Assert.NotEmpty(row.GetProperty("runtime_fields").EnumerateArray());
                Assert.NotEmpty(row.GetProperty("recorder_consumer").GetString()!);
            }
        }

        [Fact]
        public void AC2_BeforeAfterGoldenTextsAreIdenticalForEveryLiveBuilderFixture()
        {
            if (Environment.GetEnvironmentVariable("PINDER_UPDATE_PROMPT_GOLDENS") == "1")
            {
                File.WriteAllText(GoldenFixturePath(), SerializeGoldenFixture(), new UTF8Encoding(false));
            }
            byte[] checkedIn = File.ReadAllBytes(GoldenFixturePath());
            byte[] regenerated = Encoding.UTF8.GetBytes(SerializeGoldenFixture());
            Assert.Equal(checkedIn, regenerated);

            GoldenFixture[] fixture = LoadGoldenFixture();
            Assert.Equal(RequiredBuilderIds, fixture.Select(row => row.Id).ToArray());
            Assert.Equal(11, fixture.Count(row => row.Status == "live_production"));

            foreach (GoldenCase golden in GoldenCases())
            {
                Assert.Equal(golden.BeforeDocuments.Count, golden.Documents.Count);
                for (int index = 0; index < golden.Documents.Count; index++)
                {
                    Assert.Equal(golden.BeforeDocuments[index].Role, golden.Documents[index].Role);
                    Assert.Equal(golden.BeforeDocuments[index].Text, golden.Documents[index].Text);
                }

                GoldenFixture expected = fixture.Single(row => row.Id == golden.Id);
                Assert.Equal(golden.Documents.Count, expected.AfterDocuments.Count);
            }

            Assert.True(GoldenCases().Count(golden => golden.Status == "live_production") >= 11);
        }

        [Fact]
        public void AC3_FinalRangesProvideCompleteValidCoverageAndClassification()
        {
            foreach (GoldenCase golden in GoldenCases())
            {
                foreach (AnnotatedInvocationDocument document in golden.Documents)
                {
                    Assert.True(
                        document.ValidationResult.IsValid,
                        golden.Id + " " + string.Join(",", document.ValidationResult.Errors.Select(error => error.Code + "@" + error.Path)));
                    AssertCoverage(document);
                    foreach (AgentJournalProvenanceRange range in document.Ranges)
                    {
                        if (range.RangeKind == AgentJournalRangeKind.Configured)
                        {
                            Assert.True(
                                range.Source.Kind == AgentJournalSourceKind.Configuration
                                || range.Source.Kind == AgentJournalSourceKind.Catalog);
                            Assert.True(
                                !string.IsNullOrWhiteSpace(range.Source.Revision)
                                || !string.IsNullOrWhiteSpace(range.Source.ContentHash));
                        }
                        else
                        {
                            Assert.Equal(AgentJournalSourceKind.RuntimeGenerated, range.Source.Kind);
                        }
                    }
                }
            }
        }

        [Fact]
        public void AC4_RepeatedTextSurrogatePairsTrimmingAndHistoryFormattingKeepOffsets()
        {
            GameDefinition gameDefinition = TinyGameDefinition(
                steeringPrompt: "  ask about {delivered_message} then {delivered_message}\n{conversation_history}  ",
                horninessPrompt: "  tease {delivered_message} then {delivered_message}\n{conversation_history}  ");
            var history = new[]
            {
                ("Player", "first 😄 line"),
                ("Datee", "second line"),
            };
            var context = new SteeringContext(
                "avatar 😄 profile",
                "Datee",
                "Player",
                "repeat 😄 repeat",
                history);

            AnnotatedInvocationDocument document =
                GameRunPromptDocumentBuilder.BuildSteeringQuestionDocuments(
                    context,
                    gameDefinition,
                    PromptTemplates.Catalog).User;

            Assert.Equal(
                "ask about repeat 😄 repeat then repeat 😄 repeat\nPlayer: first 😄 line\nDatee: second line",
                document.Text);
            Assert.DoesNotContain(document.Ranges, range => range.StartUtf16 < 0 || range.EndUtf16 > document.Text.Length);
            Assert.Equal(2, document.Ranges.Count(range => range.Source.KeyPath == "SteeringContext.DeliveredMessage"));
            Assert.Contains(document.Ranges, range =>
                range.RangeKind == AgentJournalRangeKind.RuntimeGenerated
                && range.Source.KeyPath == "conversation_history.entry"
                && document.Text.Substring(range.StartUtf16, range.EndUtf16 - range.StartUtf16).Contains("😄", StringComparison.Ordinal));
            AssertCoverage(document);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AC4_DeliveryQuestionBuildersRequireAndAnnotateDeliveredMessage(bool steering)
        {
            GameDefinition missingToken = TinyGameDefinition(
                steeringPrompt: "Ask from history only: {conversation_history}",
                horninessPrompt: "Ask from history only: {conversation_history}");

            InvalidOperationException error = steering
                ? Assert.Throws<InvalidOperationException>(() =>
                    GameRunPromptDocumentBuilder.BuildSteeringQuestionDocuments(
                        Steering(), missingToken, PromptTemplates.Catalog))
                : Assert.Throws<InvalidOperationException>(() =>
                    GameRunPromptDocumentBuilder.BuildHorninessQuestionDocuments(
                        Horniness(), missingToken, PromptTemplates.Catalog));
            Assert.Contains("{delivered_message}", error.Message, StringComparison.Ordinal);

            GameDefinition valid = TinyGameDefinition(
                steeringPrompt: "Ask about {delivered_message}\n{conversation_history}",
                horninessPrompt: "Ask about {delivered_message}\n{conversation_history}");
            AnnotatedInvocationDocument document = steering
                ? GameRunPromptDocumentBuilder.BuildSteeringQuestionDocuments(
                    Steering(), valid, PromptTemplates.Catalog).User
                : GameRunPromptDocumentBuilder.BuildHorninessQuestionDocuments(
                    Horniness(), valid, PromptTemplates.Catalog).User;
            string keyPath = steering
                ? "SteeringContext.DeliveredMessage"
                : "HorninessQuestionContext.DeliveredMessage";
            AgentJournalProvenanceRange delivered = Assert.Single(document.Ranges.Where(range =>
                range.Source.KeyPath == keyPath));
            Assert.Equal(AgentJournalRangeKind.RuntimeGenerated, delivered.RangeKind);
            Assert.Equal("delivered 😄 line", document.Text.Substring(
                delivered.StartUtf16,
                delivered.EndUtf16 - delivered.StartUtf16));
        }

        [Fact]
        public void AC5_StaticGuardFindsNoDormantInterestChangeProductionActivation()
        {
            JsonElement row = Rows(LoadManifest()).Single(item =>
                item.GetProperty("id").GetString() == "datee.interest-change-beat.dormant");
            JsonElement guard = row.GetProperty("dormant_activation_guard");
            string pattern = guard.GetProperty("pattern").GetString()!;
            string[] allowed = guard.GetProperty("allowed_files").EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();

            string[] hits = Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
                .Where(file => Regex.IsMatch(File.ReadAllText(file), pattern))
                .Select(file => Normalize(Path.GetRelativePath(RepoRoot(), file)))
                .Where(file => !allowed.Contains(file, StringComparer.Ordinal))
                .ToArray();

            Assert.Empty(hits);
        }

        private static IEnumerable<GoldenCase> GoldenCases()
        {
            PromptCatalog catalog = PromptCatalog.ResolveCatalogOrThrow(PromptTemplates.Catalog);
            GameDefinition gameDefinition = GameDefinition.PinderDefaults;
            GameDefinition deliveryGameDefinition = TinyGameDefinition(
                steeringPrompt: "Ask {datee_name} about {delivered_message}\n{conversation_history}",
                horninessPrompt: "Write one horny followup for {delivered_message}\n{conversation_history}");
            DialogueContext dialogueContext = Dialogue();
            DateeContext dateeContext = Datee();
            EmotionalPromptCompiler compiler = new EmotionalPromptCompiler(catalog);
            CompiledEmotionalDirectorPrompt director = compiler.CompileDirector(dateeContext);
            CharacterEmotionalDirection direction = Direction();
            PromptTraceResult performance = compiler.CompilePerformance(dateeContext, direction);
            PromptEntry dramaticArc = catalog.RequireCompleteEntry("dramatic_arc", "missing dramatic_arc");
            IReadOnlyDictionary<string, string> dramaticValues = DramaticArcValues();
            GameRunPromptDocumentPair dramaticDocuments =
                GameRunPromptDocumentBuilder.BuildDramaticArcDocuments(dramaticArc, dramaticValues);
            GameRunPromptDocumentPair successDocuments =
                GameRunPromptDocumentBuilder.BuildSuccessImprovementDocuments(
                    SuccessImprovement(),
                    StatDeliveryInstructions.TryLoadDefault(),
                    deliveryGameDefinition,
                    catalog)!;
            GameRunPromptDocumentPair steeringDocuments =
                GameRunPromptDocumentBuilder.BuildSteeringQuestionDocuments(
                    Steering(),
                    deliveryGameDefinition,
                    catalog);
            GameRunPromptDocumentPair horninessDocuments =
                GameRunPromptDocumentBuilder.BuildHorninessQuestionDocuments(
                    Horniness(),
                    deliveryGameDefinition,
                    catalog);

            yield return GoldenCase.Live(
                "session.system",
                SessionSystemPromptBuilder.BuildDateeEx(dateeContext.DateePrompt, gameDefinition).Text,
                GameRunPromptDocumentBuilder.BuildDateeSystemDocument(dateeContext.DateePrompt, gameDefinition));
            yield return GoldenCase.Live(
                "session.user",
                SessionDocumentBuilder.BuildDateePromptEx(dateeContext, catalog).Text,
                GameRunPromptDocumentBuilder.BuildDateeUserDocument(dateeContext, catalog));
            yield return GoldenCase.Live(
                "datee.emotional-director.system",
                director.SystemPrompt.Text,
                GameRunPromptDocumentBuilder.BuildEmotionalDirectorSystemDocument(director.SystemPrompt));
            yield return GoldenCase.Live(
                "datee.emotional-director.user",
                director.UserPrompt.Text,
                GameRunPromptDocumentBuilder.BuildEmotionalDirectorUserDocument(director.UserPrompt));
            yield return GoldenCase.Live(
                "datee.performance",
                performance.Text,
                GameRunPromptDocumentBuilder.BuildDateePerformanceDocument(performance));
            yield return GoldenCase.Live(
                "dialogue-options.system",
                SessionSystemPromptBuilder.BuildPlayerAvatarEx(dialogueContext.PlayerAvatarPrompt, gameDefinition).Text,
                GameRunPromptDocumentBuilder.BuildPlayerAvatarSystemDocument(dialogueContext.PlayerAvatarPrompt, gameDefinition));
            yield return GoldenCase.Live(
                "dialogue-options.user",
                SessionDocumentBuilder.BuildDialogueOptionsPromptEx(dialogueContext, catalog).Text,
                GameRunPromptDocumentBuilder.BuildDialogueOptionsUserDocument(dialogueContext, catalog));
            yield return GoldenCase.Live(
                "game.setup.dramatic-arc",
                new[]
                {
                    PromptCatalog.Substitute(dramaticArc.SystemPrompt!, dramaticValues),
                    PromptCatalog.Substitute(dramaticArc.UserTemplate!, dramaticValues),
                },
                dramaticDocuments.System,
                dramaticDocuments.User);
            yield return GoldenCase.Live(
                "delivery.success-improvement",
                new[]
                {
                    successDocuments.System.Text,
                    RenderSuccessImprovementBefore(SuccessImprovement(), StatDeliveryInstructions.TryLoadDefault()!, catalog),
                },
                successDocuments.System,
                successDocuments.User);
            yield return GoldenCase.Live(
                "delivery.steering-question",
                new[]
                {
                    steeringDocuments.System.Text,
                    RenderQuestionBefore(deliveryGameDefinition.SteeringPrompt, Steering().DeliveredMessage, Steering().ConversationHistory, catalog),
                },
                steeringDocuments.System,
                steeringDocuments.User);
            yield return GoldenCase.Live(
                "delivery.horniness-question",
                new[]
                {
                    horninessDocuments.System.Text,
                    RenderQuestionBefore(deliveryGameDefinition.HorninessPrompt, Horniness().DeliveredMessage, Horniness().ConversationHistory, catalog),
                },
                horninessDocuments.System,
                horninessDocuments.User);
            yield return GoldenCase.Dormant("datee.interest-change-beat.dormant");
        }

        private static void AssertCoverage(AnnotatedInvocationDocument document)
        {
            if (document.Text.Length == 0)
            {
                Assert.Empty(document.Ranges);
                return;
            }

            int cursor = 0;
            foreach (AgentJournalProvenanceRange range in document.Ranges.OrderBy(range => range.StartUtf16))
            {
                Assert.Equal(cursor, range.StartUtf16);
                Assert.True(range.EndUtf16 > range.StartUtf16);
                cursor = range.EndUtf16;
            }

            Assert.Equal(document.Text.Length, cursor);
        }

        private static string RenderSuccessImprovementBefore(
            SuccessImprovementContext context,
            StatDeliveryInstructions instructions,
            PromptCatalog catalog)
        {
            string instruction = PromptCatalog.Substitute(
                instructions.Get(context.Stat, context.TierKey)!,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["player_name"] = context.PlayerName,
                    ["datee_name"] = context.DateeName,
                    ["delivered_message"] = context.DeliveredMessage,
                });
            return PromptCatalog.Substitute(
                    instructions.GetSuccessImprovementPromptTemplate()!,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["player_name"] = context.PlayerName,
                        ["datee_name"] = context.DateeName,
                        ["delivered_message"] = context.DeliveredMessage,
                        ["tier"] = context.TierKey ?? string.Empty,
                        ["tier_upper"] = (context.TierKey ?? string.Empty).ToUpperInvariant(),
                        ["stat"] = context.Stat.ToString(),
                        ["conversation_history"] = FormatConversationHistory(context.ConversationHistory, catalog),
                        ["instruction"] = instruction,
                        ["texting_style_block"] = string.Empty,
                    })
                .Trim();
        }

        private static string RenderQuestionBefore(
            string template,
            string deliveredMessage,
            IReadOnlyList<(string Sender, string Text)> history,
            PromptCatalog catalog)
            => PromptCatalog.Substitute(
                    template,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["player_name"] = "Player",
                        ["datee_name"] = "Datee",
                        ["delivered_message"] = deliveredMessage,
                        ["conversation_history"] = FormatConversationHistory(history, catalog),
                        ["texting_style_block"] = string.Empty,
                    })
                .Trim();

        private static string FormatConversationHistory(
            IEnumerable<(string Sender, string Text)> history,
            PromptCatalog catalog)
        {
            var lines = history.Select(item => item.Sender + ": " + item.Text).ToArray();
            return lines.Length == 0
                ? PromptTemplates.GetCatalogString(catalog, "conversation-history-empty")
                : string.Join(Environment.NewLine, lines);
        }

        private static DialogueContext Dialogue()
            => new DialogueContext(
                playerAvatarPrompt: "player prompt 😄 repeated repeated",
                dateePrompt: "datee prompt",
                conversationHistory: new[] { ("Player", "hello 😄"), ("Datee", "hey") },
                dateeLastMessage: "hey",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                horninessLevel: 7,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 4,
                availableStats: new[] { StatType.Charm, StatType.Rizz, StatType.Honesty });

        private static DateeContext Datee()
            => new DateeContext(
                dateePrompt: "datee prompt 😄 repeated repeated",
                conversationHistory: new[] { ("Player", "hello 😄"), ("Datee", "hey") },
                dateeLastMessage: "hey",
                activeTraps: Array.Empty<string>(),
                currentInterest: 18,
                playerDeliveredMessage: "I meant that more warmly than it sounded 😄.",
                interestBefore: 13,
                interestAfter: 18,
                responseDelayMinutes: 0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 7,
                deliveryTier: FailureTier.Success,
                interestBeforeState: InterestState.Interested,
                interestAfterState: InterestState.VeryIntoIt,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    RollOutcomeIntensity.Strong,
                    Diagnosis()));

        private static SuccessImprovementContext SuccessImprovement()
            => new SuccessImprovementContext(
                "player prompt 😄 repeated repeated",
                "Datee",
                "Player",
                "delivered 😄 line",
                StatType.Charm,
                "strong",
                new[] { ("Player", "hello 😄"), ("Datee", "hey") });

        private static SteeringContext Steering()
            => new SteeringContext(
                "player prompt 😄 repeated repeated",
                "Datee",
                "Player",
                "delivered 😄 line",
                new[] { ("Player", "hello 😄"), ("Datee", "hey") });

        private static HorninessQuestionContext Horniness()
            => new HorninessQuestionContext(
                "player prompt 😄 repeated repeated",
                "Datee",
                "Player",
                "delivered 😄 line",
                new[] { ("Player", "hello 😄"), ("Datee", "hey") });

        private static IReadOnlyDictionary<string, string> DramaticArcValues()
            => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["playerName"] = "Player 😄",
                ["playerStake"] = "wants to seem effortless",
                ["playerBio"] = "keeps rewriting messages",
                ["dateeName"] = "Datee",
                ["dateeStake"] = "wants sincerity",
                ["dateeBio"] = "notices evasions",
            };

        private static CharacterEmotionalDirection Direction()
            => new CharacterEmotionalDirection(
                "relief",
                CharacterEmotionalDirection.NoneSecondaryEmotion,
                "controlled",
                4,
                "escalating",
                "fear of being dismissed",
                "reads the message as specific warmth that is probably meant for them",
                "leans in with a careful question",
                "keeps the reply tentative but available",
                "Writing from relief, turns warmer while still checking sincerity");

        private static Dictionary<string, string> Diagnosis()
            => new Dictionary<string, string>
            {
                [TherapistDiagnosisContract.DerivedFeelingKey] = "Concrete detail makes emotional meaning feel safer.",
                [TherapistDiagnosisContract.DefenseReactionKey] = "Precision protects against being dismissed.",
                [TherapistDiagnosisContract.SafeConnectionKey] = "Safety permits warmer short replies.",
                [TherapistDiagnosisContract.HurtProtectionKey] = "Hurt prompts a test for honest repair.",
                [TherapistDiagnosisContract.RepairRequirementKey] = "Repair requires specific ownership.",
                [TherapistDiagnosisContract.CharmReactionKey] = "Charm can feel easy or evasive.",
                [TherapistDiagnosisContract.RizzReactionKey] = "Rizz can feel wanted or handled.",
                [TherapistDiagnosisContract.HonestyReactionKey] = "Honesty is read through concrete accountability.",
                [TherapistDiagnosisContract.ChaosReactionKey] = "Chaos can feel alive or unstable.",
                [TherapistDiagnosisContract.WitReactionKey] = "Wit can relax or deflect.",
                [TherapistDiagnosisContract.SelfAwarenessReactionKey] = "Self-awareness can feel accurate or rehearsed.",
            };

        private static GameDefinition TinyGameDefinition(string steeringPrompt, string horninessPrompt)
            => new GameDefinition(
                "Pinder",
                "gm base",
                "avatar role",
                "datee role",
                steeringPrompt: steeringPrompt,
                horninessPrompt: horninessPrompt);

        private static JsonDocument LoadManifest()
            => JsonDocument.Parse(File.ReadAllText(Path.Combine(
                RepoRoot(),
                "contracts",
                "agent-journal-provenance-builders.v1.json")));

        private static IEnumerable<JsonElement> Rows(JsonDocument manifest)
            => manifest.RootElement.GetProperty("rows").EnumerateArray();

        private static GoldenFixture[] LoadGoldenFixture()
            => JsonSerializer.Deserialize<GoldenFixture[]>(File.ReadAllText(GoldenFixturePath()),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                })!;

        private static string GoldenFixturePath()
            => Path.Combine(
                RepoRoot(),
                "tests",
                "Pinder.LlmAdapters.Tests",
                "Fixtures",
                "AgentJournals",
                "Provenance",
                "prompt-builder-goldens.v1.json");

        private static string SerializeGoldenFixture()
            => JsonSerializer.Serialize(
                GoldenCases().Select(GoldenFixture.From).ToArray(),
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                });

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

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static string Normalize(string path)
            => path.Replace(Path.DirectorySeparatorChar, '/');

        private sealed class GoldenFixture
        {
            public string Id { get; set; } = "";
            public string Status { get; set; } = "";
            public List<GoldenTextDocument> BeforeDocuments { get; set; } = new List<GoldenTextDocument>();
            public List<GoldenAnnotatedDocument> AfterDocuments { get; set; } = new List<GoldenAnnotatedDocument>();

            public static GoldenFixture From(GoldenCase golden)
                => new GoldenFixture
                {
                    Id = golden.Id,
                    Status = golden.Status,
                    BeforeDocuments = golden.BeforeDocuments
                        .Select((document, index) => GoldenTextDocument.From(index, document))
                        .ToList(),
                    AfterDocuments = golden.Documents
                        .Select((document, index) => GoldenAnnotatedDocument.From(index, document))
                        .ToList(),
                };
        }

        private sealed class GoldenTextDocument
        {
            public int Order { get; set; }
            public string Role { get; set; } = "";
            public string Text { get; set; } = "";

            public static GoldenTextDocument From(int order, BeforeDocument document)
                => new GoldenTextDocument
                {
                    Order = order,
                    Role = EnumValue(document.Role),
                    Text = document.Text,
                };
        }

        private sealed class GoldenAnnotatedDocument
        {
            public int Order { get; set; }
            public string DocumentId { get; set; } = "";
            public string Role { get; set; } = "";
            public string Kind { get; set; } = "";
            public string Text { get; set; } = "";
            public string ContentHash { get; set; } = "";
            public List<GoldenRange> Ranges { get; set; } = new List<GoldenRange>();

            public static GoldenAnnotatedDocument From(int order, AnnotatedInvocationDocument document)
                => new GoldenAnnotatedDocument
                {
                    Order = order,
                    DocumentId = document.DocumentId,
                    Role = EnumValue(document.Role),
                    Kind = document.Kind,
                    Text = document.Text,
                    ContentHash = document.ContentHash,
                    Ranges = document.Ranges.Select(GoldenRange.From).ToList(),
                };
        }

        private sealed class GoldenRange
        {
            public string DocumentId { get; set; } = "";
            public int StartUtf16 { get; set; }
            public int EndUtf16 { get; set; }
            public string RangeKind { get; set; } = "";
            public string RedactionClass { get; set; } = "";
            public GoldenSource Source { get; set; } = new GoldenSource();

            public static GoldenRange From(AgentJournalProvenanceRange range)
                => new GoldenRange
                {
                    DocumentId = range.DocumentId,
                    StartUtf16 = range.StartUtf16,
                    EndUtf16 = range.EndUtf16,
                    RangeKind = EnumValue(range.RangeKind),
                    RedactionClass = EnumValue(range.RedactionClass),
                    Source = GoldenSource.From(range.Source),
                };
        }

        private sealed class GoldenSource
        {
            public string Kind { get; set; } = "";
            public string SourceId { get; set; } = "";
            public string KeyPath { get; set; } = "";
            public string? Revision { get; set; }
            public string? ContentHash { get; set; }
            public string? EditorTargetId { get; set; }

            public static GoldenSource From(AgentJournalSourceIdentity source)
                => new GoldenSource
                {
                    Kind = EnumValue(source.Kind),
                    SourceId = source.SourceId,
                    KeyPath = source.KeyPath,
                    Revision = source.Revision,
                    ContentHash = source.ContentHash,
                    EditorTargetId = source.EditorTargetId,
                };
        }

        private sealed class GoldenCase
        {
            private GoldenCase(
                string id,
                string status,
                BeforeDocument[] beforeDocuments,
                AnnotatedInvocationDocument[] documents)
            {
                Id = id;
                Status = status;
                BeforeDocuments = beforeDocuments;
                Documents = documents;
            }

            public string Id { get; }
            public string Status { get; }
            public IReadOnlyList<BeforeDocument> BeforeDocuments { get; }
            public IReadOnlyList<AnnotatedInvocationDocument> Documents { get; }

            public static GoldenCase Live(
                string id,
                string before,
                AnnotatedInvocationDocument document)
                => new GoldenCase(
                    id,
                    "live_production",
                    new[] { new BeforeDocument(document.Role, before) },
                    new[] { document });

            public static GoldenCase Live(
                string id,
                IReadOnlyList<string> before,
                params AnnotatedInvocationDocument[] documents)
            {
                Assert.Equal(before.Count, documents.Length);
                return new GoldenCase(
                    id,
                    "live_production",
                    before.Select((text, index) => new BeforeDocument(documents[index].Role, text)).ToArray(),
                    documents);
            }

            public static GoldenCase Dormant(string id)
                => new GoldenCase(
                    id,
                    "provider_capable_dormant",
                    Array.Empty<BeforeDocument>(),
                    Array.Empty<AnnotatedInvocationDocument>());
        }

        private sealed class BeforeDocument
        {
            public BeforeDocument(AgentJournalInputRole role, string text)
            {
                Role = role;
                Text = text;
            }

            public AgentJournalInputRole Role { get; }
            public string Text { get; }
        }

        private static string EnumValue<T>(T value) where T : struct
            => Regex.Replace(value.ToString()!, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
    }
}
