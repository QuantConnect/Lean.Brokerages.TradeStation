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
public class TradeStationApiClient : IDisposable
{
    /// <summary>
    /// Maximum number of bars that can be requested in a single call to <see cref="GetBars(string, TradeStationUnitTimeIntervalType, DateTime, DateTime)"/>.
    /// </summary>
    private const int MaxBars = 57500;

    /// <summary>
    /// Represents the API Key used by the client application to authenticate requests.
    /// </summary>
    /// <remarks>
    /// In the documentation, this API Key is referred to as <c>client_id</c>.
    /// </remarks>
    private readonly string _cliendId;

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
    /// Initializes a new instance of the TradeStationApiClient class
    /// </summary>
    /// <param name="clientId">The API Key used by the client application to authenticate requests.</param>
    /// <param name="clientSecret">The secret associated with the client application’s API Key for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="refreshToken">The type of TradeStation account.</param>
    /// <param name="redirectUri">The URI to which the user will be redirected after authentication.</param>
    /// <param name="authorizationCode">The authorization code obtained from the URL during OAuth authentication. Default is an empty string.</param>
    private TradeStationApiClient(string clientId, string clientSecret, string restApiUrl, string refreshToken, string redirectUri, string authorizationCode)
    {
        _cliendId = clientId;
        _redirectUri = redirectUri;
        _baseUrl = restApiUrl;
        var httpClientHandler = new HttpClientHandler();
        var signInUri = "https://signin.tradestation.com";
        var tokenRefreshHandler = new TokenRefreshHandler(httpClientHandler, clientId, clientSecret, authorizationCode, signInUri, redirectUri, refreshToken);
        _httpClient = new(tokenRefreshHandler);
    }

    /// <summary>
    /// Initializes a new instance of the TradeStationApiClient class with the specified API Key, API Key Secret, REST API URL, redirect URI, account type.
    /// </summary>
    /// <param name="clientId">The API Key used by the client application to authenticate requests.</param>
    /// <param name="clientSecret">The secret associated with the client application’s API Key for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="tradeStationAccountType">The type of TradeStation account.</param>
    /// <param name="refreshToken">The type of TradeStation account.</param>
    /// <param name="redirectUri">The URI to which the user will be redirected after authentication.</param>
    /// <param name="authorizationCode">The authorization code obtained from the URL during OAuth authentication. Default is an empty string.</param>
    public TradeStationApiClient(string clientId, string clientSecret, string restApiUrl, TradeStationAccountType tradeStationAccountType,
        string refreshToken, string redirectUri, string authorizationCode)
        : this(clientId, clientSecret, restApiUrl, refreshToken, redirectUri, authorizationCode)
    {
        _accountID = new Lazy<string>(() =>
        {
            var account = GetAccountIDByAccountType(tradeStationAccountType).SynchronouslyAwaitTaskResult();
            Log.Trace($"TradeStationApiClient(): will use account id: {account}");
            return account;
        });
    }

    /// <summary>
    /// Initializes a new instance of the TradeStationApiClient class with the specified API Key, API Key Secret, REST API URL, redirect URI, account Id.
    /// </summary>
    /// <param name="clientId">The API Key used by the client application to authenticate requests.</param>
    /// <param name="clientSecret">The secret associated with the client application’s API Key for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="refreshToken">The type of TradeStation account.</param>
    /// <param name="redirectUri">The URI to which the user will be redirected after authentication.</param>
    /// <param name="authorizationCode">The authorization code obtained from the URL during OAuth authentication. Default is an empty string.</param>
    /// <param name="accountId">The specific user account id.</param>
    public TradeStationApiClient(string clientId, string clientSecret, string restApiUrl, string refreshToken, string redirectUri, string authorizationCode, string accountId)
        : this(clientId, clientSecret, restApiUrl, refreshToken, redirectUri, authorizationCode)
    {
        _accountID = new Lazy<string>(() =>
        {
            Log.Trace($"TradeStationApiClient(): will use account id: {accountId}");
            return accountId;
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
        await RequestAsync<TradeStationAccount>(_baseUrl, $"/v3/orderexecution/orders/{orderID}", HttpMethod.Delete);
        return true;
    }

    /// <summary>
    /// Places an order in TradeStation based on the provided Lean order and symbol or legs.
    /// </summary>
    /// <param name="leanOrderType">The type of Lean order to be placed.</param>
    /// <param name="leanTimeInForce">The Lean time-in-force for the order.</param>
    /// <param name="leanAbsoluteQuantity">The absolute quantity of the Lean order.</param>
    /// <param name="tradeAction">The action to be taken for the trade: <see cref="TradeStationTradeActionType"/> </param>
    /// <param name="symbol">The symbol for which the order is being placed.</param>
    /// <param name="legs">The collection of order legs for combo orders.</param>
    /// <param name="limitPrice">The limit price for the order (optional).</param>
    /// <param name="stopPrice">The stop price for the order (optional).</param>
    /// <param name="routeId">The identifier for the trading route associated with the specified exchange that supports TradeStation.</param>
    /// <param name="tradeStationOrderProperties">Additional TradeStation order properties (optional).</param>
    /// <returns>A <see cref="TradeStationPlaceOrderResponse"/> containing the result of the order placement.</returns>
    public async Task<TradeStationPlaceOrderResponse> PlaceOrder(
        OrderType leanOrderType,
        Lean.TimeInForce leanTimeInForce,
        decimal? leanAbsoluteQuantity = null,
        string tradeAction = null,
        string symbol = null,
        IReadOnlyCollection<TradeStationPlaceOrderLeg> legs = null,
        decimal? limitPrice = null,
        decimal? stopPrice = null,
        string routeId = null,
        TradeStationOrderProperties tradeStationOrderProperties = null)
    {
        var orderType = leanOrderType.ConvertLeanOrderTypeToTradeStation();
        

        var outsideRegularTradingHours = tradeStationOrderProperties?.OutsideRegularTradingHours ?? false;

        var (duration, expiryDateTime) = leanTimeInForce.GetBrokerageTimeInForce(leanOrderType, outsideRegularTradingHours);

        var tradeStationOrder = new TradeStationPlaceOrderRequest(
            _accountID.Value,
            orderType,
            leanAbsoluteQuantity?.ToStringInvariant(),
            symbol,
            new Models.TimeInForce(duration, expiryDateTime),
            tradeAction,
            legs
        );

        if (tradeStationOrderProperties != null)
        {
            tradeStationOrder.AdvancedOptions = new TradeStationAdvancedOptions(tradeStationOrderProperties.AllOrNone);
        }

        if (!string.IsNullOrEmpty(routeId))
        {
            tradeStationOrder.Route = routeId;
        }

        switch (leanOrderType)
        {
            case OrderType.Limit:
            case OrderType.ComboLimit:
                tradeStationOrder.LimitPrice = limitPrice?.ToStringInvariant();
                break;
            case OrderType.StopMarket:
                tradeStationOrder.StopPrice = stopPrice?.ToStringInvariant();
                break;
            case OrderType.StopLimit:
                tradeStationOrder.LimitPrice = limitPrice?.ToStringInvariant();
                tradeStationOrder.StopPrice = stopPrice?.ToStringInvariant();
                break;
        }

        return await RequestAsync<TradeStationPlaceOrderResponse>(_baseUrl, "/v3/orderexecution/orders", HttpMethod.Post,
            JsonConvert.SerializeObject(tradeStationOrder, jsonSerializerSettings)
        );
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


        var result = await RequestAsync<Models.OrderResponse>(_baseUrl, $"/v3/orderexecution/orders/{brokerId}", HttpMethod.Put,
            JsonConvert.SerializeObject(tradeStationOrder, jsonSerializerSettings));

        if (result.Error != null)
        {
            throw new Exception(result.Message);
        }

        return result;
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
        await foreach (var response in StreamRequestAsyncEnumerable($"{_baseUrl}/v3/brokerage/stream/accounts/{_accountID.Value}/orders", cancellationToken))
        {
            yield return response;
        }
    }

    /// <summary>
    /// Asynchronously streams quotes for the specified symbols from the TradeStation API.
    /// </summary>
    /// <param name="symbols">A collection of symbols for which to stream quote data.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// An asynchronous enumerable of <see cref="Quote"/> objects representing the streamed quote data.
    /// </returns>
    /// <remarks>
    /// This method opens a stream to continuously receive quote updates for the specified symbols.
    /// Each response is deserialized into a <see cref="Quote"/> object before being returned.
    /// </remarks>
    public async IAsyncEnumerable<Quote> StreamQuotes(IReadOnlyCollection<string> symbols, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var response in StreamRequestAsyncEnumerable($"{_baseUrl}/v3/marketdata/stream/quotes/{string.Join(",", symbols)}", cancellationToken))
        {
            // Skip processing the heartbeat response as it only indicates the stream is alive
            if (response.Contains("Heartbeat", StringComparison.InvariantCultureIgnoreCase))
            {
                continue;
            }
            else if (response.Contains("GoAway", StringComparison.InvariantCultureIgnoreCase))
            {
                break;
            }
            yield return JsonConvert.DeserializeObject<Quote>(response);
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
    /// Retrieves the account type for the specified TradeStation account.
    /// </summary>
    /// <returns>
    /// The task result contains the <see cref="TradeStationAccountType"/> for the account associated with the provided account ID.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the account ID is missing or invalid, or if the account cannot be found.
    /// </exception>
    /// <remarks>
    /// This method checks if the account ID is provided, and if so, it retrieves the associated account 
    /// details from TradeStation and returns the account type. If no valid account ID is provided, 
    /// an exception is thrown.
    /// </remarks>
    public async Task<TradeStationAccountType> GetAccountType()
    {
        if (string.IsNullOrEmpty(_accountID.Value))
        {
            throw new InvalidOperationException("Account ID is required to retrieve account details. Please check and provide a valid account ID.");
        }

        var accounts = await GetAccounts();
        var result = accounts.Single(acc => acc.AccountID == _accountID.Value);
        return result.AccountType;
    }

    /// <summary>
    /// Retrieves option expirations and corresponding strikes for a given ticker symbol asynchronously.
    /// </summary>
    /// <param name="ticker">The ticker symbol for which option expirations and strikes are requested.</param>
    /// <returns>
    /// An asynchronous enumerable representing the operation. Each element in the sequence contains an expiration date and a collection of strikes associated with that expiration date.
    /// </returns>
    public async IAsyncEnumerable<(DateTime expirationDate, ExpirationType expirationType, IEnumerable<decimal> strikes)> GetOptionExpirationsAndStrikes(string ticker)
    {
        var expirations = await GetOptionExpirations(ticker);

        foreach (var expiration in expirations.Expirations)
        {
            var optionStrikes = await GetOptionStrikes(ticker, expiration.Date);
            yield return (expiration.Date, expiration.Type, optionStrikes.Strikes.SelectMany(x => x));
        }
    }

    /// <summary>
    /// Fetches marketdata bars for the given symbol, interval, and timeframe. 
    /// The maximum amount of intraday bars a user can fetch is 57,600 per request.
    /// </summary>
    /// <param name="symbol">The valid symbol string.</param>
    /// <param name="unitOfTime">The unit of time for each bar interval. <see cref="TradeStationUnitTimeIntervalType"/></param>
    /// <param name="firstDate">The first date formatted as YYYY-MM-DD,2020-04-20T18:00:00Z.</param>
    /// <param name="lastDate">The last date formatted as YYYY-MM-DD,2020-04-20T18:00:00Z.</param>
    /// <returns>
    /// An asynchronous stream of <see cref="TradeStationBar"/> objects representing the market data bars
    /// within the specified time frame and interval. The stream allows for efficient handling of large
    /// datasets by processing items as they are retrieved.
    /// </returns>
    public async IAsyncEnumerable<TradeStationBar> GetBars(string symbol, TradeStationUnitTimeIntervalType unitOfTime, DateTime firstDate, DateTime lastDate)
    {
        var totalDateRange = lastDate - firstDate;

        var totalUnitTimeByIntervalTime = unitOfTime switch
        {
            TradeStationUnitTimeIntervalType.Minute => totalDateRange.TotalMinutes,
            TradeStationUnitTimeIntervalType.Hour => totalDateRange.TotalHours,
            TradeStationUnitTimeIntervalType.Daily => totalDateRange.TotalDays,
            _ => throw new NotSupportedException($"{nameof(TradeStationApiClient)}.{nameof(GetBars)}: Unsupported time interval type '{unitOfTime}'")
        };

        var totalRequestAmount = totalUnitTimeByIntervalTime / MaxBars;

        do
        {
            var newLastDate = unitOfTime switch
            {
                TradeStationUnitTimeIntervalType.Minute => firstDate.AddMinutes(MaxBars),
                TradeStationUnitTimeIntervalType.Hour => firstDate.AddHours(MaxBars),
                TradeStationUnitTimeIntervalType.Daily => firstDate.AddDays(MaxBars),
                _ => throw new NotSupportedException($"{nameof(TradeStationApiClient)}.{nameof(GetBars)}: Unsupported time interval type '{unitOfTime}'")
            };

            if (newLastDate > lastDate)
            {
                newLastDate = lastDate;
            }

            await foreach (var bar in GetBarsAsync(symbol, unitOfTime, firstDate, newLastDate))
            {
                yield return bar;
            }

            firstDate = newLastDate;

        } while (--totalRequestAmount >= 0);
    }

    /// <summary>
    /// Retrieves a list of valid trading routes that a client can use when posting an order to TradeStation.
    /// </summary>
    /// <returns>
    /// A <see cref="TradeStationRoute"/> object containing the available routes for order execution.
    /// </returns>
    public async Task<TradeStationRoute> GetRoutes()
    {
        return await RequestAsync<TradeStationRoute>(_baseUrl, "/v3/orderexecution/routes", HttpMethod.Get);
    }

    /// <summary>
    /// Fetches marketdata bars for the given symbol, interval, and timeframe. 
    /// The maximum amount of intraday bars a user can fetch is 57,500 per request.
    /// </summary>
    /// <param name="symbol">The valid symbol string.</param>
    /// <param name="unitOfTime">The unit of time for each bar interval. <see cref="TradeStationUnitTimeIntervalType"/></param>
    /// <param name="firstDate">The first date formatted as YYYY-MM-DD,2020-04-20T18:00:00Z.</param>
    /// <param name="lastDate">The last date formatted as YYYY-MM-DD,2020-04-20T18:00:00Z.</param>
    /// <returns>
    /// An asynchronous stream of <see cref="TradeStationBar"/> objects representing the market data bars
    /// within the specified time frame and interval. The stream allows for efficient handling of large
    /// datasets by processing items as they are retrieved.
    /// </returns>
    private async IAsyncEnumerable<TradeStationBar> GetBarsAsync(string symbol, TradeStationUnitTimeIntervalType unitOfTime, DateTime firstDate, DateTime lastDate)
    {
        var url = new StringBuilder($"/v3/marketdata/barcharts/{symbol}?");
        if (unitOfTime == TradeStationUnitTimeIntervalType.Hour)
        {
            url.Append($"interval=60&unit={TradeStationUnitTimeIntervalType.Minute}&");
        }
        else
        {
            url.Append($"interval=1&unit={unitOfTime}&");
        }

        url.Append($"firstdate={firstDate:yyyy-MM-ddTHH:mm:ssZ}&lastdate={lastDate:yyyy-MM-ddTHH:mm:ssZ}");

        var bars = default(IEnumerable<TradeStationBar>);
        try
        {
            bars = (await RequestAsync<TradeStationBars>(_baseUrl, url.ToString(), HttpMethod.Get)).Bars;
        }
        catch (Exception ex)
        {
            Log.Error($"{nameof(TradeStationApiClient)}.{nameof(GetBarsAsync)}: Failed to retrieve bars for symbol '{symbol}' with time interval '{unitOfTime}'. Start: {firstDate}, End: {lastDate}. Exception: {ex.Message}");
            yield break;
        }

        foreach (var bar in bars)
        {
            yield return bar;
        }
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
    private async Task<string> GetAccountIDByAccountType(TradeStationAccountType accountType)
    {
        var accounts = await GetAccounts();
        var result = accounts.SingleOrDefault(acc => acc.AccountType == accountType);
        if (result.Equals(default(Account)))
        {
            throw new InvalidOperationException($"Failed to find account type: {accountType}. Available options: {string.Join(",", accounts.Select(x => x.AccountType.ToString()))}");
        }
        return result.AccountID;
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
            $"&client_id={_cliendId}" +
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

    /// <summary>
    /// Streams JSON lines from the specified request URI as an asynchronous enumerable sequence.
    /// </summary>
    /// <param name="requestUri">The URI to which the GET request is sent.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An asynchronous enumerable sequence of JSON lines from the response.</returns>
    /// <exception cref="HttpRequestException">Thrown when the HTTP response status code does not indicate success.</exception>
    /// <exception cref="TaskCanceledException">Thrown if the operation is canceled.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the <paramref name="cancellationToken"/>.</exception>
    /// <remarks>
    /// This method sends an HTTP GET request to the specified URI and streams the response content line by line. 
    /// It ensures that the HTTP response is successful and then reads the response stream asynchronously.
    /// Each line from the response is yielded as a string.
    /// 
    /// The <paramref name="cancellationToken"/> can be used to cancel the operation at any time. 
    /// If the cancellation is requested, the method will stop reading and yielding lines.
    /// </remarks>
    private async IAsyncEnumerable<string> StreamRequestAsyncEnumerable(string requestUri, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
        {
            using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        while (!reader.EndOfStream)
                        {
                            var jsonLine = await reader.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                            if (jsonLine == null || cancellationToken.IsCancellationRequested) break;
                            yield return jsonLine;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Releases the resources used by the current instance.
    /// </summary>
    public void Dispose()
    {
        _httpClient.DisposeSafely();
    }
}