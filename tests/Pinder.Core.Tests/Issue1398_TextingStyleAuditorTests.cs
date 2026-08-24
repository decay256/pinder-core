using System.Text.Json;
using Pinder.Core.Prompts;
using Pinder.Tools.TextingStyleAuditor;
using Xunit;

namespace Pinder.Core.Tests;

public sealed class Issue1398_TextingStyleAuditorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pinder-auditor-" + Guid.NewGuid().ToString("N"));

    public Issue1398_TextingStyleAuditorTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void OptionalThirdArgumentAuditsMultiCandidateItemAndAnatomyCatalogsInProcess()
    {
        var result = RunAuditor(Item("Special", "SYNTAX:\n- emoji:\n  - may use one sparkle\n  - may use one dot"), Anatomy("trunkLengthBase", "EXPRESSION:\n- directness:\n  - may answer plainly\n  - may ask one direct question"));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("parameter=trunkLengthBase band=0", result.Output);
        Assert.Contains("loaded 1 anatomy parameter(s), 1 band(s)", result.Output);
    }

    [Fact]
    public void AuditorAcceptsEveryAxisOwnedByTheRuntimeTaxonomy()
    {
        var items = TextingStyleAggregator.SlotToSyntaxAxis
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                item_id = "item-" + group.Key,
                slot = group.First().Key,
                texting_style_fragment = $"SYNTAX:\n- {group.Key}: may express {group.Key}",
            })
            .ToArray();
        var anatomy = new[]
        {
            AnatomyParameter(TextingStyleAggregator.DirectnessGroup[0], "directness"),
            AnatomyParameter(TextingStyleAggregator.AffectGroup[0], "affect"),
            AnatomyParameter(TextingStyleAggregator.RhythmGroup[0], "rhythm"),
        };

        var result = RunAuditor(JsonSerializer.Serialize(items), JsonSerializer.Serialize(anatomy));

        Assert.Equal(0, result.ExitCode);
        foreach (string axis in TextingStyleTaxonomy.CanonicalAxes)
            Assert.Contains($"axis={axis}", result.Output);
    }

    [Theory]
    [InlineData("SYNTAX:\n- stance: may agree", "unknown axis 'stance'")]
    [InlineData("SYNTAX:\n- emoji:", "axis 'emoji' has no candidates")]
    [InlineData("SYNTAX:\n  - may agree", "unowned or empty candidate")]
    public void UnknownMalformedAndCandidateShapeItemFailuresBlock(string fragment, string expected)
    {
        var result = RunAuditor(Item("Special", fragment), Anatomy("trunkLengthBase", "EXPRESSION:\n- directness: may answer plainly"));
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expected, result.Output);
    }

    [Fact]
    public void ItemSlotOwnerMismatchIsBlocking()
    {
        var result = RunAuditor(Item("Special", "SYNTAX:\n- grammar: may omit punctuation"), Anatomy("trunkLengthBase", "EXPRESSION:\n- directness: may answer plainly"));
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("must use syntax axis 'emoji', not 'grammar'", result.Output);
    }

    [Fact]
    public void MixedValidAndLegacyItemSectionsAreBlocking()
    {
        var result = RunAuditor(
            Item("Special", "SYNTAX:\n- emoji: may use one sparkle\nTONE:\n- stance: may agree"),
            Anatomy("trunkLengthBase", "EXPRESSION:\n- directness: may answer plainly"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("item=test-item", result.Output);
        Assert.Contains("unsupported authored section 'TONE'", result.Output);
    }

    [Theory]
    [InlineData("EXPRESSION:\n- stance: may answer plainly", "unknown axis 'stance'")]
    [InlineData("EXPRESSION:\n- affect: may sound warm", "must use expression axis 'directness', not 'affect'")]
    [InlineData("EXPRESSION:\n- directness:", "axis 'directness' has no candidates")]
    public void AnatomyFailuresAreBlockingWithStableParameterAndBand(string fragment, string expected)
    {
        var result = RunAuditor(Item("Special", "SYNTAX:\n- emoji: may use one sparkle"), Anatomy("trunkLengthBase", fragment));
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("parameter=trunkLengthBase band=0", result.Output);
        Assert.Contains(expected, result.Output);
    }

    [Fact]
    public void MixedValidAndLegacyAnatomySectionsAreBlocking()
    {
        var result = RunAuditor(
            Item("Special", "SYNTAX:\n- emoji: may use one sparkle"),
            Anatomy(
                "trunkLengthBase",
                "EXPRESSION:\n- directness: may answer plainly\nTONE:\n- stance: may agree"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("parameter=trunkLengthBase band=0", result.Output);
        Assert.Contains("unsupported authored section 'TONE'", result.Output);
    }

    [Fact]
    public void ColonsInsideOrdinaryCandidateProseAreAccepted()
    {
        var result = RunAuditor(
            Item("Special", "SYNTAX:\n- emoji: may react: with one sparkle"),
            Anatomy(
                "trunkLengthBase",
                "EXPRESSION:\n- directness:\n  - may answer plainly: then ask one question"));

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void UnknownAnatomyOwnerWithoutStyleFragmentIsIgnored()
    {
        var result = RunAuditor(
            Item("Special", "SYNTAX:\n- emoji: may use one sparkle"),
            Anatomy("futureShape", ""));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("anatomy parameter with a texting-style fragment has no expression-axis owner", result.Output);
    }

    [Fact]
    public void UnknownAnatomyOwnerWithStyleFragmentIsBlocking()
    {
        var result = RunAuditor(Item("Special", "SYNTAX:\n- emoji: may use one sparkle"), Anatomy("futureShape", "EXPRESSION:\n- directness: may answer plainly"));
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("parameter=futureShape", result.Output);
        Assert.Contains("anatomy parameter with a texting-style fragment has no expression-axis owner", result.Output);
    }

    [Fact]
    public void MatrixCoveredConflictAcrossItemAndAnatomyIsInformational()
    {
        const string conflicts = "conflicts:\n" +
            "  - axis_a: { axis: emoji, value: \"may use one sparkle\" }\n" +
            "    axis_b: { axis: directness, value: \"may answer plainly\" }\n" +
            "    reason: \"opposed direction\"\n";
        var result = RunAuditor(Item("Special", "SYNTAX:\n- emoji: may use one sparkle"), Anatomy("trunkLengthBase", "EXPRESSION:\n- directness: may answer plainly"), conflicts);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("item=test-item <-> parameter=trunkLengthBase band=0", result.Output);
        Assert.Contains("matrix-covered conflict", result.Output);
        Assert.Contains("opposed direction", result.Output);
    }

    private AuditResult RunAuditor(string items, string anatomy, string conflicts = "conflicts: []\n")
    {
        string itemsPath = Write("items.json", items);
        string conflictsPath = Write("conflicts.yaml", conflicts);
        string anatomyPath = Write("anatomy.json", anatomy);
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = TextingStyleAuditorRunner.Run(new[] { itemsPath, conflictsPath, anatomyPath }, output, error);
        return new AuditResult(exitCode, output + error.ToString());
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Item(string slot, string fragment) => JsonSerializer.Serialize(new[] { new { item_id = "test-item", slot, texting_style_fragment = fragment } });
    private static string Anatomy(string parameterId, string fragment) => JsonSerializer.Serialize(new[] { new { id = parameterId, bands = new[] { new { lower = 0, upper = 1, texting_style_fragment = fragment } } } });
    private static object AnatomyParameter(string parameterId, string axis) => new
    {
        id = parameterId,
        bands = new[] { new { lower = 0, upper = 1, texting_style_fragment = $"EXPRESSION:\n- {axis}: may express {axis}" } },
    };
    public void Dispose() => Directory.Delete(_directory, recursive: true);
    private sealed record AuditResult(int ExitCode, string Output);
}
