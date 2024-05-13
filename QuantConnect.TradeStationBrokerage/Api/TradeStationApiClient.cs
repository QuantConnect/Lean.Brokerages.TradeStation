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
using System.Linq;
using Newtonsoft.Json;
using QuantConnect.Util;
using System.Diagnostics;
using QuantConnect.Orders;
using Newtonsoft.Json.Linq;
using QuantConnect.Logging;
using System.Collections.Generic;
using QuantConnect.Configuration;
using Lean = QuantConnect.Orders;
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
    /// Represents an instance of specifically configured the RestClient for TradeStation REST API.
    /// </summary>
    private readonly RestClient _restClient;

    /// <summary>
    /// Represents an instance of specifically configured the RestClient for authentication purposes.
    /// </summary>
    private readonly RestClient _restClientAuthentication;

    /// <summary>
    /// Represents an object storing AccessToken and TradeStationAccessToken information
    /// for TradeStation authentication.
    /// </summary>
    private TradeStationAccessToken _tradeStationAccessToken;

    /// <summary>
    /// Represents a cache for TradeStation trading accounts.
    /// </summary>
    /// <remarks>
    /// This cache holds instances of <see cref="Account"/> representing trading accounts
    /// used within the TradeStation platform.
    /// </remarks>
    private IEnumerable<Account> _tradingAccounts;

    /// <summary>
    /// Gets or sets the JSON serializer settings used for serialization.
    /// </summary>
    private JsonSerializerSettings jsonSerializerSettings = new() { NullValueHandling = NullValueHandling.Ignore };

    /// <summary>
    /// Initializes a new instance of the TradeStationApiClient class with the specified API Key, API Key Secret, and REST API URL.
    /// </summary>
    /// <param name="apiKey">The API Key used by the client application to authenticate requests.</param>
    /// <param name="apiKeySecret">The secret associated with the client application’s API Key for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="authorizationCodeFromUrl">The authorization code obtained from the URL during OAuth authentication.</param>
    /// <param name="signInUri">The URI of the sign-in page for TradeStation authentication. Default is "https://signin.tradestation.com".</param>
    /// <param name="redirectUri">The URI to which the user will be redirected after authentication.</param>
    /// <param name="useProxy">Boolean value indicating whether to use a proxy for API requests. Default is false.</param>
    public TradeStationApiClient(string apiKey, string apiKeySecret, string restApiUrl,
        string authorizationCodeFromUrl = "", string signInUri = "https://signin.tradestation.com", string redirectUri = "http://localhost",
        bool useProxy = false)
    {
        _apiKey = apiKey;
        _apiKeySecret = apiKeySecret;
        _authorizationCodeFromUrl = authorizationCodeFromUrl;
        _redirectUri = redirectUri;
        _restClient = new RestClient(new RestClientOptions(restApiUrl) { Proxy = useProxy ? GetProxyConfiguration() : null  } );
        _restClientAuthentication = new RestClient(new RestClientOptions(signInUri) { Proxy = useProxy ? GetProxyConfiguration() : null });
        if (!string.IsNullOrEmpty(authorizationCodeFromUrl))
        {
            _tradeStationAccessToken = GetAuthenticateToken();
        }
    }

    /// <summary>
    /// Retrieves balances for all available brokerage accounts for the current user.
    /// </summary>
    /// <returns>
    /// A TradeStationBalance object representing the combined brokerage account balances for all available accounts.
    /// </returns>
    public TradeStationBalance GetAllAccountBalances()
    {
        var accounts = GetAccounts().ToList(x => x.AccountID);
        return GetBalances(accounts);
    }

    /// <summary>
    /// Retrieves position for all available brokerage accounts for the current user.
    /// </summary>
    /// <returns>A TradeStationPosition object representing the combined brokerage position for all available accounts.</returns>
    public TradeStationPosition GetAllAccountPositions()
    {
        var accounts = GetAccounts().ToList(x => x.AccountID);
        return GetPositions(accounts);
    }

    /// <summary>
    /// Retrieves orders for all available brokerage accounts for the current user.
    /// </summary>
    /// <returns>A TradeStationOrder object representing the combined brokerage orders for all available accounts.</returns>
    public TradeStationOrder GetAllAccountOrders()
    {
        var accounts = GetAccounts().ToList(x => x.AccountID);
        return GetOrders(accounts);
    }

    /// <summary>
    /// Cancels an active order. Request valid for all account types.
    /// </summary>
    /// <param name="orderID">
    /// Order ID to cancel. Equity, option or future orderIDs should not include dashes (E.g. 1-2345-6789).
    /// Valid format orderId=123456789
    /// </param>
    public bool CancelOrder(string orderID)
    {
        try
        {
            var request = new RestRequest($"/v3/orderexecution/orders/{orderID}", Method.Delete);
            var response = ExecuteRequest(_restClient, request, true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return false;
        }
    }

    /// <summary>
    /// Places an order in TradeStation based on the provided Lean order and symbol.
    /// </summary>
    /// <param name="order">The Lean order to be placed.</param>
    /// <param name="symbol">The symbol for which the order is being placed.</param>
    /// <returns>The response containing the result of the order placement.</returns>
    public TradeStationPlaceOrderResponse PlaceOrder(Lean.Order order, string symbol)
    {
        var accountID = order.Symbol.SecurityType == SecurityType.Future
            ? GetAccounts().First(a => a.AccountType == Models.Enums.TradeStationAccountType.Futures).AccountID
            : GetAccounts().First(a => a.AccountType == Models.Enums.TradeStationAccountType.Margin).AccountID;

        var orderType = order.Type.ConvertLeanOrderTypeToTradeStation();

        var (duration, expiryDateTime) = order.TimeInForce.GetBrokerageTimeInForce();

        var tradeAction = order.Direction == Lean.OrderDirection.Buy ? "BUY" : "SELL";

        var tradeStationOrder = new TradeStationPlaceOrderRequest(accountID, orderType, order.AbsoluteQuantity.ToStringInvariant(), symbol,
                    new Models.TimeInForce(duration, expiryDateTime), tradeAction);
        switch (order)
        {
            case LimitOrder limitOrder:
                tradeStationOrder.LimitPrice = limitOrder.LimitPrice.ToStringInvariant();
                break;
            case StopMarketOrder stopMarket:
                tradeStationOrder.StopPrice = stopMarket.StopPrice.ToStringInvariant();
                break;
            case StopLimitOrder stopLimitOrder:
                tradeStationOrder.LimitPrice = stopLimitOrder.LimitPrice.ToStringInvariant();
                tradeStationOrder.StopPrice = stopLimitOrder.StopPrice.ToStringInvariant();
                break;
        }

        var request = new RestRequest($"/v3/orderexecution/orders", Method.Post);

        request.AddJsonBody(JsonConvert.SerializeObject(tradeStationOrder, jsonSerializerSettings));

        var response = ExecuteRequest(_restClient, request, true);

        return JsonConvert.DeserializeObject<TradeStationPlaceOrderResponse>(response.Content);
    }

    /// <summary>
    /// Replaces an existing order in TradeStation with the provided Lean order.
    /// </summary>
    /// <param name="order">The Lean order to replace the existing order.</param>
    /// <returns>The response containing the result of the order replacement.</returns>
    public Models.OrderResponse ReplaceOrder(Lean.Order order)
    {
        var orderID = order.BrokerId.Single();

        var tradeStationOrder = new TradeStationReplaceOrderRequest(order.AbsoluteQuantity.ToStringInvariant());
        switch (order)
        {
            case LimitOrder limitOrder:
                tradeStationOrder.LimitPrice = limitOrder.LimitPrice.ToStringInvariant();
                break;
            case StopMarketOrder stopMarket:
                tradeStationOrder.StopPrice = stopMarket.StopPrice.ToStringInvariant();
                break;
            case StopLimitOrder stopLimitOrder:
                tradeStationOrder.LimitPrice = stopLimitOrder.LimitPrice.ToStringInvariant();
                tradeStationOrder.StopPrice = stopLimitOrder.StopPrice.ToStringInvariant();
                break;
        }

        // Ensure that the order type can only be updated to Market Type. (e.g. Limit -> Market)
        if (order is MarketOrder)
        {
            tradeStationOrder.OrderType = order.Type.ConvertLeanOrderTypeToTradeStation();
        }

        var request = new RestRequest($"/v3/orderexecution/orders/{orderID}", Method.Put);
        request.AddJsonBody(JsonConvert.SerializeObject(tradeStationOrder, jsonSerializerSettings));

        try
        {
            var response = ExecuteRequest(_restClient, request, true);
            return JsonConvert.DeserializeObject<Models.OrderResponse>(response.Content);
        }
        catch
        {
            // rethrow an exception
            throw;
        }
    }

    /// <summary>
    /// Retrieves orders for the authenticated user from TradeStation brokerage accounts.
    /// </summary>
    /// <param name="accounts">
    /// List of valid Account IDs for the authenticated user in comma separated format; for example "61999124,68910124".
    /// 1 to 25 Account IDs can be specified, comma separated. Recommended batch size is 10.
    /// </param>
    /// <returns>
    /// An instance of the <see cref="TradeStationOrder"/> class representing the orders retrieved from the specified accounts.
    /// </returns>
    private TradeStationOrder GetOrders(List<string> accounts)
    {
        var request = new RestRequest($"/v3/brokerage/accounts/{string.Join(',', accounts)}/orders", Method.Get);

        var response = ExecuteRequest(_restClient, request, true);

        return JsonConvert.DeserializeObject<TradeStationOrder>(response.Content);
    }

    /// <summary>
    /// Fetches positions for the given Accounts. Request valid for Cash, Margin, Futures, and DVP account types.
    /// </summary>
    /// <param name="accounts">
    /// List of valid Account IDs for the authenticated user in comma separated format; for example "61999124,68910124".
    /// 1 to 25 Account IDs can be specified, comma separated. Recommended batch size is 10.
    /// </param>
    /// <returns></returns>
    private TradeStationPosition GetPositions(List<string> accounts)
    {
        var request = new RestRequest($"/v3/brokerage/accounts/{string.Join(',', accounts)}/positions", Method.Get);

        var response = ExecuteRequest(_restClient, request, true);

        return JsonConvert.DeserializeObject<TradeStationPosition>(response.Content);
    }

    /// <summary>
    /// Fetches the list of Brokerage Accounts available for the current user.
    /// </summary>
    /// <returns>
    /// An IEnumerable collection of Account objects representing the Brokerage Accounts available for the current user.
    /// </returns>
    private IEnumerable<Account> GetAccounts()
    {
        // If trading accounts are already cached, return them
        if (_tradingAccounts != null && _tradingAccounts.Any())
        {
            return _tradingAccounts;
        }

        var request = new RestRequest("/v3/brokerage/accounts", Method.Get);

        var response = ExecuteRequest(_restClient, request, true);

        _tradingAccounts = JsonConvert.DeserializeObject<TradeStationAccount>(response.Content).Accounts;

        return _tradingAccounts;
    }

    /// <summary>
    /// Fetches the brokerage account Balances for one or more given accounts. Request valid for Cash, Margin, Futures, and DVP account types.
    /// </summary>
    /// <param name="accounts">
    /// List of valid Account IDs for the authenticated user in comma separated format; for example "61999124,68910124".
    /// 1 to 25 Account IDs can be specified, comma separated. Recommended batch size is 10.
    /// </param>
    /// <returns>
    /// A TradeStationBalance object representing the brokerage account balances for the specified accounts.
    /// </returns>
    private TradeStationBalance GetBalances(List<string> accounts)
    {
        var request = new RestRequest($"/v3/brokerage/accounts/{string.Join(',', accounts)}/balances", Method.Get);

        var response = ExecuteRequest(_restClient, request, true);

        return JsonConvert.DeserializeObject<TradeStationBalance>(response.Content);
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
    /// <param name="refreshToken">The refresh token obtained from TradeStation API.</param>
    /// <returns>The refreshed authentication token containing access, refresh, and ID tokens along with expiration time.</returns>
    private TradeStationAccessToken RefreshAccessToken(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
    {
            throw new ArgumentException($"{nameof(TradeStationApiClient)}.{nameof(RefreshAccessToken)}:" +
                $"The refresh token provided is null or empty. Please ensure a valid refresh token is provided.");
        }

        var request = GenerateSignInRequest();

        request.AddParameter("application/x-www-form-urlencoded",
            $"grant_type=refresh_token" +
            $"&client_id={_apiKey}" +
            $"&client_secret={_apiKeySecret}" +
            $"&refresh_token={refreshToken}", ParameterType.RequestBody);

        var response = ExecuteRequest(_restClientAuthentication, request);

        var jsonResponse = JObject.Parse(response.Content);

        return new TradeStationAccessToken(
            jsonResponse["access_token"].Value<string>(),
            refreshToken,
            jsonResponse["id_token"].Value<string>(),
            jsonResponse["scope"].Value<string>(),
            jsonResponse["expires_in"].Value<int>(),
            jsonResponse["token_type"].Value<string>());
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
        var request = new RestRequest("/oauth/token", Method.Post);

        request.AddHeader("content-type", "application/x-www-form-urlencoded");
        return request;
    }

    /// <summary>
    /// Executes the rest request
    /// </summary>
    /// <param name="request">The rest request to execute</param>
    /// <returns>The rest response</returns>
    [StackTraceHidden]
    private RestResponse ExecuteRequest(RestClient restClient, RestRequest request, bool authenticate = false)
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

    /// <summary>
    /// Configures the specified RestClient instances to use a proxy with the provided proxy address, username, and password.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when proxy address, username, or password is empty or null. Indicates that these values must be correctly set in the configuration file.</exception>
    private WebProxy GetProxyConfiguration()
    {
        var proxyAddress = Config.Get("trade-station-proxy-address-port");
        var proxyUsername = Config.Get("trade-station-proxy-username");
        var proxyPassword = Config.Get("trade-station-proxy-password");

        if (new string[] { proxyAddress, proxyUsername, proxyPassword }.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("Proxy Address, Proxy Username, and Proxy Password cannot be empty or null. Please ensure these values are correctly set in the configuration file.");
        }

        return new WebProxy(proxyAddress) { Credentials = new NetworkCredential(proxyUsername, proxyPassword) };
    }
}
