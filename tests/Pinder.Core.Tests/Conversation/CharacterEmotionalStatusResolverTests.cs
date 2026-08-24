using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.Core.Tests.Conversation;

public sealed class CharacterEmotionalStatusResolverTests
{
    [Fact]
    public void Resolve_ZeroOverridesUseCharactersOwnCharmAndRizz()
    {
        CharacterProfile character = MakeProfile(7);

        CharacterEmotionalStatus status = CharacterEmotionalStatusResolver.Resolve(character, 0, 0);

        Assert.Equal(7, status.HungerForIntimacy);
        Assert.Equal(7, status.TerrorOfRejection);
    }

    [Fact]
    public void Resolve_NonZeroOverridesReplaceCharacterStats()
    {
        CharacterEmotionalStatus status = CharacterEmotionalStatusResolver.Resolve(MakeProfile(7), 4, 13);

        Assert.Equal(4, status.HungerForIntimacy);
        Assert.Equal(13, status.TerrorOfRejection);
    }

    private static CharacterProfile MakeProfile(int stats) => new(
        TestHelpers.MakeStatBlock(stats),
        "system prompt",
        "Alex",
        new TimingProfile(5, 1.0f, 0.0f, "neutral"),
        level: 1);
}
