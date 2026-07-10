using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Conversation;
using Pinder.LlmAdapters;
using Pinder.SessionSetup;

namespace Pinder.Core.Tests
{
    public class Issue1253_SequentialSynthesisTests
    {
        private class FakeBackstoryGenerator : IBackstoryGenerator
        {
            public bool WasCalled { get; private set; }
            public Task<Dictionary<string, BackstoryFact>> GenerateAsync(string characterName, string genderIdentity, string bio, IReadOnlyList<string> looksAndAssetFragments, CancellationToken cancellationToken = default)
            {
                WasCalled = true;
                var dict = new Dictionary<string, BackstoryFact>
                {
                    { "fact1", new BackstoryFact("Family", "Parents divorced", "High") }
                };
                return Task.FromResult(dict);
            }
        }

        private class FakeStakeGenerator : ISequentialStakeGenerator
        {
            public bool WasCalled { get; private set; }
            public string? PassedBio { get; private set; }
            public Dictionary<string, BackstoryFact>? PassedBackstory { get; private set; }
            public Task<List<string>> GenerateAsync(string characterName, string genderIdentity, string bio, Dictionary<string, BackstoryFact> backstory, CancellationToken cancellationToken = default)
            {
                WasCalled = true;
                PassedBio = bio;
                PassedBackstory = backstory;
                return Task.FromResult(Enumerable.Range(1, 15).Select(i => $"Stake {i}").ToList());
            }
        }

        private class FakeDiagnosisGenerator : ITherapistDiagnosisGenerator
        {
            public bool WasCalled { get; private set; }
            public string? PassedBio { get; private set; }
            public Dictionary<string, BackstoryFact>? PassedBackstory { get; private set; }
            public List<string>? PassedStakes { get; private set; }
            public Task<Dictionary<string, string>> GenerateAsync(string characterName, string genderIdentity, string bio, Dictionary<string, BackstoryFact> backstory, List<string> stakeLines, CancellationToken cancellationToken = default)
            {
                WasCalled = true;
                PassedBio = bio;
                PassedBackstory = backstory;
                PassedStakes = stakeLines;
                var dict = new Dictionary<string, string>
                {
                    { "derived_feeling", "anxiety" },
                    { "defense_reaction", "deflection" }
                };
                return Task.FromResult(dict);
            }
        }

        private class FakeLlmTransport : ILlmTransport
        {
            public string? LastSystemPrompt { get; private set; }
            public string? LastUserMessage { get; private set; }
            public double? LastTemperature { get; private set; }
            public int? LastMaxTokens { get; private set; }
            public string? LastPhase { get; private set; }
            public int CallCount { get; private set; }
            public string ResponseToReturn { get; set; } = "{}";

            public Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int maxTokens = 1024, string? phase = null, CancellationToken ct = default)
            {
                CallCount++;
                LastSystemPrompt = systemPrompt;
                LastUserMessage = userMessage;
                LastTemperature = temperature;
                LastMaxTokens = maxTokens;
                LastPhase = phase;
                return Task.FromResult(ResponseToReturn);
            }
        }

        [Fact]
        public async Task Pipeline_ExecutesStagesInOrder_PassingOutputsToNext()
        {
            var backstoryGen = new FakeBackstoryGenerator();
            var stakeGen = new FakeStakeGenerator();
            var diagnosisGen = new FakeDiagnosisGenerator();
            var pipeline = new SequentialSynthesisPipeline(backstoryGen, stakeGen, diagnosisGen);

            var result = await pipeline.SynthesizeAsync("TestChar", "they/them", "bio", new List<string>());

            Assert.True(backstoryGen.WasCalled);
            Assert.True(stakeGen.WasCalled);
            Assert.True(diagnosisGen.WasCalled);
            Assert.NotNull(stakeGen.PassedBackstory);
            Assert.True(stakeGen.PassedBackstory.ContainsKey("fact1"));
            Assert.NotNull(diagnosisGen.PassedBackstory);
            Assert.NotNull(diagnosisGen.PassedStakes);
            Assert.Equal(15, diagnosisGen.PassedStakes.Count);
            Assert.Contains("Stake 1", diagnosisGen.PassedStakes);
            Assert.Equal(string.Empty, stakeGen.PassedBio);
            Assert.Equal(string.Empty, diagnosisGen.PassedBio);
        }

        [Fact]
        public async Task TherapistDiagnosisGenerator_BuildsCorrectPromptAndParsesJson()
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData_Prompts_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "diagnosis.yaml"), "schema_version: 1\nprompts:\n  diagnosis:\n    temperature: 0.62\n    max_tokens: 888\n    system_prompt: \"SYSTEM PROMPT\"\n    user_template: \"USER {backstory} - {stakes}\"");

            var transport = new FakeLlmTransport();
            transport.ResponseToReturn = @"{ ""derived_feeling"": ""abandonment issues"", ""defense_reaction"": ""humor"" }";
            
            var catalog = PromptCatalog.LoadFromDirectory(testDir);
            var generator = new LlmTherapistDiagnosisGenerator(transport, catalog);

            var backstory = new Dictionary<string, BackstoryFact>
            {
                { "b1", new BackstoryFact("Subj", "Det", "Sig") }
            };
            var stakes = new List<string> { "Stake 1" };

            var result = await generator.GenerateAsync("Char", "he/him", "bio", backstory, stakes);

            Assert.Equal("SYSTEM PROMPT", transport.LastSystemPrompt);
            Assert.Contains("Det", transport.LastUserMessage);
            Assert.Contains("Stake 1", transport.LastUserMessage);
            Assert.Equal(0.62, transport.LastTemperature);
            Assert.Equal(888, transport.LastMaxTokens);
            Assert.Equal(LlmPhase.Synthesis, transport.LastPhase);
            Assert.Equal(1, transport.CallCount);
            
            Assert.Equal("abandonment issues", result["derived_feeling"]);
            Assert.Equal("humor", result["defense_reaction"]);
            
            Directory.Delete(testDir, true);
        }

        [Fact]
        public void TherapistDiagnosisGenerator_WithMissingUserTemplate_ThrowsBeforeLlmCall()
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData_Prompts_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            try
            {
                File.WriteAllText(Path.Combine(testDir, "diagnosis.yaml"), "schema_version: 1\nprompts:\n  diagnosis:\n    temperature: 0.7\n    max_tokens: 1024\n    system_prompt: \"SYSTEM PROMPT\"");

                var catalog = PromptCatalog.LoadFromDirectory(testDir);
                var transport = new FakeLlmTransport();

                var ex = Assert.Throws<InvalidOperationException>(
                    () => new LlmTherapistDiagnosisGenerator(transport, catalog));

                Assert.Contains("no user_template", ex.Message);
                Assert.Equal(0, transport.CallCount);
            }
            finally
            {
                Directory.Delete(testDir, true);
            }
        }
        
        [Fact]
        public void CharacterDefinitionAndProfile_RetainSynthesisFields()
        {
            var backstory = new Dictionary<string, BackstoryFact> { { "f1", new BackstoryFact("S", "D", "S") } };
            var stakes = new List<string> { "Stake line" };
            var diag = new Dictionary<string, string> { { "derived_feeling", "angst" } };

            var def = new CharacterDefinition(
                schemaVersion: 1,
                characterId: Guid.NewGuid(),
                name: "Test",
                genderIdentity: "none",
                bio: "bio",
                level: 1,
                items: new List<string>(),
                anatomy: new Dictionary<string, float>(),
                allocation: new AllocationBlock(new Dictionary<StatType, int>(), 0, new Dictionary<ShadowStatType, int>()),
                psychologicalStake: null,
                backstory: backstory,
                stakeLines: stakes,
                psychiatricDiagnosis: diag
            );

            Assert.NotNull(def.Backstory);
            Assert.True(def.Backstory.ContainsKey("f1"));
            Assert.Contains("Stake line", def.StakeLines);
            Assert.Equal("angst", def.PsychiatricDiagnosis["derived_feeling"]);

            var profile = new CharacterProfile(
                stats: new StatBlock(new Dictionary<StatType, int>(), new Dictionary<ShadowStatType, int>()),
                assembledSystemPrompt: "prompt",
                displayName: "Test",
                timing: new TimingProfile(1, 1f, 1f, "neutral"),
                level: 1,
                backstory: backstory,
                stakeLines: stakes,
                psychiatricDiagnosis: diag
            );

            Assert.NotNull(profile.Backstory);
            Assert.True(profile.Backstory.ContainsKey("f1"));
            Assert.Contains("Stake line", profile.StakeLines);
            Assert.Equal("angst", profile.PsychiatricDiagnosis["derived_feeling"]);
        }

        [Fact]
        public async Task TherapistDiagnosisGenerator_WithMalformedJson_ThrowsInsteadOfReturningEmptyDictionary()
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData_Prompts_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "diagnosis.yaml"), "schema_version: 1\nprompts:\n  diagnosis:\n    temperature: 0.7\n    max_tokens: 1024\n    system_prompt: \"SYSTEM PROMPT\"\n    user_template: \"USER {backstory} - {stakes}\"");

            var transport = new FakeLlmTransport();
            transport.ResponseToReturn = "Malformed JSON string that will fail to deserialize";

            var catalog = PromptCatalog.LoadFromDirectory(testDir);
            var generator = new LlmTherapistDiagnosisGenerator(transport, catalog);

            var backstory = new Dictionary<string, BackstoryFact>();
            var stakes = new List<string>();

            // A malformed/unparseable diagnosis response is bad model output,
            // not a valid empty diagnosis. It must fail loud (so the caller —
            // the synthesis pipeline / regeneration flow — can record a real
            // failure) instead of silently returning an empty dictionary that
            // looks like a legitimate "no diagnosis" answer.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => generator.GenerateAsync("Char", "he/him", "bio", backstory, stakes));

            Assert.IsType<JsonException>(ex.InnerException);

            Directory.Delete(testDir, true);
        }

        [Fact]
        public async Task TherapistDiagnosisGenerator_WithValidEmptyJsonObject_ReturnsEmptyDictionaryWithoutThrowing()
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData_Prompts_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "diagnosis.yaml"), "schema_version: 1\nprompts:\n  diagnosis:\n    temperature: 0.7\n    max_tokens: 1024\n    system_prompt: \"SYSTEM PROMPT\"\n    user_template: \"USER {backstory} - {stakes}\"");

            var transport = new FakeLlmTransport();
            // A well-formed, empty JSON object is the LLM's legitimate way of
            // saying "this character has no notable psychiatric diagnosis" —
            // that is success, not a parse failure, and must not throw.
            transport.ResponseToReturn = "{}";

            var catalog = PromptCatalog.LoadFromDirectory(testDir);
            var generator = new LlmTherapistDiagnosisGenerator(transport, catalog);

            var backstory = new Dictionary<string, BackstoryFact>();
            var stakes = new List<string>();

            var result = await generator.GenerateAsync("Char", "he/him", "bio", backstory, stakes);

            Assert.NotNull(result);
            Assert.Empty(result);

            Directory.Delete(testDir, true);
        }

        [Fact]
        public async Task TherapistDiagnosisGenerator_WithValidJsonButWhitespaceKeysOrValues_TrimsAndFiltersThem()
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData_Prompts_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "diagnosis.yaml"), "schema_version: 1\nprompts:\n  diagnosis:\n    temperature: 0.7\n    max_tokens: 1024\n    system_prompt: \"SYSTEM PROMPT\"\n    user_template: \"USER {backstory} - {stakes}\"");

            var transport = new FakeLlmTransport();
            transport.ResponseToReturn = @"{ ""valid_key"": ""  some value with whitespace  "", "" "": ""value with empty key"", ""empty_value"": ""   "" }";
            
            var catalog = PromptCatalog.LoadFromDirectory(testDir);
            var generator = new LlmTherapistDiagnosisGenerator(transport, catalog);

            var backstory = new Dictionary<string, BackstoryFact>();
            var stakes = new List<string>();

            // Act
            var result = await generator.GenerateAsync("Char", "he/him", "bio", backstory, stakes);

            // Assert
            Assert.Single(result);
            Assert.True(result.ContainsKey("valid_key"));
            Assert.Equal("some value with whitespace", result["valid_key"]);
            
            Directory.Delete(testDir, true);
        }
    }
}
