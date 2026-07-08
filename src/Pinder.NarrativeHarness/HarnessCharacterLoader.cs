using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.SessionSetup;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// Loaded character bundle: the production CharacterProfile (whose
    /// AssembledSystemPrompt is the real character system prompt) plus the raw
    /// CharacterDefinition (which carries PsychologicalStake + BackgroundStory).
    /// </summary>
    public sealed class LoadedCharacter
    {
        public CharacterProfile Profile { get; }
        public CharacterDefinition Definition { get; }

        public LoadedCharacter(CharacterProfile profile, CharacterDefinition definition)
        {
            Profile = profile;
            Definition = definition;
        }

        public string Name => Definition.Name;
        public string? PsychologicalStake => Definition.PsychologicalStake ?? Profile.PsychologicalStake;
        public string? BackgroundStory => Definition.BackgroundStory;
        public string AssembledSystemPrompt => Profile.AssembledSystemPrompt;
    }

    /// <summary>
    /// Reuses the production load path verbatim (same public APIs as
    /// session-runner): DataFileLocator -&gt; DirectoryCharacterStore -&gt;
    /// CharacterDefinitionLoader.Assemble. No prompt assembly is reimplemented.
    /// </summary>
    public static class HarnessCharacterLoader
    {
        // Defense-in-depth allowlist for admin-supplied character slugs
        // (--character / --pursuer-character). Accept only a leading
        // alphanumeric followed by alphanumerics/hyphen/underscore. This
        // rejects path separators ('/', '\'), parent-dir traversal (".."),
        // bare dots, leading separators/dots, drive roots, and whitespace,
        // BEFORE any path is constructed from the slug.
        private static readonly Regex SafeSlugPattern =
            new Regex("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.Compiled);

        private static void ValidateSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug) || !SafeSlugPattern.IsMatch(slug))
                throw new ArgumentException(
                    $"Invalid character slug: '{slug}'. Slugs must match ^[A-Za-z0-9][A-Za-z0-9_-]*$ "
                    + "(no path separators, no '..', no absolute paths, no whitespace).",
                    nameof(slug));
        }

        /// <summary>
        /// Synchronous entry point used by CLI-only callers (e.g.
        /// tools/NarrativeHarness/Program.cs) where blocking on disk I/O during
        /// process startup is acceptable. Delegates to <see cref="LoadAsync"/> so
        /// there is exactly one implementation of the load path; request-driven
        /// callers (the admin narrative-harness HTTP endpoint) MUST use
        /// <see cref="LoadAsync"/> directly instead of calling this method, so
        /// they never block an ASP.NET request thread on disk I/O.
        /// </summary>
        public static LoadedCharacter Load(string slug, bool archetypesEnabled = false)
        {
            return LoadAsync(slug, archetypesEnabled).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Async counterpart of <see cref="Load"/>. Same production load path
        /// (DataFileLocator -&gt; DirectoryCharacterStore -&gt;
        /// CharacterDefinitionLoader.Assemble) but with genuinely asynchronous
        /// file reads (<see cref="ReadAllTextAsync"/>) and an awaited
        /// <see cref="DirectoryCharacterStore.LoadAsync"/> call instead of
        /// <c>.GetAwaiter().GetResult()</c>. Intended for request-driven
        /// callers that must not block a thread on disk I/O while setting up
        /// an admin harness run.
        /// </summary>
        public static async Task<LoadedCharacter> LoadAsync(
            string slug, bool archetypesEnabled = false, CancellationToken cancellationToken = default)
        {
            // Guard first: reject structurally-unsafe slugs before constructing
            // any filesystem path. Valid-but-missing slugs still fall through to
            // the FileNotFoundException path below.
            ValidateSlug(slug);

            string baseDir = AppContext.BaseDirectory;

            // Production prompt assembly requires the unified PromptCatalog to be
            // wired (same as session-runner does before building any profile).
            // Wire() ancestor-searches for data/prompts and is idempotent.
            PromptWiring.Wire(Path.Combine(baseDir, "data", "prompts"), Console.Error);

            string? charDefPath = HarnessDataLocator.FindDataFile(
                baseDir, Path.Combine("data", "characters", $"{slug.ToLowerInvariant()}.json"));
            if (charDefPath == null)
                throw new FileNotFoundException(
                    $"Character not found: {slug} (no data/characters/{slug.ToLowerInvariant()}.json on the locator paths).");

            string? itemsPath = HarnessDataLocator.FindDataFile(
                baseDir, Path.Combine("data", "items", "starter-items.json"));
            if (itemsPath == null)
                throw new FileNotFoundException("Could not find data/items/starter-items.json.");

            string? anatomyPath = HarnessDataLocator.FindDataFile(
                baseDir, Path.Combine("data", "anatomy", "anatomy-parameters.json"));
            if (anatomyPath == null)
                throw new FileNotFoundException("Could not find data/anatomy/anatomy-parameters.json.");

            IItemRepository itemRepo = new JsonItemRepository(
                await ReadAllTextAsync(itemsPath, cancellationToken).ConfigureAwait(false));
            IAnatomyRepository anatomyRepo = new JsonAnatomyRepository(
                await ReadAllTextAsync(anatomyPath, cancellationToken).ConfigureAwait(false));

            string charactersDir = Path.GetDirectoryName(charDefPath)!;
            var store = new DirectoryCharacterStore(charactersDir);
            string id = await ReadCharacterIdAsync(charDefPath, cancellationToken).ConfigureAwait(false);
            CharacterDefinition? def = await store.LoadAsync(id, cancellationToken).ConfigureAwait(false);
            if (def == null)
                throw new InvalidOperationException(
                    $"DirectoryCharacterStore at {charactersDir} did not surface character_id {id}.");

            CharacterProfile profile = CharacterDefinitionLoader.Assemble(def, itemRepo, anatomyRepo, archetypesEnabled);
            return new LoadedCharacter(profile, def);
        }

        private static async Task<string> ReadCharacterIdAsync(string path, CancellationToken cancellationToken)
        {
            string json = await ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("character_id", out var idProp)
                || idProp.ValueKind != JsonValueKind.String)
                throw new FormatException($"{path} is missing required field: character_id");
            return idProp.GetString()!;
        }

        /// <summary>
        /// Genuinely asynchronous file read (async <see cref="FileStream"/> +
        /// <see cref="StreamReader"/>), mirroring
        /// <c>DirectoryCharacterStore</c>'s raw I/O helper. This project
        /// targets netstandard2.0, which has no <c>File.ReadAllTextAsync</c>,
        /// so this hand-rolled helper is what makes <see cref="LoadAsync"/>
        /// and <see cref="LoadBaseGameDefinitionAsync"/> non-blocking rather
        /// than a synchronous read wrapped in <c>Task.FromResult</c>.
        /// </summary>
        private static async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
        {
            const int bufferSize = 4096;
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
            // Matches File.ReadAllText's behaviour: auto-detect BOM, default to UTF-8.
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string text = await reader.ReadToEndAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return text;
        }

        /// <summary>
        /// Locate and load data/game-definition.yaml as the base GameDefinition.
        /// Synchronous entry point for CLI-only callers; delegates to
        /// <see cref="LoadBaseGameDefinitionAsync"/>.
        /// </summary>
        public static Pinder.LlmAdapters.GameDefinition LoadBaseGameDefinition()
        {
            return LoadBaseGameDefinitionAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Async counterpart of <see cref="LoadBaseGameDefinition"/> using
        /// <see cref="ReadAllTextAsync"/> instead of a synchronous read.
        /// Intended for request-driven callers.
        /// </summary>
        public static async Task<Pinder.LlmAdapters.GameDefinition> LoadBaseGameDefinitionAsync(
            CancellationToken cancellationToken = default)
        {
            string baseDir = AppContext.BaseDirectory;
            string? gameDefPath = HarnessDataLocator.FindDataFile(
                baseDir, Path.Combine("data", "game-definition.yaml"));
            if (gameDefPath == null)
                throw new FileNotFoundException("Could not find data/game-definition.yaml.");
            string yaml = await ReadAllTextAsync(gameDefPath, cancellationToken).ConfigureAwait(false);
            return Pinder.LlmAdapters.GameDefinition.LoadFrom(yaml);
        }
    }
}
