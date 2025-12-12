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
using System.Net;
using NUnit.Framework;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
