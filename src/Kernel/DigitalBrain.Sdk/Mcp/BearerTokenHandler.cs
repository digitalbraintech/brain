using System.Net;
using System.Net.Http.Headers;

namespace DigitalBrain.Sdk;

internal sealed class BearerTokenHandler(McpEndpoint endpoint, Func<CancellationToken, Task<string>> accessToken)
    : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
{
    private ResponseBudget? _budget;

    internal void BeginOperation(ResponseBudget? budget) => _budget = budget;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null || !endpoint.Accepts(request.RequestUri))
        {
            throw new McpOperationException($"MCP request left the configured '{endpoint.Name}' endpoint; it was not sent.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await accessToken(cancellationToken).ConfigureAwait(false));
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            response.Dispose();
            throw new McpOperationException($"MCP server '{endpoint.Name}' redirected a request; redirects are forbidden.");
        }

        if (_budget is { } budget)
        {
            response.Content = new LimitedContent(response.Content, budget);
        }

        return response;
    }

    internal sealed class ResponseBudget(long limit)
    {
        private long _bytes;

        internal void Add(int bytes)
        {
            if (Interlocked.Add(ref _bytes, bytes) > limit)
            {
                throw new McpOperationException($"MCP response exceeded the {limit / 1024} KiB limit. Narrow the request.", McpFailureKind.ContentRejected);
            }
        }
    }

    private sealed class LimitedContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly ResponseBudget _budget;

        internal LimitedContent(HttpContent inner, ResponseBudget budget)
        {
            _inner = inner;
            _budget = budget;
            foreach (var header in inner.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            using var source = await CreateContentReadStreamAsync().ConfigureAwait(false);
            await source.CopyToAsync(stream).ConfigureAwait(false);
        }

        protected override async Task<Stream> CreateContentReadStreamAsync()
            => new LimitedStream(await _inner.ReadAsStreamAsync().ConfigureAwait(false), _budget);

        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => new LimitedStream(await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), _budget);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class LimitedStream(Stream inner, ResponseBudget budget) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            budget.Add(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            budget.Add(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            budget.Add(read);
            return read;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
