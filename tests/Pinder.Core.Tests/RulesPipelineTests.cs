using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Pinder.Core.Tests;

/// <summary>
/// Runs the Python rules pipeline to catch rules drift automatically.
/// These tests shell out to a working Python 3 interpreter with pyyaml on PATH.
/// </summary>
[Trait("Category", "Rules")]
public class RulesPipelineTests
{
    private readonly ITestOutputHelper _output;
    private static readonly Lazy<PythonCommand> Python = new(ResolvePython);

    private sealed class PythonCommand
    {
        public PythonCommand(string fileName, params string[] prefixArguments)
        {
            FileName = fileName;
            PrefixArguments = prefixArguments;
        }

        public string FileName { get; }
        public string[] PrefixArguments { get; }
    }

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

    private static PythonCommand ResolvePython()
    {
        PythonCommand[] candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                new PythonCommand("python"),
                new PythonCommand("py", "-3"),
                new PythonCommand("python3"),
            }
            : new[]
            {
                new PythonCommand("python3"),
                new PythonCommand("python"),
            };

        foreach (var candidate in candidates)
        {
            try
            {
                var psi = CreatePythonStartInfo(candidate, Directory.GetCurrentDirectory());
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add("import sys; raise SystemExit(0 if sys.version_info.major == 3 else 1)");

                using var process = Process.Start(psi);
                if (process == null)
                    continue;

                if (!process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                    continue;
                }

                if (process.ExitCode == 0)
                    return candidate;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Try the next conventional Python launcher.
            }
        }

        throw new InvalidOperationException(
            "Cannot find a working Python 3 interpreter. Tried python, python3, and the Windows py -3 launcher.");
    }

    private static ProcessStartInfo CreatePythonStartInfo(PythonCommand python, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = python.FileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in python.PrefixArguments)
            psi.ArgumentList.Add(argument);

        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        return psi;
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunPythonAsync(
        string script,
        string[] arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        var psi = CreatePythonStartInfo(Python.Value, workingDirectory);
        psi.ArgumentList.Add(script);
        foreach (string argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)!;
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = new CancellationTokenSource(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between timeout observation and the kill.
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            throw new TimeoutException(
                $"Python script '{Path.GetFileName(script)}' timed out after {timeout.TotalSeconds:0.###}s");
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        return (process.ExitCode, stdout, stderr);
    }

    private Task<(int exitCode, string stdout, string stderr)> RunPipelineAsync(
        string command,
        int timeoutSeconds = 120)
    {
        var repoRoot = FindRepoRoot();
        var script = Path.Combine(repoRoot, "rules", "tools", "rules_pipeline.py");
        return RunPythonAsync(
            script,
            new[] { command },
            repoRoot,
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    [Fact]
    public async Task RoundTripCheck_ExitCodeZero_DiffUnder30()
    {
        var (exitCode, stdout, stderr) = await RunPipelineAsync("check");
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

    /// <summary>
    /// Issue #1041 (Tier C): Verify that generate_tests.py exists and that
    /// RulesSpecTests.cs is up to date with the enriched YAML source.
    /// Runs <c>rules/tools/generate_tests.py --check</c> with the resolved Python 3 interpreter.
    /// </summary>
    [Fact]
    public async Task CodegenCheck_GenerateTestsScript_IsUpToDate()
    {
        var repoRoot = FindRepoRoot();
        var generateScript = Path.Combine(repoRoot, "rules", "tools", "generate_tests.py");
        Assert.True(File.Exists(generateScript),
            $"generate_tests.py not found at expected location: {generateScript}");

        var (exitCode, stdout, stderr) =
            await RunPythonAsync(
                generateScript,
                new[] { "--check" },
                repoRoot,
                TimeSpan.FromSeconds(30));

        _output.WriteLine(stdout);
        if (!string.IsNullOrEmpty(stderr))
            _output.WriteLine($"STDERR: {stderr}");

        Assert.True(exitCode == 0,
            $"generate_tests.py --check exited {exitCode}. " +
            $"RulesSpecTests.cs may be out of date with the YAML source.\n" +
            $"Re-run rules/tools/generate_tests.py with Python 3.\nStdErr:\n{stderr}");
    }

    [Fact]
    public async Task CheckDiff_LlmClassifiesAsFormattingOnly()
    {
        // First check if there are any diffs at all
        var (checkExit, checkOut, _) = await RunPipelineAsync("check");
        var diffMatch = Regex.Match(checkOut, @"Content diff lines:\s+(\d+)");
        if (diffMatch.Success && int.Parse(diffMatch.Groups[1].Value) == 0)
        {
            _output.WriteLine("No diffs found — skipping LLM classification.");
            return;
        }

        // Run LLM-based diff classification
        var (exitCode, stdout, stderr) =
            await RunPipelineAsync("check-diff", timeoutSeconds: 90);
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

    [Fact]
    public async Task RunPython_ChattyHangingChild_TimesOutAndKillsProcessTree()
    {
        string tempDir = Directory.CreateTempSubdirectory("rules-python-timeout-").FullName;
        string script = Path.Combine(tempDir, "chatty_hang.py");
        try
        {
            File.WriteAllText(
                script,
                "import sys, time\n" +
                "for _ in range(2048):\n" +
                "    sys.stdout.write('o' * 1024)\n" +
                "    sys.stdout.flush()\n" +
                "    sys.stderr.write('e' * 1024)\n" +
                "    sys.stderr.flush()\n" +
                "time.sleep(5)\n");

            var stopwatch = Stopwatch.StartNew();
            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => RunPythonAsync(
                    script,
                    Array.Empty<string>(),
                    tempDir,
                    TimeSpan.FromMilliseconds(300)));
            stopwatch.Stop();

            Assert.Contains("chatty_hang.py", exception.Message);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(3),
                $"Timed-out child was not terminated promptly; elapsed {stopwatch.Elapsed}.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
