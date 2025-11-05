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
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Collections.Generic;
using QuantConnect.Brokerages.TradeStation.Models;

namespace QuantConnect.Brokerages.TradeStation.Api;

/// <summary>
/// Handles token refresh logic for making authenticated requests to TradeStation API.
/// </summary>
/// <remarks>
/// This class inherits from <see cref="DelegatingHandler"/> and is responsible for
/// refreshing authentication tokens and handling retries for authenticated requests.
/// </remarks>
public class TokenRefreshHandler : DelegatingHandler
{
    /// <summary>
    /// Represents the number of retry attempts made for an authenticated request.
    /// </summary>
    private int _retryCount = 0;

    /// <summary>
    /// Represents the maximum number of retry attempts for an authenticated request.
    /// </summary>
    private int _maxRetryCount = 3;

    /// <summary>
    /// Represents the time interval between retry attempts for an authenticated request.
    /// </summary>
    private TimeSpan _retryInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Represents the base URL for signing in and obtaining authentication tokens.
    /// </summary>
    private readonly string _baseSignInUrl;

    /// <summary>
    /// Represents the API Key used by the client application to authenticate requests.
    /// </summary>
    /// <remarks>
    /// In the documentation, this API Key is referred to as <c>client_id</c>.
    /// </remarks>
    private readonly string _clientId;

    /// <summary>
    /// Represents the secret associated with the client application’s API Key for authentication.
    /// </summary>
    /// <remarks>
    /// In the documentation, this API Key is referred to as <c>client_secret</c>.
    /// </remarks>
    private readonly string _clientSecret;

    /// <summary>
    /// Represents the authorization code obtained from the URL during OAuth authentication.
    /// </summary>
    private readonly string _authorizationCodeFromUrl;

    /// <summary>
    /// Represents the URI to which the user will be redirected after authentication.
    /// </summary>
    private readonly string _redirectUri;

    /// <summary>
    /// Represents an object storing AccessToken and TradeStationAccessToken information
    /// for TradeStation authentication.
    /// </summary>
    private TradeStationAccessToken _tradeStationAccessToken;

    /// <summary>
    /// Represents the refresh token used to obtain a new access token when the current one expires.
    /// </summary>
    private string _refreshToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenRefreshHandler"/> class with the specified parameters.
    /// </summary>
    /// <param name="innerHandler">The inner HTTP message handler.</param>
    /// <param name="clientId">The API Key used by the client application.</param>
    /// <param name="clientSecret">The secret associated with the client application’s API Key.</param>
    /// <param name="authorizationCodeFromUrl">The authorization code obtained during OAuth authentication.</param>
    /// <param name="baseSignInUrl">The base URL for signing in and obtaining authentication tokens.</param>
    /// <param name="redirectUri">The URI to which the user will be redirected after authentication.</param>
    /// <param name="refreshToken">Optional. The refresh token used to obtain a new access token when the current one expires.
    /// If provided explicitly through configuration, it can expedite development processes by avoiding constant requests for new refresh tokens.
    /// If omitted, the handler may automatically determine the need for refreshing based on the presence of the authorization code obtained from the URL during OAuth authentication.</param>
    public TokenRefreshHandler(HttpMessageHandler innerHandler, string clientId, string clientSecret, string authorizationCodeFromUrl, string baseSignInUrl,
        string redirectUri, string refreshToken) : base(innerHandler)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _redirectUri = redirectUri;
        _authorizationCodeFromUrl = authorizationCodeFromUrl;
        _baseSignInUrl = baseSignInUrl;
        _refreshToken = refreshToken;
    }

    /// <summary>
    /// Sends an HTTP request asynchronously and handles retries for certain status codes.
    /// </summary>
    /// <param name="request">The HTTP request message to send.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains an HTTP response message.
    /// </returns>
    /// <remarks>
    /// This method overrides the base implementation to send an HTTP request asynchronously.
    /// It handles retries for certain status codes such as unauthorized (401).
    /// If the initial request returns an unauthorized status code and valid authentication tokens are available,
    /// this method attempts to refresh the access token and resend the request with updated authentication headers.
    /// </remarks>
    protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = null;

        for (_retryCount = 0; _retryCount < _maxRetryCount; _retryCount++)
        {
            if (_tradeStationAccessToken != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(_tradeStationAccessToken.TokenType, _tradeStationAccessToken.AccessToken);
            }

            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    break;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    if (_tradeStationAccessToken == null && string.IsNullOrEmpty(_refreshToken))
                    {
                        _tradeStationAccessToken = await GetAuthenticateToken(cancellationToken);
                        _refreshToken = _tradeStationAccessToken.RefreshToken;
                    }
                    else
                    {
                        _tradeStationAccessToken = await RefreshAccessToken(_refreshToken, cancellationToken);
                    }
                }
                else
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                // This means either HttpClient timeout or user cancellation
                Logging.Log.Error($"{nameof(TokenRefreshHandler)}.{nameof(SendAsync)}.{nameof(TaskCanceledException)}: {ex}. " +
                    $"Request: {request.Method} {request.RequestUri}, attempt {_retryCount + 1}/{_maxRetryCount}.");

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
            }

            await Task.Delay(_retryInterval, cancellationToken);
        }

        return response;
    }


    /// <summary>
    /// Retrieves the authentication token from TradeStation API.
    /// </summary>
    /// <returns>The authentication token containing access, refresh, and ID tokens along with expiration time.</returns>
    private async Task<TradeStationAccessToken> GetAuthenticateToken(CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "client_id", _clientId },
            { "client_secret", _clientSecret },
            { "code", _authorizationCodeFromUrl },
            { "redirect_uri", _redirectUri },
        };

        var response = await SendSignInAsync(new FormUrlEncodedContent(parameters), cancellationToken);

        return JsonConvert.DeserializeObject<TradeStationAccessToken>(response);
    }

    /// <summary>
    /// Refreshes the authentication token using the refresh token from TradeStation API.
    /// </summary>
    /// <param name="refreshToken">The refresh token obtained from TradeStation API.</param>
    /// <returns>The refreshed authentication token containing access, refresh, and ID tokens along with expiration time.</returns>
    private async Task<TradeStationAccessToken> RefreshAccessToken(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new ArgumentException($"{nameof(TradeStationApiClient)}.{nameof(RefreshAccessToken)}:" +
                $"The refresh token provided is null or empty. Please ensure a valid refresh token is provided.");
        }

        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "client_id", _clientId },
            { "refresh_token", refreshToken }
        };

        // The secret for the client application’s API Key. Required for standard Auth Code Flow. Not required for Auth Code Flow with PKCE.
        // https://api.tradestation.com/docs/fundamentals/authentication/refresh-tokens
        if (!string.IsNullOrEmpty(_clientSecret))
        {
            parameters["client_secret"] = _clientSecret;
        }

        var response = await SendSignInAsync(new FormUrlEncodedContent(parameters), cancellationToken);

        return JsonConvert.DeserializeObject<TradeStationAccessToken>(response);
    }

    /// <summary>
    /// Sends a sign-in request asynchronously to the specified URL endpoint using POST method.
    /// </summary>
    /// <param name="content">The content to be sent in the request body.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a string representing the response content.
    /// </returns>
    /// <remarks>
    /// This method sends a sign-in request asynchronously using POST method to the specified URL endpoint.
    /// It expects a FormUrlEncodedContent object containing the necessary data for sign-in.
    /// </remarks>
    private async Task<string> SendSignInAsync(FormUrlEncodedContent content, CancellationToken cancellationToken = default)
    {
        using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_baseSignInUrl}/oauth/token"))
        {
            requestMessage.Content = content;

            try
            {
                var responseMessage = await base.SendAsync(requestMessage, cancellationToken);

                responseMessage.EnsureSuccessStatusCode();

                return await responseMessage.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Logging.Log.Error($"{nameof(TokenRefreshHandler)}.{nameof(SendSignInAsync)} failed. Request: [{requestMessage.Method}] {requestMessage.RequestUri}. " +
                    $"IsCancellationRequested = {cancellationToken.IsCancellationRequested}, ExceptionType: {ex.GetType().Name}, Message: {ex}");
                throw;
            }
        }
    }
}
