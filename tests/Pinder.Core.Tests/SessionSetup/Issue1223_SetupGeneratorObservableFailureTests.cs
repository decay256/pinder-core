using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests.SessionSetup
{
    public class Issue1223_SetupGeneratorObservableFailureTests
    {
        // ── 1. OPTIONS EXPOSE DEGRADATION CALLBACK (RED, reflection) ─────────────────
        [Fact]
        public void OptionsExposeDegradationCallback_ShouldExist()
        {
            AssertOptionsExposesCallback(typeof(LlmStakeGenerator.Options));
            AssertOptionsExposesCallback(typeof(LlmOutfitDescriber.Options));
        }

        // ── 2. SHARED RESULT TYPE EXISTS (RED, reflection) ──────────────────────────
        [Fact]
        public void SharedResultTypeExists_InSessionSetupAssembly()
        {
            var assembly = typeof(LlmStakeGenerator).Assembly;
            Type? resultType = null;
            
            foreach (var type in assembly.GetTypes())
            {
                if (type.Name.Contains("SetupGenerationResult", StringComparison.OrdinalIgnoreCase))
                {
                    resultType = type;
                    break;
                }
            }

            Assert.NotNull(resultType);
        }

        // ── 3. PARITY GUARD — FAILURE STILL RETURNS EMPTY (must PASS now) ───────────
        [Fact]
        public async Task ParityGuard_FailureStillReturnsEmpty_StakeAndDramaticArc()
        {
            var throwingTransport = new ThrowingLlmTransport();

            // Test LlmStakeGenerator
            var stakeGen = new LlmStakeGenerator(throwingTransport);
            string stakeResult = await stakeGen.GenerateAsync("Alice", "some system prompt");
            Assert.Equal(string.Empty, stakeResult);

            // Test LlmDramaticArcGenerator
            var arcGen = new LlmDramaticArcGenerator(throwingTransport);
            string arcResult = await arcGen.GenerateAsync(
                "Player", "PlayerStake", "PlayerBio",
                "Datee", "DateeStake", "DateeBio");
            Assert.Equal(string.Empty, arcResult);
        }

        // ── 4. PARITY GUARD — SUCCESS RETURNS TRIMMED TEXT (must PASS now) ──────────
        [Fact]
        public async Task ParityGuard_SuccessReturnsTrimmedText()
        {
            var successTransport = new FakeLlmTransport("  hello stake  ");
            var stakeGen = new LlmStakeGenerator(successTransport);
            
            string stakeResult = await stakeGen.GenerateAsync("Alice", "some system prompt");
            Assert.Equal("hello stake", stakeResult);
        }

        // ── 5. BEHAVIORAL RED (SKIPPED) ─────────────────────────────────────────────
        // We chose to SKIP test #5 (wiring the currently-nonexistent degradation callback via reflection
        // and asserting it fires on transport failure) because attempting to dynamically invoke or cast 
        // to a callback whose delegate signature and types (including SetupGenerationResult) are 
        // completely missing at compile time would be extremely brittle and complex (requiring Reflection.Emit
        // or DynamicMethod stubbing), which is prone to false positives/negatives. 
        // We rely on tests #1 and #2 as our solid RED gates to enforce the contract.

        // ── Helpers ───────────────────────────────────────────────────────────────

        private void AssertOptionsExposesCallback(Type optionsType)
        {
            var properties = optionsType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance);

            PropertyInfo? targetProp = null;
            foreach (var prop in properties)
            {
                string name = prop.Name;
                bool nameMatches = name.Contains("Degrad", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("Outcome", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("Result", StringComparison.OrdinalIgnoreCase);

                bool isDelegate = typeof(Delegate).IsAssignableFrom(prop.PropertyType);
                bool isSettable = prop.CanWrite && prop.GetSetMethod(nonPublic: false) != null;

                if (nameMatches && isDelegate && isSettable)
                {
                    targetProp = prop;
                    break;
                }
            }

            Assert.True(targetProp != null, $"Expected a public settable delegate-typed property with name containing Degrad/Outcome/Result on type {optionsType.FullName}, but none was found.");
        }
    }

    internal sealed class ThrowingLlmTransport : ILlmTransport
    {
        public Task<string> SendAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int maxTokens = 1024,
            string? phase = null,
            CancellationToken ct = default)
        {
            throw new LlmTransportException("boom");
        }
    }

    internal sealed class FakeLlmTransport : ILlmTransport
    {
        private readonly string _response;

        public FakeLlmTransport(string response)
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
            return Task.FromResult(_response);
        }
    }
}
