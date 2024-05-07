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
using System.Net;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Collections.Generic;
using QuantConnect.Brokerages.TradeStation.Models;

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

    private readonly RestClient _restClientAuthentication;

    private TradeStationAccessToken _tradeStationAccessToken;

    /// <summary>
    /// Initializes a new instance of the TradeStationApiClient class with the specified API Key, API Key Secret, and REST API URL.
    /// </summary>
    /// <param name="apiKey">The API Key used by the client application to authenticate requests.</param>
    /// <param name="apiKeySecret">The secret associated with the client application’s API Key for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="authorizationCodeFromUrl">The authorization code obtained from the URL during OAuth authentication.</param>
    /// <param name="redirectUri">The URI to which the user will be redirected after authentication.</param>
    public TradeStationApiClient(string apiKey, string apiKeySecret, string restApiUrl,
        string authorizationCodeFromUrl = "", string signInUri = "https://signin.tradestation.com", string redirectUri = "http://localhost")
    {
        _apiKey = apiKey;
        _apiKeySecret = apiKeySecret;
        _authorizationCodeFromUrl = authorizationCodeFromUrl;
        _redirectUri = redirectUri;
        _restClient = new RestClient(restApiUrl);
        _restClient.Proxy = GetProxy();
        _restClientAuthentication = new RestClient(signInUri);
        _restClientAuthentication.Proxy = GetProxy();
        if (!string.IsNullOrEmpty(authorizationCodeFromUrl))
        {
            _tradeStationAccessToken = GetAuthenticateToken();
        }
    }

    /// <summary>
    /// Fetches the list of Brokerage Accounts available for the current user.
    /// </summary>
    /// <returns>
    /// An IEnumerable collection of Account objects representing the Brokerage Accounts available for the current user.
    /// </returns>
    public IEnumerable<Account> GetAccounts()
    {
        var request = new RestRequest("/brokerage/accounts", Method.GET);

        var response = ExecuteRequest(_restClient, request, true);

        return JsonConvert.DeserializeObject<TradeStationAccount>(response.Content).Accounts;
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
        return uri.Uri.AbsoluteUri;
    }

    /// <summary>
    /// Refreshes the authentication token using the refresh token from TradeStation API.
    /// </summary>
    /// <returns>The refreshed authentication token containing access, refresh, and ID tokens along with expiration time.</returns>
    private TradeStationAccessToken RefreshAccessToken()
    {
        var request = GenerateSignInRequest();

        request.AddParameter("application/x-www-form-urlencoded",
            $"grant_type=refresh_token" +
            $"&client_id={_apiKey}" +
            $"&client_secret={_apiKeySecret}" +
            $"&refresh_token={_tradeStationAccessToken.RefreshToken}", ParameterType.RequestBody);

        var response = ExecuteRequest(_restClientAuthentication, request);

        return JsonConvert.DeserializeObject<TradeStationAccessToken>(response.Content);
    }

    /// <summary>
    /// Retrieves the authentication token from TradeStation API.
    /// </summary>
    /// <returns>The authentication token containing access, refresh, and ID tokens along with expiration time.</returns>
    private TradeStationAccessToken GetAuthenticateToken()
    {
        var request = GenerateSignInRequest();

        request.AddParameter("application/x-www-form-urlencoded",
            $"grant_type=authorization_code" +
            $"&client_id={_apiKey}" +
            $"&client_secret={_apiKeySecret}" +
            $"&code={_authorizationCodeFromUrl}" +
            $"&redirect_uri={_redirectUri}", ParameterType.RequestBody);

        var response = ExecuteRequest(_restClientAuthentication, request);

        return JsonConvert.DeserializeObject<TradeStationAccessToken>(response.Content);
    }

    /// <summary>
    /// Generates a REST request for signing in.
    /// </summary>
    /// <returns>A <see cref="RestRequest"/> configured for signing in.</returns>
    private RestRequest GenerateSignInRequest()
    {
        var request = new RestRequest("/oauth/token", Method.POST);

        request.AddHeader("content-type", "application/x-www-form-urlencoded");
        return request;
    }

    /// <summary>
    /// Executes the rest request
    /// </summary>
    /// <param name="request">The rest request to execute</param>
    /// <returns>The rest response</returns>
    [StackTraceHidden]
    private IRestResponse ExecuteRequest(RestClient restClient, IRestRequest request, bool authenticate = false)
    {
        if (authenticate)
        {
            // TODO: Implement validation for the LastTimeUpdate AccessToken and initiate a refresh if necessary before making the request.
            // This ensures that the AccessToken remains valid and up-to-date for successful authorization.
            request.AddOrUpdateHeader("Authorization", $"{_tradeStationAccessToken.TokenType} {_tradeStationAccessToken.AccessToken}");
        }

        var response = restClient.Execute(request);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new Exception($"{nameof(TradeStationApiClient)}.{nameof(ExecuteRequest)} request failed: " +
                                $"[{(int)response.StatusCode}] {response.StatusDescription}, " +
                                $"Content: {response.Content}, ErrorMessage: {response.ErrorMessage}");
        }

        return response;
    }

    private IWebProxy GetProxy()
    {
        var proxy = new WebProxy("http://185.199.213.13:56480");
        proxy.Credentials = new NetworkCredential("UIPSO9MD", "0FFHAKE5");
        return proxy;
    }
}
