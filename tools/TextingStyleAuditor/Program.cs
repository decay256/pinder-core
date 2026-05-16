using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Pinder.Core.Prompts;

namespace Pinder.Tools.TextingStyleAuditor;

/// <summary>
/// Audits every item in <c>data/items/starter-items.json</c> for
/// internally-incoherent texting-style fragments: fragments whose
/// axis lines contain pairs that conflict under the matrix in
/// <c>data/persona/texting-style-conflicts.yaml</c>.
///
/// Usage:
///   dotnet run --project tools/TextingStyleAuditor [--csv]
///
///   --csv  Machine-readable output for CI ingestion.
///
/// Exit code 0 = all items coherent; 1 = at least one incoherent item.
///
/// See #907.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        bool csv = args.Length > 0 && args[0] == "--csv";

        // Locate repo root relative to the build output directory.
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Console.Error.WriteLine("ERROR: cannot locate repo root from CWD.");
            return 2;
        }

        string itemsPath = Path.Combine(repoRoot, "data", "items", "starter-items.json");
        string conflictsPath = Path.Combine(repoRoot, "data", "persona", "texting-style-conflicts.yaml");

        if (!File.Exists(itemsPath))
        {
            Console.Error.WriteLine($"ERROR: {itemsPath} not found.");
            return 2;
        }
        if (!File.Exists(conflictsPath))
        {
            Console.Error.WriteLine($"ERROR: {conflictsPath} not found.");
            return 2;
        }

        var conflicts = new TextingStyleConflicts(conflictsPath);
        int itemCount = 0;
        int badCount = 0;

        string json = File.ReadAllText(itemsPath);
        using var doc = JsonDocument.Parse(json);

        if (!csv)
        {
            Console.WriteLine($"Loaded {conflicts.Count} conflict rules.");
            Console.WriteLine();
        }

        foreach (var itemElement in doc.RootElement.EnumerateArray())
        {
            itemCount++;
            var fragmentProp = itemElement.TryGetProperty("texting_style_fragment", out var frag);
            string fragment = frag.GetString();
            if (!fragmentProp || string.IsNullOrEmpty(fragment))
                continue;

            var syntax = TextingStyleAggregator.ParseSyntaxAxes(fragment);

            // Check all pairs within this item's syntax axes.
            foreach (var kv1 in syntax)
            {
                foreach (var kv2 in syntax)
                {
                    // Only check each unordered pair once (lexicographic).
                    if (string.CompareOrdinal(kv1.Key, kv2.Key) >= 0)
                        continue;

                    var reason = conflicts.GetReason(
                        (kv1.Key, kv1.Value), (kv2.Key, kv2.Value));
                    if (reason == null)
                        continue;

                    string id = "?";
                    if (itemElement.TryGetProperty("id", out var idProp))
                    {
                        id = idProp.GetString() ?? "?";
                    }

                    if (csv)
                    {
                        Console.WriteLine(
                            $"ITEM,\"{id}\",\"{kv1.Key}\",\"{kv1.Value}\",\"{kv2.Key}\",\"{kv2.Value}\",\"{reason}\"");
                    }
                    else
                    {
                        Console.WriteLine($"INCOHERENT item={id}");
                        Console.WriteLine($"  [{kv1.Key}] {kv1.Value}");
                        Console.WriteLine($"  [{kv2.Key}] {kv2.Value}");
                        Console.WriteLine($"  -> {reason}");
                        Console.WriteLine();
                    }
                    badCount++;
                }
            }
        }

        if (!csv)
        {
            if (badCount == 0)
                Console.WriteLine($"OK — all {itemCount} items are coherent.");
            else
                Console.WriteLine($"{badCount} incoherent conflict(s) found across {itemCount} items.");
        }

        return badCount == 0 ? 0 : 1;
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "data", "items", "starter-items.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
