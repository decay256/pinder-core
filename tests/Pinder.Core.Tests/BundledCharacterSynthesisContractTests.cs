using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pinder.Core.Characters;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    public sealed class BundledCharacterSynthesisContractTests
    {
        [Fact]
        public void EveryBundledCharacterHasCompleteSessionSynthesisData()
        {
            var characterDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "../../../../../data/characters");

            foreach (var path in Directory.EnumerateFiles(characterDirectory, "*.json"))
            {
                if (string.Equals(Path.GetFileName(path), "character-schema.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var character = CharacterDefinitionLoader.ParseDefinition(File.ReadAllText(path));
                Assert.False(string.IsNullOrWhiteSpace(character.ConsolidatedPersonality));
                Assert.False(string.IsNullOrWhiteSpace(character.ConsolidatedBackstory));
                Assert.NotNull(character.StakeLines);
                Assert.NotNull(character.PsychiatricDiagnosis);
                Assert.Equal(15, character.StakeLines!.Count);

                var diagnosisValidation = TherapistDiagnosisContract.ValidateRequiredFields(
                    character.PsychiatricDiagnosis);
                Assert.True(
                    diagnosisValidation.IsValid,
                    $"{Path.GetFileName(path)} has incomplete therapist diagnosis: " +
                    $"{diagnosisValidation.Violation?.Code} " +
                    $"{diagnosisValidation.Violation?.Field}");

                Assert.Equal(
                    TherapistDiagnosisContract.RequiredFields,
                    character.PsychiatricDiagnosis!.Keys.Take(
                        TherapistDiagnosisContract.RequiredFields.Count));
            }
        }

        [Fact]
        public void BundledCharactersDoNotPersistDeprecatedExpressionTargets()
        {
            var characterDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "../../../../../data/characters");
            string[] deprecated = { "sad", "happy", "serius" };

            foreach (var path in Directory.EnumerateFiles(characterDirectory, "*.json"))
            {
                if (string.Equals(Path.GetFileName(path), "character-schema.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                Assert.True(document.RootElement.TryGetProperty("anatomy", out var anatomy));
                foreach (string field in deprecated)
                {
                    Assert.False(
                        anatomy.TryGetProperty(field, out _),
                        $"{Path.GetFileName(path)} must not persist deprecated anatomy.{field}.");
                }
            }
        }

        [Fact]
        public void LegacyCharacterMissingSynthesisFieldsStillParsesForRegeneration()
        {
            var character = CharacterDefinitionLoader.ParseDefinition(MinimalCharacterJson());

            Assert.Null(character.ConsolidatedPersonality);
            Assert.Null(character.ConsolidatedBackstory);
            Assert.Null(character.StakeLines);
            Assert.Null(character.PsychiatricDiagnosis);
        }

        [Fact]
        public void PartialLegacyDiagnosisIsExplicitlyIncompleteForRuntimeUse()
        {
            var character = CharacterDefinitionLoader.ParseDefinition(
                MinimalCharacterJson(@"""psychiatric_diagnosis"": {
                    ""derived_feeling"": ""fear of becoming forgettable"",
                    ""defense_reaction"": ""turns sincerity into a test""
                }"));

            var validation = TherapistDiagnosisContract.ValidateRequiredFields(
                character.PsychiatricDiagnosis);

            Assert.False(validation.IsValid);
            Assert.NotNull(validation.Violation);
            Assert.Equal(
                TherapistDiagnosisContract.SafeConnectionKey,
                validation.Violation!.Field);
            Assert.Equal(
                TherapistDiagnosisContract.MissingRequiredFieldCode,
                validation.Violation.Code);
        }

        private static string MinimalCharacterJson(string? extraRootProperty = null)
        {
            string extra = string.IsNullOrWhiteSpace(extraRootProperty)
                ? string.Empty
                : "," + Environment.NewLine + extraRootProperty;

            return @"{
                ""schema_version"": 2,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""name"": ""Legacy"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""legacy profile"",
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
                }" + extra + @"
            }";
        }
    }
}
