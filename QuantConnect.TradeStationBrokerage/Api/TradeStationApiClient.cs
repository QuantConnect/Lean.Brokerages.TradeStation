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
using System.Linq;
using System.Text;
using System.Net.Http;
using Newtonsoft.Json;
using QuantConnect.Util;
using QuantConnect.Orders;
using QuantConnect.Logging;
using System.Threading.Tasks;
using QuantConnect.Configuration;
using System.Collections.Generic;
using Lean = QuantConnect.Orders;
using QuantConnect.Brokerages.TradeStation.Models;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

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
    /// Represents the URI to which the user will be redirected after authentication.
    /// </summary>
    private readonly string _redirectUri;

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
    /// HttpClient is used for making HTTP requests and handling HTTP responses from web resources identified by a Uri.
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// The base URL used for constructing API endpoints.
    /// </summary>
    private readonly string _baseUrl;

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
        _redirectUri = redirectUri;
        _baseUrl = restApiUrl;

        var httpClientHandler = new HttpClientHandler();
        if (useProxy)
        {
            httpClientHandler.Proxy = GetProxyConfiguration();
        }
        var tokenRefreshHandler = new TokenRefreshHandler(httpClientHandler, apiKey, apiKeySecret, authorizationCodeFromUrl, signInUri, redirectUri,
            Config.GetValue<string>("trade-station-refresh-token"));
        _httpClient = new(tokenRefreshHandler);
    }

    /// <summary>
    /// Retrieves balances for all available brokerage accounts for the current user.
    /// </summary>
    /// <returns>
    /// A TradeStationBalance object representing the combined brokerage account balances for all available accounts.
    /// </returns>
    public async Task<TradeStationBalance> GetAllAccountBalances()
    {
        var accounts = (await GetAccounts()).ToList(x => x.AccountID);
        return await GetBalances(accounts);
    }

    /// <summary>
    /// Retrieves position for all available brokerage accounts for the current user.
    /// </summary>
    /// <returns>A TradeStationPosition object representing the combined brokerage position for all available accounts.</returns>
    public async Task<TradeStationPosition> GetAllAccountPositions()
    {
        var accounts = (await GetAccounts()).ToList(x => x.AccountID);
        return await GetPositions(accounts);
    }

    /// <summary>
    /// Retrieves orders for all available brokerage accounts for the current user.
    /// </summary>
    /// <returns>A TradeStationOrder object representing the combined brokerage orders for all available accounts.</returns>
    public async Task<TradeStationOrder> GetAllAccountOrders()
    {
        var accounts = (await GetAccounts()).ToList(x => x.AccountID);
        return await GetOrders(accounts);
    }

    /// <summary>
    /// Cancels an active order. Request valid for all account types.
    /// </summary>
    /// <param name="orderID">
    /// Order ID to cancel. Equity, option or future orderIDs should not include dashes (E.g. 1-2345-6789).
    /// Valid format orderId=123456789
    /// </param>
    public async Task<bool> CancelOrder(string orderID)
    {
        try
        {
            await RequestAsync<TradeStationAccount>(_baseUrl, $"/v3/orderexecution/orders/{orderID}", HttpMethod.Delete);
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
    /// <param name="orderProperty">Additional TradeStation order properties.</param>
    /// <param name="symbol">The symbol for which the order is being placed.</param>
    /// <param name="accountType">The account type in current session.</param>
    /// <returns>The response containing the result of the order placement.</returns>
    public async Task<TradeStationPlaceOrderResponse> PlaceOrder(Lean.Order order, TradeStationOrderProperties orderProperty, string symbol,
        TradeStationAccountType accountType)
    {
        var accountID = (await GetAccounts()).Single(acc => acc.AccountType == accountType).AccountID;

        var orderType = order.Type.ConvertLeanOrderTypeToTradeStation();

        var (duration, expiryDateTime) = order.TimeInForce.GetBrokerageTimeInForce();

        var tradeAction = order.Direction == OrderDirection.Buy ? "BUY" : "SELL";
        if (orderProperty != null)
        {
            tradeAction = orderProperty.PositionSide.ToStringInvariant().ToUpper();
        }

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

        return await RequestAsync<TradeStationPlaceOrderResponse>(_baseUrl, $"/v3/orderexecution/orders", HttpMethod.Post,
            JsonConvert.SerializeObject(tradeStationOrder, jsonSerializerSettings));
    }

    /// <summary>
    /// Replaces an existing order in TradeStation with the provided Lean order.
    /// </summary>
    /// <param name="order">The Lean order to replace the existing order.</param>
    /// <returns>The response containing the result of the order replacement.</returns>
    public async Task<Models.OrderResponse> ReplaceOrder(Lean.Order order)
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

        try
        {
            return await RequestAsync<Models.OrderResponse>(_baseUrl, $"/v3/orderexecution/orders/{orderID}", HttpMethod.Put,
                JsonConvert.SerializeObject(tradeStationOrder, jsonSerializerSettings));
        }
        catch
        {
            // rethrow an exception
            throw;
        }
    }

    /// <summary>
    /// Asynchronously streams orders for the accounts retrieved from the brokerage service.
    /// </summary>
    /// <returns>
    /// An asynchronous enumerable of strings representing order information.
    /// </returns>
    /// <remarks>
    /// This method retrieves accounts from the brokerage service, then opens a stream to continuously receive order updates for these accounts.
    /// </remarks>
    public async IAsyncEnumerable<string> StreamOrders()
    {
        var accounts = (await GetAccounts()).ToList(x => x.AccountID);

        using (var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v3/brokerage/stream/accounts/{string.Join(',', accounts)}/orders"))
        {
            using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        while (!reader.EndOfStream)
                        {
                            var jsonLine = await reader.ReadLineAsync();
                            if (jsonLine == null) break;
                            yield return jsonLine;
                        }
                    }
                }
            }
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
    private async Task<TradeStationOrder> GetOrders(List<string> accounts)
    {
        return await RequestAsync<TradeStationOrder>(_baseUrl, $"/v3/brokerage/accounts/{string.Join(',', accounts)}/orders", HttpMethod.Get);
    }

    /// <summary>
    /// Fetches positions for the given Accounts. Request valid for Cash, Margin, Futures, and DVP account types.
    /// </summary>
    /// <param name="accounts">
    /// List of valid Account IDs for the authenticated user in comma separated format; for example "61999124,68910124".
    /// 1 to 25 Account IDs can be specified, comma separated. Recommended batch size is 10.
    /// </param>
    /// <returns></returns>
    private async Task<TradeStationPosition> GetPositions(List<string> accounts)
    {
        return await RequestAsync<TradeStationPosition>(_baseUrl, $"/v3/brokerage/accounts/{string.Join(',', accounts)}/positions", HttpMethod.Get);
    }

    /// <summary>
    /// Fetches the list of Brokerage Accounts available for the current user.
    /// </summary>
    /// <returns>
    /// An IEnumerable collection of Account objects representing the Brokerage Accounts available for the current user.
    /// </returns>
    private async Task<IEnumerable<Account>> GetAccounts()
    {
        // If trading accounts are already cached, return them
        if (_tradingAccounts != null && _tradingAccounts.Any())
        {
            return _tradingAccounts;
        }

        _tradingAccounts = (await RequestAsync<TradeStationAccount>(_baseUrl, "/v3/brokerage/accounts", HttpMethod.Get)).Accounts;

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
    private async Task<TradeStationBalance> GetBalances(List<string> accounts)
    {
        return await RequestAsync<TradeStationBalance>(_baseUrl, $"/v3/brokerage/accounts/{string.Join(',', accounts)}/balances", HttpMethod.Get);
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
    /// Sends an HTTP request asynchronously and deserializes the response content to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response content to.</typeparam>
    /// <param name="baseUrl">The base URL of the request.</param>
    /// <param name="resource">The resource path of the request relative to the base URL.</param>
    /// <param name="httpMethod">The HTTP method of the request.</param>
    /// <param name="jsonBody">Optional. The JSON body of the request.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains the deserialized response content.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseUrl"/>, <paramref name="resource"/>, or <paramref name="httpMethod"/> is null.</exception>
    /// <exception cref="HttpRequestException">Thrown when the HTTP request fails.</exception>
    /// <exception cref="JsonException">Thrown when the JSON deserialization fails.</exception>
    /// <exception cref="Exception">Thrown when an unexpected error occurs.</exception>
    private async Task<T> RequestAsync<T>(string baseUrl, string resource, HttpMethod httpMethod, string jsonBody = null)
    {
        using (var requestMessage = new HttpRequestMessage(httpMethod, $"{baseUrl}{resource}"))
        {
            if (jsonBody != null)
            {
                requestMessage.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }

            try
            {
                var responseMessage = await _httpClient.SendAsync(requestMessage);

                if (!responseMessage.IsSuccessStatusCode)
                {
                    throw new Exception(JsonConvert.DeserializeObject<TradeStationError>(await responseMessage.Content.ReadAsStringAsync()).Message);
                }

                var response = await responseMessage.Content.ReadAsStringAsync();

                return JsonConvert.DeserializeObject<T>(response);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
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