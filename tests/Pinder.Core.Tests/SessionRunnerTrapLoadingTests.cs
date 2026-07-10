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
    /// Tests that validate the session runner's trap loading via TrapRegistryLoader.
    /// Issue #353 established real trap loading. Trap data is a core gameplay
    /// contract: missing/corrupt traps.json now fails setup (throws) instead of
    /// silently falling back to NullTrapRegistry. NullTrapRegistry is only used
    /// deliberately, via the explicit --disable-traps flag (see Resolve tests).
    /// </summary>
    [Trait("Category", "SessionRunner")]
    public sealed class SessionRunnerTrapLoadingTests : IDisposable
    {
        private readonly string? _originalEnvVar;

        public SessionRunnerTrapLoadingTests()
        {
            // Preserve original env var value for cleanup
            _originalEnvVar = Environment.GetEnvironmentVariable(TrapRegistryLoader.EnvVarName);
        }

        public void Dispose()
        {
            // Restore original env var
            if (_originalEnvVar != null)
                Environment.SetEnvironmentVariable(TrapRegistryLoader.EnvVarName, _originalEnvVar);
            else
                Environment.SetEnvironmentVariable(TrapRegistryLoader.EnvVarName, null);
        }

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

        private static string FindRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (dir != null && !File.Exists(Path.Combine(dir, "data", "traps", "traps.json")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            Assert.NotNull(dir);
            return dir!;
        }

        [Fact]
        public void Load_WithEnvVar_LoadsFromSpecifiedPath()
        {
            // Arrange: point env var to real traps.json
            string trapsPath = FindTrapsJson();
            Environment.SetEnvironmentVariable(TrapRegistryLoader.EnvVarName, trapsPath);
            var warnings = new StringWriter();

            // Act
            ITrapRegistry registry = TrapRegistryLoader.Load("/nonexistent", warnings);

            // Assert: loaded real traps, not fallback
            Assert.NotNull(registry.GetTrap(StatType.Rizz));
            Assert.Contains("[INFO] Loaded traps", warnings.ToString());
        }

        [Fact]
        public void Load_WithUpwardSearch_FindsTrapsJson()
        {
            // Arrange: clear env var so it uses upward search
            Environment.SetEnvironmentVariable(TrapRegistryLoader.EnvVarName, null);
            string repoRoot = FindRepoRoot();
            // Start from a subdirectory — loader should walk up
            string subDir = Path.Combine(repoRoot, "session-runner", "bin");
            var warnings = new StringWriter();

            // Act
            ITrapRegistry registry = TrapRegistryLoader.Load(subDir, warnings);

            // Assert
            Assert.NotNull(registry.GetTrap(StatType.Charm));
            Assert.Contains("[INFO] Loaded traps", warnings.ToString());
        }

        [Fact]
        public void Load_WithMissingFile_ThrowsInsteadOfFallingBack()
        {
            // Arrange: point env var to nonexistent path
            Environment.SetEnvironmentVariable(TrapRegistryLoader.EnvVarName, "/nonexistent/traps.json");
            var infoWriter = new StringWriter();

            // Act + Assert: trap data is a core gameplay contract — missing
            // data must fail setup, not silently disable the mechanic.
            var ex = Assert.Throws<FileNotFoundException>(
                () => TrapRegistryLoader.Load("/also-nonexistent", infoWriter));
            Assert.Contains("traps.json", ex.Message);
        }

        [Fact]
        public void Load_WithCorruptJson_ThrowsInsteadOfFallingBack()
        {
            // Arrange: write corrupt JSON to temp file
            string tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "{ this is not valid json [[[");
                Environment.SetEnvironmentVariable(TrapRegistryLoader.EnvVarName, tempFile);
                var infoWriter = new StringWriter();

                // Act + Assert
                var ex = Assert.Throws<InvalidDataException>(
                    () => TrapRegistryLoader.Load("/nonexistent", infoWriter));
                Assert.Contains(tempFile, ex.Message);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void Resolve_WithDisableTrapsFlag_ReturnsNullRegistryWithoutTouchingTrapsJson()
        {
            // Arrange: point env var at a path that would throw if the loader
            // ever tried to read it — proves --disable-traps short-circuits
            // before any traps.json access is attempted.
            Environment.SetEnvironmentVariable(TrapRegistryLoader.EnvVarName, "/nonexistent/traps.json");
            var infoWriter = new StringWriter();

            // Act: this mirrors Program.Setup.cs's handling of the explicit
            // --disable-traps CLI flag.
            ITrapRegistry registry = TrapRegistryLoader.Resolve(disableTraps: true, "/also-nonexistent", infoWriter);

            // Assert: deliberate no-traps mode — null lookups, clearly logged
            // as intentional (not a silent failure fallback).
            Assert.IsType<NullTrapRegistry>(registry);
            Assert.Null(registry.GetTrap(StatType.Charm));
            Assert.Contains("[INFO] Traps disabled via --disable-traps", infoWriter.ToString());
        }

        [Fact]
        public void Resolve_WithoutDisableTrapsFlag_LoadsRealRegistry()
        {
            // Arrange
            string trapsPath = FindTrapsJson();
            Environment.SetEnvironmentVariable(TrapRegistryLoader.EnvVarName, trapsPath);
            var infoWriter = new StringWriter();

            // Act
            ITrapRegistry registry = TrapRegistryLoader.Resolve(disableTraps: false, "/nonexistent", infoWriter);

            // Assert: real trap data loaded, not the null fallback
            Assert.IsType<JsonTrapRepository>(registry);
            Assert.NotNull(registry.GetTrap(StatType.Rizz));
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
    }
}
