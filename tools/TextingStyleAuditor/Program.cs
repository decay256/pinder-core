using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pinder.Core.Prompts;

/// <summary>
/// TextingStyleAuditor — data hygiene tool for pinder-core#907.
///
/// Walks data/items/starter-items.json and:
///   1. Reports pairs of items whose texting_style_fragment values would
///      generate a conflict under the loaded conflict matrix.
///   2. Reports items with internally-incoherent fragments (e.g. both
///      "never asks questions, only states" AND "always ends with a question"
///      on a single item).
///
/// Exit code 0 = zero conflicts found.
/// Exit code 1 = conflicts found (details printed to stdout).
///
/// Usage:
///   dotnet run --project tools/TextingStyleAuditor -- [path/to/starter-items.json] [path/to/conflicts.yaml]
///
/// Defaults: looks for data/items/starter-items.json and
///           data/persona/texting-style-conflicts.yaml relative to the
///           solution root (walks up from the binary directory, same
///           convention as Pinder.Core.Tests).
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        string? itemsPath     = args.Length > 0 ? args[0] : null;
        string? conflictsPath = args.Length > 1 ? args[1] : null;

        string repoRoot = FindRepoRoot();

        itemsPath     ??= Path.Combine(repoRoot, "data", "items", "starter-items.json");
        conflictsPath ??= Path.Combine(repoRoot, "data", "persona", "texting-style-conflicts.yaml");

        if (!File.Exists(itemsPath))
        {
            Console.Error.WriteLine($"ERROR: items file not found: {itemsPath}");
            return 2;
        }
        if (!File.Exists(conflictsPath))
        {
            Console.Error.WriteLine($"ERROR: conflicts file not found: {conflictsPath}");
            return 2;
        }

        Console.WriteLine($"[TextingStyleAuditor] items: {itemsPath}");
        Console.WriteLine($"[TextingStyleAuditor] conflicts: {conflictsPath}");

        // Load conflict matrix.
        TextingStyleConflicts conflicts;
        try
        {
            conflicts = TextingStyleConflicts.LoadFromYaml(File.ReadAllText(conflictsPath));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR loading conflicts YAML: {ex.Message}");
            return 2;
        }

        Console.WriteLine($"[TextingStyleAuditor] loaded {conflicts.Entries.Count} conflict entries");
        Console.WriteLine();

        // Load items.
        List<ItemEntry> items;
        try
        {
            items = LoadItems(File.ReadAllText(itemsPath));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR loading items JSON: {ex.Message}");
            return 2;
        }

        Console.WriteLine($"[TextingStyleAuditor] loaded {items.Count} items");
        Console.WriteLine();

        int issueCount = 0;

        // -----------------------------------------------------------------------
        // Check 1: Internal incoherence — single item whose texting_style_fragment
        // contains two values on the SAME axis that conflict with each other.
        // (Rare in practice since each fragment block has one value per axis,
        // but defensive check covers future authoring mistakes.)
        // -----------------------------------------------------------------------
        Console.WriteLine("=== Check 1: internally-incoherent items ===");
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Fragment)) continue;

            var syntaxAxes = TextingStyleAggregator.ParseSyntaxAxes(item.Fragment);
            var toneAxes   = TextingStyleAggregator.ParseToneAxes(item.Fragment);
            var allAxes    = syntaxAxes
                .Concat(toneAxes)
                .Select(kv => (axis: kv.Key, value: kv.Value))
                .ToList();

            for (int i = 0; i < allAxes.Count; i++)
            {
                for (int j = i + 1; j < allAxes.Count; j++)
                {
                    var a = allAxes[i];
                    var b = allAxes[j];
                    var reason = conflicts.GetReason(a, b);
                    if (reason != null)
                    {
                        Console.WriteLine(
                            $"  INCOHERENT ITEM: id={item.Id ?? "(no id)"} slot={item.Slot ?? "?"}\n" +
                            $"    axis_a: {a.axis}: {a.value}\n" +
                            $"    axis_b: {b.axis}: {b.value}\n" +
                            $"    reason: {reason}");
                        issueCount++;
                    }
                }
            }
        }

        if (issueCount == 0)
            Console.WriteLine("  (none)");
        Console.WriteLine();

        // -----------------------------------------------------------------------
        // Check 2: Cross-item conflicts — pairs of items from DIFFERENT slots
        // whose fragments contribute conflicting axis values to the same
        // character's aggregated profile.
        // -----------------------------------------------------------------------
        Console.WriteLine("=== Check 2: cross-item conflicts (pairs from different slots) ===");

        int crossIssues = 0;

        // Group items by the axis they contribute in the v1 rule.
        // Syntax: item in slot X contributes axis slotToAxis[X].
        // Tone: items in any slot contribute tone axes too (but the aggregator
        //       only reads TONE from anatomy, not items — so cross-item tone
        //       conflicts via items don't land in the aggregated profile in v1).
        //       We still check SYNTAX axes for cross-item conflicts.
        var slotToAxis = TextingStyleAggregator.SlotToSyntaxAxis;

        // Build per-item (axis, value) contributions.
        var contributions = new List<(string itemId, string slot, string axis, string value)>();
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Fragment) || string.IsNullOrEmpty(item.Slot)) continue;
            if (!slotToAxis.TryGetValue(item.Slot, out var axis)) continue;
            var syntaxAxes = TextingStyleAggregator.ParseSyntaxAxes(item.Fragment);
            string? value;
            if (syntaxAxes.TryGetValue(axis, out value) && !string.IsNullOrWhiteSpace(value))
            {
                contributions.Add((item.Id ?? "(no id)", item.Slot, axis, value!));
            }
        }

        // Check all pairs from DIFFERENT slots.
        for (int i = 0; i < contributions.Count; i++)
        {
            for (int j = i + 1; j < contributions.Count; j++)
            {
                var a = contributions[i];
                var b = contributions[j];

                // Same slot = same axis = mutually exclusive by v1 rule (only one item per slot).
                // Still report it for completeness.
                var reason = conflicts.GetReason((a.axis, a.value), (b.axis, b.value));
                if (reason != null)
                {
                    Console.WriteLine(
                        $"  CONFLICT: item1={a.itemId}(slot={a.slot}) {a.axis}=\"{a.value}\"\n" +
                        $"             item2={b.itemId}(slot={b.slot}) {b.axis}=\"{b.value}\"\n" +
                        $"             reason: {reason}");
                    crossIssues++;
                    issueCount++;
                }
            }
        }

        if (crossIssues == 0)
            Console.WriteLine("  (none)");
        Console.WriteLine();

        // -----------------------------------------------------------------------
        // Summary
        // -----------------------------------------------------------------------
        if (issueCount == 0)
        {
            Console.WriteLine("RESULT: OK — zero conflicts found in current dataset.");
            return 0;
        }
        else
        {
            Console.WriteLine($"RESULT: {issueCount} issue(s) found. See output above.");
            return 1;
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 12; i++)
        {
            if (File.Exists(Path.Combine(dir, "Pinder.Core.sln")))
                return dir;
            var parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir) break;
            dir = parent;
        }
        // Fallback: current working directory.
        return Directory.GetCurrentDirectory();
    }

    private static List<ItemEntry> LoadItems(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Items may be a top-level array or wrapped in { "items": [...] }
        JsonElement arrayEl = root.ValueKind == JsonValueKind.Array
            ? root
            : root.GetProperty("items");

        var result = new List<ItemEntry>();
        foreach (var el in arrayEl.EnumerateArray())
        {
            string? id       = el.TryGetProperty("id",       out var idProp)   ? idProp.GetString()   : null;
            string? slot     = el.TryGetProperty("slot",     out var slotProp) ? slotProp.GetString() : null;
            string? fragment = el.TryGetProperty("texting_style_fragment", out var fProp) ? fProp.GetString() : null;
            result.Add(new ItemEntry { Id = id, Slot = slot, Fragment = fragment });
        }
        return result;
    }

    private class ItemEntry
    {
        public string? Id       { get; set; }
        public string? Slot     { get; set; }
        public string? Fragment { get; set; }
    }
}
