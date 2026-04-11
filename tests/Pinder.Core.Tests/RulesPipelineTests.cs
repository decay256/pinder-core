using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Pinder.Core.Tests;

/// <summary>
/// Runs the Python rules pipeline to catch rules drift automatically.
/// These tests shell out to python3, so they require python3 + pyyaml on PATH.
/// </summary>
[Trait("Category", "Rules")]
public class RulesPipelineTests
{
    private readonly ITestOutputHelper _output;

    public RulesPipelineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FindRepoRoot()
    {
        // Try environment variable first
        var envRoot = Environment.GetEnvironmentVariable("PINDER_REPO_ROOT");
        if (!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot))
            return envRoot;

        // Walk up from test assembly location
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "rules", "tools", "rules_pipeline.py")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        // Fallback: common workspace path
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".openclaw", "workspace", "pinder-core");
        if (Directory.Exists(fallback))
            return fallback;

        throw new InvalidOperationException(
            "Cannot find pinder-core repo root. Set PINDER_REPO_ROOT env var.");
    }

    private (int exitCode, string stdout, string stderr) RunPipeline(string command, int timeoutSeconds = 120)
    {
        var repoRoot = FindRepoRoot();
        var script = Path.Combine(repoRoot, "rules", "tools", "rules_pipeline.py");

        var psi = new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = $"{script} {command}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            process.Kill();
            throw new TimeoutException($"Pipeline command '{command}' timed out after {timeoutSeconds}s");
        }

        return (process.ExitCode, stdout, stderr);
    }

    [Fact]
    public void RoundTripCheck_ExitCodeZero_DiffUnder30()
    {
        var (exitCode, stdout, stderr) = RunPipeline("check");
        _output.WriteLine(stdout);
        if (!string.IsNullOrEmpty(stderr))
            _output.WriteLine($"STDERR: {stderr}");

        Assert.Equal(0, exitCode);

        // Extract "Content diff lines: N" from output
        var match = Regex.Match(stdout, @"Content diff lines:\s+(\d+)");
        Assert.True(match.Success, $"Could not find 'Content diff lines' in output:\n{stdout}");

        var diffCount = int.Parse(match.Groups[1].Value);
        Assert.True(diffCount < 30,
            $"Round-trip diff is {diffCount} lines (threshold: 30). Output:\n{stdout}");
    }

    [Fact]
    public void CheckDiff_LlmClassifiesAsFormattingOnly()
    {
        // First check if there are any diffs at all
        var (checkExit, checkOut, _) = RunPipeline("check");
        var diffMatch = Regex.Match(checkOut, @"Content diff lines:\s+(\d+)");
        if (diffMatch.Success && int.Parse(diffMatch.Groups[1].Value) == 0)
        {
            _output.WriteLine("No diffs found — skipping LLM classification.");
            return;
        }

        // Run LLM-based diff classification
        var (exitCode, stdout, stderr) = RunPipeline("check-diff", timeoutSeconds: 90);
        var verdict = stdout.Trim();
        _output.WriteLine($"check-diff verdict: {verdict}");

        if (verdict.StartsWith("SKIP:"))
        {
            _output.WriteLine($"Skipping LLM check: {verdict}");
            return; // Graceful skip — no API key or curl issue
        }

        Assert.Equal(0, exitCode);
        Assert.True(verdict == "FORMATTING_ONLY",
            $"LLM detected content loss in round-trip diff:\n{verdict}");
    }
}
