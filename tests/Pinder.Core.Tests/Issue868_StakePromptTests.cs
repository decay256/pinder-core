using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    public class Issue868_StakePromptTests
    {
        private static string PromptsRoot
            => TestRepoLocator.FindRepoSubdir("data", "prompts");

        [Fact]
        public void Stake_UserTemplate_ContainsAll15Stems()
        {
            string[] expectedStems = {
                "1. The most humiliating thing that happened to me this week was when…",
                "2. The thing about my body I'm convinced everyone notices but actually no one does is…",
                "3. My last sexual accident or mishap was when I…",
                "4. The kink I've never said out loud to anyone is…",
                "5. The substance I leaned on harder than I should have last month was … and I used it to …",
                "6. The most embarrassing impulse purchase on my last bank statement is…",
                "7. If you opened my browser history at 3am last Tuesday you'd find…",
                "8. The last lie I told on a dating profile or in a chat was…",
                "9. The most undignified thing my body did in public recently was…",
                "10. The thing I do alone in my apartment that I'd be humiliated to be filmed doing is…",
                "11. The single object in my bedroom I could not explain to a stranger is…",
                "12. The last time I cried and where it happened was…",
                "13. My last named ex was [name] and the specific reason it ended was…",
                "14. The lowest professional moment of the last year was when I…",
                "15. The thing I genuinely believe will happen to me in the next two years that everyone else would call delusional is…"
            };

            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);
            string userMessage = LlmStakeGenerator.BuildUserMessage("TEST PROFILE", catalog);
            
            foreach (var stem in expectedStems)
            {
                Assert.Contains(stem, userMessage);
            }
        }

        [Fact]
        public void Stake_SystemPrompt_ContainsSentinel()
        {
            string sentinel = "sentence-completion engine for a comedy hookup-app simulator";
            
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);
            var entry = catalog.TryGet("stake");
            
            Assert.NotNull(entry);
            Assert.Contains(sentinel, entry!.SystemPrompt);
        }
    }
}
