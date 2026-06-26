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
using System.Text;
using System.Linq;
using System.Buffers;
using System.Net.Http;
using System.Threading;
using QuantConnect.Util;
using System.Globalization;
using QuantConnect.Logging;
using System.Threading.Tasks;
using QuantConnect.Configuration;
using System.Collections.Concurrent;

namespace QuantConnect.Brokerages.TradeStation.Api;

/// <summary>
/// A small wrapper around <see cref="HttpClient"/> that provides retry-on-timeout logic
/// and attempt-scoped timeouts. This wrapper intentionally manages per-attempt timeouts
/// itself (the internal HttpClient.Timeout is set to <see cref="Timeout.InfiniteTimeSpan"/>).
/// </summary>
public class HttpClientRetryWrapper : IDisposable
{
    /// <summary>
    /// The underlying <see cref="HttpClient"/> used to send requests.
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// The base URL used for constructing API endpoints.
    /// </summary>
    private readonly string _baseUrl;

    /// <summary>
    /// Maximum number of retry attempts. The logic will attempt attempts 0.._maxRetries (inclusive)
    /// and throw if the number of attempts exceeds this value.
    /// </summary>
    private readonly int _maxRetries = 3;

    /// <summary>
    /// Per-attempt timeout. Each attempt creates its own <see cref="CancellationTokenSource"/>
    /// that is canceled after this timespan.
    /// </summary>
    private readonly TimeSpan _ctsAttemptTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Delay between retries (back-off multiplier is applied in the loop).
    /// </summary>
    private readonly TimeSpan _backOffDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The 5-minute rolling window used both as the upper bound on a single <c>429 Too Many Requests</c>
    /// back-off (guarding against unexpectedly large or epoch-style <c>X-RateLimit-Reset</c>/<c>Retry-After</c>
    /// values) and as the proactive quota window for the general and quote-snapshot resource groups, which
    /// share TradeStation's largest documented window. Defaults to 5 minutes; configurable via the
    /// <c>trade-station-rate-limit-max-backoff-seconds</c> setting.
    /// </summary>
    private static readonly TimeSpan _maxRateLimitDelay = TimeSpan.FromSeconds(Config.GetInt("trade-station-rate-limit-max-backoff-seconds", 300));

    // Proactive throttles (token buckets) keep REST traffic under TradeStation's documented quotas so a
    // 429 is avoided in the first place. Quotas are per rolling window and configurable via the
    // trade-station-rate-limit-* settings. Streaming endpoints do not consume request quota (no gate).
    // See https://api.tradestation.com/docs/fundamentals/rate-limiting/.

    /// <summary>
    /// Throttles keyed by resource group. The fixed groups (general 320 / 5 min, quote 500 / 5 min) are
    /// seeded here; per-option-endpoint gates (90 / 1 min each) are added lazily in <see cref="GetRateGate"/>
    /// since their key is only known per request.
    /// </summary>
    private readonly ConcurrentDictionary<string, RateGate> _rateGates = new()
    {
        ["general"] = new RateGate(Config.GetInt("trade-station-rate-limit-general-requests", 320), _maxRateLimitDelay),
        ["quote"] = new RateGate(Config.GetInt("trade-station-rate-limit-quote-requests", 500), _maxRateLimitDelay),
    };

    /// <summary>
    /// Number of requests allowed per <see cref="_optionWindow"/> for each individual option endpoint.
    /// </summary>
    private readonly int _optionQuota = Config.GetInt("trade-station-rate-limit-option-requests", 90);

    /// <summary>
    /// Rolling window for each option endpoint's request quota.
    /// </summary>
    private readonly TimeSpan _optionWindow = TimeSpan.FromSeconds(Config.GetInt("trade-station-rate-limit-option-window-seconds", 60));

    /// <summary>
    /// Initializes a new instance of <see cref="HttpClientRetryWrapper"/>.
    /// This constructor is for production use and creates a <see cref="TokenRefreshHandler"/>
    /// on top of the provided <see cref="HttpMessageHandler"/>.
    /// </summary>
    /// <param name="baseUrl">Base URL for requests (no trailing slash required).</param>
    /// <param name="clientId">OAuth client id (for token refresh handler).</param>
    /// <param name="clientSecret">OAuth client secret (for token refresh handler).</param>
    /// <param name="authorizationCodeFromUrl">Authorization code (for token refresh handler).</param>
    /// <param name="redirectUri">Redirect URI (for token refresh handler).</param>
    /// <param name="refreshToken">Refresh token (for token refresh handler).</param>
    public HttpClientRetryWrapper(string baseUrl, string clientId, string clientSecret, string authorizationCodeFromUrl, string redirectUri, string refreshToken)
    {
        _baseUrl = baseUrl.TrimEnd('/');

        var httpClientHandler = new HttpClientHandler();
        var signInUri = "https://signin.tradestation.com";
        var tokenRefreshHandler = new TokenRefreshHandler(httpClientHandler, clientId, clientSecret, authorizationCodeFromUrl, signInUri, redirectUri, refreshToken);

        _httpClient = new(tokenRefreshHandler)
        {
            // Important: Avoid HttpClient's internal timeout and manage per-attempt timeouts manually.
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    /// <summary>
    /// Internal constructor that allows injection of a custom <see cref="HttpMessageHandler"/>
    /// and tuning of retry/timeouts for unit testing.
    /// </summary>
    /// <param name="baseUrl">Base URL for requests.</param>
    /// <param name="handler">Message handler to use for HttpClient.</param>
    /// <param name="maxRetries">Maximum number of retries.</param>
    /// <param name="ctsAttemptTimeout">Per-attempt timeout.</param>
    /// <param name="backOffDelay">Back-off delay between attempts.</param>
    internal HttpClientRetryWrapper(
        string baseUrl,
        HttpMessageHandler handler,
        int maxRetries,
        TimeSpan ctsAttemptTimeout,
        TimeSpan backOffDelay)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _maxRetries = maxRetries;
        _ctsAttemptTimeout = ctsAttemptTimeout;
        _backOffDelay = backOffDelay;

        _httpClient = new HttpClient(handler ?? new HttpClientHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    /// <summary>
    /// Sends a GET request and returns the resulting <see cref="HttpResponseMessage"/> while
    /// streaming the response content (calls <see cref="HttpCompletionOption.ResponseHeadersRead"/>).
    /// </summary>
    /// <param name="resource">Relative resource path (appended to <see cref="_baseUrl"/>).</param>
    /// <param name="cancellationToken">Cancellation token provided by the caller to cancel the overall operation.</param>
    /// <returns>The HTTP response message.</returns>
    public async Task<HttpResponseMessage> GetStreamAsync(string resource, CancellationToken cancellationToken)
    {
        return await SendAsync(resource, HttpMethod.Get, jsonBody: null, retryOnTimeout: true, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>
    /// Sends an HTTP request with retry logic for per-attempt timeouts. The method honors the cancellation contract:
    /// if <paramref name="externalCancellationToken"/> is canceled, the method will rethrow <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="resource">Relative resource path (appended to <see cref="_baseUrl"/>).</param>
    /// <param name="httpMethod">HTTP method to use (GET/POST/PUT/etc.).</param>
    /// <param name="jsonBody">Optional JSON body to send (will be serialized as UTF-8 application/json).</param>
    /// <param name="retryOnTimeout">If true, the method will retry when an attempt times out (default: true).</param>
    /// <param name="httpCompletionOption">Completion option to pass to <see cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption, CancellationToken)"/>.</param>
    /// <param name="externalCancellationToken">Cancellation token to cancel the entire operation from the caller side.</param>
    /// <returns>The successful <see cref="HttpResponseMessage"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller-supplied <paramref name="externalCancellationToken"/> is canceled.</exception>
    public async Task<HttpResponseMessage> SendAsync(
        string resource,
        HttpMethod httpMethod,
        string jsonBody,
        bool retryOnTimeout,
        HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, // default in <see cref="HttpClient.DefaultCompletionOption"/>
        CancellationToken externalCancellationToken = default)
    {
        // Proactive throttle: keep this resource group under TradeStation's documented quota so we avoid
        // tripping a 429 in the first place. Streaming endpoints do not consume request quota (null gate).
        var rateGate = GetRateGate(resource);

        for (var attempt = 0; ; attempt++)
        {
            WaitToProceed(rateGate, externalCancellationToken);

            using var requestMessage = new HttpRequestMessage(httpMethod, $"{_baseUrl}{resource}");
            using var timeoutCts = new CancellationTokenSource(_ctsAttemptTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken, timeoutCts.Token);
            if (jsonBody != null)
            {
                requestMessage.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(requestMessage, httpCompletionOption, linkedCts.Token);
            }
            catch (TaskCanceledException tce) when (!externalCancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                LogError(nameof(SendAsync), tce, requestMessage.Method.Method, requestMessage.RequestUri.ToString(), $"attempt={attempt}/{_maxRetries}");
                if (!retryOnTimeout || attempt >= _maxRetries)
                {
                    throw;
                }
                await Task.Delay(_backOffDelay, externalCancellationToken);
                continue;
            }
            catch (Exception ex)
            {
                LogError(nameof(SendAsync), ex, requestMessage.Method.Method, requestMessage.RequestUri.ToString());
                throw;
            }

            // Reactive rate-limit back-off: a 429 is not a hard failure. This is a safety net behind the
            // proactive throttle above (the per-account quota can still be tripped by other sessions, window
            // drift, or replays). Honor the server's X-RateLimit-Reset (falling back to Retry-After, then the
            // default back-off) and retry within the retry budget so a transient quota hit does not fail the operation.
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < _maxRetries)
            {
                var retryDelay = GetRateLimitRetryDelay(response);
                Log.Trace($"{nameof(HttpClientRetryWrapper)}.{nameof(SendAsync)}: rate limited (429) on [{requestMessage.Method.Method}]({requestMessage.RequestUri}), " +
                    $"backing off {retryDelay.TotalSeconds:F1}s before retry (attempt={attempt}/{_maxRetries}).");
                response.Dispose();
                await Task.Delay(retryDelay, externalCancellationToken);
                continue;
            }

            return response;
        }
    }

    /// <summary>
    /// Blocks until the supplied <paramref name="rateGate"/> allows another request, honoring cancellation.
    /// A <c>null</c> gate (streaming endpoints) returns immediately.
    /// </summary>
    /// <param name="rateGate">The throttle to wait on, or <c>null</c> to skip throttling.</param>
    /// <param name="cancellationToken">Token used to abort the wait.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled while waiting.</exception>
    private static void WaitToProceed(RateGate rateGate, CancellationToken cancellationToken)
    {
        if (rateGate == null)
        {
            return;
        }

        // Poll with a short timeout so a canceled token can abort the wait instead of blocking for the
        // full quota window (RateGate.WaitToProceed has no cancellation-aware overload).
        while (!rateGate.WaitToProceed(TimeSpan.FromMilliseconds(250)))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// Resolves the proactive throttle for the given <paramref name="resource"/> based on its TradeStation
    /// quota group, creating it lazily. Returns <c>null</c> for streaming endpoints, which do not consume
    /// request quota.
    /// </summary>
    /// <param name="resource">Relative resource path being requested.</param>
    /// <returns>The <see cref="RateGate"/> guarding the resource's quota group, or <c>null</c> when unthrottled.</returns>
    private RateGate GetRateGate(string resource)
    {
        if (resource.Contains("/stream/", StringComparison.OrdinalIgnoreCase))
        {
            // Streaming endpoints do not consume request quota.
            return null;
        }

        if (resource.Contains("/marketdata/options/", StringComparison.OrdinalIgnoreCase))
        {
            // Each option endpoint has its own quota and its key is only known per request, so create it
            // lazily. The static factory avoids allocating a closure on the (common) already-present path.
            return _rateGates.GetOrAdd($"option:{GetOptionEndpointName(resource)}",
                static (_, quota) => new RateGate(quota.Occurrences, quota.Window), (Occurrences: _optionQuota, Window: _optionWindow));
        }

        if (resource.Contains("/marketdata/quotes/", StringComparison.OrdinalIgnoreCase))
        {
            return _rateGates["quote"];
        }

        return _rateGates["general"];
    }

    /// <summary>
    /// Delimiters that terminate the option endpoint segment within a resource path.
    /// </summary>
    private static readonly SearchValues<char> _optionEndpointDelimiters = SearchValues.Create("/?");

    /// <summary>
    /// Extracts the option endpoint name (the path segment following <c>/options/</c>) used to give each
    /// option endpoint its own throttle.
    /// </summary>
    /// <param name="resource">Relative resource path containing <c>/options/</c>.</param>
    /// <returns>The endpoint segment (e.g. <c>expirations</c> or <c>strikes</c>).</returns>
    private static string GetOptionEndpointName(string resource)
    {
        const string marker = "/options/";
        var start = resource.IndexOf(marker, StringComparison.OrdinalIgnoreCase) + marker.Length;
        var rest = resource.AsSpan(start);
        var end = rest.IndexOfAny(_optionEndpointDelimiters);
        return (end >= 0 ? rest[..end] : rest).ToString();
    }

    /// <summary>
    /// Computes how long to wait before retrying a <c>429 Too Many Requests</c> response, preferring the
    /// <c>X-RateLimit-Reset</c> header (seconds until the quota window resets), then <c>Retry-After</c>,
    /// then the default back-off. The result is clamped to <see cref="_maxRateLimitDelay"/>.
    /// </summary>
    /// <param name="response">The rate-limited HTTP response.</param>
    /// <returns>The delay to wait before the next attempt.</returns>
    private TimeSpan GetRateLimitRetryDelay(HttpResponseMessage response)
    {
        var delay = _backOffDelay;

        if (TryGetSecondsHeader(response, "X-RateLimit-Reset", out var resetDelay))
        {
            delay = resetDelay;
        }
        else if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfterDelta)
        {
            delay = retryAfterDelta;
        }
        else if (TryGetSecondsHeader(response, "Retry-After", out var retryAfterSeconds))
        {
            delay = retryAfterSeconds;
        }

        if (delay < TimeSpan.Zero)
        {
            delay = _backOffDelay;
        }

        return delay > _maxRateLimitDelay ? _maxRateLimitDelay : delay;
    }

    /// <summary>
    /// Attempts to read the named response header and interpret its value as a number of seconds.
    /// </summary>
    /// <param name="response">The HTTP response to read from.</param>
    /// <param name="headerName">The header name to look up.</param>
    /// <param name="delay">The parsed delay, when the header is present and numeric.</param>
    /// <returns><c>true</c> when the header was present and parsed; otherwise <c>false</c>.</returns>
    private static bool TryGetSecondsHeader(HttpResponseMessage response, string headerName, out TimeSpan delay)
    {
        delay = default;
        if (response.Headers.TryGetValues(headerName, out var values))
        {
            var raw = values.FirstOrDefault();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                delay = TimeSpan.FromSeconds(seconds);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Logs error details. This method centralizes logging and can be adapted to your logging framework.
    /// </summary>
    /// <param name="method">The method in which the error occurred.</param>
    /// <param name="exception">The exception caught.</param>
    /// <param name="httpMethod">HTTP method of the request.</param>
    /// <param name="requestUri">Request URI string.</param>
    /// <param name="message">Optional message (defaults to "empty").</param>
    private static void LogError(string method, Exception exception, string httpMethod, string requestUri, string message = "")
    {
        Log.Error($"{nameof(HttpClientRetryWrapper)}.{method}.{exception.GetType().Name}: message = {message}. Request: [{httpMethod}]({requestUri}).\n{exception}");
    }

    /// <summary>
    /// Disposes the underlying <see cref="HttpClient"/> and the proactive rate-limit gates.
    /// </summary>
    public void Dispose()
    {
        _httpClient?.DisposeSafely();
        foreach (var rateGate in _rateGates.Values)
        {
            rateGate.DisposeSafely();
        }
    }
}
