﻿/*
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
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Util;
using QuantConnect.Orders;
using QuantConnect.Packets;
using System.Globalization;
using QuantConnect.Logging;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Orders.Fees;
using System.Collections.Generic;
using QuantConnect.Brokerages.TradeStation.Api;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation;

[BrokerageFactory(typeof(TradeStationBrokerageFactory))]
public class TradeStationBrokerage : Brokerage, IDataQueueHandler, IDataQueueUniverseProvider
{
    /// <inheritdoc cref="TradeStationApiClient" />
    private readonly TradeStationApiClient _tradeStationApiClient;

    /// <inheritdoc cref="TradeStationSymbolMapper" />
    private TradeStationSymbolMapper _symbolMapper;

    private readonly IDataAggregator _aggregator;
    private readonly EventBasedDataQueueHandlerSubscriptionManager _subscriptionManager;

    /// <summary>
    /// Returns true if we're currently connected to the broker
    /// </summary>
    public override bool IsConnected { get; }

    /// <summary>
    /// Constructor for the TradeStation brokerage.
    /// </summary>
    /// <remarks>
    /// This constructor initializes a new instance of the TradeStationBrokerage class with the provided parameters.
    /// </remarks>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="apiKeySecret">The API key secret for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="authorizationCodeFromUrl">The authorization code obtained from the URL.</param>
    /// <param name="useProxy">Boolean value indicating whether to use a proxy for TradeStation API requests. Default is false.</param>
    public TradeStationBrokerage(string apiKey, string apiKeySecret, string restApiUrl, string authorizationCodeFromUrl, bool useProxy)
        : this(apiKey, apiKeySecret, restApiUrl, authorizationCodeFromUrl, Composer.Instance.GetPart<IDataAggregator>(), useProxy)
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
    /// <param name="authorizationCodeFromUrl">The authorization code obtained from the URL.</param>
    /// <param name="aggregator">An instance of the data aggregator used for consolidate ticks.</param>
    /// <param name="useProxy">Boolean value indicating whether to use a proxy for TradeStation API requests. Default is false.</param>
    public TradeStationBrokerage(string apiKey, string apiKeySecret, string restApiUrl, string authorizationCodeFromUrl, IDataAggregator aggregator,
        bool useProxy = false)
        : base("TemplateBrokerage")
    {
        _tradeStationApiClient = new TradeStationApiClient(apiKey, apiKeySecret, restApiUrl, authorizationCodeFromUrl, useProxy: useProxy);

        _symbolMapper = new TradeStationSymbolMapper();

        _aggregator = aggregator;
        _subscriptionManager = new EventBasedDataQueueHandlerSubscriptionManager();
        _subscriptionManager.SubscribeImpl += (s, t) => Subscribe(s);
        _subscriptionManager.UnsubscribeImpl += (s, t) => Unsubscribe(s);

        // Useful for some brokerages:

        // Brokerage helper class to lock websocket message stream while executing an action, for example placing an order
        // avoid race condition with placing an order and getting filled events before finished placing
        // _messageHandler = new BrokerageConcurrentMessageHandler<>();

        // Rate gate limiter useful for API/WS calls
        // _connectionRateLimiter = new RateGate();
    }

    #region IDataQueueHandler

    /// <summary>
    /// Subscribe to the specified configuration
    /// </summary>
    /// <param name="dataConfig">defines the parameters to subscribe to a data feed</param>
    /// <param name="newDataAvailableHandler">handler to be fired on new data available</param>
    /// <returns>The new enumerator for this subscription request</returns>
    public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
    {
        if (!CanSubscribe(dataConfig.Symbol))
        {
            return null;
        }

        var enumerator = _aggregator.Add(dataConfig, newDataAvailableHandler);
        _subscriptionManager.Subscribe(dataConfig);

        return enumerator;
    }

    /// <summary>
    /// Removes the specified configuration
    /// </summary>
    /// <param name="dataConfig">Subscription config to be removed</param>
    public void Unsubscribe(SubscriptionDataConfig dataConfig)
    {
        _subscriptionManager.Unsubscribe(dataConfig);
        _aggregator.Remove(dataConfig);
    }

    /// <summary>
    /// Sets the job we're subscribing for
    /// </summary>
    /// <param name="job">Job we're subscribing for</param>
    public void SetJob(LiveNodePacket job)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region Brokerage

    /// <summary>
    /// Gets all open orders on the account.
    /// NOTE: The order objects returned do not have QC order IDs.
    /// </summary>
    /// <returns>The open orders returned from IB</returns>
    public override List<Order> GetOpenOrders()
    {
        var orders = _tradeStationApiClient.GetAllAccountOrders();

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
        var positions = _tradeStationApiClient.GetAllAccountPositions();

        foreach (var positionError in positions.Errors)
        {
            Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(GetAccountHoldings)}: Error encountered in Account ID: {positionError.AccountID}. Type: {positionError.Error}. Message: {positionError.Message}");
        }

        var holdings = new List<Holding>();
        foreach (var position in positions.Positions)
        {
            if (position.AssetType != TradeStationAssetType.Future)
            {
                continue;
            }

            var leanSymbol = _symbolMapper.GetLeanSymbol(SymbolRepresentation.ParseFutureTicker(position.Symbol).Underlying, SecurityType.Future, Market.USA, position.ExpirationDate);

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
        var balances = _tradeStationApiClient.GetAllAccountBalances();

        foreach (var balanceError in balances.Errors)
        {
            Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(GetCashBalance)}: Error encountered in Account ID: {balanceError.AccountID}. Type: {balanceError.Error}. Message: {balanceError.Message}");
        }

        var cashBalance = new List<CashAmount>();
        foreach (var balance in balances.Balances)
        {
            if (balance.AccountType != TradeStationAccountType.Margin)
            {
                continue;
            }

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

        var symbol = _symbolMapper.GetBrokerageSymbol(order.Symbol);
        var result = _tradeStationApiClient.PlaceOrder(order, symbol);

        foreach (var error in result.Errors ?? Enumerable.Empty<Models.TradeStationError>())
        {
            OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, $"{nameof(TradeStationBrokerage)} Order Event")
            { Status = OrderStatus.Invalid, Message = error.Message });
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "PlaceOrderInvalid", error.Message));
            return false;
        }

        foreach (var brokerageOrder in result.Orders)
        {
            order.BrokerId.Add(brokerageOrder.OrderID);

            OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, $"{nameof(TradeStationBrokerage)} Order Event")
            { Status = OrderStatus.Submitted });

            return true;
        }

        return false;
    }

    /// <summary>
    /// Updates the order with the same id
    /// </summary>
    /// <param name="order">The new order information</param>
    /// <returns>True if the request was made for the order to be updated, false otherwise</returns>
    public override bool UpdateOrder(Order order)
    {
        try
        {
            var result = _tradeStationApiClient.ReplaceOrder(order);

            OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, $"{nameof(TradeStationBrokerage)} Order Event")
            {
                Status = OrderStatus.UpdateSubmitted
            });

            return true;
        }
        catch (Exception exception)
        {
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, "UpdateOrderInvalid", exception.Message));
            return false;
        }
    }

    /// <summary>
    /// Cancels the order with the specified ID
    /// </summary>
    /// <param name="order">The order to cancel</param>
    /// <returns>True if the request was made for the order to be canceled, false otherwise</returns>
    public override bool CancelOrder(Order order)
    {
        var orderID = order.BrokerId.Single();
        return _tradeStationApiClient.CancelOrder(orderID);
    }

    /// <summary>
    /// Connects the client to the broker's remote servers
    /// </summary>
    public override void Connect()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Disconnects the client from the broker's remote servers
    /// </summary>
    public override void Disconnect()
    {
        throw new NotImplementedException();
    }

    #endregion

    #region IDataQueueUniverseProvider

    /// <summary>
    /// Method returns a collection of Symbols that are available at the data source.
    /// </summary>
    /// <param name="symbol">Symbol to lookup</param>
    /// <param name="includeExpired">Include expired contracts</param>
    /// <param name="securityCurrency">Expected security currency(if any)</param>
    /// <returns>Enumerable of Symbols, that are associated with the provided Symbol</returns>
    public IEnumerable<Symbol> LookupSymbols(Symbol symbol, bool includeExpired, string securityCurrency = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Returns whether selection can take place or not.
    /// </summary>
    /// <remarks>This is useful to avoid a selection taking place during invalid times, for example IB reset times or when not connected,
    /// because if allowed selection would fail since IB isn't running and would kill the algorithm</remarks>
    /// <returns>True if selection can take place</returns>
    public bool CanPerformSelection()
    {
        throw new NotImplementedException();
    }

    #endregion

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
    /// Adds the specified symbols to the subscription
    /// </summary>
    /// <param name="symbols">The symbols to be added keyed by SecurityType</param>
    private bool Subscribe(IEnumerable<Symbol> symbols)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Removes the specified symbols to the subscription
    /// </summary>
    /// <param name="symbols">The symbols to be removed keyed by SecurityType</param>
    private bool Unsubscribe(IEnumerable<Symbol> symbols)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the history for the requested symbols
    /// <see cref="IBrokerage.GetHistory(Data.HistoryRequest)"/>
    /// </summary>
    /// <param name="request">The historical data request</param>
    /// <returns>An enumerable of bars covering the span specified in the request</returns>
    public override IEnumerable<BaseData> GetHistory(Data.HistoryRequest request)
    {
        if (!CanSubscribe(request.Symbol))
        {
            return null; // Should consistently return null instead of an empty enumerable
        }

        throw new NotImplementedException();
    }
}
