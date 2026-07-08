using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Core.Tests.Conversation;

public sealed class SendersTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("[scene]:Riley", "Riley")]
    [InlineData("[scene]:", "")]
    [InlineData("[scene]", "[scene]")]
    [InlineData("Riley", "Riley")]
    public void StripScenePrefix_PreservesDisplaySenderBehavior(
        string? sender,
        string expected)
    {
        Assert.Equal(expected, Senders.StripScenePrefix(sender));
    }
}
