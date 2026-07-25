using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public sealed class TherapistDiagnosisContractTests
    {
        private static string RepoRoot => TestRepoLocator.RepoRoot;

        [Fact]
        public void RequiredFields_IncludeEmotionalReactionFieldsInLockedOrder()
        {
            Assert.Equal(
                ExpectedDiagnosisFields,
                TherapistDiagnosisContract.RequiredFields);
        }

        [Fact]
        public void ValidateRequiredFields_NullDiagnosis_ReportsStableViolation()
        {
            var result = TherapistDiagnosisContract.ValidateRequiredFields(null);

            Assert.False(result.IsValid);
            Assert.NotNull(result.Violation);
            Assert.Equal(TherapistDiagnosisContract.DerivedFeelingKey, result.Violation!.Field);
            Assert.Equal(TherapistDiagnosisContract.MissingDiagnosisCode, result.Violation.Code);
        }

        [Fact]
        public void ValidateRequiredFields_MissingBlankWhitespaceAndCasing_AreStableViolations()
        {
            AssertViolation(
                new Dictionary<string, string>
                {
                    [TherapistDiagnosisContract.DerivedFeelingKey] = "fear of being ordinary",
                },
                TherapistDiagnosisContract.DefenseReactionKey,
                TherapistDiagnosisContract.MissingRequiredFieldCode);

            AssertViolation(
                new Dictionary<string, string>
                {
                    [TherapistDiagnosisContract.DerivedFeelingKey] = "",
                    [TherapistDiagnosisContract.DefenseReactionKey] = "turns sincerity into a bit",
                },
                TherapistDiagnosisContract.DerivedFeelingKey,
                TherapistDiagnosisContract.BlankRequiredFieldCode);

            AssertViolation(
                new Dictionary<string, string>
                {
                    [TherapistDiagnosisContract.DerivedFeelingKey] = "   ",
                    [TherapistDiagnosisContract.DefenseReactionKey] = "turns sincerity into a bit",
                },
                TherapistDiagnosisContract.DerivedFeelingKey,
                TherapistDiagnosisContract.BlankRequiredFieldCode);

            AssertViolation(
                new Dictionary<string, string>
                {
                    ["Derived_Feeling"] = "fear of being ordinary",
                    [TherapistDiagnosisContract.DefenseReactionKey] = "turns sincerity into a bit",
                },
                TherapistDiagnosisContract.DerivedFeelingKey,
                TherapistDiagnosisContract.MissingRequiredFieldCode);
        }

        [Fact]
        public void ValidateRequiredFields_CompleteMapAllowsBoundarySpecificExtras()
        {
            var result = TherapistDiagnosisContract.ValidateRequiredFields(
                CompleteDiagnosisWith(("future_regeneration_note", "loader/runtime may preserve this flat-map extra")));

            Assert.True(result.IsValid);
            Assert.Null(result.Violation);
        }

        [Fact]
        public void Loader_PreservesStringExtrasForLegacyRegeneration()
        {
            string json = WithDiagnosis(
                CompleteDiagnosisJsonProperties(
                    ("future_regeneration_note", "kept")));

            var definition = CharacterDefinitionLoader.ParseDefinition(json);

            Assert.NotNull(definition.PsychiatricDiagnosis);
            Assert.Equal("kept", definition.PsychiatricDiagnosis!["future_regeneration_note"]);
        }

        [Fact]
        public void CharacterDefinitionWriter_OutputKeepsDiagnosisFlatMapSchemaShape()
        {
            var diagnosis = CompleteDiagnosisWith();
            var definition = new CharacterDefinition(
                CharacterDefinition.CurrentSchemaVersion,
                Guid.Parse("550e8400-e29b-41d4-a716-446655440000"),
                "TestChar",
                "they/them",
                "test bio",
                1,
                new List<string>(),
                new Dictionary<string, float>(),
                new AllocationBlock(
                    new Dictionary<StatType, int>
                    {
                        [StatType.Charm] = 1,
                        [StatType.Rizz] = 1,
                        [StatType.Honesty] = 1,
                        [StatType.Chaos] = 1,
                        [StatType.Wit] = 1,
                        [StatType.SelfAwareness] = 1,
                    },
                    0,
                    new Dictionary<ShadowStatType, int>
                    {
                        [ShadowStatType.Madness] = 0,
                        [ShadowStatType.Despair] = 0,
                        [ShadowStatType.Denial] = 0,
                        [ShadowStatType.Fixation] = 0,
                        [ShadowStatType.Dread] = 0,
                        [ShadowStatType.Overthinking] = 0,
                    }),
                psychiatricDiagnosis: diagnosis);

            using var document = JsonDocument.Parse(CharacterDefinitionWriter.Write(definition));
            var diagnosisObject = document.RootElement.GetProperty("psychiatric_diagnosis");
            string[] writtenFields = diagnosisObject.EnumerateObject().Select(property => property.Name).ToArray();

            Assert.Equal(TherapistDiagnosisContract.RequiredFields, writtenFields);
        }

        [Fact]
        public void RuntimeSchemaAndDiagnosisPrompt_StayInExactFieldParity()
        {
            string[] runtimeFields = TherapistDiagnosisContract.RequiredFields.ToArray();
            string[] schemaRequiredFields = ReadSchemaRequiredDiagnosisFields();
            string[] schemaPropertyFields = ReadSchemaDiagnosisPropertyFields();
            string[] promptFields = ReadDiagnosisPromptObjectFields();

            Assert.Equal(runtimeFields, schemaRequiredFields);
            Assert.Equal(runtimeFields, schemaPropertyFields);
            Assert.Equal(runtimeFields, promptFields);
            Assert.True(ReadSchemaDiagnosisAdditionalPropertiesIsFalse());
        }

        [Fact]
        public void DiagnosisPrompt_InstructsActionableCharacterSpecificFormulations()
        {
            string systemPrompt = PromptCatalog
                .LoadFromDirectory(Path.Combine(RepoRoot, "data", "prompts"))
                .Get("diagnosis")
                .SystemPrompt!;

            Assert.Contains("second-person", systemPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("character-specific", systemPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Do not mention roll tiers", systemPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Do not write finished datee replies", systemPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Do not introduce facts", systemPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Do not use diagnoses as insults", systemPrompt, StringComparison.OrdinalIgnoreCase);
        }

        private static void AssertViolation(
            IReadOnlyDictionary<string, string>? diagnosis,
            string expectedField,
            string expectedCode)
        {
            var result = TherapistDiagnosisContract.ValidateRequiredFields(diagnosis);

            Assert.False(result.IsValid);
            Assert.NotNull(result.Violation);
            Assert.Equal(expectedField, result.Violation!.Field);
            Assert.Equal(expectedCode, result.Violation.Code);
        }

        private static readonly string[] ExpectedDiagnosisFields =
        {
            "derived_feeling",
            "defense_reaction",
            "safe_connection",
            "hurt_protection",
            "repair_requirement",
            "charm_reaction",
            "rizz_reaction",
            "honesty_reaction",
            "chaos_reaction",
            "wit_reaction",
            "self_awareness_reaction",
        };

        private static Dictionary<string, string> CompleteDiagnosisWith(
            params (string Key, string Value)[] extras)
        {
            var diagnosis = ExpectedDiagnosisFields.ToDictionary(
                field => field,
                field => $"specific formulation for {field}");

            foreach (var extra in extras)
                diagnosis[extra.Key] = extra.Value;

            return diagnosis;
        }

        private static string CompleteDiagnosisJsonProperties(
            params (string Key, string Value)[] extras)
        {
            return string.Join(
                "," + Environment.NewLine,
                CompleteDiagnosisWith(extras)
                    .Select(pair => $@"""{pair.Key}"": ""{pair.Value}"""));
        }

        private static string WithDiagnosis(string diagnosisProperties)
        {
            return @"{
                ""schema_version"": 2,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""name"": ""TestChar"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test bio"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""allocation"": {
                    ""spent"": {
                        ""charm"": 1, ""rizz"": 1, ""honesty"": 1,
                        ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1
                    },
                    ""unspent_pool"": 0,
                    ""shadows"": {
                        ""madness"": 0, ""despair"": 0, ""denial"": 0,
                        ""fixation"": 0, ""dread"": 0, ""overthinking"": 0
                    }
                },
                ""psychiatric_diagnosis"": {
                    " + diagnosisProperties + @"
                }
            }";
        }

        private static string[] ReadSchemaRequiredDiagnosisFields()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(CharacterSchemaPath()));
            return document.RootElement
                .GetProperty("properties")
                .GetProperty("psychiatric_diagnosis")
                .GetProperty("required")
                .EnumerateArray()
                .Select(element => element.GetString()!)
                .ToArray();
        }

        private static string[] ReadSchemaDiagnosisPropertyFields()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(CharacterSchemaPath()));
            return document.RootElement
                .GetProperty("properties")
                .GetProperty("psychiatric_diagnosis")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray();
        }

        private static bool ReadSchemaDiagnosisAdditionalPropertiesIsFalse()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(CharacterSchemaPath()));
            return document.RootElement
                .GetProperty("properties")
                .GetProperty("psychiatric_diagnosis")
                .GetProperty("additionalProperties")
                .GetBoolean() == false;
        }

        private static string[] ReadDiagnosisPromptObjectFields()
        {
            var catalog = PromptCatalog.LoadFromDirectory(Path.Combine(RepoRoot, "data", "prompts"));
            string systemPrompt = catalog.Get("diagnosis").SystemPrompt!;
            var objectMatch = Regex.Match(systemPrompt, "\\{(?<body>[\\s\\S]*?)\\}");
            Assert.True(objectMatch.Success, "Diagnosis prompt must include a JSON object shape.");

            return Regex.Matches(objectMatch.Groups["body"].Value, "\"(?<field>[a-z_]+)\"\\s*:")
                .Cast<Match>()
                .Select(match => match.Groups["field"].Value)
                .ToArray();
        }

        private static string CharacterSchemaPath()
        {
            return Path.Combine(RepoRoot, "data", "characters", "character-schema.json");
        }
    }
}
