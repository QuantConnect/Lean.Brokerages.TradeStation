/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.IO;
using System.Net;
using System.Text;
using NUnit.Framework;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using QuantConnect.Configuration;
using QuantConnect.Brokerages.TradeStation.Api;

namespace QuantConnect.Brokerages.TradeStation.Tests;

[TestFixture]
public class TradeStationBrokerageHttpClientRetryWrapperTests
{
    private readonly string _baseUrl = "https://api.test/";

    [Test]
    public async Task SendAsyncSuccessfulResponseReturnsResponse()
    {
        var handler = new TestHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            };
            return Task.FromResult(response);
        });

        using var wrapper = new HttpClientRetryWrapper(_baseUrl, handler, 5, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));

        var response = await wrapper.SendAsync("/resource", HttpMethod.Get, jsonBody: null, retryOnTimeout: true, externalCancellationToken: CancellationToken.None);

        Assert.IsNotNull(response);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Test]
    public async Task StreamOrdersSuccessfulResponseReturnsHeartbeat()
    {
        var apiClient = TradeStationBrokerageAdditionalTests.CreateTradeStationApiClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        var heartbeatResponseCounter = default(int);
        await foreach (var response in apiClient.StreamOrders(cts.Token))
        {
            Logging.Log.Trace(response);

            if (response.Contains("Heartbeat", StringComparison.InvariantCultureIgnoreCase))
            {
                heartbeatResponseCounter += 1;

                if (heartbeatResponseCounter >= 10)
                {
                    cts.Cancel();
                }
            }
        }

        Assert.IsTrue(cts.IsCancellationRequested);
        Assert.GreaterOrEqual(heartbeatResponseCounter, 2);
    }

    [Test]
    public void StreamRequestThrowsTimeoutExceptionWhenStreamSilentlyHangs()
    {
        // Regression test for issue #94: a silently-dropped SSE connection used to leave ReadLineAsync
        // blocked indefinitely with no exception, so the reconnect logic never fired and all subsequent
        // order fills were lost. The stream must now surface a TimeoutException after a period of silence.
        var previousTimeout = Config.Get("trade-station-stream-read-timeout-seconds");
        Config.Set("trade-station-stream-read-timeout-seconds", "1");
        try
        {
            var handler = new TestHttpMessageHandler((req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    // Emit a single heartbeat line and then hang forever, mimicking a NAT/half-close drop.
                    Content = new StreamContent(new HeartbeatThenHangStream("{\"Heartbeat\":1}"))
                };
                return Task.FromResult(response);
            });

            using var wrapper = new HttpClientRetryWrapper(
                _baseUrl, handler, maxRetries: 1, ctsAttemptTimeout: TimeSpan.FromSeconds(30), backOffDelay: TimeSpan.Zero);
            using var apiClient = new TradeStationApiClient(wrapper, accountId: "123");

            Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await foreach (var _ in apiClient.StreamOrders(CancellationToken.None))
                {
                }
            });
        }
        finally
        {
            Config.Set("trade-station-stream-read-timeout-seconds", previousTimeout);
        }
    }

    [Test]
    public void SendAsyncCallerCancellationThrowsTaskCanceledException()
    {
        var handler = new TestHttpMessageHandler((req, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var wrapper = new HttpClientRetryWrapper(
            _baseUrl,
            handler,
            maxRetries: 1,
            ctsAttemptTimeout: TimeSpan.FromSeconds(1),
            backOffDelay: TimeSpan.Zero);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await wrapper.SendAsync("/resource", HttpMethod.Get, jsonBody: null, retryOnTimeout: true, externalCancellationToken: cts.Token);
        });
    }

    [Test]
    public void SendAsyncAttemptTimeoutsRetriesAndThrowsWhenMaxRetriesReached()
    {
        var handler = new TestHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var wrapper = new HttpClientRetryWrapper(
            _baseUrl,
            handler,
            maxRetries: 2,
            ctsAttemptTimeout: TimeSpan.Zero,
            backOffDelay: TimeSpan.Zero);

        Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await wrapper.SendAsync("/resource", HttpMethod.Get, jsonBody: null, retryOnTimeout: true, externalCancellationToken: CancellationToken.None);
        });
    }

    [TestCase(true, 5, 6)]
    [TestCase(false, 5, 1)]
    public void SendAsyncRetryOnTimeoutFlagControlsRetries(bool retryOnTimeout, int maxRetries, int expectedCallSendAsyncCounter)
    {
        // Arrange
        var callCount = 0;

        var handler = new TestHttpMessageHandler((req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Complete the task as canceled when the token is canceled (simulates per-attempt timeout).
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        });

        using var wrapper = new HttpClientRetryWrapper(
            _baseUrl,
            handler,
            maxRetries: maxRetries,
            ctsAttemptTimeout: TimeSpan.Zero,
            backOffDelay: TimeSpan.Zero);

        Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await wrapper.SendAsync(
                resource: "/resource",
                httpMethod: HttpMethod.Get,
                jsonBody: null,
                retryOnTimeout: retryOnTimeout,
                externalCancellationToken: CancellationToken.None);
        });

        Assert.AreEqual(expectedCallSendAsyncCounter, callCount, $"retryOnTimeout={retryOnTimeout}: expected {expectedCallSendAsyncCounter} handler calls but saw {callCount}");
    }
}

/// <summary>
/// Minimal HttpMessageHandler used in tests that returns a preconfigured response
/// or observes the incoming cancellation token.
/// </summary>
public class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

    public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        _sendAsync = sendAsync;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _sendAsync(request, cancellationToken);
    }
}

/// <summary>
/// A read-only stream that returns a single chunk of data once and then blocks forever (honoring the
/// read cancellation token), simulating an SSE connection that emits one heartbeat and is then silently
/// dropped without a TCP RST.
/// </summary>
public class HeartbeatThenHangStream : Stream
{
    private readonly byte[] _firstChunk;
    private int _position;

    public HeartbeatThenHangStream(string firstLine)
    {
        _firstChunk = Encoding.UTF8.GetBytes(firstLine + "\n");
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < _firstChunk.Length)
        {
            var count = Math.Min(buffer.Length, _firstChunk.Length - _position);
            _firstChunk.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            return count;
        }

        // Block until the caller's per-read inactivity timeout cancels the read.
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
