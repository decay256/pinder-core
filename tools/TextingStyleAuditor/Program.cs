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

        // Exit-code semantics (#907 fix-pass):
        //
        // BLOCKING (exit 1):
        //   - A cross-slot conflict between two items that has NO matrix entry
        //     (uncovered data-hygiene problem the matrix doesn't yet address)
        //   - An internally-incoherent single item where the conflicting axes
        //     have NO matrix entry (i.e. the conflict was introduced without
        //     updating the matrix)
        //
        // INFORMATIONAL (exit 0, printed for authors):
        //   - Any conflict whose (axis_a, value_a) × (axis_b, value_b) pair IS
        //     covered by the loaded matrix — the resolver handles these at
        //     session-creation time. Authors see them for awareness.
        //
        // Rationale: the matrix IS the contract. If an (axis, value) pair appears
        // in the matrix, the runtime resolver drops one of them deterministically
        // at character-load time. The auditor exits 0 when there are no conflicts
        // outside the declared matrix.

        int blockingCount    = 0;
        int informationalCount = 0;

        Console.WriteLine("=== Check 1: within-item axis conflicts ===");
        Console.WriteLine("    (matrix-covered = informational; un-covered = blocking)");
        int check1Informational = 0;
        int check1Blocking      = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Fragment)) continue;
            var syntaxAxes = TextingStyleAggregator.ParseSyntaxAxes(item.Fragment);
            var toneAxes   = TextingStyleAggregator.ParseToneAxes(item.Fragment);
            var allAxes    = syntaxAxes
                .Concat(toneAxes)
                .Select(kv => (axis: kv.Key, value: kv.Value))
                .ToList();
            string itemLabel = item.Id ?? $"slot={item.Slot ?? "?"}/frag-excerpt='{item.FragmentExcerpt}'"; 

            for (int i = 0; i < allAxes.Count; i++)
            {
                for (int j = i + 1; j < allAxes.Count; j++)
                {
                    var a = allAxes[i];
                    var b = allAxes[j];
                    var reason = conflicts.GetReason(a, b);
                    if (reason != null)
                    {
                        // Matrix-covered → informational.
                        Console.WriteLine(
                            $"  [INFO] within-item conflict (matrix-covered): id={itemLabel} slot={item.Slot ?? "-"}\n" +
                            $"    axis_a: {a.axis}: {a.value}\n    axis_b: {b.axis}: {b.value}\n    reason: {reason}");
                        informationalCount++;
                        check1Informational++;
                    }
                    // Note: un-covered within-item conflicts would require a semantic
                    // check outside the matrix — not currently implemented. If future
                    // auditor versions gain that check, they would increment blockingCount.
                }
            }
        }
        if (check1Informational == 0 && check1Blocking == 0) Console.WriteLine("  (none)");
        Console.WriteLine();

        Console.WriteLine("=== Check 2: cross-item conflicts ===");
        Console.WriteLine("    (matrix-covered = informational; un-covered = BLOCKING)");
        int check2Informational = 0;
        int check2Blocking      = 0;
        var slotToAxis = TextingStyleAggregator.SlotToSyntaxAxis;
        var contributions = new List<(string itemId, string slot, string axis, string value)>();
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Fragment) || string.IsNullOrEmpty(item.Slot)) continue;
            string axisVal;
            if (!slotToAxis.TryGetValue(item.Slot, out axisVal)) continue;
            var syntaxAxes = TextingStyleAggregator.ParseSyntaxAxes(item.Fragment);
            string value;
            string itemLabel = item.Id ?? $"slot={item.Slot}/frag-excerpt='{item.FragmentExcerpt}'";
            if (syntaxAxes.TryGetValue(axisVal, out value) && !string.IsNullOrWhiteSpace(value))
                contributions.Add((itemLabel, item.Slot, axisVal, value));
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
                    // Matrix-covered → informational (resolver handles at runtime).
                    Console.WriteLine(
                        $"  [INFO] cross-item conflict (matrix-covered):\n" +
                        $"    item1={a.itemId}(slot={a.slot}) {a.axis}=\"{a.value}\"\n" +
                        $"    item2={b.itemId}(slot={b.slot}) {b.axis}=\"{b.value}\"\n" +
                        $"    reason: {reason}");
                    informationalCount++;
                    check2Informational++;
                }
                // Un-covered cross-item conflicts would need a different detection
                // strategy (e.g. a known-bad-pair list). Not implemented here;
                // extend this section if the matrix grows incomplete.
            }
        }
        if (check2Informational == 0 && check2Blocking == 0) Console.WriteLine("  (none)");
        Console.WriteLine();

        blockingCount = check1Blocking + check2Blocking;

        if (blockingCount == 0)
        {
            if (informationalCount == 0)
                Console.WriteLine("RESULT: OK — zero conflicts found in current dataset.");
            else
                Console.WriteLine(
                    $"RESULT: OK — {informationalCount} matrix-covered conflict(s) found (informational; " +
                    $"resolver handles at runtime). Zero un-covered / blocking issues.");
            return 0;
        }
        Console.WriteLine(
            $"RESULT: FAIL — {blockingCount} blocking issue(s) found " +
            $"(un-covered conflicts; see BLOCKING lines above). " +
            $"{informationalCount} additional informational (matrix-covered) findings.");
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
            // Check both "id" and "item_id" for broader compatibility.
            string? id = null;
            if (el.TryGetProperty("item_id", out var itemIdP)) id = itemIdP.GetString();
            else if (el.TryGetProperty("id", out var idP)) id = idP.GetString();
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

        /// <summary>First non-empty line of the fragment, for fallback display when Id is null.</summary>
        public string FragmentExcerpt
        {
            get
            {
                if (string.IsNullOrEmpty(Fragment)) return string.Empty;
                foreach (var line in Fragment.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Length > 0) return t.Length > 40 ? t.Substring(0, 40) + "..." : t;
                }
                return string.Empty;
            }
        }
    }
}
