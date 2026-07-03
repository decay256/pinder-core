using Xunit;
using Pinder.Core.Characters;

namespace Pinder.Core.Tests
{
    public class Issue1299_ConflictTagsPriorityRemovedTests
    {
        [Fact]
        public void ItemDefinition_ConflictTagsProperty_DoesNotExist()
        {
            Assert.Null(typeof(ItemDefinition).GetProperty("ConflictTags"));
        }

        [Fact]
        public void ItemDefinition_PriorityProperty_DoesNotExist()
        {
            Assert.Null(typeof(ItemDefinition).GetProperty("Priority"));
        }
    }
}
