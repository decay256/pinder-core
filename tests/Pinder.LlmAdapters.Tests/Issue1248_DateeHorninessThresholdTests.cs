using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests;

public class Issue1248_DateeHorninessThresholdTests
{
    private static DateeContext MakeDateeContext(
        int interestAfter, 
        bool horninessOverlayApplied = false, 
        FailureTier horninessTier = FailureTier.Success)
    {
        return new DateeContext(
            dateePrompt: "datee prompt",
            conversationHistory: new List<(string, string)> { ("P", "hey"), ("O", "hi") },
            dateeLastMessage: "hi",
            activeTraps: Array.Empty<string>(),
            currentInterest: interestAfter,
            playerDeliveredMessage: "wow, you look hot",
            interestBefore: interestAfter,
            interestAfter: interestAfter,
            responseDelayMinutes: 2.0,
            playerName: "P",
            dateeName: "O",
            horninessOverlayApplied: horninessOverlayApplied,
            horninessTier: horninessTier);
    }

    private bool ContainsAny(string text, params string[] options)
    {
        foreach (var opt in options)
        {
            if (text.Contains(opt, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    [Fact]
    public void Threshold_Constant_Is18()
    {
        Assert.Equal(18, SessionDocumentBuilder.HorninessWarmthThreshold);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(17)]
    public void HorninessOverlayApplied_BelowThreshold_GuidanceIsGuardedNegative(int interest)
    {
        var ctx = MakeDateeContext(interest, horninessOverlayApplied: true, horninessTier: FailureTier.Misfire);
        var result = SessionDocumentBuilder.BuildDateePrompt(ctx);

        // The implementer must include "horny" or "too eager"/"too forward" in the prompt text.
        Assert.True(ContainsAny(result, "horny", "too eager", "too forward"), 
            "Should contain a recognizable horniness-reaction header/marker.");

        // The below-threshold block must contain one of these guarded-direction cues.
        Assert.True(ContainsAny(result, "guarded", "cooler", "awkward", "does not land", "not charming"),
            "Should contain guarded/negative direction language.");

        // Must still reaffirm the 25 gate
        Assert.Contains("25", result);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(20)]
    [InlineData(24)]
    public void HorninessOverlayApplied_AtOrAboveThreshold_GuidanceAllowsWarmth(int interest)
    {
        var ctx = MakeDateeContext(interest, horninessOverlayApplied: true, horninessTier: FailureTier.Success);
        var result = SessionDocumentBuilder.BuildDateePrompt(ctx);

        Assert.True(ContainsAny(result, "horny", "too eager", "too forward"),
            "Should contain a recognizable horniness-reaction header/marker.");

        // The at/above threshold block must contain one of these warmth-permitting cues.
        Assert.True(ContainsAny(result, "may", "can", "mutual", "warmly", "flirt"),
            "Should contain warmth-permitting direction.");

        // Must still reaffirm the 25 gate
        Assert.Contains("25", result);
    }

    [Fact]
    public void HorninessOverlayApplied_HighInterest_StillGatedAt25()
    {
        var ctx = MakeDateeContext(24, horninessOverlayApplied: true, horninessTier: FailureTier.Success);
        var result = SessionDocumentBuilder.BuildDateePrompt(ctx);

        // The existing prompt already includes "Below Interest 25" rule.
        Assert.Contains("Below Interest 25", result);
        
        // Assert it does NOT say the date is secured
        Assert.DoesNotContain("date is secured", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secured at " + 24, result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("25", result);
    }

    [Fact]
    public void HorninessOverlayApplied_BoundaryAt17vs18()
    {
        var block17 = SessionDocumentBuilder.GetHorninessReactionGuidance(17, true, FailureTier.Success);
        Assert.True(ContainsAny(block17, "guarded", "cooler", "awkward", "does not land", "not charming"));
        Assert.False(ContainsAny(block17, "mutual", "warmly", "flirt back"));

        var block18 = SessionDocumentBuilder.GetHorninessReactionGuidance(18, true, FailureTier.Success);
        Assert.True(ContainsAny(block18, "may", "can", "mutual", "warmly", "flirt"));
    }

    [Fact]
    public void NoHorninessOverlay_NoHorninessBlock()
    {
        var ctx = MakeDateeContext(20, horninessOverlayApplied: false);
        var result = SessionDocumentBuilder.BuildDateePrompt(ctx);

        // The rest of the prompt (resistance block etc.) is unchanged, and horniness reaction is absent
        Assert.False(ContainsAny(result, "horny", "too eager", "too forward"));
    }
}
