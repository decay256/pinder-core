using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Pinder.RemoteAssets.Tests
{
    /// <summary>
    /// Test-only <see cref="HttpMessageHandler"/> driven by an enqueued
    /// list of canned responses + a delegate factory. Records every
    /// request the wrapper issued so test assertions can inspect
    /// URL, method, and headers.
    /// </summary>
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders =
            new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();

        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        /// <summary>
        /// Snapshots of each request's body bytes, captured BEFORE the
        /// production code disposes the <see cref="HttpRequestMessage"/>
        /// (which disposes the underlying <see cref="HttpContent"/>).
        /// Index-parallel to <see cref="Requests"/>. Entries for requests
        /// with no body are <c>null</c>.
        /// </summary>
        public List<byte[]?> RequestBodies { get; } = new List<byte[]?>();

        /// <summary>
        /// Snapshots of each request's Content-Type header (value
        /// including parameters like multipart boundary), captured BEFORE
        /// disposal. Index-parallel to <see cref="Requests"/>. Null for
        /// requests with no content.
        /// </summary>
        public List<string?> RequestContentTypes { get; } = new List<string?>();

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responders.Enqueue(responder);
        }

        public void Enqueue(HttpResponseMessage response)
        {
            _responders.Enqueue(_ => response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            // Capture body bytes + content-type NOW, before the
            // production code's `using` block disposes the
            // HttpRequestMessage. Required for multipart-write tests in
            // #855 that need to assert on the on-wire body.
            if (request.Content != null)
            {
                byte[] bodyBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                RequestBodies.Add(bodyBytes);
                RequestContentTypes.Add(request.Content.Headers.ContentType?.ToString());
            }
            else
            {
                RequestBodies.Add(null);
                RequestContentTypes.Add(null);
            }

            if (_responders.Count == 0)
                throw new InvalidOperationException(
                    $"FakeHttpMessageHandler received an unexpected request: {request.Method} {request.RequestUri}");
            var responder = _responders.Dequeue();
            var resp = responder(request);
            resp.RequestMessage = request;
            return resp;
        }
    }
}
