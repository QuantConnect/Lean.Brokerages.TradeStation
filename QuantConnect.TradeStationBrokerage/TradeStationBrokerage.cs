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
using QuantConnect.Data;
using QuantConnect.Orders;
using Newtonsoft.Json.Linq;
using System.Globalization;
using QuantConnect.Logging;
using System.Threading.Tasks;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Orders.Fees;
using System.Collections.Generic;
using QuantConnect.Configuration;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using QuantConnect.Brokerages.CrossZero;
using QuantConnect.Brokerages.TradeStation.Api;
using QuantConnect.Brokerages.TradeStation.Models;
using TimeInForce = QuantConnect.Orders.TimeInForce;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation;

/// <summary>
/// Represents the TradeStation Brokerage implementation.
/// </summary>
[BrokerageFactory(typeof(TradeStationBrokerageFactory))]
public partial class TradeStationBrokerage : Brokerage
{
    private bool _isInitialized;
    private Exception _lastError;

    /// <summary>
    /// TradeStation api client implementation
    /// </summary>
    private TradeStationApiClient _tradeStationApiClient;

    /// <summary>
    /// Provides the mapping between Lean symbols and brokerage specific symbols.
    /// </summary>
    private TradeStationSymbolMapper _symbolMapper;

    /// <summary>
    /// Indicates whether the application is subscribed to stream order updates.
    /// </summary>
    private bool _isSubscribeOnStreamOrderUpdate;

    /// <summary>
    /// Signals to a <see cref="CancellationToken"/> that it should be canceled.
    /// </summary>
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    /// <summary>
    /// Represents an AutoResetEvent synchronization primitive used to signal when the brokerage connection is established.
    /// </summary>
    private readonly AutoResetEvent _autoResetEvent = new(false);

    /// <summary>
    /// A manual reset event that is used to signal the completion of an order update operation.
    /// </summary>
    private readonly ManualResetEvent _orderUpdateEndManualResetEvent = new(false);

    /// <summary>
    /// A thread-safe dictionary to track the response result submission status by brokerage ID.
    /// </summary>
    /// <remarks>
    /// This dictionary uses brokerage IDs as keys (of type <see cref="string"/>) 
    /// and a boolean value as the value to indicate whether the response result has been 
    /// submitted (<see langword="true"/>) or not (<see langword="false"/>).
    /// </remarks>
    private ConcurrentDictionary<string, bool> _updateSubmittedResponseResultByBrokerageID = new();

    /// <summary>
    /// A concurrent dictionary to store the order ID and the corresponding filled quantity.
    /// </summary>
    private ConcurrentDictionary<int, decimal> _orderIdToFillQuantity = new();

    /// <summary>
    /// Provides a thread-safe service for caching and managing original orders when they are part of a group.
    /// </summary>
    private GroupOrderCacheManager _groupOrderCacheManager = new();

    /// <summary>
    /// Specifies the type of account on TradeStation in current session.
    /// </summary>
    private TradeStationAccountType _tradeStationAccountType;

    /// <summary>
    /// Represents a type capable of fetching the holdings for the specified symbol
    /// </summary>
    protected ISecurityProvider SecurityProvider { get; private set; }

    /// <summary>
    /// Brokerage helper class to lock message stream while executing an action, for example placing an order
    /// </summary>
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
    /// Parameterless constructor for brokerage
    /// </summary>
    public TradeStationBrokerage() : base("TradeStation")
    {
    }

    /// <summary>
    /// Constructor for the TradeStation brokerage.
    /// </summary>
    /// <remarks>
    /// This constructor initializes a new instance of the TradeStationBrokerage class with the provided parameters.
    /// </remarks>
    /// <param name="clientId">The API key for authentication.</param>
    /// <param name="apiKeySecret">The API key secret for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="redirectUrl">The redirect URL to generate great link to get right "authorizationCodeFromUrl"</param>
    /// <param name="authorizationCode">The authorization code obtained from the URL.</param>
    /// <param name="accountType">The type of TradeStation account for the current session.
    /// For <see cref="TradeStationAccountType.Cash"/> or <seealso cref="TradeStationAccountType.Margin"/> accounts,
    /// it is used for trading <seealso cref="SecurityType.Equity"/> and <seealso cref="SecurityType.Option"/>.
    /// For <seealso cref="TradeStationAccountType.Futures"/> accounts, it is used for trading <seealso cref="SecurityType.Future"/> contracts.</param>
    /// <param name="algorithm">The algorithm instance is required to retrieve account type</param>
    public TradeStationBrokerage(string clientId, string apiKeySecret, string restApiUrl, string redirectUrl, string authorizationCode,
        string accountType, IAlgorithm algorithm)
        : this(clientId, apiKeySecret, restApiUrl, redirectUrl, authorizationCode, string.Empty, accountType, algorithm?.Portfolio?.Transactions, algorithm?.Portfolio)
    { }

    /// <summary>
    /// Constructor for the TradeStation brokerage.
    /// </summary>
    /// <remarks>
    /// This constructor initializes a new instance of the TradeStationBrokerage class with the provided parameters.
    /// </remarks>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="apiKeySecret">The API key secret for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="refreshToken">The refresh token used to obtain new access tokens for authentication.</param>
    /// <param name="accountType">The type of TradeStation account for the current session.
    /// For <see cref="TradeStationAccountType.Cash"/> or <seealso cref="TradeStationAccountType.Margin"/> accounts,
    /// it is used for trading <seealso cref="SecurityType.Equity"/> and <seealso cref="SecurityType.Option"/>.
    /// For <seealso cref="TradeStationAccountType.Futures"/> accounts, it is used for trading <seealso cref="SecurityType.Future"/> contracts.</param>
    /// <param name="algorithm">The algorithm instance is required to retrieve account type</param>
    public TradeStationBrokerage(string apiKey, string apiKeySecret, string restApiUrl, string refreshToken, string accountType, IAlgorithm algorithm)
        : this(apiKey, apiKeySecret, restApiUrl, string.Empty, string.Empty, refreshToken, accountType, algorithm?.Portfolio?.Transactions, algorithm?.Portfolio)
    { }

    /// <summary>
    /// Constructor for the TradeStation brokerage.
    /// </summary>
    /// <remarks>
    /// This constructor initializes a new instance of the TradeStationBrokerage class with the provided parameters.
    /// </remarks>
    /// <param name="clientId">The API key for authentication.</param>
    /// <param name="clientSecret">The API key secret for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="redirectUrl">The redirect URL to generate great link to get right "authorizationCodeFromUrl"</param>
    /// <param name="authorizationCode">The authorization code obtained from the URL.</param>
    /// <param name="refreshToken">The refresh token used to obtain new access tokens for authentication.</param>
    /// <param name="accountType">The type of TradeStation account for the current session.
    /// For <see cref="TradeStationAccountType.Cash"/> or <seealso cref="TradeStationAccountType.Margin"/> accounts, it is used for trading <seealso cref="SecurityType.Equity"/> and <seealso cref="SecurityType.Option"/>.
    /// For <seealso cref="TradeStationAccountType.Futures"/> accounts, it is used for trading <seealso cref="SecurityType.Future"/> contracts.</param>
    /// <param name="orderProvider">The order provider.</param>
    /// <param name="securityProvider">The type capable of fetching the holdings for the specified symbol</param>
    public TradeStationBrokerage(string clientId, string clientSecret, string restApiUrl, string redirectUrl,
        string authorizationCode, string refreshToken, string accountType, IOrderProvider orderProvider, ISecurityProvider securityProvider)
        : base("TradeStation")
    {
        Initialize(clientId, clientSecret, restApiUrl, redirectUrl, authorizationCode, refreshToken, accountType, orderProvider, securityProvider);
    }

    protected void Initialize(string clientId, string clientSecret, string restApiUrl, string redirectUrl, string authorizationCode,
        string refreshToken, string accountType, IOrderProvider orderProvider, ISecurityProvider securityProvider)
    {
        if (_isInitialized)
        {
            return;
        }
        _isInitialized = true;
        SecurityProvider = securityProvider;
        OrderProvider = orderProvider;
        _symbolMapper = new TradeStationSymbolMapper();
        _tradeStationAccountType = TradeStationExtensions.ParseAccountType(accountType);
        _tradeStationApiClient = new TradeStationApiClient(clientId, clientSecret, restApiUrl,
            _tradeStationAccountType, refreshToken, redirectUrl, authorizationCode);
        _messageHandler = new(HandleTradeStationMessage);

        SubscriptionManager = new EventBasedDataQueueHandlerSubscriptionManager()
        {
            SubscribeImpl = (symbols, _) => Subscribe(symbols),
            UnsubscribeImpl = (symbols, _) => UnSubscribe(symbols)
        };

        _aggregator = Composer.Instance.GetPart<IDataAggregator>();
        if (_aggregator == null)
        {
            // toolbox downloader case
            var aggregatorName = Config.Get("data-aggregator", "QuantConnect.Lean.Engine.DataFeeds.AggregationManager");
            Log.Trace($"AlpacaBrokerage.AlpacaBrokerage(): found no data aggregator instance, creating {aggregatorName}");
            _aggregator = Composer.Instance.GetExportedValueByTypeName<IDataAggregator>(aggregatorName);
        }

        ValidateSubscription();
    }

    #region Brokerage

    /// <summary>
    /// Gets all open orders on the account.
    /// NOTE: The order objects returned do not have QC order IDs.
    /// </summary>
    /// <returns>The open orders returned from TradeStation</returns>
    public override List<Order> GetOpenOrders()
    {
        var orders = _tradeStationApiClient.GetOrders().SynchronouslyAwaitTaskResult();
        var leanOrders = new List<Order>();

        foreach (var order in orders.Orders.Where(o => o.Status is TradeStationOrderStatusType.Ack or TradeStationOrderStatusType.Don))
        {
            if (order.Legs.Count == 1)
            {
                var leg = order.Legs.First();
                leanOrders.Add(CreateLeanOrder(order, leg));
            }
            else
            {
                var totalLegShares = order.Legs.Sum(leg => leg.QuantityOrdered);

                var groupOrderManager = new GroupOrderManager(order.Legs.Count, totalLegShares);

                foreach (var leg in order.Legs)
                {
                    leanOrders.Add(CreateLeanOrder(order, leg, groupOrderManager));
                }
            }
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

            if (leanSymbol.SecurityType is SecurityType.Future or SecurityType.Option && leanSymbol.ID.Date.Date < DateTime.UtcNow.ConvertFromUtc(leanSymbol.GetSymbolExchangeTimeZone()).Date)
            {
                Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(GetAccountHoldings)}: The {leanSymbol} was expired and skipped.");
                continue;
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
        else if (!IsRightAccountForSymbolSecurityType(order.Symbol.SecurityType))
        {
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1,
                $"Unable to process the order. The security type '{order.Symbol.SecurityType}' does not match the account type '{_tradeStationAccountType}'. Please check your account settings and try again."));
            return false;
        }

        var result = default(bool);
        _messageHandler.WithLockedStream(() =>
        {
            var holdingQuantity = SecurityProvider.GetHoldingsQuantity(order.Symbol);

            var isPlaceCrossOrder = TryCrossZeroPositionOrder(order, holdingQuantity);

            if (isPlaceCrossOrder == null)
            {
                if (!_groupOrderCacheManager.TryGetGroupCachedOrders(order, out var orders))
                {
                    result = true;
                    return;
                }

                var response = PlaceTradeStationOrder(orders, holdingQuantity);
                result = response != null && response.Value.Orders.Count > 0;
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
    /// <param name="orders">The collection orders to be placed.</param>
    /// <param name="holdingQuantity">The holding quantity associated with the order.</param>
    /// <param name="isSubmittedEvent">Indicates if the order submission event should be triggered.</param>
    /// <returns>A response from TradeStation after placing the order.</returns>
    private TradeStationPlaceOrderResponse? PlaceTradeStationOrder(IReadOnlyCollection<Order> orders, decimal holdingQuantity, bool isSubmittedEvent = true)
    {
        var order = orders.First();
        switch (order.Type)
        {
            case OrderType.ComboMarket:
            case OrderType.ComboLimit:
                return PlaceOrderCommon(orders, order.Type, order.TimeInForce, 0m, "", "", order.GetLimitPrice(), 0m, isSubmittedEvent);
            case OrderType.Market:
            case OrderType.Limit:
            case OrderType.StopMarket:
            case OrderType.StopLimit:
                var symbol = _symbolMapper.GetBrokerageSymbol(order.Symbol);
                var tradeAction = ConvertDirection(order.SecurityType, order.Direction, holdingQuantity);
                return PlaceOrderCommon(orders, order.Type, order.TimeInForce, order.AbsoluteQuantity, tradeAction, symbol, order.GetLimitPrice(), order.GetStopPrice(), isSubmittedEvent);
            default:
                throw new NotSupportedException($"{nameof(TradeStationBrokerage)}.{nameof(PlaceTradeStationOrder)}:" +
                    $" The order type '{order.Type}' is not supported for conversion to TradeStation order type.");
        };
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
        var tradeAction = ConvertDirection(crossZeroOrderRequest.LeanOrder.SecurityType, crossZeroOrderRequest.OrderPosition);

        var crossZeroOrderResponse = default(CrossZeroOrderResponse);
        _messageHandler.WithLockedStream(() =>
        {
            var response = PlaceOrderCommon(new List<Order> { crossZeroOrderRequest.LeanOrder }, crossZeroOrderRequest.OrderType, crossZeroOrderRequest.LeanOrder.TimeInForce,
                crossZeroOrderRequest.AbsoluteOrderQuantity, tradeAction, symbol, crossZeroOrderRequest.LeanOrder.GetLimitPrice(), crossZeroOrderRequest.LeanOrder.GetStopPrice(), isPlaceOrderWithLeanEvent);

            if (response == null || !response.Value.Orders.Any())
            {
                crossZeroOrderResponse = new CrossZeroOrderResponse(string.Empty, false);
                return;
            }

            var brokerageId = response.Value.Orders.Single().OrderID;
            crossZeroOrderResponse = new CrossZeroOrderResponse(brokerageId, true);
        });
        return crossZeroOrderResponse;
    }

    /// <summary>
    /// Places a common order.
    /// </summary>
    /// <param name="orders">The collection orders to be placed.</param>
    /// <param name="orderType">The type of the order.</param>
    /// <param name="timeInForce">The time in force for the order.</param>
    /// <param name="quantity">The quantity of the order.</param>
    /// <param name="tradeAction">The trade action (BUY/SELL) of the order.</param>
    /// <param name="symbol">The symbol for the order.</param>
    /// <param name="limitPrice">The limit price for the order, if applicable.</param>
    /// <param name="stopPrice">The stop price for the order, if applicable.</param>
    /// <param name="isSubmittedEvent">Indicates if the order submission event should be triggered.</param>
    /// <returns>A response from TradeStation after placing the order.</returns>
    private TradeStationPlaceOrderResponse? PlaceOrderCommon(IReadOnlyCollection<Order> orders, OrderType orderType, TimeInForce timeInForce, decimal quantity, string tradeAction,
        string symbol, decimal? limitPrice, decimal? stopPrice, bool isSubmittedEvent)
    {
        var response = default(TradeStationPlaceOrderResponse);
        var properties = orders.First().Properties as TradeStationOrderProperties;
        if (orders.Count == 1)
        {
            response = _tradeStationApiClient.PlaceOrder(orderType, timeInForce, quantity, tradeAction, symbol, limitPrice: limitPrice, stopPrice: stopPrice, tradeStationOrderProperties: properties).SynchronouslyAwaitTaskResult();
        }
        else
        {
            var orderLegs = CreateOrderLegs(orders);
            response = _tradeStationApiClient.PlaceOrder(orderType, timeInForce, legs: orderLegs, limitPrice: limitPrice, tradeStationOrderProperties: properties).SynchronouslyAwaitTaskResult();
        }

        foreach (var brokerageOrder in response.Orders)
        {
            var exceptOneFailed = default(bool);
            foreach (var order in orders)
            {
                // Check if the order failed due to an existing position. Reason: [EC601,EC602,EC701,EC702]: You are long/short N shares.
                if (brokerageOrder.Message.Contains("Order failed", StringComparison.InvariantCultureIgnoreCase))
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, $"{nameof(TradeStationBrokerage)} Order Event")
                    { Status = OrderStatus.Invalid, Message = brokerageOrder.Message });
                    exceptOneFailed = true;
                    continue;
                }

                if (string.IsNullOrEmpty(brokerageOrder.OrderID))
                {
                    // die
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, -1, $"Brokerage OrderId not found for {order.Id}: {brokerageOrder.Message}"));
                }

                if (!order.BrokerId.Contains(brokerageOrder.OrderID))
                {
                    order.BrokerId.Add(brokerageOrder.OrderID);
                }

                if (isSubmittedEvent)
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, $"{nameof(TradeStationBrokerage)} Order Event")
                    { Status = OrderStatus.Submitted });
                }
            }

            if (exceptOneFailed)
            {
                return null;
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
        var holdingQuantity = SecurityProvider.GetHoldingsQuantity(order.Symbol);

        if (!TryGetUpdateCrossZeroOrderQuantity(order, out var orderQuantity))
        {
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1, $"{nameof(TradeStationBrokerage)}.{nameof(UpdateOrder)}: Unable to modify order quantities."));
            return false;
        }

        if (!_groupOrderCacheManager.TryGetGroupCachedOrders(order, out var orders))
        {
            return true;
        }

        // Always use the first order in the group, as combo orders determine direction based on the first order's details.
        order = orders.First();
        var brokerageOrderId = order.BrokerId.Last();

        var response = default(bool);
        _messageHandler.WithLockedStream(() =>
        {
            try
            {
                var result = _tradeStationApiClient.ReplaceOrder(brokerageOrderId, order.Type, Math.Abs(orderQuantity), order.GetLimitPrice(), order.GetStopPrice()).SynchronouslyAwaitTaskResult();

                foreach (var order in orders)
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, $"{nameof(TradeStationBrokerage)}.{nameof(UpdateOrder)} Order Event")
                    {
                        Status = OrderStatus.UpdateSubmitted
                    });
                }
                response = true;
                _updateSubmittedResponseResultByBrokerageID[brokerageOrderId] = true;
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
        if (!_groupOrderCacheManager.TryGetGroupCachedOrders(order, out var orders))
        {
            return true;
        }

        var brokerageOrderId = order.BrokerId.Last();

        var result = default(bool);
        _messageHandler.WithLockedStream(() =>
        {
            if (_tradeStationApiClient.CancelOrder(brokerageOrderId).SynchronouslyAwaitTaskResult())
            {
                result = true;
            }
        });
        return result;
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
        if (!_isSubscribeOnStreamOrderUpdate && _lastError != null)
        {
            // we were not able to connect and there's an exception, let's bubble it up
            throw _lastError;
        }
    }

    /// <summary>
    /// Disconnects the client from the broker's remote servers
    /// </summary>
    public override void Disconnect()
    {
        _cancellationTokenSource.Cancel();
        if (!_orderUpdateEndManualResetEvent.WaitOne(TimeSpan.FromSeconds(5)))
        {
            Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(Disconnect)}: TimeOut waiting for stream order task to end.");
        }
        StopQuoteStreamingTask(updateCancellationToken: false);
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
    /// Determines if the provided <paramref name="securityType"/> matches the <see cref="TradeStationAccountType"/>.
    /// </summary>
    /// <param name="securityType">The type of security to check.</param>
    /// <returns>
    /// <c>true</c> if the security type is <see cref="SecurityType.Future"/> and the account type is <see cref="TradeStationAccountType.Futures"/>;
    /// otherwise, <c>true</c>.
    /// </returns>
    private bool IsRightAccountForSymbolSecurityType(SecurityType securityType) => securityType switch
    {
        SecurityType.Future => _tradeStationAccountType == TradeStationAccountType.Futures,
        _ => _tradeStationAccountType != TradeStationAccountType.Futures
    };

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
                _isSubscribeOnStreamOrderUpdate = false;
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
                    _lastError = ex;
                    Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnOrderUpdate)}.Exception: {ex}");
                }
                Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnOrderUpdate)}: Connection lost. Reconnecting in 10 seconds...");
                _cancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            }
            _orderUpdateEndManualResetEvent.Set();
        }, _cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        return _autoResetEvent.WaitOne(TimeSpan.FromSeconds(25), _cancellationTokenSource.Token);
    }

    /// <summary>
    /// Handles incoming TradeStation messages in JSON format.
    /// </summary>
    /// <param name="json">The JSON string containing the TradeStation message.</param>
    private void HandleTradeStationMessage(string json)
    {
        if (OrderProvider == null)
        {
            // we are used as a data source only, not a brokerage
            return;
        }

        try
        {
            var jObj = JObject.Parse(json);
            if (_isSubscribeOnStreamOrderUpdate && jObj["AccountID"] != null)
            {
                var brokerageOrder = jObj.ToObject<TradeStationOrder>();

                var leanOrderStatus = default(OrderStatus);
                switch (brokerageOrder.Status)
                {
                    case TradeStationOrderStatusType.Ack:
                        // Remove the order entry when the order is acknowledged (indicating successful submission)
                        _updateSubmittedResponseResultByBrokerageID.TryRemove(new(brokerageOrder.OrderID, true));
                        return;
                    // Sometimes, a filled event is received without the ClosedDateTime property set. 
                    // Subsequently, another event is received with the ClosedDateTime property correctly populated.
                    case TradeStationOrderStatusType.Fll when brokerageOrder.ClosedDateTime != default:
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
                    // Sometimes, a Out event is received without the ClosedDateTime property set. 
                    // Subsequently, another event is received with the ClosedDateTime property correctly populated.
                    case TradeStationOrderStatusType.Out when brokerageOrder.ClosedDateTime != default:
                        // Remove the order entry if it was marked as submitted but is now out
                        // Sometimes, the order receives an "Out" status on every even occurrence
                        if (_updateSubmittedResponseResultByBrokerageID.TryRemove(new(brokerageOrder.OrderID, true)))
                        {
                            return;
                        }
                        leanOrderStatus = OrderStatus.Canceled;
                        break;
                    default:
                        Log.Debug($"{nameof(TradeStationBrokerage)}.{nameof(HandleTradeStationMessage)}.TradeStationStreamStatus: {json}");
                        return;
                };

                var leanOrders = new List<Order>();
                if (!TryGetOrRemoveCrossZeroOrder(brokerageOrder.OrderID, leanOrderStatus, out var crossZeroLeanOrder))
                {
                    leanOrders = OrderProvider.GetOrdersByBrokerageId(brokerageOrder.OrderID);
                }
                else
                {
                    leanOrders.Add(crossZeroLeanOrder);
                }

                if (leanOrders == null || leanOrders.Count == 0)
                {
                    Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(HandleTradeStationMessage)}. order id not found: {brokerageOrder.OrderID}");
                    return;
                }

                foreach (var leg in brokerageOrder.Legs)
                {
                    var leanSymbol = _symbolMapper.GetLeanSymbol(leg.Underlying ?? leg.Symbol, leg.AssetType.ConvertAssetTypeToSecurityType(), Market.USA,
                        leg.ExpirationDate, leg.StrikePrice, leg.OptionType.ConvertOptionTypeToOptionRight());

                    // Ensure there is an order with the specific symbol in leanOrders.
                    var leanOrder = leanOrders.FirstOrDefault(order => order.Symbol == leanSymbol);

                    if (leanOrder == null)
                    {
                        Log.Error($"Error in {nameof(TradeStationBrokerage)}.{nameof(HandleTradeStationMessage)}: " +
                            $"Could not find order with symbol '{leanSymbol}' in leanOrders. " +
                            $"Brokerage Order ID: {brokerageOrder.OrderID}. " +
                            $"Leg details - Symbol: {leg.Symbol}, Underlying: {leg.Underlying}, " +
                            $"Asset Type: {leg.AssetType}, Expiration Date: {leg.ExpirationDate}, " +
                            $"Strike Price: {leg.StrikePrice}, Option Type: {leg.OptionType}. " +
                            $"Please verify that the order was correctly added to leanOrders.");
                        return;
                    }

                    // TradeStation sends the accumulative filled quantity but we need the partial amount for our event
                    _orderIdToFillQuantity.TryGetValue(leanOrder.Id, out var previousExecutionAmount);
                    var accumulativeFilledQuantity = _orderIdToFillQuantity[leanOrder.Id] = leg.BuyOrSell.IsShort() ? decimal.Negate(leg.ExecQuantity) : leg.ExecQuantity;

                    if (leanOrderStatus.IsClosed())
                    {
                        _orderIdToFillQuantity.TryRemove(leanOrder.Id, out _);
                    }

                    var orderEvent = new OrderEvent(
                        leanOrder,
                        DateTime.UtcNow,
                        new OrderFee(new CashAmount(brokerageOrder.CommissionFee, Currencies.USD)),
                        brokerageOrder.RejectReason)
                    {
                        Status = leanOrderStatus,
                        FillPrice = leg.ExecutionPrice,
                        FillQuantity = accumulativeFilledQuantity - previousExecutionAmount
                    };

                    // if we filled the order and have another contingent order waiting, submit it
                    if (!TryHandleRemainingCrossZeroOrder(leanOrder, orderEvent))
                    {
                        OnOrderEvent(orderEvent);
                    }
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
                    default:
                        Log.Debug($"{nameof(TradeStationBrokerage)}.{nameof(HandleTradeStationMessage)}.TradeStationStreamStatus: {json}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Raw json: {json}");
            throw;
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
    /// For Futures, returns <see cref="TradeStationTradeActionType.Buy"/> if the order direction is Buy, otherwise returns <see cref="TradeStationTradeActionType.Sell"/>.
    /// For Equities or Options, calls <see cref="GetOrderPosition(OrderDirection, decimal)"/> to determine the trade action type.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when an unsupported <see cref="SecurityType"/> is provided.</exception>
    /// <exception cref="NotSupportedException">Thrown when an unsupported order position is provided.</exception>
    private static string ConvertDirection(SecurityType securityType, OrderDirection leanOrderDirection, decimal holdingQuantity)
    {
        return ConvertDirection(securityType, GetOrderPosition(leanOrderDirection, holdingQuantity));
    }

    private static string ConvertDirection(SecurityType securityType, OrderPosition orderPosition)
    {
        var tradeAction = default(TradeStationTradeActionType);
        switch (securityType)
        {
            case SecurityType.Equity:
            case SecurityType.Option:
                switch (orderPosition)
                {
                    // Increasing existing long position or opening new long position from zero
                    case OrderPosition.BuyToOpen:
                        tradeAction = securityType == SecurityType.Option ? TradeStationTradeActionType.BuyToOpen : TradeStationTradeActionType.Buy;
                        break;
                    // Decreasing existing short position or opening new short position from zero
                    case OrderPosition.SellToOpen:
                        tradeAction = securityType == SecurityType.Option ? TradeStationTradeActionType.SellToOpen : TradeStationTradeActionType.SellShort;
                        break;
                    // Buying from an existing short position (reducing, closing or flipping)
                    case OrderPosition.BuyToClose:
                        tradeAction = securityType == SecurityType.Option ? TradeStationTradeActionType.BuyToClose : TradeStationTradeActionType.BuyToCover;
                        break;
                    // Selling from an existing long position (reducing, closing or flipping)
                    case OrderPosition.SellToClose:
                        tradeAction = securityType == SecurityType.Option ? TradeStationTradeActionType.SellToClose : TradeStationTradeActionType.Sell;
                        break;
                    // This should never happen
                    default:
                        throw new NotSupportedException("The specified order position is not supported.");
                };
                break;
            default:
                // futures are just buy or sell
                tradeAction = (orderPosition == OrderPosition.BuyToOpen || orderPosition == OrderPosition.BuyToClose)
                    ? TradeStationTradeActionType.Buy : TradeStationTradeActionType.Sell;
                break;
        }
        return tradeAction.ToStringInvariant().ToUpperInvariant();
    }

    /// <summary>
    /// Creates a collection of TradeStation order legs and determines the group limit price for the orders.
    /// </summary>
    /// <param name="orders">
    /// A collection of <see cref="Order"/> objects representing the orders to be processed.
    /// </param>
    /// <returns>
    /// A tuple containing a read-only collection of <see cref="TradeStationPlaceOrderLeg"/> representing the order legs.
    /// </returns>
    private IReadOnlyCollection<TradeStationPlaceOrderLeg> CreateOrderLegs(IReadOnlyCollection<Order> orders)
    {
        var legs = new List<TradeStationPlaceOrderLeg>();
        foreach (var order in orders)
        {
            var holdingQuantity = SecurityProvider.GetHoldingsQuantity(order.Symbol);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(order.Symbol);

            var tradeActionMultiple = default(string);
            if (order.Symbol.SecurityType == SecurityType.Equity)
            {
                tradeActionMultiple = GetOrderPosition(order.Direction, holdingQuantity).ToStringInvariant().ToUpperInvariant();
            }
            else
            {
                tradeActionMultiple = ConvertDirection(order.SecurityType, order.Direction, holdingQuantity);
            }
            legs.Add(new TradeStationPlaceOrderLeg(order.AbsoluteQuantity.ToStringInvariant(), brokerageSymbol, tradeActionMultiple));
        }

        return legs;
    }

    /// <summary>
    /// Creates a Lean order based on the given TradeStation order and leg details.
    /// </summary>
    /// <param name="order">The TradeStation order containing overall order information.</param>
    /// <param name="leg">The specific leg of the order, representing the individual component of a multi-leg order.</param>
    /// <param name="groupOrderManager">The manager responsible for coordinating multi-leg group orders.</param>
    /// <returns>A Lean <see cref="Order"/> object that corresponds to the provided TradeStation order and leg.</returns>
    /// <exception cref="NotSupportedException">Thrown when the TradeStation order type is not supported by this method.</exception>
    private Order CreateLeanOrder(TradeStationOrder order, Models.Leg leg, GroupOrderManager groupOrderManager = null)
    {
        var orderQuantity = leg.BuyOrSell.IsShort() ? decimal.Negate(leg.QuantityOrdered) : leg.QuantityOrdered;
        var leanSymbol = _symbolMapper.GetLeanSymbol(leg.Underlying ?? leg.Symbol, leg.AssetType.ConvertAssetTypeToSecurityType(), Market.USA,
                                                      leg.ExpirationDate, leg.StrikePrice, leg.OptionType.ConvertOptionTypeToOptionRight());

        Order leanOrder = order.OrderType switch
        {
            TradeStationOrderType.Market when groupOrderManager == null => new MarketOrder(leanSymbol, orderQuantity, order.OpenedDateTime),
            TradeStationOrderType.Market when groupOrderManager != null => new ComboMarketOrder(leanSymbol, orderQuantity, order.OpenedDateTime, groupOrderManager),
            TradeStationOrderType.Limit when groupOrderManager == null => new LimitOrder(leanSymbol, orderQuantity, order.LimitPrice, order.OpenedDateTime),
            TradeStationOrderType.Limit when groupOrderManager != null => new ComboLimitOrder(leanSymbol, orderQuantity, order.LimitPrice, order.OpenedDateTime, groupOrderManager),
            TradeStationOrderType.StopMarket => new StopMarketOrder(leanSymbol, orderQuantity, order.StopPrice, order.OpenedDateTime),
            TradeStationOrderType.StopLimit => new StopLimitOrder(leanSymbol, orderQuantity, order.StopPrice, order.LimitPrice, order.OpenedDateTime),
            _ => throw new NotSupportedException($"Unsupported order type: {order.OrderType}")
        };

        return leanOrder.SetOrderStatusAndBrokerId(order, leg);
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
