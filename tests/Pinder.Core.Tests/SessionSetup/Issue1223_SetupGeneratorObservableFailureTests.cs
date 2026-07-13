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

        // ── 3. PARITY GUARD — FAILURE RETENTION AND BUBBLING (must PASS now) ───────────
        [Fact]
        public async Task ParityGuard_FailureBubblesWithoutDegradationCallback_ForAllSetupGenerators()
        {
            var throwingTransport = new ThrowingLlmTransport();

            // Test LlmStakeGenerator (now bubbles up)
            var stakeGen = new LlmStakeGenerator(throwingTransport);
            await Assert.ThrowsAsync<LlmTransportException>(() => stakeGen.GenerateAsync("Alice", "some system prompt"));

            // Test LlmBackgroundGenerator (now bubbles up)
            var bgGen = new LlmBackgroundGenerator(throwingTransport);
            await Assert.ThrowsAsync<LlmTransportException>(() => bgGen.GenerateAsync("Alice", "some system prompt"));

            // Test LlmDramaticArcGenerator (throws)
            var arcGen = new LlmDramaticArcGenerator(throwingTransport);
            await Assert.ThrowsAsync<LlmTransportException>(() => arcGen.GenerateAsync(
                "Player", "PlayerStake", "PlayerBio",
                "Datee", "DateeStake", "DateeBio"));

            // Test LlmOutfitDescriber (throws)
            var outfitGen = new LlmOutfitDescriber(throwingTransport);
            await Assert.ThrowsAsync<LlmTransportException>(() => outfitGen.GenerateAsync(
                "Player", new List<string>(), "Datee", new List<string>()));
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

        // ── 5. BEHAVIORAL TESTS (IMPLEMENTED & GREEN) ─────────────────────────────
        [Fact]
        public async Task DegradationCallback_FiresOnTransportFailure_ForAllGenerators()
        {
            var throwingTransport = new ThrowingLlmTransport();

            // 1. Stake Generator
            SetupGenerationResult? stakeResult = null;
            var stakeGen = new LlmStakeGenerator(throwingTransport, new LlmStakeGenerator.Options
            {
                OnDegraded = r => stakeResult = r
            });
            await stakeGen.GenerateAsync("Alice", "system prompt");
            Assert.NotNull(stakeResult);
            Assert.True(stakeResult.Degraded);
            Assert.Equal("transport_error", stakeResult.ErrorCode);
            Assert.Equal("stake", stakeResult.GeneratorName);

            // 2. Background Generator
            SetupGenerationResult? backgroundResult = null;
            var backgroundGen = new LlmBackgroundGenerator(throwingTransport, new LlmBackgroundGenerator.Options
            {
                OnDegraded = r => backgroundResult = r
            });
            await backgroundGen.GenerateAsync("Alice", "system prompt");
            Assert.NotNull(backgroundResult);
            Assert.True(backgroundResult.Degraded);
            Assert.Equal("transport_error", backgroundResult.ErrorCode);
            Assert.Equal("background", backgroundResult.GeneratorName);

            // 3. Outfit Describer
            SetupGenerationResult? outfitResult = null;
            var outfitGen = new LlmOutfitDescriber(throwingTransport, new LlmOutfitDescriber.Options
            {
                OnDegraded = r => outfitResult = r
            });
            await outfitGen.GenerateAsync("Alice", new List<string>(), "Bob", new List<string>());
            Assert.NotNull(outfitResult);
            Assert.True(outfitResult.Degraded);
            Assert.Equal("transport_error", outfitResult.ErrorCode);
            Assert.Equal("outfit", outfitResult.GeneratorName);

            // 4. Dramatic Arc Generator
            SetupGenerationResult? arcResult = null;
            var arcGen = new LlmDramaticArcGenerator(throwingTransport, new LlmDramaticArcGenerator.Options
            {
                OnDegraded = r => arcResult = r
            });
            await arcGen.GenerateAsync("Alice", "stake", "bio", "Bob", "stake", "bio");
            Assert.NotNull(arcResult);
            Assert.True(arcResult.Degraded);
            Assert.Equal("transport_error", arcResult.ErrorCode);
            Assert.Equal("dramatic_arc", arcResult.GeneratorName);
        }

        [Fact]
        public async Task DegradationCallback_FiresOnEmptyOutput_ForAllGenerators()
        {
            var emptyTransport = new FakeLlmTransport("   ");

            // 1. Stake Generator
            SetupGenerationResult? stakeResult = null;
            var stakeGen = new LlmStakeGenerator(emptyTransport, new LlmStakeGenerator.Options
            {
                OnDegraded = r => stakeResult = r
            });
            await stakeGen.GenerateAsync("Alice", "system prompt");
            Assert.NotNull(stakeResult);
            Assert.True(stakeResult.Degraded);
            Assert.Equal("empty_output", stakeResult.ErrorCode);
            Assert.Equal("stake", stakeResult.GeneratorName);

            // 2. Background Generator
            SetupGenerationResult? backgroundResult = null;
            var backgroundGen = new LlmBackgroundGenerator(emptyTransport, new LlmBackgroundGenerator.Options
            {
                OnDegraded = r => backgroundResult = r
            });
            await backgroundGen.GenerateAsync("Alice", "system prompt");
            Assert.NotNull(backgroundResult);
            Assert.True(backgroundResult.Degraded);
            Assert.Equal("empty_output", backgroundResult.ErrorCode);
            Assert.Equal("background", backgroundResult.GeneratorName);

            // 3. Outfit Describer
            SetupGenerationResult? outfitResult = null;
            var outfitGen = new LlmOutfitDescriber(emptyTransport, new LlmOutfitDescriber.Options
            {
                OnDegraded = r => outfitResult = r
            });
            await outfitGen.GenerateAsync("Alice", new List<string>(), "Bob", new List<string>());
            Assert.NotNull(outfitResult);
            Assert.True(outfitResult.Degraded);
            Assert.Equal("empty_output", outfitResult.ErrorCode);
            Assert.Equal("outfit", outfitResult.GeneratorName);

            // 4. Dramatic Arc Generator
            SetupGenerationResult? arcResult = null;
            var arcGen = new LlmDramaticArcGenerator(emptyTransport, new LlmDramaticArcGenerator.Options
            {
                OnDegraded = r => arcResult = r
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                arcGen.GenerateAsync("Alice", "stake", "bio", "Bob", "stake", "bio"));
            Assert.NotNull(arcResult);
            Assert.True(arcResult.Degraded);
            Assert.Equal("empty_output", arcResult.ErrorCode);
            Assert.Equal("dramatic_arc", arcResult.GeneratorName);
        }

        [Fact]
        public async Task CancellationBehavior_IsPreserved_ForOptionalGenerators()
        {
            var cancelingTransport = new CancelingLlmTransport();

            SetupGenerationResult? stakeResult = null;
            var stakeGen = new LlmStakeGenerator(cancelingTransport, new LlmStakeGenerator.Options
            {
                OnDegraded = r => stakeResult = r
            });
            string stakeText = await stakeGen.GenerateAsync("Alice", "system prompt");
            Assert.Equal(string.Empty, stakeText);
            Assert.Null(stakeResult);

            SetupGenerationResult? backgroundResult = null;
            var backgroundGen = new LlmBackgroundGenerator(cancelingTransport, new LlmBackgroundGenerator.Options
            {
                OnDegraded = r => backgroundResult = r
            });
            string backgroundText = await backgroundGen.GenerateAsync("Alice", "system prompt");
            Assert.Equal(string.Empty, backgroundText);
            Assert.Null(backgroundResult);

            SetupGenerationResult? outfitResult = null;
            var outfitGen = new LlmOutfitDescriber(cancelingTransport, new LlmOutfitDescriber.Options
            {
                OnDegraded = r => outfitResult = r
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                outfitGen.GenerateAsync("Alice", new List<string>(), "Bob", new List<string>()));
            Assert.Null(outfitResult);

            SetupGenerationResult? arcResult = null;
            var arcGen = new LlmDramaticArcGenerator(cancelingTransport, new LlmDramaticArcGenerator.Options
            {
                OnDegraded = r => arcResult = r
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                arcGen.GenerateAsync("Alice", "stake", "bio", "Bob", "stake", "bio"));
            Assert.Null(arcResult);
        }

        [Fact]
        public async Task HappyPath_DoesNotFireOnDegraded()
        {
            var happyTransport = new FakeLlmTransport("actual content");

            SetupGenerationResult? stakeResult = null;
            var stakeGen = new LlmStakeGenerator(happyTransport, new LlmStakeGenerator.Options
            {
                OnDegraded = r => stakeResult = r
            });
            await stakeGen.GenerateAsync("Alice", "system prompt");
            Assert.Null(stakeResult);
        }

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

    internal sealed class CancelingLlmTransport : ILlmTransport
    {
        public Task<string> SendAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int maxTokens = 1024,
            string? phase = null,
            CancellationToken ct = default)
        {
            throw new OperationCanceledException("simulated cancellation");
        }
    }
}
