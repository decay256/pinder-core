using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.SessionRunner;
using Pinder.SessionSetup;

partial class Program
{
    internal static CharacterProfile LoadCharacter(
        string? defPath,
        string? name,
        ref IItemRepository? itemRepo,
        ref IAnatomyRepository? anatomyRepo,
        ref ITimingRepository? timingRepo,
        bool archetypesEnabled = false)
    {
        // Explicit --player-def / --datee-def takes priority.
        if (defPath != null)
        {
            EnsureReposLoaded(ref itemRepo, ref anatomyRepo, ref timingRepo);
            return CharacterDefinitionLoader.Load(
                defPath, itemRepo!, anatomyRepo!, archetypesEnabled, timingRepo);
        }

        // --player / --datee name: resolve through DirectoryCharacterStore
        // exclusively. #840 removed the prompt-file fallback; failure to find
        // data/characters/{slug}.json is a user-facing error rather than a
        // silent reach-for-stale-text-files.
        if (name != null)
        {
            string? charDefPath = Pinder.Core.Data.DataFileLocator.FindDataFile(
                AppContext.BaseDirectory,
                Path.Combine("data", "characters", $"{name.ToLowerInvariant()}.json"));

            if (charDefPath == null)
            {
                string available = ListAvailableCharacters();
                throw new FileNotFoundException(
                    $"Character not found: {name} (no data/characters/{name.ToLowerInvariant()}.json on the locator paths).\n" +
                    $"Available characters: {available}");
            }

            EnsureReposLoaded(ref itemRepo, ref anatomyRepo, ref timingRepo);
            string charactersDir = Path.GetDirectoryName(charDefPath)!;
            var store = new DirectoryCharacterStore(charactersDir);
            string id = ReadCharacterIdFromFile(charDefPath);
            CharacterDefinition? def = store.LoadAsync(id).GetAwaiter().GetResult();
            if (def == null)
                throw new InvalidOperationException(
                    $"DirectoryCharacterStore at {charactersDir} did not surface character_id {id} from {charDefPath}");
            return CharacterDefinitionLoader.Assemble(
                def, itemRepo!, anatomyRepo!, archetypesEnabled, timingRepo);
        }

        throw new InvalidOperationException("Neither definition path nor name provided");
    }

    internal static string ListAvailableCharacters()
    {
        string? charactersDir = Pinder.Core.Data.DataFileLocator.FindDataFile(
            AppContext.BaseDirectory, Path.Combine("data", "characters"));
        if (charactersDir == null || !Directory.Exists(charactersDir))
            return "(no characters directory found)";
        var slugs = new List<string>();
        foreach (var f in Directory.EnumerateFiles(charactersDir, "*.json"))
        {
            var slug = Path.GetFileNameWithoutExtension(f);
            if (!string.IsNullOrEmpty(slug)) slugs.Add(slug);
        }
        slugs.Sort(StringComparer.Ordinal);
        return slugs.Count == 0 ? "(none)" : string.Join(", ", slugs);
    }

    internal static string ReadCharacterIdFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("character_id", out var idProp)
            || idProp.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"{path} is missing required field: character_id");
        }
        return idProp.GetString()!;
    }

    internal static void EnsureReposLoaded(
        ref IItemRepository? itemRepo,
        ref IAnatomyRepository? anatomyRepo,
        ref ITimingRepository? timingRepo)
    {
        if (itemRepo != null && anatomyRepo != null && timingRepo != null)
            return;

        string baseDir = AppContext.BaseDirectory;

        string? itemsPath = Pinder.Core.Data.DataFileLocator.FindDataFile(baseDir, Path.Combine("data", "items", "starter-items.json"));
        if (itemsPath == null)
            throw new FileNotFoundException("Could not find data/items/starter-items.json — ensure data files are present in the repo");

        string? anatomyPath = Pinder.Core.Data.DataFileLocator.FindDataFile(baseDir, Path.Combine("data", "anatomy", "anatomy-parameters.json"));
        if (anatomyPath == null)
            throw new FileNotFoundException("Could not find data/anatomy/anatomy-parameters.json — ensure data files are present in the repo");

        string? timingPath = Pinder.Core.Data.DataFileLocator.FindDataFile(baseDir, Path.Combine("data", "timing", "response-profiles.json"));
        if (timingPath == null)
            throw new FileNotFoundException("Could not find data/timing/response-profiles.json - ensure data files are present in the repo");

        itemRepo = new JsonItemRepository(File.ReadAllText(itemsPath));
        anatomyRepo = new JsonAnatomyRepository(File.ReadAllText(anatomyPath));
        timingRepo = new JsonTimingRepository(File.ReadAllText(timingPath));
    }
}
