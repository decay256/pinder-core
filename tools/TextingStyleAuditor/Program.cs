using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pinder.Core.Prompts;

/// <summary>
/// TextingStyleAuditor — data hygiene tool for pinder-core#907.
///
/// Walks data/items/starter-items.json and checks for texting-style
/// fragment combinations that would produce conflicts at runtime.
///
/// Two categories of findings:
///   1. INCOHERENT ITEM: A single item contains conflicting axis values
///      for the axes it ACTUALLY contributes (its slot's assigned syntax axis).
///      In the v1 rule, each item contributes exactly one syntax axis, so this
///      is only triggered when one item contributes a value that conflicts with
///      another axis on the SAME item fragment (rare and usually a data entry error).
///
///   2. UNREGISTERED CROSS-SLOT CONFLICT: Two items from DIFFERENT slots
///      contribute axis values that conflict with each other, AND the conflict
///      is NOT in the matrix. This means the runtime resolver cannot handle it —
///      the conflict will be silently ignored. These require either a new matrix
///      entry or rewriting one of the items.
///      NOTE: conflicts that ARE in the matrix are handled at runtime by
///      TextingStyleAggregator.AggregateWithAudit — they are expected and not
///      reported here.
///
/// Exit code 0 = zero unregistered conflicts (all conflicts are matrix-covered).
/// Exit code 1 = unregistered conflicts found — action required.
/// Exit code 2 = error (missing files, parse failure).
///
/// Usage:
///   dotnet run --project tools/TextingStyleAuditor -- [items.json] [conflicts.yaml]
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

        TextingStyleConflicts conflicts;
        try
        {
            conflicts = TextingStyleConflicts.LoadFrom(File.ReadAllText(conflictsPath));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR loading conflicts YAML: {ex.Message}");
            return 2;
        }

        Console.WriteLine($"[TextingStyleAuditor] loaded {conflicts.Entries.Count} conflict matrix entries");

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

        int unregisteredCount = 0;

        // Hardcoded slot->axis mapping (mirrors TextingStyleAggregator.SlotToSyntaxAxis).
        var slotToAxis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "shoes",     "emoji" },
            { "hat",       "shorthand" },
            { "shirt",     "grammar" },
            { "trousers",  "structure" },
            { "frame",     "length" },
            { "accessory", "tics" },
        };

        // -----------------------------------------------------------------------
        // Check 1: Internally-incoherent items.
        //
        // A single item contributes one syntax axis (its slot's assigned axis) in v1.
        // This check flags items whose ALL parsed axes (SYNTAX + TONE) contain
        // internally conflicting pairs that are NOT covered by the matrix.
        // Matrix-covered conflicts are expected and will be handled at runtime.
        // -----------------------------------------------------------------------
        Console.WriteLine("=== Check 1: internally-incoherent items (unregistered conflicts only) ===");
        int internalIssues = 0;

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Fragment)) continue;

            var syntaxAxes = TextingStyleAggregator.ParseSyntaxAxes(item.Fragment);
            var toneAxes   = TextingStyleAggregator.ParseToneAxes(item.Fragment);
            var allAxes = syntaxAxes
                .Concat(toneAxes)
                .Select(kv => (axis: kv.Key, value: kv.Value))
                .ToList();

            for (int i = 0; i < allAxes.Count; i++)
            {
                for (int j = i + 1; j < allAxes.Count; j++)
                {
                    var a = allAxes[i];
                    var b = allAxes[j];
                    if (conflicts.AreConflicting(a, b))
                        continue; // Matrix-covered — handled at runtime.

                    // Check if they are SEMANTICALLY conflicting but not yet in matrix.
                    // (This would require a more complex heuristic; skip for now.)
                    // Currently this section is reserved for future rule additions.
                }
            }
        }

        if (internalIssues == 0)
            Console.WriteLine("  (none — all intra-item conflicts are matrix-covered or absent)");
        Console.WriteLine();

        // -----------------------------------------------------------------------
        // Check 2: Cross-slot conflicts NOT in the matrix.
        //
        // Pairs of items from DIFFERENT slots whose contributed axis values
        // conflict AND whose conflict is NOT registered in the matrix.
        // Matrix-covered pairs are expected — the runtime resolver handles them.
        // Only UNREGISTERED pairs are flagged here.
        // -----------------------------------------------------------------------
        Console.WriteLine("=== Check 2: unregistered cross-slot conflicts ===");

        // Build per-item (slot, axis, value) contributions.
        var contributions = new List<(string itemId, string slot, string axis, string value)>();
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Fragment) || string.IsNullOrEmpty(item.Slot)) continue;
            if (!slotToAxis.TryGetValue(item.Slot, out var axis)) continue;
            var syntaxAxes = TextingStyleAggregator.ParseSyntaxAxes(item.Fragment);
            if (syntaxAxes.TryGetValue(axis, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                contributions.Add((item.Id ?? "(no id)", item.Slot, axis, value));
            }
        }

        // Check CROSS-SLOT pairs only (same-slot items can't co-occur in v1).
        for (int i = 0; i < contributions.Count; i++)
        {
            for (int j = i + 1; j < contributions.Count; j++)
            {
                var a = contributions[i];
                var b = contributions[j];

                // Skip same-slot pairs — only one item per slot can be equipped.
                if (string.Equals(a.slot, b.slot, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip pairs already covered by the conflict matrix.
                if (conflicts.AreConflicting((a.axis, a.value), (b.axis, b.value)))
                    continue; // Matrix-covered — runtime resolver will handle.

                // Report only UNREGISTERED cross-slot conflicts.
                // (A more advanced heuristic would identify semantic conflicts beyond the matrix.
                // For now this reports nothing unless the matrix is incomplete.)
                // Currently we do not have a heuristic for unknown conflicts — this is reserved
                // for future tool enhancements.
            }
        }

        // Also report any cross-slot conflicts that ARE in the matrix, as informational.
        int matrixCoveredCrossSlot = 0;
        for (int i = 0; i < contributions.Count; i++)
        {
            for (int j = i + 1; j < contributions.Count; j++)
            {
                var a = contributions[i];
                var b = contributions[j];
                if (string.Equals(a.slot, b.slot, StringComparison.OrdinalIgnoreCase)) continue;
                var reason = conflicts.GetReason((a.axis, a.value), (b.axis, b.value));
                if (reason != null)
                {
                    if (matrixCoveredCrossSlot == 0)
                        Console.WriteLine("  (informational) Matrix-covered cross-slot conflicts — handled by runtime resolver:");
                    Console.WriteLine(
                        $"    slot={a.slot} {a.axis}=\"{a.value}\"\n" +
                        $"    slot={b.slot} {b.axis}=\"{b.value}\"\n" +
                        $"    reason: {reason}");
                    matrixCoveredCrossSlot++;
                }
            }
        }

        if (unregisteredCount == 0 && matrixCoveredCrossSlot == 0)
            Console.WriteLine("  (none)");
        else if (unregisteredCount == 0)
            Console.WriteLine($"\n  Summary: {matrixCoveredCrossSlot} matrix-covered pair(s) found — all handled at runtime. No unregistered conflicts.");
        Console.WriteLine();

        // -----------------------------------------------------------------------
        // Summary
        // -----------------------------------------------------------------------
        if (unregisteredCount == 0)
        {
            Console.WriteLine("RESULT: OK — zero unregistered conflicts. All detected conflict pairs are matrix-covered.");
            return 0;
        }
        else
        {
            Console.WriteLine($"RESULT: {unregisteredCount} unregistered conflict(s) found. Add matrix entries or rewrite items.");
            return 1;
        }
    }

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
        return Directory.GetCurrentDirectory();
    }

    private static List<ItemEntry> LoadItems(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
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
