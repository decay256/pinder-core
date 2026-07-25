using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public partial class EngineInjectionBlockTests
    {
        [Fact]
        public void Issue1333_Interest15_OptionsLabelAndDateeNarrativeAreCanonical()
        {
            var optionsPrompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(currentInterest: 15));
            Assert.Contains("Interest: 15/25", optionsPrompt);
            Assert.Contains("Interested", optionsPrompt);
            Assert.DoesNotContain("Very Into It", optionsPrompt);

            var dateePrompt = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 14, interestAfter: 15));
            Assert.Contains("Sable is at Interest 15/25", dateePrompt);
            Assert.Contains("Engaged but not sold", dateePrompt);
            Assert.Contains("Resistance level: Unstable agreement", dateePrompt);
            Assert.DoesNotContain("Interested but holding back", dateePrompt);
        }
    }
}
