using System.Text.Json;
using Pinder.Core.Prompts;
using Pinder.LlmAdapters;

namespace Pinder.Tools.TextingStyleAuditor;

public static class TextingStyleAuditorRunner
{
    private static readonly HashSet<string> SyntaxAxes = new(StringComparer.OrdinalIgnoreCase)
    {
        "emoji", "shorthand", "grammar", "structure", "length", "tics",
    };

    private static readonly HashSet<string> ExpressionAxes = new(StringComparer.OrdinalIgnoreCase)
    {
        "directness", "affect", "rhythm",
    };

    private static readonly IReadOnlyDictionary<string, string> AnatomyOwners = BuildAnatomyOwners();

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        string repoRoot = FindRepoRoot();
        string itemsPath = args.Length > 0 ? args[0] : Path.Combine(repoRoot, "data", "items", "starter-items.json");
        string conflictsPath = args.Length > 1 ? args[1] : Path.Combine(repoRoot, "data", "persona", "texting-style-conflicts.yaml");
        string anatomyPath = args.Length > 2 ? args[2] : Path.Combine(repoRoot, "data", "anatomy", "anatomy-parameters.json");

        foreach (var input in new[] { (Name: "items", Path: itemsPath), (Name: "conflicts", Path: conflictsPath), (Name: "anatomy", Path: anatomyPath) })
        {
            if (!File.Exists(input.Path))
            {
                stderr.WriteLine($"ERROR: {input.Name} file not found: {input.Path}");
                return 2;
            }
        }

        stdout.WriteLine($"[TextingStyleAuditor] items: {itemsPath}");
        stdout.WriteLine($"[TextingStyleAuditor] conflicts: {conflictsPath}");
        stdout.WriteLine($"[TextingStyleAuditor] anatomy: {anatomyPath}");

        TextingStyleConflicts conflicts;
        try
        {
            conflicts = TextingStyleConflictYamlLoader.LoadFrom(File.ReadAllText(conflictsPath));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
        {
            stderr.WriteLine($"ERROR loading conflicts YAML: {ex.Message}");
            return 2;
        }

        var findings = new List<Finding>();
        var contributions = new List<Contribution>();
        int itemCount;
        int anatomyParameterCount;
        int anatomyBandCount;
        try
        {
            itemCount = AuditItems(File.ReadAllText(itemsPath), findings, contributions);
            (anatomyParameterCount, anatomyBandCount) = AuditAnatomy(File.ReadAllText(anatomyPath), findings, contributions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            stderr.WriteLine($"ERROR loading catalog JSON: {ex.Message}");
            return 2;
        }

        stdout.WriteLine($"[TextingStyleAuditor] loaded {conflicts.Entries.Count} conflict entries");
        stdout.WriteLine($"[TextingStyleAuditor] loaded {itemCount} item(s)");
        stdout.WriteLine($"[TextingStyleAuditor] loaded {anatomyParameterCount} anatomy parameter(s), {anatomyBandCount} band(s)");
        foreach (var group in contributions.GroupBy(entry => (entry.Location, entry.Axis)))
            stdout.WriteLine($"[TextingStyleAuditor] validated {group.Key.Location} axis={group.Key.Axis} candidates={group.Count()}");

        AddConflictFindings(conflicts, contributions, findings);
        foreach (var finding in findings)
            stdout.WriteLine($"[{finding.Level}] {finding.Location}: {finding.Message}");

        int blockingCount = findings.Count(finding => finding.Level == "BLOCKING");
        int informationalCount = findings.Count(finding => finding.Level == "INFO");
        if (blockingCount > 0)
        {
            stdout.WriteLine($"RESULT: FAIL - {blockingCount} blocking issue(s); {informationalCount} informational finding(s).");
            return 1;
        }

        stdout.WriteLine($"RESULT: OK - zero blocking issues; {informationalCount} matrix-covered conflict(s) informational.");
        return 0;
    }

    private static int AuditItems(string json, ICollection<Finding> findings, ICollection<Contribution> contributions)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement items = GetArray(document.RootElement, "items", "items catalog");
        int count = 0;
        foreach (JsonElement item in items.EnumerateArray())
        {
            int index = count++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                findings.Add(Blocking($"item_index={index}", "candidate shape must be an object"));
                continue;
            }

            string id = ReadString(item, "item_id") ?? ReadString(item, "id") ?? $"index-{index}";
            string location = $"item={id}";
            string? fragment = ReadString(item, "texting_style_fragment");
            if (string.IsNullOrWhiteSpace(fragment)) continue;

            string? slot = ReadString(item, "slot");
            if (string.IsNullOrWhiteSpace(slot) || !TextingStyleAggregator.SlotToSyntaxAxis.TryGetValue(slot, out string? ownerAxis))
            {
                findings.Add(Blocking(location, $"slot '{slot ?? "<missing>"}' has no syntax-axis owner"));
                continue;
            }

            ParseFragment(fragment, location, "SYNTAX", SyntaxAxes, ownerAxis, findings, contributions);
        }
        return count;
    }

    private static (int Parameters, int Bands) AuditAnatomy(string json, ICollection<Finding> findings, ICollection<Contribution> contributions)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement parameters = GetArray(document.RootElement, "parameters", "anatomy catalog");
        int parameterCount = 0;
        int bandCount = 0;
        foreach (JsonElement parameter in parameters.EnumerateArray())
        {
            int parameterIndex = parameterCount++;
            if (parameter.ValueKind != JsonValueKind.Object)
            {
                findings.Add(Blocking($"parameter_index={parameterIndex}", "candidate shape must be an object"));
                continue;
            }

            string id = ReadString(parameter, "id") ?? $"index-{parameterIndex}";
            AnatomyOwners.TryGetValue(id, out string? ownerAxis);

            if (!parameter.TryGetProperty("bands", out JsonElement bands) || bands.ValueKind != JsonValueKind.Array)
            {
                findings.Add(Blocking($"parameter={id}", "candidate shape requires a bands array"));
                continue;
            }

            int bandIndex = 0;
            bool reportedMissingOwner = false;
            foreach (JsonElement band in bands.EnumerateArray())
            {
                string location = $"parameter={id} band={bandIndex}";
                bandCount++;
                bandIndex++;
                if (band.ValueKind != JsonValueKind.Object)
                {
                    findings.Add(Blocking(location, "candidate shape must be an object"));
                    continue;
                }

                string? fragment = ReadString(band, "texting_style_fragment");
                if (string.IsNullOrWhiteSpace(fragment))
                    continue;

                if (ownerAxis == null)
                {
                    if (!reportedMissingOwner)
                    {
                        findings.Add(Blocking(
                            $"parameter={id}",
                            "anatomy parameter with a texting-style fragment has no expression-axis owner"));
                        reportedMissingOwner = true;
                    }
                    continue;
                }

                ParseFragment(fragment, location, "EXPRESSION", ExpressionAxes, ownerAxis, findings, contributions);
            }
        }
        return (parameterCount, bandCount);
    }

    private static void ParseFragment(string fragment, string location, string requiredSection, ISet<string> validAxes, string ownerAxis, ICollection<Finding> findings, ICollection<Contribution> contributions)
    {
        var candidates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        bool inSection = false;
        bool sawSection = false;
        string? currentAxis = null;

        foreach (string rawLine in fragment.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (TryGetAuthoredSectionHeader(line, out string? section))
            {
                inSection = string.Equals(section, requiredSection, StringComparison.Ordinal);
                if (inSection)
                    sawSection = true;
                else
                    findings.Add(Blocking(location, $"unsupported authored section '{section}'"));
                currentAxis = null;
                continue;
            }
            if (!inSection) continue;

            int indentation = rawLine.TakeWhile(char.IsWhiteSpace).Count();
            if (!line.StartsWith("-", StringComparison.Ordinal))
            {
                findings.Add(Blocking(location, $"candidate shape is malformed near '{line}'"));
                currentAxis = null;
                continue;
            }

            string bullet = line[1..].Trim();
            if (indentation > 0)
            {
                if (currentAxis == null || bullet.Length == 0)
                    findings.Add(Blocking(location, "candidate shape contains an unowned or empty candidate"));
                else
                    candidates[currentAxis].Add(bullet);
                continue;
            }

            int colon = bullet.IndexOf(':');
            if (colon <= 0)
            {
                findings.Add(Blocking(location, $"candidate shape is malformed near '{line}'"));
                currentAxis = null;
                continue;
            }

            string axisToken = bullet[..colon].Trim();
            int parenthesis = axisToken.IndexOf('(');
            if (parenthesis > 0) axisToken = axisToken[..parenthesis].Trim();
            if (!validAxes.Contains(axisToken))
            {
                findings.Add(Blocking(location, $"unknown axis '{axisToken}'"));
                currentAxis = null;
                continue;
            }

            currentAxis = axisToken;
            if (!candidates.TryAdd(currentAxis, new List<string>()))
                findings.Add(Blocking(location, $"axis '{currentAxis}' is declared more than once"));
            string inlineCandidate = bullet[(colon + 1)..].Trim();
            if (inlineCandidate.Length > 0) candidates[currentAxis].Add(inlineCandidate);
        }

        if (!sawSection) findings.Add(Blocking(location, $"candidate shape requires a {requiredSection}: section"));
        foreach (var pair in candidates)
        {
            if (!string.Equals(pair.Key, ownerAxis, StringComparison.OrdinalIgnoreCase))
                findings.Add(Blocking(location, $"must use {requiredSection.ToLowerInvariant()} axis '{ownerAxis}', not '{pair.Key}'"));
            if (pair.Value.Count == 0) findings.Add(Blocking(location, $"axis '{pair.Key}' has no candidates"));
            foreach (string candidate in pair.Value)
                contributions.Add(new Contribution(location, pair.Key.ToLowerInvariant(), candidate));
        }
        if (!candidates.ContainsKey(ownerAxis))
            findings.Add(Blocking(location, $"must use {requiredSection.ToLowerInvariant()} axis '{ownerAxis}'"));
    }

    private static bool TryGetAuthoredSectionHeader(string line, out string? section)
    {
        section = null;
        if (!line.EndsWith(':') || line.StartsWith("-", StringComparison.Ordinal))
            return false;

        string candidate = line[..^1].Trim();
        if (candidate.Length == 0 ||
            candidate.Any(character => !char.IsUpper(character) && character is not ' ' and not '_'))
        {
            return false;
        }

        section = candidate;
        return true;
    }

    private static void AddConflictFindings(TextingStyleConflicts conflicts, IReadOnlyList<Contribution> contributions, ICollection<Finding> findings)
    {
        for (int leftIndex = 0; leftIndex < contributions.Count; leftIndex++)
        {
            Contribution left = contributions[leftIndex];
            for (int rightIndex = leftIndex + 1; rightIndex < contributions.Count; rightIndex++)
            {
                Contribution right = contributions[rightIndex];
                string? reason = conflicts.GetReason((left.Axis, left.Value), (right.Axis, right.Value));
                if (reason != null)
                    findings.Add(new Finding("INFO", $"{left.Location} <-> {right.Location}", $"matrix-covered conflict: {left.Axis}='{left.Value}' versus {right.Axis}='{right.Value}'; reason: {reason}"));
            }
        }
    }

    private static JsonElement GetArray(JsonElement root, string propertyName, string label)
    {
        JsonElement candidate = root;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out JsonElement property)) candidate = property;
        if (candidate.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{label} root must be an array or contain an '{propertyName}' array");
        return candidate;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property)) return null;
        if (property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (property.ValueKind != JsonValueKind.String) throw new InvalidDataException($"property '{propertyName}' must be a string");
        return property.GetString();
    }

    private static IReadOnlyDictionary<string, string> BuildAnatomyOwners()
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddOwners(TextingStyleAggregator.DirectnessGroup, "directness");
        AddOwners(TextingStyleAggregator.AffectGroup, "affect");
        AddOwners(TextingStyleAggregator.RhythmGroup, "rhythm");
        return owners;

        void AddOwners(IEnumerable<string> parameters, string axis)
        {
            foreach (string parameter in parameters) owners.Add(parameter, axis);
        }
    }

    private static string FindRepoRoot()
    {
        string directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 12; depth++)
        {
            if (File.Exists(Path.Combine(directory, "Pinder.Core.sln"))) return directory;
            string? parent = Directory.GetParent(directory)?.FullName;
            if (parent == null) break;
            directory = parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static Finding Blocking(string location, string message) => new("BLOCKING", location, message);
    private sealed record Contribution(string Location, string Axis, string Value);
    private sealed record Finding(string Level, string Location, string Message);
}
