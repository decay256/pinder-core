using System;
using System.IO;
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
                Assert.True(character.PsychiatricDiagnosis!.TryGetValue("derived_feeling", out var feeling));
                Assert.False(string.IsNullOrWhiteSpace(feeling));
                Assert.True(character.PsychiatricDiagnosis.TryGetValue("defense_reaction", out var defense));
                Assert.False(string.IsNullOrWhiteSpace(defense));
            }
        }
    }
}
