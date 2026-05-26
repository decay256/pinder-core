// Pinder Session Runner — real GameSession + PinderLlmAdapter
// Outputs markdown matching the session-001 playtest format
using System;
using System.Threading.Tasks;

partial class Program
{
    static async Task<int> Main(string[] args)
    {
        var setupResult = await SetupSessionAsync(args);
        if (setupResult.ShouldExit)
        {
            return setupResult.ExitCode;
        }

        var loopResult = await RunGameLoopAsync(setupResult, args);

        ReportAndShutdown(setupResult, loopResult);

        return 0;
    }
}
