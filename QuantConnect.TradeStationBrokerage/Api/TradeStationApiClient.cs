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
using System.Linq;
using System.Text;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading;
using QuantConnect.Util;
using QuantConnect.Orders;
using QuantConnect.Logging;
using System.Threading.Tasks;
using QuantConnect.Configuration;
using System.Collections.Generic;
using Lean = QuantConnect.Orders;
using System.Runtime.CompilerServices;
using QuantConnect.Brokerages.TradeStation.Models;
using QuantConnect.Brokerages.TradeStation.Models.Enums;
using QuantConnect.Brokerages.TradeStation.Models.Interfaces;

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
    /// Stores the account ID for a TradeStation account, categorized by its <see cref="TradeStationAccountType"/>.
    /// </summary>
    private Lazy<string> _accountID;

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
    /// Initializes a new instance of the TradeStationApiClient class with the specified API Key, API Key Secret, REST API URL, redirect URI, account type, and optional parameters.
    /// </summary>
    /// <param name="apiKey">The API Key used by the client application to authenticate requests.</param>
    /// <param name="apiKeySecret">The secret associated with the client application’s API Key for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="redirectUri">The URI to which the user will be redirected after authentication.</param>
    /// <param name="tradeStationAccountType">The type of TradeStation account.</param>
    /// <param name="authorizationCodeFromUrl">The authorization code obtained from the URL during OAuth authentication. Default is an empty string.</param>
    /// <param name="signInUri">The URI of the sign-in page for TradeStation authentication. Default is "https://signin.tradestation.com".</param>
    public TradeStationApiClient(string apiKey, string apiKeySecret, string restApiUrl, string redirectUri, TradeStationAccountType tradeStationAccountType,
        string authorizationCodeFromUrl = "", string signInUri = "https://signin.tradestation.com")
    {
        _apiKey = apiKey;
        _redirectUri = redirectUri;
        _baseUrl = restApiUrl;

        var httpClientHandler = new HttpClientHandler();
        var tokenRefreshHandler = new TokenRefreshHandler(httpClientHandler, apiKey, apiKeySecret, authorizationCodeFromUrl, signInUri, redirectUri,
            Config.GetValue<string>("trade-station-refresh-token"));
        _httpClient = new(tokenRefreshHandler);
        _accountID = new Lazy<string>(() =>
        {
            return GetAccountIDByAccountType(tradeStationAccountType).SynchronouslyAwaitTaskResult();
        });
    }

    /// <summary>
    /// Retrieves balances for available brokerage accounts for the current user.
    /// </summary>
    /// <returns>
    /// A TradeStationBalance object representing the combined brokerage account balances for available accounts.
    /// </returns>
    public async Task<TradeStationBalance> GetAccountBalance()
    {
        return await GetBalanceByID(_accountID.Value);
    }

    /// <summary>
    /// Retrieves position for available brokerage account for the current user.
    /// </summary>
    /// <returns>A TradeStationPosition object representing the combined brokerage position for account.</returns>
    public async Task<TradeStationPosition> GetAccountPositions()
    {
        return await GetPositions(_accountID.Value);
    }

    /// <summary>
    /// Retrieves orders for available brokerage account for the current user.
    /// </summary>
    /// <returns>A TradeStationOrder object representing the combined brokerage orders for account.</returns>
    public async Task<TradeStationOrderResponse> GetOrders()
    {
        return await GetOrdersByAccountID(_accountID.Value);
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
    /// <param name="leanOrderType">The type of Lean order to be placed.</param>
    /// <param name="leanTimeInForce">The Lean time-in-force for the order.</param>
    /// <param name="leanAbsoluteQuantity">The absolute quantity of the Lean order.</param>
    /// <param name="tradeAction">The action to be taken for the trade: <see cref="TradeStationTradeActionType"/> </param>
    /// <param name="symbol">The symbol for which the order is being placed.</param>
    /// <param name="limitPrice">The limit price for the order (optional).</param>
    /// <param name="stopPrice">The stop price for the order (optional).</param>
    /// <returns>A <see cref="TradeStationPlaceOrderResponse"/> containing the result of the order placement.</returns>
    public async Task<TradeStationPlaceOrderResponse> PlaceOrder(OrderType leanOrderType, Lean.TimeInForce leanTimeInForce, decimal leanAbsoluteQuantity,
        string tradeAction, string symbol, decimal? limitPrice = null, decimal? stopPrice = null)
    {
        var orderType = leanOrderType.ConvertLeanOrderTypeToTradeStation();

        var (duration, expiryDateTime) = leanTimeInForce.GetBrokerageTimeInForce();

        var tradeStationOrder = new TradeStationPlaceOrderRequest(_accountID.Value, orderType, leanAbsoluteQuantity.ToStringInvariant(), symbol,
                    new Models.TimeInForce(duration, expiryDateTime), tradeAction);

        switch (leanOrderType)
        {
            case OrderType.Limit:
                tradeStationOrder.LimitPrice = limitPrice.Value.ToStringInvariant();
                break;
            case OrderType.StopMarket:
                tradeStationOrder.StopPrice = stopPrice.Value.ToStringInvariant();
                break;
            case OrderType.StopLimit:
                tradeStationOrder.LimitPrice = limitPrice.Value.ToStringInvariant();
                tradeStationOrder.StopPrice = stopPrice.Value.ToStringInvariant();
                break;
        }
        return await RequestAsync<TradeStationPlaceOrderResponse>(_baseUrl, $"/v3/orderexecution/orders", HttpMethod.Post,
            JsonConvert.SerializeObject(tradeStationOrder, jsonSerializerSettings));
    }

    /// <summary>
    /// Replaces an existing order with the specified parameters.
    /// </summary>
    /// <param name="brokerId">The unique identifier of the broker.</param>
    /// <param name="leanOrderType">The type of order to be placed.</param>
    /// <param name="quantity">The new quantity for the order. If null, the quantity remains unchanged.</param>
    /// <param name="limitPrice">The limit price for the order. If null, the limit price remains unchanged.</param>
    /// <param name="stopPrice">The stop price for the order. If null, the stop price remains unchanged.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains an <see cref="OrderResponse"/> object representing the response to the order replacement.</returns>
    /// <remarks>
    /// This method replaces an existing order with new parameters such as quantity, limit price, and stop price.
    /// If any parameter is not provided (null), the corresponding value of the existing order will remain unchanged.
    /// </remarks>
    public async Task<Models.OrderResponse> ReplaceOrder(string brokerId, OrderType leanOrderType, decimal quantity,
        decimal? limitPrice = null, decimal? stopPrice = null)
    {
        var tradeStationOrder = new TradeStationReplaceOrderRequest(quantity.ToStringInvariant(), _accountID.Value, brokerId);

        if (limitPrice.HasValue)
        {
            tradeStationOrder.LimitPrice = limitPrice.Value.ToStringInvariant();
        }

        if (stopPrice.HasValue)
        {
            tradeStationOrder.StopPrice = stopPrice.Value.ToStringInvariant();
        }

        tradeStationOrder.OrderType = leanOrderType.ConvertLeanOrderTypeToTradeStation();

        try
        {

            return await RequestAsync<Models.OrderResponse>(_baseUrl, $"/v3/orderexecution/orders/{brokerId}", HttpMethod.Put,
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
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    public async IAsyncEnumerable<string> StreamOrders([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v3/brokerage/stream/accounts/{_accountID.Value}/orders"))
        {
            using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        Log.Trace($"{nameof(TradeStationApiClient)}.{nameof(StreamOrders)}: We are now starting to read the order stream.");
                        while (!reader.EndOfStream)
                        {
                            var jsonLine = await reader.ReadLineAsync();
                            if (jsonLine == null) break;
                            yield return jsonLine;
                        }
                        Log.Trace($"{nameof(TradeStationApiClient)}.{nameof(StreamOrders)}: We have completed reading the order stream.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Retrieves a snapshot of quotes for a given ticker from TradeStation.
    /// </summary>
    /// <param name="ticker">The ticker symbol for which to retrieve the quote snapshot.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the <see cref="TradeStationQuoteSnapshot"/> for the specified ticker.</returns>

    public async Task<TradeStationQuoteSnapshot> GetQuoteSnapshot(string ticker)
    {
        return await RequestAsync<TradeStationQuoteSnapshot>(_baseUrl, $"/v3/marketdata/quotes/{ticker}", HttpMethod.Get);
    }

    /// <summary>
    /// Retrieves the account ID for a specified TradeStation account type.
    /// </summary>
    /// <param name="accountType">The type of TradeStation account to retrieve the ID for.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the account ID 
    /// for the specified account type.
    /// </returns>
    /// <remarks>
    /// If the account ID is already cached, it returns the cached value. Otherwise, it fetches the
    /// account information, filters it by the specified account type, caches the account ID, and returns it.
    /// </remarks>
    public async Task<string> GetAccountIDByAccountType(TradeStationAccountType accountType)
    {
        return (await GetAccounts()).Single(acc => acc.AccountType == accountType).AccountID;
    }

    /// <summary>
    /// Retrieves option expirations for a given ticker symbol asynchronously.
    /// </summary>
    /// <param name="ticker">The ticker symbol for which option expirations are requested.</param>
    /// <returns>
    /// An asynchronous task representing the operation. The task result contains option expirations for the specified ticker symbol.
    /// </returns>
    private async Task<TradeStationOptionExpiration> GetOptionExpirations(string ticker)
    {
        return await RequestAsync<TradeStationOptionExpiration>(_baseUrl, $"/v3/marketdata/options/expirations/{ticker}", HttpMethod.Get);
    }

    /// <summary>
    /// Retrieves option strikes for a given underlying asset and expiration date asynchronously.
    /// </summary>
    /// <param name="underlying">The symbol of the underlying asset for which option strikes are requested.</param>
    /// <param name="expirationDate">The expiration date of the options.</param>
    /// <returns>
    /// An asynchronous task representing the operation. The task result contains option strikes for the specified underlying asset and expiration date.
    /// </returns>
    private async Task<TradeStationOptionStrike> GetOptionStrikes(string underlying, DateTime expirationDate)
    {
        return await RequestAsync<TradeStationOptionStrike>(_baseUrl, $"/v3/marketdata/options/strikes/{underlying}?expiration={expirationDate.ToStringInvariant("MM-dd-yyyy")}", HttpMethod.Get);
    }

    /// <summary>
    /// Retrieves orders for the authenticated user from TradeStation brokerage account.
    /// </summary>
    /// <param name="accounts">The valid Account ID for the authenticated user.</param>
    /// <returns>
    /// An instance of the <see cref="TradeStationOrderResponse"/> class representing the orders retrieved from the specified account.
    /// </returns>
    private async Task<TradeStationOrderResponse> GetOrdersByAccountID(string accountID)
    {
        return await RequestAsync<TradeStationOrderResponse>(_baseUrl, $"/v3/brokerage/accounts/{accountID}/orders", HttpMethod.Get);
    }

    /// <summary>
    /// Fetches positions for the given Account. Request valid for Cash, Margin, Futures, and DVP account types.
    /// </summary>
    /// <param name="accounts"> The valid Account ID for the authenticated user.</param>
    /// <returns></returns>
    private async Task<TradeStationPosition> GetPositions(string accountID)
    {
        return await RequestAsync<TradeStationPosition>(_baseUrl, $"/v3/brokerage/accounts/{accountID}/positions", HttpMethod.Get);
    }

    /// <summary>
    /// Fetches the list of Brokerage Accounts available for the current user.
    /// </summary>
    /// <returns>
    /// An IEnumerable collection of Account objects representing the Brokerage Accounts available for the current user.
    /// </returns>
    private async Task<IEnumerable<Account>> GetAccounts()
    {
        return (await RequestAsync<TradeStationAccount>(_baseUrl, "/v3/brokerage/accounts", HttpMethod.Get)).Accounts;
    }

    /// <summary>
    /// Fetches the brokerage account Balances for one or more given accounts. Request valid for Cash, Margin, Futures, and DVP account types.
    /// </summary>
    /// <param name="accountID">The valid Account IDs for the authenticated user.</param>
    /// <returns>
    /// A TradeStationBalance object representing the brokerage account balances for the specified accounts.
    /// </returns>
    private async Task<TradeStationBalance> GetBalanceByID(string accountID)
    {
        return await RequestAsync<TradeStationBalance>(_baseUrl, $"/v3/brokerage/accounts/{accountID}/balances", HttpMethod.Get);
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

                var deserializeResponse = JsonConvert.DeserializeObject<T>(response);

                if (deserializeResponse is ITradeStationError errors && errors.Errors != null)
                {
                    foreach (var positionError in errors.Errors)
                    {
                        throw new Exception($"Error in {nameof(TradeStationApiClient)}.{nameof(RequestAsync)}: {positionError.Message} while accessing resource: {resource}");
                    }
                }

                return deserializeResponse;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
    }
}