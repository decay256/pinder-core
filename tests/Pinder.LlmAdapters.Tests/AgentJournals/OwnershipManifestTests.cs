using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals
{
    public sealed class OwnershipManifestTests
    {
        private static readonly string[] RequiredIds =
        {
            "game.datee.performance",
            "game.avatar.reply",
            "game.avatar.emotional-director",
            "game.emotional-director",
            "game.dialogue-options",
            "game.setup.dramatic-arc",
            "game.prefetch.option-branch",
            "game.speculation.option-branch",
            "character.synthesis",
            "admin.temporary-chat",
            "admin.prompt-speculation",
            "narrative.harness",
            "session.simulation",
            "game.delivery.success-improvement",
            "game.delivery.horniness-question",
            "game.delivery.steering-question",
            "game.datee.interest-change-beat"
        };

        private static readonly string[] RequiredRowProperties =
        {
            "id",
            "status",
            "status_evidence",
            "activation_rule",
            "owner",
            "owner_description",
            "pi_agent_session",
            "journal_destination",
            "context_membership",
            "player_delivery",
            "visibility",
            "retention_policy_key",
            "required_owner_ids",
            "required_correlation_ids",
            "forbidden_owner_ids",
            "provenance_builder_ids",
            "implementation_matchers",
            "verifier_group"
        };

        [Fact]
        public void ManifestContainsClosedSeventeenRowInventory()
        {
            using var document = LoadManifest();
            JsonElement root = document.RootElement;

            Assert.Equal("agent-journal-invocation-ownership.v1", root.GetProperty("schema_version").GetString());
            Assert.True(root.GetProperty("closed_inventory").GetBoolean());
            Assert.Equal(17, root.GetProperty("inventory_size").GetInt32());

            string[] ids = Rows(root)
                .Select(row => row.GetProperty("id").GetString()!)
                .ToArray();

            Assert.Equal(RequiredIds, ids);
            Assert.Equal(RequiredIds.Length, ids.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void RowsCarryRequiredOwnershipStatusVisibilityRetentionAndMatchers()
        {
            using var document = LoadManifest();
            foreach (JsonElement row in Rows(document.RootElement))
            {
                string id = row.GetProperty("id").GetString()!;
                foreach (string property in RequiredRowProperties)
                {
                    Assert.True(row.TryGetProperty(property, out _), $"{id} missing {property}");
                }

                Assert.NotEmpty(StringArray(row, "status_evidence"));
                Assert.NotEmpty(row.GetProperty("activation_rule").GetString()!);
                Assert.NotEmpty(row.GetProperty("owner").GetString()!);
                Assert.NotEmpty(row.GetProperty("journal_destination").GetString()!);
                Assert.NotEmpty(row.GetProperty("context_membership").GetString()!);
                Assert.NotEmpty(row.GetProperty("player_delivery").GetString()!);
                Assert.NotEmpty(row.GetProperty("visibility").GetString()!);
                Assert.NotEmpty(row.GetProperty("retention_policy_key").GetString()!);
                Assert.NotEmpty(StringArray(row, "required_owner_ids"));
                Assert.NotEmpty(StringArray(row, "required_correlation_ids"));
                Assert.NotEmpty(StringArray(row, "forbidden_owner_ids"));
                Assert.NotEmpty(StringArray(row, "provenance_builder_ids"));

                JsonElement[] matchers = row.GetProperty("implementation_matchers").EnumerateArray().ToArray();
                Assert.NotEmpty(matchers);
                foreach (JsonElement matcher in matchers)
                {
                    Assert.True(matcher.TryGetProperty("kind", out JsonElement kindElement), $"{id} matcher missing kind");
                    string kind = kindElement.GetString()!;
                    Assert.Contains(kind, new[] { "symbol", "production_call", "web_review_anchor", "no_production_caller" });
                    if (kind == "symbol" || kind == "production_call")
                    {
                        string pattern = matcher.GetProperty("pattern").GetString()!;
                        Assert.NotEqual(".*", pattern);
                        Assert.NotEmpty(matcher.GetProperty("file").GetString()!);
                    }
                }
            }
        }

        [Fact]
        public void ManifestHasAmendedStatusCountsAndNoDeadRows()
        {
            using var document = LoadManifest();
            var rows = Rows(document.RootElement).ToArray();

            Assert.Equal(16, rows.Count(row => row.GetProperty("status").GetString() == "live_production"));
            Assert.Single(rows.Where(row => row.GetProperty("status").GetString() == "provider_capable_dormant"));
            Assert.Empty(rows.Where(row => row.GetProperty("status").GetString() == "dead_with_proof"));
        }

        [Theory]
        [InlineData("game.delivery.success-improvement", "DeliveryStage.cs", "GetSuccessImprovementAsync")]
        [InlineData("game.delivery.horniness-question", "DeliveryStage.cs", "GetHorninessQuestionAsync")]
        [InlineData("game.delivery.steering-question", "SteeringEngine.cs", "GetSteeringQuestionAsync")]
        public void ThreeAmendedLiveDeliveryRowsBindToConfirmedProductionPaths(
            string id,
            string expectedFile,
            string expectedPattern)
        {
            JsonElement row = Row(id);

            Assert.Equal("live_production", row.GetProperty("status").GetString());
            Assert.Contains(row.GetProperty("implementation_matchers").EnumerateArray(), matcher =>
                matcher.TryGetProperty("file", out JsonElement file)
                && file.GetString()!.EndsWith(expectedFile, StringComparison.Ordinal)
                && matcher.TryGetProperty("pattern", out JsonElement pattern)
                && pattern.GetString()!.Contains(expectedPattern, StringComparison.Ordinal));
            Assert.DoesNotContain("agent_session_id", StringArray(row, "required_owner_ids"));
        }

        [Fact]
        public void AvatarEmotionalDirectorStaticScannerCoversLiveGeneratorCandidate()
        {
            JsonElement row = Row("game.avatar.emotional-director");

            Assert.Contains(row.GetProperty("implementation_matchers").EnumerateArray(), matcher =>
                matcher.TryGetProperty("file", out JsonElement file)
                && file.GetString()!.EndsWith("PinderLlmAdapter.AvatarEmotionalDirector.cs", StringComparison.Ordinal)
                && matcher.TryGetProperty("pattern", out JsonElement pattern)
                && pattern.GetString() == "GenerateAvatarEmotionalDirectionAsync\\(");

            string source = File.ReadAllText(Path.Combine(
                RepoRoot(),
                "src",
                "Pinder.LlmAdapters",
                "PinderLlmAdapter.AvatarEmotionalDirector.cs"));
            Assert.Contains("GenerateAvatarEmotionalDirectionAsync(", source, StringComparison.Ordinal);

            string pythonVerifier = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "verify-agent-journal-ownership.py"));
            string powershellVerifier = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "verify-agent-journal-ownership.ps1"));
            Assert.Contains("AvatarEmotionalDirector", pythonVerifier, StringComparison.Ordinal);
            Assert.Contains("Generate(Avatar)?EmotionalDirectionAsync", pythonVerifier, StringComparison.Ordinal);
            Assert.Contains("AvatarEmotionalDirector", powershellVerifier, StringComparison.Ordinal);
            Assert.Contains("Generate(Avatar)?EmotionalDirectionAsync", powershellVerifier, StringComparison.Ordinal);
        }

        [Fact]
        public void DormantInterestChangeRowHasNoCallerActivationGuard()
        {
            JsonElement row = Row("game.datee.interest-change-beat");

            Assert.Equal("provider_capable_dormant", row.GetProperty("status").GetString());
            string activationRule = row.GetProperty("activation_rule").GetString()!;
            Assert.Contains("fails", activationRule, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("planning", activationRule, StringComparison.OrdinalIgnoreCase);

            JsonElement noCallerMatcher = Assert.Single(
                row.GetProperty("implementation_matchers").EnumerateArray(),
                matcher => matcher.GetProperty("kind").GetString() == "no_production_caller");
            Assert.Equal("GetInterestChangeBeatAsync\\(", noCallerMatcher.GetProperty("pattern").GetString());

            string[] allowedFiles = noCallerMatcher.GetProperty("allowed_files")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
            Assert.Contains("src/Pinder.LlmAdapters/PinderLlmAdapter.cs", allowedFiles);
            Assert.Contains("src/Pinder.Core/Interfaces/ILlmAdapter.cs", allowedFiles);
            Assert.Contains("src/Pinder.Core/Conversation/NullLlmAdapter.cs", allowedFiles);
        }

        [Fact]
        public void GameRunAndNonGameRunRowsCannotExchangeOwnerIds()
        {
            using var document = LoadManifest();
            foreach (JsonElement row in Rows(document.RootElement))
            {
                string id = row.GetProperty("id").GetString()!;
                string[] requiredOwnerIds = StringArray(row, "required_owner_ids");
                string[] requiredCorrelationIds = StringArray(row, "required_correlation_ids");
                string[] forbiddenOwnerIds = StringArray(row, "forbidden_owner_ids");

                if (id.StartsWith("game.", StringComparison.Ordinal))
                {
                    Assert.Contains("game_run_id", requiredOwnerIds);
                    Assert.Contains("game_run_id", requiredCorrelationIds);
                    Assert.DoesNotContain("game_run_id", forbiddenOwnerIds);
                }
                else
                {
                    Assert.DoesNotContain("game_run_id", requiredOwnerIds);
                    Assert.DoesNotContain("game_run_id", requiredCorrelationIds);
                    Assert.Contains("game_run_id", forbiddenOwnerIds);
                }
            }
        }

        [Fact]
        public void WebOwnedRowsHaveCheckedInReviewAnchors()
        {
            string docs = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "agent-journal-invocation-ownership.md"));

            foreach (string id in new[] { "admin.temporary-chat", "admin.prompt-speculation" })
            {
                JsonElement row = Row(id);
                JsonElement matcher = Assert.Single(row.GetProperty("implementation_matchers").EnumerateArray());
                Assert.Equal("web_review_anchor", matcher.GetProperty("kind").GetString());
                string anchor = matcher.GetProperty("anchor").GetString()!;
                Assert.Contains(anchor, docs, StringComparison.Ordinal);
                Assert.Contains("admin_execution_id", StringArray(row, "required_owner_ids"));
                Assert.Contains("game_run_id", StringArray(row, "forbidden_owner_ids"));
            }
        }

        private static JsonElement Row(string id)
        {
            using JsonDocument document = LoadManifest();
            JsonElement match = Rows(document.RootElement).Single(row => row.GetProperty("id").GetString() == id);
            return match.Clone();
        }

        private static IEnumerable<JsonElement> Rows(JsonElement root) =>
            root.GetProperty("rows").EnumerateArray();

        private static string[] StringArray(JsonElement row, string property) =>
            row.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();

        private static JsonDocument LoadManifest() =>
            JsonDocument.Parse(File.ReadAllText(Path.Combine(
                RepoRoot(),
                "contracts",
                "agent-journal-invocation-ownership.v1.json")));

        private static string RepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "Pinder.Core.sln"))
                    && Directory.Exists(Path.Combine(current, "contracts")))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
        }
    }
}
