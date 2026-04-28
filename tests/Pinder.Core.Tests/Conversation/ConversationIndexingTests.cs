using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Core.Tests.Conversation;

/// <summary>
/// Pin tests for <see cref="ConversationIndexing"/> — the helper that
/// reconciles physical <c>ConversationHistory</c> indices with logical
/// (turn, role) pairs in the presence of leading <c>[scene]</c> entries
/// (issue #333). These tests exist because two pinder-web consumers
/// shipped pair-math (<c>i / 2</c>, <c>i % 2 == 0</c>) that silently
/// misattributed turn numbers and text-diffs once scene entries were
/// seeded at the front of the history (review feedback on PR #350).
/// </summary>
public sealed class ConversationIndexingTests
{
    // ── helpers ─────────────────────────────────────────────────────────

    private static (string Sender, string Text) Scene(string text)
        => (Senders.Scene, text);

    private static (string Sender, string Text) Player(string text)
        => ("Reuben", text);

    private static (string Sender, string Text) Opp(string text)
        => ("Sable", text);

    // ── 0 scene entries (legacy / pre-#333 layout) ──────────────────────

    [Fact]
    public void NoScene_TurnNumberAt_MatchesLegacyPairMath()
    {
        // Legacy: (player, opp, player, opp, ...) → turns 1, 1, 2, 2, ...
        var history = new[]
        {
            Player("p1"), Opp("o1"),
            Player("p2"), Opp("o2"),
            Player("p3"), Opp("o3"),
        };

        Assert.Equal(1, ConversationIndexing.TurnNumberAt(history, 0));
        Assert.Equal(1, ConversationIndexing.TurnNumberAt(history, 1));
        Assert.Equal(2, ConversationIndexing.TurnNumberAt(history, 2));
        Assert.Equal(2, ConversationIndexing.TurnNumberAt(history, 3));
        Assert.Equal(3, ConversationIndexing.TurnNumberAt(history, 4));
        Assert.Equal(3, ConversationIndexing.TurnNumberAt(history, 5));
    }

    [Fact]
    public void NoScene_IsPlayerEntryAt_MatchesLegacyPairMath()
    {
        var history = new[]
        {
            Player("p1"), Opp("o1"),
            Player("p2"), Opp("o2"),
        };

        Assert.True(ConversationIndexing.IsPlayerEntryAt(history, 0));
        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 1));
        Assert.True(ConversationIndexing.IsPlayerEntryAt(history, 2));
        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 3));
    }

    // ── 1 scene entry ───────────────────────────────────────────────────

    [Fact]
    public void OneScene_TurnNumberAt_Skips()
    {
        // 1 scene + (player, opp, player, opp, ...).
        var history = new[]
        {
            Scene("player bio"),
            Player("p1"), Opp("o1"),
            Player("p2"), Opp("o2"),
        };

        Assert.Equal(0, ConversationIndexing.TurnNumberAt(history, 0));
        Assert.Equal(1, ConversationIndexing.TurnNumberAt(history, 1));
        Assert.Equal(1, ConversationIndexing.TurnNumberAt(history, 2));
        Assert.Equal(2, ConversationIndexing.TurnNumberAt(history, 3));
        Assert.Equal(2, ConversationIndexing.TurnNumberAt(history, 4));
    }

    [Fact]
    public void OneScene_IsPlayerEntryAt_Skips()
    {
        var history = new[]
        {
            Scene("player bio"),
            Player("p1"), Opp("o1"),
            Player("p2"), Opp("o2"),
        };

        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 0));   // scene
        Assert.True(ConversationIndexing.IsPlayerEntryAt(history, 1));    // p1
        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 2));   // o1
        Assert.True(ConversationIndexing.IsPlayerEntryAt(history, 3));    // p2
        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 4));   // o2
    }

    // ── 3 scene entries (full #333: bio + bio + outfit) ─────────────────

    [Fact]
    public void ThreeScene_TurnNumberAt_AllScenesReturnZero_PairMathRebasedOnFirstNonScene()
    {
        // 3 scene + 3 turns of (player, opp).
        var history = new[]
        {
            Scene("player bio"),
            Scene("opponent bio"),
            Scene("outfit description"),
            Player("p1"), Opp("o1"),
            Player("p2"), Opp("o2"),
            Player("p3"), Opp("o3"),
        };

        // Scene entries → turn 0
        Assert.Equal(0, ConversationIndexing.TurnNumberAt(history, 0));
        Assert.Equal(0, ConversationIndexing.TurnNumberAt(history, 1));
        Assert.Equal(0, ConversationIndexing.TurnNumberAt(history, 2));

        // Turn 1: indices 3+4
        Assert.Equal(1, ConversationIndexing.TurnNumberAt(history, 3));
        Assert.Equal(1, ConversationIndexing.TurnNumberAt(history, 4));

        // Turn 2: indices 5+6
        Assert.Equal(2, ConversationIndexing.TurnNumberAt(history, 5));
        Assert.Equal(2, ConversationIndexing.TurnNumberAt(history, 6));

        // Turn 3: indices 7+8
        Assert.Equal(3, ConversationIndexing.TurnNumberAt(history, 7));
        Assert.Equal(3, ConversationIndexing.TurnNumberAt(history, 8));
    }

    [Fact]
    public void ThreeScene_IsPlayerEntryAt_ScenesNeverPlayer_PairMathRebased()
    {
        var history = new[]
        {
            Scene("player bio"),
            Scene("opponent bio"),
            Scene("outfit description"),
            Player("p1"), Opp("o1"),
            Player("p2"), Opp("o2"),
        };

        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 0));  // scene
        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 1));  // scene
        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 2));  // scene
        Assert.True(ConversationIndexing.IsPlayerEntryAt(history, 3));   // p1
        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 4));  // o1
        Assert.True(ConversationIndexing.IsPlayerEntryAt(history, 5));   // p2
        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 6));  // o2
    }

    // ── 2 scene entries (test-override path: bio + bio, outfit skipped) ─

    [Fact]
    public void TwoScene_MatchesProductionTestOverridePath()
    {
        // The PlaybackLlmTransport / test-override path skips the outfit
        // describer, so a real test session has 2 scene entries up front.
        var history = new[]
        {
            Scene("player bio"),
            Scene("opponent bio"),
            Player("p1"), Opp("o1"),
        };

        Assert.Equal(0, ConversationIndexing.TurnNumberAt(history, 0));
        Assert.Equal(0, ConversationIndexing.TurnNumberAt(history, 1));
        Assert.Equal(1, ConversationIndexing.TurnNumberAt(history, 2));
        Assert.Equal(1, ConversationIndexing.TurnNumberAt(history, 3));

        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 0));
        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 1));
        Assert.True(ConversationIndexing.IsPlayerEntryAt(history, 2));
        Assert.False(ConversationIndexing.IsPlayerEntryAt(history, 3));
    }

    // ── EnumerateConversation streaming variant ─────────────────────────

    [Fact]
    public void EnumerateConversation_TagsEveryEntryWithItsLogicalRole()
    {
        var history = new[]
        {
            Scene("player bio"),
            Scene("opponent bio"),
            Player("p1"), Opp("o1"),
            Player("p2"), Opp("o2"),
        };

        var views = ConversationIndexing.EnumerateConversation(history).ToArray();

        Assert.Equal(6, views.Length);

        // Scene entries
        Assert.True(views[0].IsScene);
        Assert.Equal(0, views[0].TurnNumber);
        Assert.False(views[0].IsPlayerEntry);
        Assert.Equal(0, views[0].PhysicalIndex);

        Assert.True(views[1].IsScene);
        Assert.Equal(0, views[1].TurnNumber);
        Assert.False(views[1].IsPlayerEntry);

        // Turn 1 player
        Assert.False(views[2].IsScene);
        Assert.Equal(1, views[2].TurnNumber);
        Assert.True(views[2].IsPlayerEntry);
        Assert.Equal(2, views[2].PhysicalIndex);
        Assert.Equal("p1", views[2].Text);

        // Turn 1 opponent
        Assert.False(views[3].IsScene);
        Assert.Equal(1, views[3].TurnNumber);
        Assert.False(views[3].IsPlayerEntry);

        // Turn 2 player + opponent
        Assert.Equal(2, views[4].TurnNumber);
        Assert.True(views[4].IsPlayerEntry);
        Assert.Equal(2, views[5].TurnNumber);
        Assert.False(views[5].IsPlayerEntry);
    }

    [Fact]
    public void EnumerateConversation_EmptyHistory_ReturnsNoViews()
    {
        var history = System.Array.Empty<(string Sender, string Text)>();
        var views = ConversationIndexing.EnumerateConversation(history).ToArray();
        Assert.Empty(views);
    }

    [Fact]
    public void EnumerateConversation_ScenesOnly_AllZero()
    {
        var history = new[]
        {
            Scene("player bio"),
            Scene("opponent bio"),
            Scene("outfit description"),
        };

        var views = ConversationIndexing.EnumerateConversation(history).ToArray();
        Assert.Equal(3, views.Length);
        foreach (var v in views)
        {
            Assert.True(v.IsScene);
            Assert.Equal(0, v.TurnNumber);
            Assert.False(v.IsPlayerEntry);
        }
    }

    // ── IsSceneAt sanity ────────────────────────────────────────────────

    [Fact]
    public void IsSceneAt_DistinguishesSceneFromCharacterSenders()
    {
        var history = new[]
        {
            Scene("bio"),
            Player("hi"),
            Opp("hello"),
        };

        Assert.True(ConversationIndexing.IsSceneAt(history, 0));
        Assert.False(ConversationIndexing.IsSceneAt(history, 1));
        Assert.False(ConversationIndexing.IsSceneAt(history, 2));
    }
}
