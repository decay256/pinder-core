using Xunit;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Tests
{
    public class PublicApiDeadSurfaceTests
    {
        [Fact]
        public void FailurePoolInterface_IsNotARegisteredCoreExtensionPoint()
        {
            var coreAssembly = typeof(IDiceRoller).Assembly;

            Assert.Null(coreAssembly.GetType("Pinder.Core.Interfaces.IFailurePool", throwOnError: false));
        }
    }
}
