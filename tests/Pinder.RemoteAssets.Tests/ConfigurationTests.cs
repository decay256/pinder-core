using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Pinder.RemoteAssets;

namespace Pinder.RemoteAssets.Tests
{
    public class ConfigurationTests
    {
        private readonly HttpMessageHandler _handler = new FakeHttpMessageHandler();
        private readonly Func<CancellationToken, Task<string>> _auth = _ => Task.FromResult("token");
        private readonly CharacterPayloadParser _parser = _ => null;

        [Fact]
        public void Configuration_HttpsBaseUrl_Succeeds()
        {
            var uri = new Uri("https://example.com");
            var config = new Configuration(uri, _handler, _auth, _parser);
            Assert.Equal(uri, config.BaseUrl);
        }

        [Fact]
        public void Configuration_HttpBaseUrl_ThrowsArgumentException()
        {
            var uri = new Uri("http://example.com");
            var ex = Assert.Throws<ArgumentException>(() => new Configuration(uri, _handler, _auth, _parser));
            Assert.Contains("HTTPS", ex.Message);
            Assert.Contains("http", ex.Message);
        }

        [Fact]
        public void Configuration_TestOptOut_AllowsHttpBaseUrl()
        {
            var uri = new Uri("http://example.com");
            var config = new Configuration(uri, _handler, _auth, _parser, allowInsecureBaseUrl: true);
            Assert.Equal(uri, config.BaseUrl);
            Assert.Equal("http", config.BaseUrl.Scheme);
        }
    }
}
