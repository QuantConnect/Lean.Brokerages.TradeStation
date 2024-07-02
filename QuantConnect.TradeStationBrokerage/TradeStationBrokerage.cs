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
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using Newtonsoft.Json;
using QuantConnect.Api;
using System.Threading;
using QuantConnect.Util;
using QuantConnect.Orders;
using Newtonsoft.Json.Linq;
using System.Globalization;
using QuantConnect.Logging;
using System.Threading.Tasks;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Orders.Fees;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Net.NetworkInformation;
using QuantConnect.Brokerages.CrossZero;
using QuantConnect.Brokerages.TradeStation.Api;
using QuantConnect.Brokerages.TradeStation.Models;
using TimeInForce = QuantConnect.Orders.TimeInForce;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation;

[BrokerageFactory(typeof(TradeStationBrokerageFactory))]
public class TradeStationBrokerage : Brokerage
{
    /// <inheritdoc cref="TradeStationApiClient" />
    private readonly TradeStationApiClient _tradeStationApiClient;

    /// <inheritdoc cref="TradeStationSymbolMapper" />
    private TradeStationSymbolMapper _symbolMapper;

    /// <summary>
    /// Indicates whether the application is subscribed to stream order updates.
    /// </summary>
    private bool _isSubscribeOnStreamOrderUpdate;

    /// <inheritdoc cref="CancellationTokenSource"/>
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    /// <summary>
    /// Represents an AutoResetEvent synchronization primitive used to signal when the brokerage connection is established.
    /// </summary>
    private readonly AutoResetEvent _autoResetEvent = new(false);

    /// <summary>
    /// Collection of pre-defined option rights.
    /// Initialized for performance optimization as the API only returns strike price without indicating the right.
    /// </summary>
    private readonly IEnumerable<OptionRight> _optionRights = new[] { OptionRight.Call, OptionRight.Put };

    /// <inheritdoc cref="ISecurityProvider"/>
    private ISecurityProvider _securityProvider { get; }

    /// <inheritdoc cref="BrokerageConcurrentMessageHandler{T}"/>
    private BrokerageConcurrentMessageHandler<string> _messageHandler;

    /// <summary>
    /// Order provider
    /// </summary>
    protected IOrderProvider OrderProvider { get; private set; }

    /// <summary>
    /// Returns true if we're currently connected to the broker
    /// </summary>
    public override bool IsConnected { get => _isSubscribeOnStreamOrderUpdate; }

    /// <summary>
    /// Constructor for the TradeStation brokerage.
    /// </summary>
    /// <remarks>
    /// This constructor initializes a new instance of the TradeStationBrokerage class with the provided parameters.
    /// </remarks>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="apiKeySecret">The API key secret for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="redirectUrl">The redirect URL to generate great link to get right "authorizationCodeFromUrl"</param>
    /// <param name="authorizationCodeFromUrl">The authorization code obtained from the URL.</param>
    /// <param name="accountType">The type of TradeStation account for the current session.
    /// For <see cref="TradeStationAccountType.Cash"/> or <seealso cref="TradeStationAccountType.Margin"/> accounts,
    /// it is used for trading <seealso cref="SecurityType.Equity"/> and <seealso cref="SecurityType.Option"/>.
    /// For <seealso cref="TradeStationAccountType.Futures"/> accounts, it is used for trading <seealso cref="SecurityType.Future"/> contracts.</param>
    /// <param name="algorithm">The algorithm instance is required to retrieve account type</param>
    public TradeStationBrokerage(string apiKey, string apiKeySecret, string restApiUrl, string redirectUrl, string authorizationCodeFromUrl,
        string accountType, IAlgorithm algorithm)
        : this(apiKey, apiKeySecret, restApiUrl, redirectUrl, authorizationCodeFromUrl, accountType, algorithm?.Portfolio?.Transactions, algorithm?.Portfolio)
    {
    }

    /// <summary>
    /// Constructor for the TradeStation brokerage.
    /// </summary>
    /// <remarks>
    /// This constructor initializes a new instance of the TradeStationBrokerage class with the provided parameters.
    /// </remarks>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="apiKeySecret">The API key secret for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="redirectUrl">The redirect URL to generate great link to get right "authorizationCodeFromUrl"</param>
    /// <param name="authorizationCodeFromUrl">The authorization code obtained from the URL.</param>
    /// <param name="accountType">The type of TradeStation account for the current session.
    /// For <see cref="TradeStationAccountType.Cash"/> or <seealso cref="TradeStationAccountType.Margin"/> accounts, it is used for trading <seealso cref="SecurityType.Equity"/> and <seealso cref="SecurityType.Option"/>.
    /// For <seealso cref="TradeStationAccountType.Futures"/> accounts, it is used for trading <seealso cref="SecurityType.Future"/> contracts.</param>
    /// <param name="orderProvider">The order provider.</param>
    public TradeStationBrokerage(string apiKey, string apiKeySecret, string restApiUrl, string redirectUrl,
        string authorizationCodeFromUrl, string accountType, IOrderProvider orderProvider, ISecurityProvider securityProvider)
        : base("TradeStation")
    {
        _securityProvider = securityProvider;
        OrderProvider = orderProvider;
        _symbolMapper = new TradeStationSymbolMapper();
        _tradeStationApiClient = new TradeStationApiClient(apiKey, apiKeySecret, restApiUrl, redirectUrl,
            TradeStationExtensions.ParseAccountType(accountType), authorizationCodeFromUrl);
        _messageHandler = new(HandleTradeStationMessage);
        ValidateSubscription();
    }

    #region Brokerage

    /// <summary>
    /// Gets all open orders on the account.
    /// NOTE: The order objects returned do not have QC order IDs.
    /// </summary>
    /// <returns>The open orders returned from IB</returns>
    public override List<Order> GetOpenOrders()
    {
        var orders = _tradeStationApiClient.GetOrders().SynchronouslyAwaitTaskResult();

        var leanOrders = new List<Order>();
        foreach (var order in orders.Orders.Where(o => o.Status is TradeStationOrderStatusType.Ack or TradeStationOrderStatusType.Don))
        {
            var leg = order.Legs.First();
            // TODO: Where may we take Market ?
            var leanSymbol = _symbolMapper.GetLeanSymbol(leg.Underlying ?? leg.Symbol, leg.AssetType.ConvertAssetTypeToSecurityType(), Market.USA,
                leg.ExpirationDate, leg.StrikePrice, leg.OptionType.ConvertOptionTypeToOptionRight());

            var leanOrder = default(Order);
            switch (order.OrderType)
            {
                case TradeStationOrderType.Market:
                    leanOrder = new MarketOrder(leanSymbol, leg.QuantityOrdered, order.OpenedDateTime);
                    break;
                case TradeStationOrderType.Limit:
                    leanOrder = new LimitOrder(leanSymbol, leg.QuantityOrdered, order.LimitPrice, order.OpenedDateTime);
                    break;
                case TradeStationOrderType.StopMarket:
                    leanOrder = new StopMarketOrder(leanSymbol, leg.QuantityOrdered, order.StopPrice, order.OpenedDateTime);
                    break;
                case TradeStationOrderType.StopLimit:
                    leanOrder = new StopLimitOrder(leanSymbol, leg.QuantityOrdered, order.StopPrice, order.LimitPrice, order.OpenedDateTime);
                    break;
            }

            leanOrder.Status = OrderStatus.Submitted;
            if (leg.ExecQuantity > 0m && leg.ExecQuantity != leg.QuantityOrdered)
            {
                leanOrder.Status = OrderStatus.PartiallyFilled;
            }

            leanOrder.BrokerId.Add(order.OrderID);
            leanOrders.Add(leanOrder);
        }
        return leanOrders;
    }

    /// <summary>
    /// Gets all holdings for the account
    /// </summary>
    /// <returns>The current holdings from the account</returns>
    public override List<Holding> GetAccountHoldings()
    {
        var positions = _tradeStationApiClient.GetAccountPositions().SynchronouslyAwaitTaskResult();

        var holdings = new List<Holding>();
        foreach (var position in positions.Positions)
        {
            var leanSymbol = default(Symbol);
            switch (position.AssetType)
            {
                case TradeStationAssetType.Future:
                    leanSymbol = _symbolMapper.GetLeanSymbol(SymbolRepresentation.ParseFutureTicker(position.Symbol).Underlying, SecurityType.Future, Market.USA, position.ExpirationDate);
                    break;
                case TradeStationAssetType.Stock:
                    leanSymbol = _symbolMapper.GetLeanSymbol(position.Symbol, SecurityType.Equity, Market.USA);
                    break;
                case TradeStationAssetType.StockOption:
                    var optionParam = _symbolMapper.ParsePositionOptionSymbol(position.Symbol);
                    leanSymbol = _symbolMapper.GetLeanSymbol(optionParam.symbol, SecurityType.Option, Market.USA, optionParam.expiryDate, optionParam.strikePrice, optionParam.optionRight == 'C' ? OptionRight.Call : OptionRight.Put);
                    break;
            }

            holdings.Add(new Holding()
            {
                AveragePrice = position.AveragePrice,
                ConversionRate = position.ConversionRate,
                CurrencySymbol = Currencies.USD,
                MarketValue = position.MarketValue,
                MarketPrice = position.Last,
                Quantity = position.Quantity,
                Symbol = leanSymbol,
                UnrealizedPnL = position.UnrealizedProfitLoss,
                UnrealizedPnLPercent = position.UnrealizedProfitLossPercent
            });
        }

        return holdings;
    }

    /// <summary>
    /// Gets the current cash balance for each currency held in the brokerage account
    /// </summary>
    /// <returns>The current cash balance for each currency available for trading</returns>
    public override List<CashAmount> GetCashBalance()
    {
        var balances = _tradeStationApiClient.GetAccountBalance().SynchronouslyAwaitTaskResult();

        var cashBalance = new List<CashAmount>();
        foreach (var balance in balances.Balances)
        {
            cashBalance.Add(new CashAmount(decimal.Parse(balance.CashBalance, CultureInfo.InvariantCulture), Currencies.USD));
        }

        return cashBalance;
    }

    /// <summary>
    /// Places a new order and assigns a new broker ID to the order
    /// </summary>
    /// <param name="order">The order to be placed</param>
    /// <returns>True if the request for a new order has been placed, false otherwise</returns>
    public override bool PlaceOrder(Order order)
    {
        if (!CanSubscribe(order.Symbol))
        {
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1,
                $"Symbol is not supported {order.Symbol}"));
            return false;
        }

        var result = default(bool);
        _messageHandler.WithLockedStream(() =>
        {
            var holdingQuantity = _securityProvider.GetHoldingsQuantity(order.Symbol);

            var isPlaceCrossOrder = TryCrossZeroPositionOrder(order, holdingQuantity);

            if (isPlaceCrossOrder == null)
            {
                var response = PlaceTradeStationOrder(order, holdingQuantity);
                if (response == null)
                {
                    result = false;
                }
                result = true;
            }
            else
            {
                result = isPlaceCrossOrder.Value;
            }
        });
        return result;
    }

    /// <summary>
    /// Places an order using TradeStation.
    /// </summary>
    /// <param name="order">The order to be placed.</param>
    /// <param name="holdingQuantity">The holding quantity associated with the order.</param>
    /// <param name="isSubmittedEvent">Indicates if the order submission event should be triggered.</param>
    /// <returns>A response from TradeStation after placing the order.</returns>
    private TradeStationPlaceOrderResponse? PlaceTradeStationOrder(Order order, decimal holdingQuantity, bool isSubmittedEvent = true)
    {
        var symbol = _symbolMapper.GetBrokerageSymbol(order.Symbol);
        var tradeAction = ConvertDirection(order.SecurityType, order.Direction, holdingQuantity).ToStringInvariant().ToUpperInvariant();
        return PlaceOrderCommon(order, order.Type, order.TimeInForce, order.AbsoluteQuantity, tradeAction, symbol, order.GetLimitPrice(), order.GetStopPrice(), isSubmittedEvent);
    }

    /// <summary>
    /// Places a CrossZero order.
    /// </summary>
    /// <param name="crossZeroOrderRequest">The CrossZero order request containing the necessary details.</param>
    /// <param name="isPlaceOrderWithLeanEvent">Indicates if the Lean event should be triggered upon order placement.</param>
    /// <returns>A response indicating the success or failure of the CrossZero order placement.</returns>
    protected override CrossZeroOrderResponse PlaceCrossZeroOrder(CrossZeroFirstOrderRequest crossZeroOrderRequest, bool isPlaceOrderWithLeanEvent)
    {
        var symbol = _symbolMapper.GetBrokerageSymbol(crossZeroOrderRequest.LeanOrder.Symbol);
        var tradeAction = crossZeroOrderRequest.OrderPosition.ConvertDirection(crossZeroOrderRequest.LeanOrder.SecurityType).ToStringInvariant().ToUpperInvariant();

        var response = PlaceOrderCommon(crossZeroOrderRequest.LeanOrder, crossZeroOrderRequest.OrderType, crossZeroOrderRequest.LeanOrder.TimeInForce,
            crossZeroOrderRequest.AbsoluteOrderQuantity, tradeAction, symbol, crossZeroOrderRequest.LeanOrder.GetLimitPrice(), crossZeroOrderRequest.LeanOrder.GetStopPrice(), isPlaceOrderWithLeanEvent);

        if (response == null || !response.Value.Orders.Any())
        {
            return new CrossZeroOrderResponse(string.Empty, false);
        }

        var brokerageId = response.Value.Orders.Single().OrderID;
        return new CrossZeroOrderResponse(brokerageId, true);
    }

    /// <summary>
    /// Places a common order.
    /// </summary>
    /// <param name="order">The order to be placed.</param>
    /// <param name="orderType">The type of the order.</param>
    /// <param name="timeInForce">The time in force for the order.</param>
    /// <param name="quantity">The quantity of the order.</param>
    /// <param name="tradeAction">The trade action (BUY/SELL) of the order.</param>
    /// <param name="symbol">The symbol for the order.</param>
    /// <param name="limitPrice">The limit price for the order, if applicable.</param>
    /// <param name="stopPrice">The stop price for the order, if applicable.</param>
    /// <param name="isSubmittedEvent">Indicates if the order submission event should be triggered.</param>
    /// <returns>A response from TradeStation after placing the order.</returns>
    private TradeStationPlaceOrderResponse? PlaceOrderCommon(Order order, OrderType orderType, TimeInForce timeInForce, decimal quantity, string tradeAction, string symbol, decimal? limitPrice, decimal? stopPrice, bool isSubmittedEvent)
    {
        var response = _tradeStationApiClient.PlaceOrder(orderType, timeInForce, quantity, tradeAction, symbol, limitPrice, stopPrice).SynchronouslyAwaitTaskResult();

        foreach (var brokerageOrder in response.Orders)
        {
            order.BrokerId.Add(brokerageOrder.OrderID);

            // Check if the order failed due to an existing position. Reason: [EC601,EC602,EC701,EC702]: You are long/short N shares.
            if (brokerageOrder.Message.Contains("Order failed", StringComparison.InvariantCultureIgnoreCase))
            {
                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, $"{nameof(TradeStationBrokerage)} Order Event")
                { Status = OrderStatus.Invalid, Message = brokerageOrder.Message });
                return null;
            }

            if (isSubmittedEvent)
            {
                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, $"{nameof(TradeStationBrokerage)} Order Event")
                { Status = OrderStatus.Submitted });
            }
        }
        return response;
    }

    /// <summary>
    /// Updates the order with the same id
    /// </summary>
    /// <param name="order">The new order information</param>
    /// <returns>True if the request was made for the order to be updated, false otherwise</returns>
    public override bool UpdateOrder(Order order)
    {
        var holdingQuantity = _securityProvider.GetHoldingsQuantity(order.Symbol);

        if (!TryGetUpdateCrossZeroOrderQuantity(order, out var orderQuantity))
        {
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1, $"{nameof(TradeStationBrokerage)}.{nameof(UpdateOrder)}: Unable to modify order quantities."));
            return false;
        }

        var response = default(bool);
        _messageHandler.WithLockedStream(() =>
        {
            try
            {
                var result = _tradeStationApiClient.ReplaceOrder(order.BrokerId.Last(), order.Type, Math.Abs(orderQuantity), order.GetLimitPrice(), order.GetStopPrice()).SynchronouslyAwaitTaskResult();
                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, $"{nameof(TradeStationBrokerage)}.{nameof(UpdateOrder)} Order Event")
                {
                    Status = OrderStatus.UpdateSubmitted
                });
                response = true;
            }
            catch (Exception exception)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, "UpdateOrderInvalid", exception.Message));
                response = false;
            }
        });
        return response;
    }

    /// <summary>
    /// Cancels the order with the specified ID
    /// </summary>
    /// <param name="order">The order to cancel</param>
    /// <returns>True if the request was made for the order to be canceled, false otherwise</returns>
    public override bool CancelOrder(Order order)
    {
        var brokerageOrderId = order.BrokerId.Last();
        if (_tradeStationApiClient.CancelOrder(brokerageOrderId).SynchronouslyAwaitTaskResult())
        {
            OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, "CancelOrder")
            { Status = OrderStatus.Canceled });
            return true;
        }
        return false;
    }

    /// <summary>
    /// Connects the client to the broker's remote servers
    /// </summary>
    public override void Connect()
    {
        if (IsConnected)
        {
            return;
        }

        _isSubscribeOnStreamOrderUpdate = SubscribeOnOrderUpdate();
    }

    /// <summary>
    /// Disconnects the client from the broker's remote servers
    /// </summary>
    public override void Disconnect()
    {
        _cancellationTokenSource.Cancel();
    }

    #endregion

    /// <summary>
    /// Retrieves a quote snapshot for a given symbol from TradeStation.
    /// This method is intended to be used for testing purposes.
    /// </summary>
    /// <param name="symbol">The symbol for which to retrieve the quote snapshot.</param>
    /// <returns>A <see cref="Models.TradeStationQuoteSnapshot"/> containing the quote data for the specified symbol.</returns>
    protected TradeStationQuoteSnapshot GetQuote(Symbol symbol)
    {
        var brokerageTicker = _symbolMapper.GetBrokerageSymbol(symbol);
        return _tradeStationApiClient.GetQuoteSnapshot(brokerageTicker).SynchronouslyAwaitTaskResult();
    }

    /// <summary>
    /// Determines whether a symbol can be subscribed to.
    /// </summary>
    /// <param name="symbol">The symbol to check for subscription eligibility.</param>
    /// <returns>
    ///   <c>true</c> if the symbol can be subscribed to; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This method checks if the provided symbol is eligible for subscription based on certain criteria.
    /// Symbols containing the substring "universe" or those identified as canonical are not eligible for subscription.
    /// </remarks>
    private bool CanSubscribe(Symbol symbol)
    {
        if (symbol.Value.IndexOfInvariant("universe", true) != -1 || symbol.IsCanonical())
        {
            return false;
        }

        return _symbolMapper.SupportedSecurityType.Contains(symbol.SecurityType);
    }

    /// <summary>
    /// Subscribes to order updates and processes them asynchronously.
    /// </summary>
    /// <returns>
    /// A boolean value indicating whether the subscription was successfully established within the specified timeout period.
    /// </returns>
    /// <remarks>
    /// This method starts a new long-running task that continuously listens for order updates from the TradeStation API.
    /// If an exception occurs during the streaming process, the method will wait for 10 seconds before attempting to reconnect.
    /// </remarks>
    private bool SubscribeOnOrderUpdate()
    {
        Task.Factory.StartNew(async () =>
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnOrderUpdate)}: Starting to listen for order updates...");
                try
                {
                    await foreach (var json in _tradeStationApiClient.StreamOrders(_cancellationTokenSource.Token))
                    {
                        _messageHandler.HandleNewMessage(json);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnOrderUpdate)}.Exception: {ex}");
                }
                Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnOrderUpdate)}: Connection lost. Reconnecting in 10 seconds...");
                _cancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            }
        }, _cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        return _autoResetEvent.WaitOne(TimeSpan.FromSeconds(30), _cancellationTokenSource.Token);
    }

    /// <summary>
    /// Handles incoming TradeStation messages in JSON format.
    /// </summary>
    /// <param name="json">The JSON string containing the TradeStation message.</param>
    private void HandleTradeStationMessage(string json)
    {
        var jObj = JObject.Parse(json);
        if (_isSubscribeOnStreamOrderUpdate && jObj["AccountID"] != null)
        {
            var brokerageOrder = jObj.ToObject<TradeStationOrder>();

            var leanOrderStatus = default(OrderStatus);
            switch (brokerageOrder.Status)
            {
                case TradeStationOrderStatusType.Fll:
                case TradeStationOrderStatusType.Brf:
                    leanOrderStatus = OrderStatus.Filled;
                    break;
                case TradeStationOrderStatusType.Fpr:
                    leanOrderStatus = OrderStatus.PartiallyFilled;
                    break;
                case TradeStationOrderStatusType.Rej:
                case TradeStationOrderStatusType.Tsc:
                case TradeStationOrderStatusType.Rjr:
                case TradeStationOrderStatusType.Bro:
                    leanOrderStatus = OrderStatus.Invalid;
                    break;
                case TradeStationOrderStatusType.Out:
                    leanOrderStatus = OrderStatus.Canceled;
                    break;
                default:
                    Log.Debug($"{nameof(TradeStationBrokerage)}.{nameof(HandleTradeStationMessage)}.TradeStationStreamStatus: {json}");
                    return;
            };

            if (!TryGetOrRemoveCrossZeroOrder(brokerageOrder.OrderID, leanOrderStatus, out var leanOrder))
            {
                leanOrder = OrderProvider.GetOrdersByBrokerageId(brokerageOrder.OrderID)?.SingleOrDefault();
            }

            if (leanOrder == null)
            {
                // If the lean order is still null, wait for up to 10 seconds before trying again to get the order from the cache.
                // This is necessary when a CrossZeroOrder was placed successfully and we need to ensure the order is available.
                Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(HandleTradeStationMessage)}. order id not found: {brokerageOrder.OrderID}");
                return;
            }

            var leg = brokerageOrder.Legs.First();
            // TODO: Where may we take Market ?
            var leanSymbol = _symbolMapper.GetLeanSymbol(leg.Underlying ?? leg.Symbol, leg.AssetType.ConvertAssetTypeToSecurityType(), Market.USA,
                leg.ExpirationDate, leg.StrikePrice, leg.OptionType.ConvertOptionTypeToOptionRight());

            var orderEvent = new OrderEvent(
                leanOrder.Id,
                leanSymbol,
                brokerageOrder.OpenedDateTime,
                leanOrder.Status,
                leg.BuyOrSell.IsShort() ? OrderDirection.Sell : OrderDirection.Buy,
                leg.ExecutionPrice,
                leg.BuyOrSell.IsShort() ? decimal.Negate(leg.ExecQuantity) : leg.ExecQuantity,
                new OrderFee(new CashAmount(brokerageOrder.CommissionFee, Currencies.USD)),
                message: brokerageOrder.RejectReason)
            { Status = leanOrderStatus };

            // if we filled the order and have another contingent order waiting, submit it
            if (!TryHandleRemainingCrossZeroOrder(leanOrder, orderEvent))
            {
                OnOrderEvent(orderEvent);
            }
        }
        else if (jObj["StreamStatus"] != null)
        {
            var status = jObj.ToObject<TradeStationStreamStatus>();
            switch (status.StreamStatus)
            {
                case "EndSnapshot":
                    _isSubscribeOnStreamOrderUpdate = true;
                    _autoResetEvent.Set();
                    break;
                case "GoAway": // Before stream is terminated by server GoAway status is sent indicating that client must restart the stream
                    _isSubscribeOnStreamOrderUpdate = false;
                    break;
                default:
                    Log.Debug($"{nameof(TradeStationBrokerage)}.{nameof(HandleTradeStationMessage)}.TradeStationStreamStatus: {json}");
                    break;
            }
        }
    }

    /// <summary>
    /// Converts the given <see cref="OrderDirection"/> and <see cref="SecurityType"/> to a <see cref="TradeStationTradeActionType"/>.
    /// </summary>
    /// <param name="securityType">The type of security (e.g., Equity, Option, Future).</param>
    /// <param name="leanOrderDirection">The direction of the order (Buy or Sell).</param>
    /// <param name="holdingQuantity">The quantity of holdings.</param>
    /// <returns>
    /// A <see cref="TradeStationTradeActionType"/> that represents the trade action type for TradeStation.
    /// For Futures, returns Buy if the order direction is Buy, otherwise returns Sell.
    /// For Equities or Options, calls <see cref="GetOrderPosition(OrderDirection, decimal)"/> to determine the trade action type.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when an unsupported <see cref="SecurityType"/> is provided.</exception>
    private static TradeStationTradeActionType ConvertDirection(SecurityType securityType, OrderDirection leanOrderDirection, decimal holdingQuantity)
    {
        switch (securityType)
        {
            case SecurityType.Equity:
            case SecurityType.Option:
                return GetOrderPosition(leanOrderDirection, holdingQuantity).ConvertDirection(securityType);
            default:
                return leanOrderDirection == OrderDirection.Buy ? TradeStationTradeActionType.Buy : TradeStationTradeActionType.Sell;
        }
    }

    private class ModulesReadLicenseRead : QuantConnect.Api.RestResponse
    {
        [JsonProperty(PropertyName = "license")]
        public string License;
        [JsonProperty(PropertyName = "organizationId")]
        public string OrganizationId;
    }

    /// <summary>
    /// Validate the user of this project has permission to be using it via our web API.
    /// </summary>
    private static void ValidateSubscription()
    {
        try
        {
            const int productId = 346;
            var userId = Globals.UserId;
            var token = Globals.UserToken;
            var organizationId = Globals.OrganizationID;
            // Verify we can authenticate with this user and token
            var api = new ApiConnection(userId, token);
            if (!api.Connected)
            {
                throw new ArgumentException("Invalid api user id or token, cannot authenticate subscription.");
            }
            // Compile the information we want to send when validating
            var information = new Dictionary<string, object>()
                {
                    {"productId", productId},
                    {"machineName", Environment.MachineName},
                    {"userName", Environment.UserName},
                    {"domainName", Environment.UserDomainName},
                    {"os", Environment.OSVersion}
                };
            // IP and Mac Address Information
            try
            {
                var interfaceDictionary = new List<Dictionary<string, object>>();
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(nic => nic.OperationalStatus == OperationalStatus.Up))
                {
                    var interfaceInformation = new Dictionary<string, object>();
                    // Get UnicastAddresses
                    var addresses = nic.GetIPProperties().UnicastAddresses
                        .Select(uniAddress => uniAddress.Address)
                        .Where(address => !IPAddress.IsLoopback(address)).Select(x => x.ToString());
                    // If this interface has non-loopback addresses, we will include it
                    if (!addresses.IsNullOrEmpty())
                    {
                        interfaceInformation.Add("unicastAddresses", addresses);
                        // Get MAC address
                        interfaceInformation.Add("MAC", nic.GetPhysicalAddress().ToString());
                        // Add Interface name
                        interfaceInformation.Add("name", nic.Name);
                        // Add these to our dictionary
                        interfaceDictionary.Add(interfaceInformation);
                    }
                }
                information.Add("networkInterfaces", interfaceDictionary);
            }
            catch (Exception)
            {
                // NOP, not necessary to crash if fails to extract and add this information
            }
            // Include our OrganizationId is specified
            if (!string.IsNullOrEmpty(organizationId))
            {
                information.Add("organizationId", organizationId);
            }
            var request = new RestRequest("modules/license/read", Method.POST) { RequestFormat = DataFormat.Json };
            request.AddParameter("application/json", JsonConvert.SerializeObject(information), ParameterType.RequestBody);
            api.TryRequest(request, out ModulesReadLicenseRead result);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Request for subscriptions from web failed, Response Errors : {string.Join(',', result.Errors)}");
            }

            var encryptedData = result.License;
            // Decrypt the data we received
            DateTime? expirationDate = null;
            long? stamp = null;
            bool? isValid = null;
            if (encryptedData != null)
            {
                // Fetch the org id from the response if we are null, we need it to generate our validation key
                if (string.IsNullOrEmpty(organizationId))
                {
                    organizationId = result.OrganizationId;
                }
                // Create our combination key
                var password = $"{token}-{organizationId}";
                var key = SHA256.HashData(Encoding.UTF8.GetBytes(password));
                // Split the data
                var info = encryptedData.Split("::");
                var buffer = Convert.FromBase64String(info[0]);
                var iv = Convert.FromBase64String(info[1]);
                // Decrypt our information
                using var aes = new AesManaged();
                var decryptor = aes.CreateDecryptor(key, iv);
                using var memoryStream = new MemoryStream(buffer);
                using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
                using var streamReader = new StreamReader(cryptoStream);
                var decryptedData = streamReader.ReadToEnd();
                if (!decryptedData.IsNullOrEmpty())
                {
                    var jsonInfo = JsonConvert.DeserializeObject<JObject>(decryptedData);
                    expirationDate = jsonInfo["expiration"]?.Value<DateTime>();
                    isValid = jsonInfo["isValid"]?.Value<bool>();
                    stamp = jsonInfo["stamped"]?.Value<int>();
                }
            }
            // Validate our conditions
            if (!expirationDate.HasValue || !isValid.HasValue || !stamp.HasValue)
            {
                throw new InvalidOperationException("Failed to validate subscription.");
            }

            var nowUtc = DateTime.UtcNow;
            var timeSpan = nowUtc - Time.UnixTimeStampToDateTime(stamp.Value);
            if (timeSpan > TimeSpan.FromHours(12))
            {
                throw new InvalidOperationException("Invalid API response.");
            }
            if (!isValid.Value)
            {
                throw new ArgumentException($"Your subscription is not valid, please check your product subscriptions on our website.");
            }
            if (expirationDate < nowUtc)
            {
                throw new ArgumentException($"Your subscription expired {expirationDate}, please renew in order to use this product.");
            }
        }
        catch (Exception e)
        {
            Log.Error($"ValidateSubscription(): Failed during validation, shutting down. Error : {e.Message}");
            Environment.Exit(1);
        }
    }
}
