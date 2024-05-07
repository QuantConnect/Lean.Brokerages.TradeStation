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
using RestSharp;

namespace QuantConnect.Brokerages.TradeStation.Api;

/// <summary>
/// TradeStation api client implementation
/// </summary>
public class TradeStationApiClient
{
    /// <summary>
    /// Represents the API Key used by the client application to authenticate requests.
    /// </summary>
    /// <remarks>
    /// In the documentation, this API Key is referred to as <c>client_id</c>.
    /// </remarks>
    private readonly string _apiKey;

    /// <summary>
    /// Represents the secret associated with the client application’s API Key for authentication.
    /// </summary>
    /// <remarks>
    /// In the documentation, this API Key is referred to as <c>client_secret</c>.
    /// </remarks>
    private readonly string _apiKeySecret;

    /// <summary>
    /// Represents the authorization code obtained from the URL during OAuth authentication.
    /// </summary>
    /// <remarks>
    /// In the documentation, this API Key is referred to as <c>code</c>. <see cref="GetSignInUrl"/>
    /// </remarks>
    private readonly string _authorizationCodeFromUrl;

    /// <summary>
    /// Represents the URI to which the user will be redirected after authentication.
    /// </summary>
    private readonly string _redirectUri;

    /// <summary>
    /// Represents an instance of the RestClient class from the RestSharp library.
    /// </summary>
    private readonly RestClient _restClient;

    /// <summary>
    /// Initializes a new instance of the TradeStationApiClient class with the specified API Key, API Key Secret, and REST API URL.
    /// </summary>
    /// <param name="apiKey">The API Key used by the client application to authenticate requests.</param>
    /// <param name="apiKeySecret">The secret associated with the client application’s API Key for authentication.</param>
    /// <param name="authorizationCodeFromUrl">The authorization code obtained from the URL during OAuth authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="redirectUri">The URI to which the user will be redirected after authentication.</param>
    public TradeStationApiClient(string apiKey, string apiKeySecret, string authorizationCodeFromUrl, string restApiUrl, string redirectUri = "http://localhost")
    {
        _apiKey = apiKey;
        _apiKeySecret = apiKeySecret;
        _authorizationCodeFromUrl = authorizationCodeFromUrl;
        _restClient = new RestClient(restApiUrl);
        _redirectUri = redirectUri;
    }

    /// <summary>
    /// Generates the Sign-In URL for TradeStation authorization.
    /// </summary>
    /// <returns>The URL string used for signing in.</returns>
    /// <remarks>
    /// This method creates a URL for signing in to TradeStation's API. Pay attention to the "Scope" part, 
    /// which determines the areas of access granted by the TradeStation API. For more information on scopes,
    /// refer to the <a href="https://api.tradestation.com/docs/fundamentals/authentication/scopes">TradeStation API documentation</a>.
    /// </remarks>
    public string GetSignInUrl()
    {
        var uri = new UriBuilder($"https://signin.tradestation.com/authorize?" +
            $"response_type=code" +
            $"&client_id={_apiKey}" +
            $"&audience=https://api.tradestation.com" +
            $"&redirect_uri={_redirectUri}" +
            $"&scope=openid offline_access MarketData ReadAccount Trade OptionSpreads Matrix");
        return uri.ToString();
    }
}
