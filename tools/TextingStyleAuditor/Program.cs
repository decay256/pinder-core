using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pinder.Core.Prompts;

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

        Console.WriteLine($"[TextingStyleAuditor] loaded {conflicts.Entries.Count} conflict entries");
        Console.WriteLine();

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
                            $"    axis_a: {a.axis}: {a.value}\n    axis_b: {b.axis}: {b.value}\n    reason: {reason}");
                        issueCount++;
                    }
                }
            }
        }
        if (issueCount == 0) Console.WriteLine("  (none)");
        Console.WriteLine();

        Console.WriteLine("=== Check 2: cross-item conflicts ===");
        int crossIssues = 0;
        var slotToAxis = TextingStyleAggregator.SlotToSyntaxAxis;
        var contributions = new List<(string itemId, string slot, string axis, string value)>();
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Fragment) || string.IsNullOrEmpty(item.Slot)) continue;
            string axisVal;
            if (!slotToAxis.TryGetValue(item.Slot, out axisVal)) continue;
            var syntaxAxes = TextingStyleAggregator.ParseSyntaxAxes(item.Fragment);
            string value;
            if (syntaxAxes.TryGetValue(axisVal, out value) && !string.IsNullOrWhiteSpace(value))
                contributions.Add((item.Id ?? "(no id)", item.Slot, axisVal, value));
        }

        for (int i = 0; i < contributions.Count; i++)
        {
            for (int j = i + 1; j < contributions.Count; j++)
            {
                var a = contributions[i];
                var b = contributions[j];
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
        if (crossIssues == 0) Console.WriteLine("  (none)");
        Console.WriteLine();

        if (issueCount == 0)
        {
            Console.WriteLine("RESULT: OK — zero conflicts found in current dataset.");
            return 0;
        }
        Console.WriteLine($"RESULT: {issueCount} issue(s) found. See output above.");
        return 1;
    }

    private static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 12; i++)
        {
            if (File.Exists(Path.Combine(dir, "Pinder.Core.sln"))) return dir;
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
            ? root : root.GetProperty("items");
        var result = new List<ItemEntry>();
        foreach (var el in arrayEl.EnumerateArray())
        {
            string? id = el.TryGetProperty("id", out var idP) ? idP.GetString() : null;
            string? slot = el.TryGetProperty("slot", out var sP) ? sP.GetString() : null;
            string? fragment = el.TryGetProperty("texting_style_fragment", out var fP) ? fP.GetString() : null;
            result.Add(new ItemEntry { Id = id, Slot = slot, Fragment = fragment });
        }
        return result;
    }

    private class ItemEntry
    {
        public string? Id { get; set; }
        public string? Slot { get; set; }
        public string? Fragment { get; set; }
    }
}
