using System;
using System.Threading.Tasks;
using Pinder.Tools.NarrativeHarness;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Covers the async load path added to <see cref="HarnessCharacterLoader"/>
    /// (audit finding: the admin narrative-harness controller was performing
    /// synchronous file reads and a blocked-on async store call during setup,
    /// tying up an ASP.NET request thread). <see cref="HarnessCharacterLoader.LoadAsync"/>
    /// and <see cref="HarnessCharacterLoader.LoadBaseGameDefinitionAsync"/> are
    /// the request-driven counterparts of the pre-existing synchronous
    /// <see cref="HarnessCharacterLoader.Load"/> / <see cref="HarnessCharacterLoader.LoadBaseGameDefinition"/>,
    /// which remain in place for CLI-only callers (tools/NarrativeHarness/Program.cs)
    /// and are now implemented by delegating to the async versions.
    ///
    /// These tests mirror the existing sync coverage (Issue859 slug hardening,
    /// Issue1179 archetypes-on-path) to prove the async methods preserve the
    /// exact same guard/error/assembly behaviour as their synchronous
    /// counterparts, plus a correctness check that both paths produce an
    /// equivalent result for the same input.
    /// </summary>
    public class Issue_HarnessCharacterLoaderAsyncTests
    {
        [Theory]
        [InlineData("../../etc/passwd")] // classic traversal
        [InlineData("..")]               // bare parent-dir
        [InlineData("foo/bar")]          // forward-slash separator
        [InlineData("foo\\bar")]         // backslash separator
        [InlineData("/abs/path")]        // leading separator / absolute
        [InlineData("")]                 // empty
        [InlineData("  ")]               // whitespace-only
        [InlineData("a/../b")]           // embedded traversal
        public async Task LoadAsync_RejectsMaliciousOrInvalidSlugs_WithArgumentException(string slug)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => HarnessCharacterLoader.LoadAsync(slug));
        }

        [Fact]
        public async Task LoadAsync_RejectsNullSlug_WithArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => HarnessCharacterLoader.LoadAsync(null!));
        }

        /// <summary>
        /// Positive control mirroring Issue859: a structurally-valid slug that
        /// simply doesn't exist on disk must still surface FileNotFoundException
        /// (not swallowed or converted by the async path).
        /// </summary>
        [Fact]
        public async Task LoadAsync_ValidShapedButMissingSlug_ThrowsFileNotFound_NotArgumentException()
        {
            var ex = await Record.ExceptionAsync(
                () => HarnessCharacterLoader.LoadAsync("definitely-not-a-real-character"));
            Assert.NotNull(ex);
            Assert.IsNotType<ArgumentException>(ex);
            Assert.IsType<System.IO.FileNotFoundException>(ex);
        }

        /// <summary>
        /// Correctness: the async load path must assemble the exact same
        /// production system prompt as the synchronous path for the same
        /// slug/flag combination (mirrors Issue1179's archetypes-on assertion).
        /// </summary>
        [Fact]
        public async Task LoadAsync_Zyx_ArchetypesEnabled_MatchesSyncLoad()
        {
            var syncLoaded = HarnessCharacterLoader.Load("zyx", archetypesEnabled: true);
            var asyncLoaded = await HarnessCharacterLoader.LoadAsync("zyx", archetypesEnabled: true);

            Assert.Equal(syncLoaded.AssembledSystemPrompt, asyncLoaded.AssembledSystemPrompt);
            Assert.Equal(syncLoaded.Name, asyncLoaded.Name);
            Assert.Contains("ACTIVE ARCHETYPE", asyncLoaded.AssembledSystemPrompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task LoadBaseGameDefinitionAsync_MatchesSyncLoadBaseGameDefinition()
        {
            var syncDef = HarnessCharacterLoader.LoadBaseGameDefinition();
            var asyncDef = await HarnessCharacterLoader.LoadBaseGameDefinitionAsync();

            Assert.Equal(syncDef.Name, asyncDef.Name);
            Assert.Equal(syncDef.GameMasterPrompt, asyncDef.GameMasterPrompt);
        }

        /// <summary>
        /// LoadAsync must return a genuinely awaitable Task rather than blocking
        /// the calling thread synchronously to completion — running several
        /// loads concurrently via Task.WhenAll must succeed without deadlocking,
        /// which would be the observable symptom of a re-introduced
        /// <c>.GetAwaiter().GetResult()</c> inside the async method.
        /// </summary>
        [Fact]
        public async Task LoadAsync_MultipleConcurrentCalls_AllCompleteWithoutDeadlock()
        {
            var tasks = new[]
            {
                HarnessCharacterLoader.LoadAsync("zyx"),
                HarnessCharacterLoader.LoadAsync("zyx"),
                HarnessCharacterLoader.LoadAsync("zyx"),
            };

            var results = await Task.WhenAll(tasks);

            Assert.All(results, r => Assert.False(string.IsNullOrEmpty(r.AssembledSystemPrompt)));
        }
    }
}
