using System;
using Xunit;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Tests
{
    public class ExceptionToStringTests
    {
        [Fact]
        public void LlmTransportException_ToString_ContainsCustomDiagnosticParameters()
        {
            var inner = new Exception("network connection timed out");
            var exception = new LlmTransportException("Streaming failed", LlmFailureKind.Network, inner);

            var result = exception.ToString();

            Assert.Contains("[TransportFailureDetails]", result);
            Assert.Contains("FailureKind: Network", result);
            Assert.Contains("Streaming failed", result);
            Assert.Contains("network connection timed out", result);
        }
    }
}
