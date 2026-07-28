using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
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
                var dict = CompleteDiagnosisWith(
                    ("derived_feeling", "anxiety"),
                    ("defense_reaction", "deflection"));
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
            public Queue<string>? ResponsesToReturn { get; set; }

            public Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int maxTokens = 1024, string? phase = null, CancellationToken ct = default)
            {
                CallCount++;
                LastSystemPrompt = systemPrompt;
                LastUserMessage = userMessage;
                LastTemperature = temperature;
                LastMaxTokens = maxTokens;
                LastPhase = phase;
                if (ResponsesToReturn != null && ResponsesToReturn.Count > 0)
                    return Task.FromResult(ResponsesToReturn.Dequeue());
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
            transport.ResponseToReturn = DiagnosisJsonWith(
                ("Derived_Feeling", "abandonment issues"),
                ("defense_reaction", "humor"),
                ("extra_note", "ignored"));
            
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
            Assert.Equal(TherapistDiagnosisContract.RequiredFields, result.Keys.ToArray());
            Assert.DoesNotContain("extra_note", result.Keys);
            
            Directory.Delete(testDir, true);
        }

        [Fact]
        public async Task TherapistDiagnosisGenerator_ExtractsJsonObjectFromMarkdownWrappedResponse()
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData_Prompts_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "diagnosis.yaml"), "schema_version: 1\nprompts:\n  diagnosis:\n    temperature: 0.62\n    max_tokens: 888\n    system_prompt: \"SYSTEM PROMPT\"\n    user_template: \"USER {backstory} - {stakes}\"");

            var transport = new FakeLlmTransport
            {
                ResponseToReturn = "Here is the object:\n```json\n" +
                    DiagnosisJsonWith(
                        ("derived_feeling", "being left behind"),
                        ("defense_reaction", "performative detachment")) +
                    "\n```"
            };

            var catalog = PromptCatalog.LoadFromDirectory(testDir);
            var generator = new LlmTherapistDiagnosisGenerator(transport, catalog);

            try
            {
                var result = await generator.GenerateAsync(
                    "Char",
                    "he/him",
                    "bio",
                    new Dictionary<string, BackstoryFact>(),
                    new List<string>());

                Assert.Equal("being left behind", result["derived_feeling"]);
                Assert.Equal("performative detachment", result["defense_reaction"]);
                Assert.Equal(TherapistDiagnosisContract.RequiredFields, result.Keys.ToArray());
                Assert.Equal(1, transport.CallCount);
            }
            finally
            {
                Directory.Delete(testDir, true);
            }
        }

        [Fact]
        public async Task TherapistDiagnosisGenerator_RetriesMalformedResponseUntilJsonObject()
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData_Prompts_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "diagnosis.yaml"), "schema_version: 1\nprompts:\n  diagnosis:\n    temperature: 0.62\n    max_tokens: 888\n    system_prompt: \"SYSTEM PROMPT\"\n    user_template: \"USER {backstory} - {stakes}\"");

            var transport = new FakeLlmTransport
            {
                ResponsesToReturn = new Queue<string>(new[]
                {
                    "I would diagnose this as anxious clowning.",
                    DiagnosisJsonWith(
                        ("derived_feeling", "social exposure"),
                        ("defense_reaction", "preemptive irony"))
                })
            };

            var catalog = PromptCatalog.LoadFromDirectory(testDir);
            var generator = new LlmTherapistDiagnosisGenerator(transport, catalog);

            try
            {
                var result = await generator.GenerateAsync(
                    "Char",
                    "he/him",
                    "bio",
                    new Dictionary<string, BackstoryFact>(),
                    new List<string>());

                Assert.Equal("social exposure", result["derived_feeling"]);
                Assert.Equal("preemptive irony", result["defense_reaction"]);
                Assert.Equal(TherapistDiagnosisContract.RequiredFields, result.Keys.ToArray());
                Assert.Equal(2, transport.CallCount);
            }
            finally
            {
                Directory.Delete(testDir, true);
            }
        }

        [Fact]
        public async Task TherapistDiagnosisGenerator_RootArrayWithDiagnosisObject_RetriesThenThrows()
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData_Prompts_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "diagnosis.yaml"), "schema_version: 1\nprompts:\n  diagnosis:\n    temperature: 0.62\n    max_tokens: 888\n    system_prompt: \"SYSTEM PROMPT\"\n    user_template: \"USER {backstory} - {stakes}\"");

            var transport = new FakeLlmTransport
            {
                ResponseToReturn = @"[{ ""derived_feeling"": ""social exposure"", ""defense_reaction"": ""preemptive irony"" }]"
            };

            var catalog = PromptCatalog.LoadFromDirectory(testDir);
            var generator = new LlmTherapistDiagnosisGenerator(transport, catalog);

            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => generator.GenerateAsync(
                        "Char",
                        "he/him",
                        "bio",
                        new Dictionary<string, BackstoryFact>(),
                        new List<string>()));

                Assert.IsType<JsonException>(ex.InnerException);
                Assert.Contains("NoValidObject", ex.InnerException!.Message);
                Assert.Equal(3, transport.CallCount);
            }
            finally
            {
                Directory.Delete(testDir, true);
            }
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
        public async Task SynthesisPipelineResult_RoundTripsThroughDefinitionWriterLoaderAndProfileAssembly()
        {
            var pipeline = new SequentialSynthesisPipeline(
                new FakeBackstoryGenerator(),
                new FakeStakeGenerator(),
                new FakeDiagnosisGenerator());
            var synthesis = await pipeline.SynthesizeAsync(
                "Boundary Test",
                "they/them",
                "public bio",
                Array.Empty<string>());

            var def = new CharacterDefinition(
                schemaVersion: CharacterDefinition.CurrentSchemaVersion,
                characterId: Guid.NewGuid(),
                name: "Boundary Test",
                genderIdentity: "they/them",
                bio: "public bio",
                level: 2,
                items: new List<string>(),
                anatomy: new Dictionary<string, float>(),
                allocation: BuildAllocation(),
                psychologicalStake: "They need steady presence to risk honest attachment.",
                backstory: synthesis.Backstory,
                stakeLines: synthesis.StakeLines,
                psychiatricDiagnosis: synthesis.PsychiatricDiagnosis,
                consolidatedPersonality: "Alert, funny, and careful about needing anyone.",
                consolidatedBackstory: "A move made abandonment feel ordinary, so they narrate pain as control."
            );

            string persisted = CharacterDefinitionWriter.Write(def);
            var loadedDefinition = CharacterDefinitionLoader.ParseDefinition(persisted);
            var assembledProfile = CharacterDefinitionLoader.Parse(
                persisted,
                LoadItemRepo(),
                LoadAnatomyRepo());

            AssertSynthesisFieldsSurvive(
                synthesis,
                loadedDefinition.Backstory,
                loadedDefinition.StakeLines,
                loadedDefinition.PsychiatricDiagnosis);
            Assert.Equal(def.PsychologicalStake, loadedDefinition.PsychologicalStake);
            Assert.Equal(def.ConsolidatedPersonality, loadedDefinition.ConsolidatedPersonality);
            Assert.Equal(def.ConsolidatedBackstory, loadedDefinition.ConsolidatedBackstory);

            AssertSynthesisFieldsSurvive(
                synthesis,
                assembledProfile.Backstory,
                assembledProfile.StakeLines,
                assembledProfile.PsychiatricDiagnosis);
            Assert.Equal(def.PsychologicalStake, assembledProfile.PsychologicalStake);
            Assert.Equal(def.ConsolidatedPersonality, assembledProfile.ConsolidatedPersonality);
            Assert.Equal(def.ConsolidatedBackstory, assembledProfile.ConsolidatedBackstory);
            Assert.Contains("anxiety", assembledProfile.AssembledSystemPrompt);
            Assert.Contains("Alert, funny, and careful about needing anyone.", assembledProfile.AssembledSystemPrompt);
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
            Assert.Equal(3, transport.CallCount);

            Directory.Delete(testDir, true);
        }

        [Fact]
        public async Task TherapistDiagnosisGenerator_WithEmptyJsonObject_RetriesThenThrows()
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData_Prompts_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "diagnosis.yaml"), "schema_version: 1\nprompts:\n  diagnosis:\n    temperature: 0.7\n    max_tokens: 1024\n    system_prompt: \"SYSTEM PROMPT\"\n    user_template: \"USER {backstory} - {stakes}\"");

            var transport = new FakeLlmTransport();
            // Cognitive subtext requires both diagnosis fields, so an empty
            // object is a contract violation even though it is valid JSON.
            transport.ResponseToReturn = "{}";

            var catalog = PromptCatalog.LoadFromDirectory(testDir);
            var generator = new LlmTherapistDiagnosisGenerator(transport, catalog);

            var backstory = new Dictionary<string, BackstoryFact>();
            var stakes = new List<string>();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => generator.GenerateAsync("Char", "he/him", "bio", backstory, stakes));

            Assert.IsType<JsonException>(ex.InnerException);
            Assert.Contains("derived_feeling", ex.InnerException!.Message);
            Assert.Equal(3, transport.CallCount);

            Directory.Delete(testDir, true);
        }

        [Fact]
        public async Task TherapistDiagnosisGenerator_WithIncompleteJson_RetriesThenThrows()
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData_Prompts_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "diagnosis.yaml"), "schema_version: 1\nprompts:\n  diagnosis:\n    temperature: 0.7\n    max_tokens: 1024\n    system_prompt: \"SYSTEM PROMPT\"\n    user_template: \"USER {backstory} - {stakes}\"");

            var transport = new FakeLlmTransport();
            transport.ResponseToReturn = @"{ ""derived_feeling"": ""  social exposure  "", ""defense_reaction"": ""   "" }";
            
            var catalog = PromptCatalog.LoadFromDirectory(testDir);
            var generator = new LlmTherapistDiagnosisGenerator(transport, catalog);

            var backstory = new Dictionary<string, BackstoryFact>();
            var stakes = new List<string>();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => generator.GenerateAsync("Char", "he/him", "bio", backstory, stakes));

            Assert.IsType<JsonException>(ex.InnerException);
            Assert.Contains("defense_reaction", ex.InnerException!.Message);
            Assert.Equal(3, transport.CallCount);
            
            Directory.Delete(testDir, true);
        }

        [Fact]
        public async Task TherapistDiagnosisGenerator_WithMissingEmotionalFormulation_RetriesThenThrows()
        {
            var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData_Prompts_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "diagnosis.yaml"), "schema_version: 1\nprompts:\n  diagnosis:\n    temperature: 0.7\n    max_tokens: 1024\n    system_prompt: \"SYSTEM PROMPT\"\n    user_template: \"USER {backstory} - {stakes}\"");

            var transport = new FakeLlmTransport();
            transport.ResponseToReturn = DiagnosisJsonWithout("self_awareness_reaction");

            var catalog = PromptCatalog.LoadFromDirectory(testDir);
            var generator = new LlmTherapistDiagnosisGenerator(transport, catalog);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => generator.GenerateAsync(
                    "Char",
                    "he/him",
                    "bio",
                    new Dictionary<string, BackstoryFact>(),
                    new List<string>()));

            Assert.IsType<JsonException>(ex.InnerException);
            Assert.Contains("self_awareness_reaction", ex.InnerException!.Message);
            Assert.Equal(3, transport.CallCount);

            Directory.Delete(testDir, true);
        }

        private static Dictionary<string, string> CompleteDiagnosisWith(
            params (string Key, string Value)[] overrides)
        {
            var diagnosis = TherapistDiagnosisContract.RequiredFields.ToDictionary(
                field => field,
                field => $"specific formulation for {field}");

            foreach (var entry in overrides)
                diagnosis[entry.Key] = entry.Value;

            return diagnosis;
        }

        private static string DiagnosisJsonWith(params (string Key, string Value)[] overrides)
        {
            return ToJsonObject(CompleteDiagnosisWith(overrides));
        }

        private static string DiagnosisJsonWithout(string omittedField)
        {
            return ToJsonObject(
                CompleteDiagnosisWith()
                    .Where(pair => !string.Equals(pair.Key, omittedField, StringComparison.Ordinal))
                    .ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        private static string ToJsonObject(IReadOnlyDictionary<string, string> values)
        {
            return "{ " + string.Join(
                ", ",
                values.Select(pair => $"\"{pair.Key}\": \"{pair.Value}\"")) + " }";
        }

        private static AllocationBlock BuildAllocation()
        {
            var spent = Enum.GetValues(typeof(StatType))
                .Cast<StatType>()
                .ToDictionary(stat => stat, _ => 0);
            var shadows = Enum.GetValues(typeof(ShadowStatType))
                .Cast<ShadowStatType>()
                .ToDictionary(shadow => shadow, _ => 0);
            return new AllocationBlock(spent, 0, shadows);
        }

        private static IItemRepository LoadItemRepo()
            => new JsonItemRepository(TestRepoLocator.ReadDataFile("items/starter-items.json"));

        private static IAnatomyRepository LoadAnatomyRepo()
            => new JsonAnatomyRepository(TestRepoLocator.ReadDataFile("anatomy/anatomy-parameters.json"));

        private static void AssertSynthesisFieldsSurvive(
            CharacterSynthesisResult expected,
            IReadOnlyDictionary<string, BackstoryFact>? backstory,
            IReadOnlyList<string>? stakeLines,
            IReadOnlyDictionary<string, string>? psychiatricDiagnosis)
        {
            Assert.NotNull(backstory);
            Assert.Equal(expected.Backstory.Keys, backstory!.Keys);
            foreach (var pair in expected.Backstory)
            {
                Assert.Equal(pair.Value.BioLie, backstory[pair.Key].BioLie);
                Assert.Equal(pair.Value.TragicReality, backstory[pair.Key].TragicReality);
            }

            Assert.NotNull(stakeLines);
            Assert.Equal(expected.StakeLines, stakeLines);

            Assert.NotNull(psychiatricDiagnosis);
            Assert.Equal(TherapistDiagnosisContract.RequiredFields, psychiatricDiagnosis!.Keys.ToArray());
            Assert.Equal(expected.PsychiatricDiagnosis.Count, psychiatricDiagnosis.Count);
            foreach (string field in TherapistDiagnosisContract.RequiredFields)
                Assert.Equal(expected.PsychiatricDiagnosis[field], psychiatricDiagnosis[field]);
        }
    }
}
