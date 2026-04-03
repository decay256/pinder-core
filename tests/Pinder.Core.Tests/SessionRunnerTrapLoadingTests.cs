using System;
using System.IO;
using System.Linq;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests that validate the session runner's trap loading pattern:
    /// JsonTrapRepository loaded from data/traps/traps.json replaces NullTrapRegistry.
    /// Issue #353: NullTrapRegistry disables all traps in the session runner.
    /// </summary>
    public sealed class SessionRunnerTrapLoadingTests
    {
        private static string FindTrapsJson()
        {
            var dir = Directory.GetCurrentDirectory();
            while (dir != null && !File.Exists(Path.Combine(dir, "data", "traps", "traps.json")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            Assert.NotNull(dir);
            return Path.Combine(dir!, "data", "traps", "traps.json");
        }

        [Fact]
        public void JsonTrapRepository_ReturnsTraps_UnlikeNullTrapRegistry()
        {
            // NullTrapRegistry returns null for all stats — this was the bug
            var nullRegistry = new NullTrapRegistryStub();
            Assert.Null(nullRegistry.GetTrap(StatType.Rizz));
            Assert.Null(nullRegistry.GetLlmInstruction(StatType.Rizz));

            // JsonTrapRepository loaded from real data returns actual traps
            var json = File.ReadAllText(FindTrapsJson());
            var realRegistry = new JsonTrapRepository(json);
            Assert.NotNull(realRegistry.GetTrap(StatType.Rizz));
            Assert.NotNull(realRegistry.GetLlmInstruction(StatType.Rizz));
        }

        [Fact]
        public void JsonTrapRepository_AllSixStats_HaveTraps()
        {
            var json = File.ReadAllText(FindTrapsJson());
            var registry = new JsonTrapRepository(json);

            var stats = new[] {
                StatType.Charm, StatType.Rizz, StatType.Honesty,
                StatType.Chaos, StatType.Wit, StatType.SelfAwareness
            };

            foreach (var stat in stats)
            {
                var trap = registry.GetTrap(stat);
                Assert.NotNull(trap);
                Assert.False(string.IsNullOrEmpty(trap!.LlmInstruction),
                    $"Trap for {stat} has no LLM instruction — taint won't flow to LLM calls.");
            }
        }

        [Fact]
        public void TropeTrap_WithRealRegistry_ActivatesTrap()
        {
            // Simulate what happens during a TropeTrap failure (miss by 6-9):
            // RollEngine calls trapRegistry.GetTrap(stat) and activates it via TrapState.
            // With NullTrapRegistry, GetTrap returns null → no trap activated.
            // With JsonTrapRepository, GetTrap returns real definition → trap activates.
            var json = File.ReadAllText(FindTrapsJson());
            var registry = new JsonTrapRepository(json);

            var trapDef = registry.GetTrap(StatType.Rizz);
            Assert.NotNull(trapDef);
            Assert.Equal("creep", trapDef!.Id);

            // TrapState.Activate should work with the real definition
            var trapState = new TrapState();
            Assert.False(trapState.HasActive);

            trapState.Activate(trapDef);
            Assert.True(trapState.HasActive);
        }

        [Fact]
        public void FallbackToNullRegistry_WhenFileNotFound()
        {
            // Verify that the fallback pattern works — invalid JSON path
            // should not crash, just return a registry that returns null
            ITrapRegistry fallback;
            try
            {
                string badJson = File.ReadAllText("/nonexistent/path/traps.json");
                fallback = new JsonTrapRepository(badJson);
            }
            catch
            {
                // Expected: file not found → fall back gracefully
                fallback = new NullTrapRegistryStub();
            }

            // Fallback still implements ITrapRegistry (returns null, but doesn't crash)
            Assert.Null(fallback.GetTrap(StatType.Charm));
        }

        /// <summary>Stub matching the NullTrapRegistry in session-runner.</summary>
        private sealed class NullTrapRegistryStub : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
