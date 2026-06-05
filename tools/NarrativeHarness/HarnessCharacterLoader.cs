using System;
using System.IO;
using System.Text.Json;
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
        public static LoadedCharacter Load(string slug)
        {
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

            IItemRepository itemRepo = new JsonItemRepository(File.ReadAllText(itemsPath));
            IAnatomyRepository anatomyRepo = new JsonAnatomyRepository(File.ReadAllText(anatomyPath));

            string charactersDir = Path.GetDirectoryName(charDefPath)!;
            var store = new DirectoryCharacterStore(charactersDir);
            string id = ReadCharacterId(charDefPath);
            CharacterDefinition? def = store.LoadAsync(id).GetAwaiter().GetResult();
            if (def == null)
                throw new InvalidOperationException(
                    $"DirectoryCharacterStore at {charactersDir} did not surface character_id {id}.");

            CharacterProfile profile = CharacterDefinitionLoader.Assemble(def, itemRepo, anatomyRepo);
            return new LoadedCharacter(profile, def);
        }

        private static string ReadCharacterId(string path)
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("character_id", out var idProp)
                || idProp.ValueKind != JsonValueKind.String)
                throw new FormatException($"{path} is missing required field: character_id");
            return idProp.GetString()!;
        }

        /// <summary>Locate and load data/game-definition.yaml as the base GameDefinition.</summary>
        public static Pinder.LlmAdapters.GameDefinition LoadBaseGameDefinition()
        {
            string baseDir = AppContext.BaseDirectory;
            string? gameDefPath = HarnessDataLocator.FindDataFile(
                baseDir, Path.Combine("data", "game-definition.yaml"));
            if (gameDefPath == null)
                throw new FileNotFoundException("Could not find data/game-definition.yaml.");
            return Pinder.LlmAdapters.GameDefinition.LoadFrom(File.ReadAllText(gameDefPath));
        }
    }
}
