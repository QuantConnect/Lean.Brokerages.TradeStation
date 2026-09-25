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
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using QuantConnect.Lean.Engine.Results;
using QuantConnect.Brokerages.CrossZero;
using QuantConnect.Brokerages.TradeStation.Api;
using QuantConnect.Brokerages.LevelOneOrderBook;
using QuantConnect.Brokerages.TradeStation.Models;
using TimeInForce = QuantConnect.Orders.TimeInForce;
using QuantConnect.Brokerages.TradeStation.Models.Enums;
using System.Net.Http;

[assembly: InternalsVisibleTo("QuantConnect.Brokerages.TradeStation.Tests")]

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
    /// Translates prices between Lean's internal representation and TradeStation's brokerage format for futures.
    /// </summary>
    private PriceMapper _priceMapper;

    /// <summary>
    /// Indicates whether the application is subscribed to stream order updates.
    /// </summary>
    private bool _isSubscribeOnStreamOrderUpdate;

    /// <summary>
    /// Indicates whether the very first order-stream snapshot has completed (i.e. an <c>EndSnapshot</c>
    /// has been received at least once). Used to tell a reconnect's snapshot apart from the initial one:
    /// the initial snapshot is ignored (Lean's starting order state comes from the REST setup handler),
    /// while a reconnect's snapshot is reconciled to recover terminal events (e.g. fills) that the live
    /// stream missed while the connection was down.
    /// </summary>
    private bool _initialSnapshotCompleted;

    /// <summary>
    /// Indicates whether the warning for an update rejected because the order's prior stop already triggered has been fired.
    /// </summary>
    private volatile bool _priorStopTriggeredWarningFired;

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
    private protected ConcurrentDictionary<string, bool> _updateSubmittedResponseResultByBrokerageID = new();

    /// <summary>
    /// Contains brokerage order IDs for orders placed by Lean whose WebSocket updates
    /// should be ignored in <see cref="HandleTradeStationMessage"/>.
    /// This prevents WebSocket events from interfering with Lean-driven order lifecycle
    /// management. Manually placed TWS orders are always processed.
    /// </summary>
    private protected ConcurrentDictionary<string, bool> _skipWebSocketUpdatesForLeanOrders = [];

    /// <summary>
    /// Whether the warning about canceling a member of an order group was already sent
    /// </summary>
    private bool _contingentOrderCancelWarningSent;

    /// <summary>
    /// A concurrent dictionary to store the order ID and the corresponding filled quantity.
    /// </summary>
    private ConcurrentDictionary<int, decimal> _orderIdToFillQuantity = new();

    /// <summary>
    /// Last replace sent per brokerage order; skips the repeats from updating every leg of a combo.
    /// Cleared when the order closes or the replace is rejected.
    /// </summary>
    private readonly ConcurrentDictionary<string, (decimal Quantity, decimal? LimitPrice, decimal? StopPrice, decimal? TrailingAmount, bool? TrailingAsPercentage)> _lastSubmittedUpdateByBrokerageOrderId = new();

    /// <summary>
    /// Brokerage order ids with a cancel in flight; skips the repeats from cancelling every leg of a combo.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _pendingCancelByBrokerageOrderId = new();

    /// <summary>
    /// Provides a thread-safe service for caching and managing original orders when they are part of a group.
    /// </summary>
    /// <summary>
    /// Specifies the type of account on TradeStation in current session.
    /// </summary>
    private TradeStationAccountType _tradeStationAccountType;

    /// <summary>
    /// Containing the available trading routes.
    /// </summary>
    /// <remarks>
    /// The routes are only loaded when accessed for the first time, ensuring efficient resource usage.
    /// </remarks>
    private Lazy<Dictionary<SecurityType, ReadOnlyCollection<Route>>> _routes;

    /// <summary>
    /// Maps various exchanges to their corresponding routing codes.
    /// </summary>
    /// <remarks>
    /// This dictionary is used to convert exchange identifiers to their specific routing strings
    /// required for order placement or other exchange-specific operations.
    /// </remarks>
    private readonly Dictionary<Exchange, string> _leanExchangeToTradeStationRoute = new()
    {
        { Exchange.BOSTON, "NQBX" },
        { Exchange.NASDAQ, "NSDQ" },
        { Exchange.ARCA_Options, "NYSE Arca" },
        { Exchange.ISE_MERCURY, "ISE Mercury" },
        { Exchange.MIAX_PEARL, "MPRL" },
        { Exchange.MIAX_SAPPHIRE, "SPHR"},
        { Exchange.BATS_Y, "BYX" },
        { Exchange.MEMX, "MXOP" },
        { Exchange.NASDAQ_BX, "Nasdaq BX" },
        { Exchange.MIAX_EMERALD, "EMLD" },
        { Exchange.ISE_GEMINI, "GMNI" },
        { Exchange.AMEX_Options, "NYSE Amex" }
    };

    /// <summary>
    /// Maps TradeStation routing codes to Lean Exchange ones.
    /// </summary>
    private readonly Dictionary<string, Exchange> _tradeStationRouteToLeanExchange = new();

    /// <summary>
    /// Maps TradeStation update-order rejection substrings that should be treated as non-fatal warnings
    /// (because the order is no longer modifiable) to the warning code and reason emitted to Lean.
    /// </summary>
    private static readonly Dictionary<string, (string Code, string Reason)> _updateOrderSoftRejects = new(StringComparer.InvariantCultureIgnoreCase)
    {
        ["Failed to Cancel/Replace order: Not an open order."] = ("UpdateNotOpenOrder", "the order is already closed"),
        ["Cancel/Replace not allowed after cancel has been attempted"] = ("UpdateAfterCancelAttempted", "a cancel has already been attempted on the order"),
    };

    /// <summary>
    /// Maps TradeStation cancel-order rejection messages that mean the order is no longer active at the brokerage
    /// (so there is nothing left to cancel) to the warning code and reason emitted to Lean. These are treated as
    /// non-fatal: the Lean order is transitioned to a terminal state instead of crashing the algorithm.
    /// </summary>
    private static readonly Dictionary<string, (string Code, string Reason)> _cancelOrderSoftRejects = new(StringComparer.InvariantCultureIgnoreCase)
    {
        // The order exists in TradeStation but is already in a terminal state (e.g. filled/expired).
        ["Not an open order."] = ("CancelNotOpenOrder", "the order is already closed"),
        // The order id has been purged from TradeStation entirely (e.g. a DAY order on a later trading session).
        ["Invalid order ID."] = ("CancelOrderInvalid", "the order no longer exists at the brokerage"),
    };

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
    /// Enables or disables concurrent processing of messages to and from the brokerage.
    /// </summary>
    public override bool ConcurrencyEnabled => true;

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
    /// <param name="accountId">The specific user account id.</param>
    public TradeStationBrokerage(string clientId, string apiKeySecret, string restApiUrl, string redirectUrl, string authorizationCode,
        string accountType, IAlgorithm algorithm, string accountId = "")
        : this(clientId, apiKeySecret, restApiUrl, redirectUrl, authorizationCode, string.Empty, accountType, algorithm?.Portfolio?.Transactions, algorithm?.Portfolio, accountId)
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
    /// <param name="accountId">The specific user account id.</param>
    public TradeStationBrokerage(string apiKey, string apiKeySecret, string restApiUrl, string refreshToken, string accountType, IAlgorithm algorithm, string accountId = "")
        : this(apiKey, apiKeySecret, restApiUrl, string.Empty, string.Empty, refreshToken, accountType, algorithm?.Portfolio?.Transactions, algorithm?.Portfolio, accountId)
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
    /// <param name="accountId">The specific user account id.</param>
    public TradeStationBrokerage(string clientId, string clientSecret, string restApiUrl, string redirectUrl,
        string authorizationCode, string refreshToken, string accountType, IOrderProvider orderProvider, ISecurityProvider securityProvider, string accountId = "")
        : base("TradeStation")
    {
        Initialize(clientId, clientSecret, restApiUrl, redirectUrl, authorizationCode, refreshToken, accountType, orderProvider, securityProvider, accountId);
        _leanExchangeToTradeStationRoute.DoForEach(l => _tradeStationRouteToLeanExchange.Add(l.Value, l.Key));
    }

    protected void Initialize(string clientId, string clientSecret, string restApiUrl, string redirectUrl, string authorizationCode,
        string refreshToken, string accountType, IOrderProvider orderProvider, ISecurityProvider securityProvider, string accountId)
    {
        if (_isInitialized)
        {
            return;
        }
        _isInitialized = true;
        SecurityProvider = securityProvider;
        OrderProvider = orderProvider;
        _symbolMapper = new TradeStationSymbolMapper();

        if (string.IsNullOrEmpty(accountId))
        {
            _tradeStationAccountType = TradeStationExtensions.ParseAccountType(accountType);
            _tradeStationApiClient = new TradeStationApiClient(clientId, clientSecret, restApiUrl,
                _tradeStationAccountType, refreshToken, redirectUrl, authorizationCode, OnBrokerageMessageEventHandler);
        }
        else
        {
            _tradeStationApiClient = new TradeStationApiClient(clientId, clientSecret, restApiUrl, refreshToken, redirectUrl, authorizationCode, accountId,
                OnBrokerageMessageEventHandler);
            _tradeStationAccountType = _tradeStationApiClient.GetAccountType().SynchronouslyAwaitTaskResult();
            Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(Initialize)}: AccountID: {accountId} - AccountType: {_tradeStationAccountType}");
        }

        _priceMapper = new PriceMapper();
        _messageHandler = new(HandleTradeStationMessage, ConcurrencyEnabled);

        _aggregator = Composer.Instance.GetPart<IDataAggregator>();
        if (_aggregator == null)
        {
            // toolbox downloader case
            var aggregatorName = Config.Get("data-aggregator", "QuantConnect.Lean.Engine.DataFeeds.AggregationManager");
            Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(Initialize)}: found no data aggregator instance, creating {aggregatorName}");
            _aggregator = Composer.Instance.GetExportedValueByTypeName<IDataAggregator>(aggregatorName);
        }

        _levelOneServiceManager = new LevelOneServiceManager(
            _aggregator,
            (symbols, _) => Subscribe(symbols),
            (symbols, _) => Unsubscribe(symbols));

        _routes = new Lazy<Dictionary<SecurityType, ReadOnlyCollection<Route>>>(() =>
        {
            return _tradeStationApiClient.GetRoutes().SynchronouslyAwaitTaskResult().Routes
            .SelectMany(route => route.AssetTypes.Select(assetType => new { SecurityType = assetType.ConvertAssetTypeToSecurityType(), Route = route }))
            .GroupBy(x => x.SecurityType)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Route).ToList().AsReadOnly());
        });

        DeploymentDetailsHelper.Add("trade-station-account-type", _tradeStationAccountType.ToStringInvariant());
        DeploymentDetailsHelper.Add("trade-station-account-id", accountId);
        ValidateSubscription();
    }

    #region Brokerage

    /// <summary>
    /// Rebuilds, best effort, the contingencies of the given open orders based on their linked orders
    /// </summary>
    internal static void SetContingencies(List<(TradeStationOrder BrokerageOrder, List<Order> LeanOrders)> openOrders)
    {
        try
        {
            var openOrdersById = openOrders.Where(x => !string.IsNullOrEmpty(x.BrokerageOrder.OrderID)).ToDictionary(x => x.BrokerageOrder.OrderID);
            // the members of each group and the children of each parent, by brokerage order id
            var groups = new Dictionary<string, (ContingencyType Type, HashSet<string> Members)>();
            var children = new Dictionary<string, HashSet<string>>();
            foreach (var openOrder in openOrders)
            {
                var brokerageOrderId = openOrder.BrokerageOrder.OrderID;
                foreach (var linkedOrder in openOrder.BrokerageOrder.ConditionalOrders ?? [])
                {
                    if (string.IsNullOrEmpty(linkedOrder.OrderID) || !openOrdersById.ContainsKey(linkedOrder.OrderID))
                    {
                        // the linked order is gone: a parent which already filled, so this order is working
                        continue;
                    }

                    switch (linkedOrder.Relationship?.ToUpperInvariant())
                    {
                        case "OCO":
                        case "BRK":
                            // all the orders of the group are linked to each other, so they all get the same key
                            var groupOrderIds = (openOrder.BrokerageOrder.ConditionalOrders ?? [])
                                .Where(x => !string.IsNullOrEmpty(x.OrderID) && openOrdersById.ContainsKey(x.OrderID)
                                    && ("OCO".Equals(x.Relationship, StringComparison.InvariantCultureIgnoreCase) || "BRK".Equals(x.Relationship, StringComparison.InvariantCultureIgnoreCase)))
                                .Select(x => x.OrderID).Append(brokerageOrderId).Distinct().OrderBy(x => x, StringComparer.Ordinal);
                            var key = string.Join(",", groupOrderIds);
                            if (!groups.TryGetValue(key, out var group))
                            {
                                var type = linkedOrder.Relationship.Equals("BRK", StringComparison.InvariantCultureIgnoreCase) ? ContingencyType.OneUpdatesOther : ContingencyType.OneCancelsOther;
                                groups[key] = group = (type, new HashSet<string>());
                            }
                            group.Members.Add(brokerageOrderId);
                            break;
                        case "OSP":
                            // the linked order is our parent
                            AddChild(linkedOrder.OrderID, brokerageOrderId);
                            break;
                        case "OSO":
                            // the linked order is our child
                            AddChild(brokerageOrderId, linkedOrder.OrderID);
                            break;
                    }
                }
            }

            void AddChild(string parentId, string childId)
            {
                if (!children.TryGetValue(parentId, out var parentChildren))
                {
                    children[parentId] = parentChildren = new HashSet<string>();
                }
                parentChildren.Add(childId);
            }

            foreach (var (type, members) in groups.Values.Where(group => group.Members.Count > 1))
            {
                OrderContingency.Relate(type, members.SelectMany(member => openOrdersById[member].LeanOrders));
            }
            foreach (var (parentId, parentChildren) in children)
            {
                OrderContingency.Trigger(openOrdersById[parentId].LeanOrders, parentChildren.SelectMany(child => openOrdersById[child].LeanOrders));
            }
        }
        catch (Exception error)
        {
            // best effort, they will be handled as plain orders
            Log.Error(error, "Failed to rebuild the contingencies of the open orders");
            foreach (var order in openOrders.SelectMany(x => x.LeanOrders))
            {
                order.Contingency = null;
            }
        }
    }

    /// <summary>
    /// Gets all open orders on the account.
    /// NOTE: The order objects returned do not have QC order IDs.
    /// </summary>
    /// <returns>The open orders returned from TradeStation</returns>
    public override List<Order> GetOpenOrders()
    {
        var orders = _tradeStationApiClient.GetOrders().SynchronouslyAwaitTaskResult();
        var leanOrders = new List<Order>();

        var openOrders = new List<(TradeStationOrder BrokerageOrder, List<Order> LeanOrders)>();
        // the orders sent by another (OSO) are held until it fills, and the sent ones (OPN) are not yet acknowledged
        foreach (var order in orders.Orders.Where(o => o.Status is TradeStationOrderStatusType.Ack or TradeStationOrderStatusType.Don or TradeStationOrderStatusType.Oso
            or TradeStationOrderStatusType.Opn))
        {
            if (TryConvertToLeanOrder(order, out var convertedOrders))
            {
                leanOrders.AddRange(convertedOrders);
                openOrders.Add((order, convertedOrders));
            }
        }
        SetContingencies(openOrders);
        return leanOrders;
    }

    private bool TryConvertToLeanOrder(TradeStationOrder order, out List<Order> leanOrders)
    {
        if (order.Legs.Count == 1)
        {
            var leg = order.Legs.First();

            if (TryCreateLeanOrder(order, leg, out var leanOrder))
            {
                leanOrders = [leanOrder];
                return true;
            }
        }
        else
        {
            var groupQuantity = GroupOrderExtensions.GetGroupQuantityByEachLegQuantity(
                order.Legs.Select(leg => leg.QuantityOrdered),
                decimal.IsNegative(order.LimitPrice) ? OrderDirection.Sell : OrderDirection.Buy
            );
            var groupOrderManager = new GroupOrderManager(order.Legs.Count, groupQuantity);

            var tempLegOrders = new List<Order>();
            foreach (var leg in order.Legs)
            {
                if (TryCreateLeanOrder(order, leg, out var leanOrder, groupOrderManager))
                {
                    tempLegOrders.Add(leanOrder);
                }
                else
                {
                    // If any leg fails to create a Lean order, clear tempLegOrders to prevent partial group orders.
                    tempLegOrders.Clear();
                    break;
                }
            }

            if (tempLegOrders.Count > 0)
            {
                leanOrders = tempLegOrders;
                return true;
            }
        }
        leanOrders = null;
        return false;
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
            if (!_symbolMapper.TryGetLeanSymbol(position.Symbol, position.AssetType, position.ExpirationDate, out var leanSymbol))
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, 1, $"The asset type '{position.AssetType}' for symbol '{position.Symbol}' is not supported. This position has been skipped."));
                continue;
            }

            if (leanSymbol.SecurityType is SecurityType.Future or SecurityType.Option && leanSymbol.ID.Date.Date < DateTime.UtcNow.ConvertFromUtc(leanSymbol.GetSymbolExchangeTimeZone()).Date)
            {
                Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(GetAccountHoldings)}: The {leanSymbol} was expired and skipped.");
                continue;
            }

            holdings.Add(new Holding()
            {
                AveragePrice = _priceMapper.GetLeanPrice(leanSymbol, position.AveragePrice),
                ConversionRate = position.ConversionRate,
                CurrencySymbol = Currencies.USD,
                MarketValue = position.MarketValue,
                MarketPrice = _priceMapper.GetLeanPrice(leanSymbol, position.Last),
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

        if (order.Contingency != null)
        {
            // contingent orders are placed together, as an order group and/or order sends order, once they have all arrived
            if (ContingentOrderCache.TryGetContingentCachedOrders(order, out var contingentOrders))
            {
                PlaceContingentOrders(contingentOrders);
            }
            return true;
        }

        if (!GroupOrderCacheManager.TryGetGroupCachedOrders(order, out var orders))
        {
            return true;
        }

        try
        {
            _messageHandler.WithLockedStream(() =>
            {
                PlaceTradeStationOrder(orders);
            });
        }
        catch (Exception error)
        {
            Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(PlaceOrder)}: " + error);

            var orderEvents = orders.ToList(o => new OrderEvent(o, DateTime.UtcNow, OrderFee.Zero, $"PlaceOrder")
            {
                Status = OrderStatus.Invalid,
                Message = error.Message
            });
            OnOrderEvents(orderEvents);
        }
        return true;
    }

    /// <summary>
    /// Places a set of contingent orders: orders where one cancels (OCO) or reduces (BRK) the rest are placed as an order group,
    /// and the orders triggered by another are sent along with it as order sends order (OSO)
    /// </summary>
    /// <param name="contingentOrders">All the orders of the set, parents come before the orders they trigger</param>
    private void PlaceContingentOrders(List<Order> contingentOrders)
    {
        try
        {
            _messageHandler.WithLockedStream(() =>
            {
                // the orders in the order TradeStation confirms them: the orders sent by another come before it
                var placedOrders = new List<(Order Order, TradeStationPlaceOrderRequest Request)>(contingentOrders.Count);
                var roots = contingentOrders.Where(order => order.GetContingencyLink(ContingencyRole.Child) == null).ToList();
                var requests = roots.Select(order => CreateContingentOrderRequest(order, contingentOrders, placedOrders, new Dictionary<Symbol, decimal>())).ToList();

                TradeStationPlaceOrderResponse response;
                if (requests.Count == 1)
                {
                    response = _tradeStationApiClient.PlaceOrder(requests[0]).SynchronouslyAwaitTaskResult();
                }
                else
                {
                    var groupType = GetOrderGroupType(roots[0].GetSiblingLink());
                    response = _tradeStationApiClient.PlaceOrderGroup(new TradeStationOrderGroupRequest(groupType, requests)).SynchronouslyAwaitTaskResult();
                }

                var brokerageOrders = response.Orders ?? [];
                var error = brokerageOrders.FirstOrDefault(brokerageOrder => !string.IsNullOrEmpty(brokerageOrder.Error) || string.IsNullOrEmpty(brokerageOrder.OrderID));
                var confirmedOrders = error == null ? MatchConfirmedOrders(placedOrders, brokerageOrders) : null;
                if (confirmedOrders == null)
                {
                    // we do not leave any order behind
                    foreach (var brokerageOrder in brokerageOrders.Where(brokerageOrder => !string.IsNullOrEmpty(brokerageOrder.OrderID)))
                    {
                        try
                        {
                            _tradeStationApiClient.CancelOrder(brokerageOrder.OrderID).SynchronouslyAwaitTaskResult();
                        }
                        catch (Exception cancelError)
                        {
                            Log.Error(cancelError);
                        }
                    }
                    throw new InvalidOperationException(error?.Message ?? $"Unexpected orders in the response: [{string.Join(", ", brokerageOrders.Select(x => x.Message))}]");
                }

                var orderEvents = new List<OrderEvent>(placedOrders.Count);
                for (var i = 0; i < placedOrders.Count; i++)
                {
                    var brokerageOrderId = confirmedOrders[i].OrderID;
                    placedOrders[i].Order.BrokerId.Add(brokerageOrderId);
                    _skipWebSocketUpdatesForLeanOrders[brokerageOrderId] = true;
                    orderEvents.Add(new OrderEvent(placedOrders[i].Order, DateTime.UtcNow, OrderFee.Zero, $"{nameof(TradeStationBrokerage)} Order Event") { Status = OrderStatus.Submitted });
                }
                OnOrderEvents(orderEvents);
            });
        }
        catch (Exception error)
        {
            Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(PlaceContingentOrders)}: " + error);

            OnOrderEvents(contingentOrders.ToList(order => new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, "PlaceOrder")
            {
                Status = OrderStatus.Invalid,
                Message = error.Message
            }));
        }
    }

    /// <summary>
    /// Creates the request for a contingent order, including the orders it sends once filled (OSO)
    /// </summary>
    /// <param name="order">The order to create the request for</param>
    /// <param name="contingentOrders">All the orders of the set</param>
    /// <param name="placedOrders">The orders and their requests in the order TradeStation confirms them</param>
    /// <param name="triggeredQuantity">The quantity by symbol of the parent orders, which will be filled by the time this order starts working</param>
    private TradeStationPlaceOrderRequest CreateContingentOrderRequest(Order order, List<Order> contingentOrders, List<(Order Order, TradeStationPlaceOrderRequest Request)> placedOrders,
        Dictionary<Symbol, decimal> triggeredQuantity)
    {
        var tradeStationOrderProperties = order.Properties as OrderProperties;
        if (!GetTradeStationOrderRouteIdByOrderSecurityTypes(tradeStationOrderProperties, new List<SecurityType> { order.SecurityType }, out var routeId))
        {
            throw new InvalidOperationException($"Failed to find a valid TradeStation route for exchange '{tradeStationOrderProperties.Exchange.Name}' with the security type: {order.SecurityType}.");
        }

        // the trade action is determined based on the holdings once the parent orders have filled
        var holdingQuantity = SecurityProvider.GetHoldingsQuantity(order.Symbol) + triggeredQuantity.GetValueOrDefault(order.Symbol);
        var tradeAction = ConvertDirection(order.SecurityType, order.Direction, holdingQuantity);
        var (trailingAmount, trailingAsPercentage) = order.GetTrailingStopInfo();
        var request = _tradeStationApiClient.CreatePlaceOrderRequest(order.Type, order.TimeInForce, order.AbsoluteQuantity, tradeAction, _symbolMapper.GetBrokerageSymbol(order.Symbol),
            limitPrice: order.GetLimitPrice(_priceMapper), stopPrice: order.GetStopPrice(_priceMapper), trailingAmount: trailingAmount, trailingAsPercentage: trailingAsPercentage,
            routeId: routeId, tradeStationOrderProperties: tradeStationOrderProperties as TradeStationOrderProperties);

        var parent = order.GetContingencyLink(ContingencyRole.Parent);
        if (parent != null)
        {
            var childrenTriggeredQuantity = new Dictionary<Symbol, decimal>(triggeredQuantity);
            childrenTriggeredQuantity[order.Symbol] = childrenTriggeredQuantity.GetValueOrDefault(order.Symbol) + order.Quantity;

            // the orders related to each other go in the same group, the independent ones all together
            request.OSOs = order.GetContingentChildren(contingentOrders)
                .GroupBy(child => child.GetSiblingLink()?.Id ?? 0)
                .Select(group => new TradeStationOrderGroupRequest(GetOrderGroupType(group.First().GetSiblingLink()),
                    group.Select(child => CreateContingentOrderRequest(child, contingentOrders, placedOrders, childrenTriggeredQuantity)).ToList()))
                .ToList();
        }
        placedOrders.Add((order, request));
        return request;
    }

    /// <summary>
    /// Matches the placed orders with the orders TradeStation confirms, which come in the same order. Their confirmation messages,
    /// like "Sent order: Sell 1 AAPL @ 1000.00 Limit", are only used to rule out the orders they clearly don't belong to
    /// </summary>
    /// <returns>The confirmed order of each placed order, null if any can't be matched</returns>
    private static List<Models.OrderResponse> MatchConfirmedOrders(List<(Order Order, TradeStationPlaceOrderRequest Request)> placedOrders, List<Models.OrderResponse> confirmedOrders)
    {
        if (confirmedOrders.Count != placedOrders.Count)
        {
            return null;
        }
        var remaining = new List<Models.OrderResponse>(confirmedOrders);
        var matches = new List<Models.OrderResponse>(placedOrders.Count);
        foreach (var (_, request) in placedOrders)
        {
            var index = remaining.FindIndex(confirmed => !IsConfirmationOfOtherOrder(confirmed.Message, request));
            if (index == -1)
            {
                return null;
            }
            matches.Add(remaining[index]);
            remaining.RemoveAt(index);
        }
        return matches;
    }

    /// <summary>
    /// The order types as the confirmation messages end with them, like "Sent order: Sell 1 AAPL @ 50.00 Stop Market"
    /// </summary>
    private static readonly Dictionary<string, TradeStationOrderType> ConfirmedOrderTypes = new(StringComparer.InvariantCultureIgnoreCase)
    {
        { "Market", TradeStationOrderType.Market },
        { "Limit", TradeStationOrderType.Limit },
        { "Stop Market", TradeStationOrderType.StopMarket },
        { "Stop Limit", TradeStationOrderType.StopLimit }
    };

    /// <summary>
    /// Whether the confirmation message clearly belongs to another order: it's for the other side or another order type
    /// </summary>
    private static bool IsConfirmationOfOtherOrder(string message, TradeStationPlaceOrderRequest request)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }
        var words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var otherSide = request.TradeAction.StartsWith("BUY", StringComparison.InvariantCultureIgnoreCase) ? "Sell" : "Buy";
        if (words.Contains(otherSide, StringComparer.InvariantCultureIgnoreCase))
        {
            return true;
        }
        // the order type follows the price, if any: "@ 1000.00 Limit", "@ Market"
        var orderType = string.Join(' ', words.SkipWhile(word => word != "@").Skip(1)
            .SkipWhile(word => decimal.TryParse(word, NumberStyles.Number, CultureInfo.InvariantCulture, out _)));
        return ConfirmedOrderTypes.TryGetValue(orderType, out var confirmedOrderType) && confirmedOrderType != request.OrderType;
    }

    /// <summary>
    /// Gets the order group type for the given contingency
    /// </summary>
    private static string GetOrderGroupType(ContingencyLink member)
    {
        if (member == null)
        {
            return TradeStationOrderGroupRequest.Normal;
        }
        return member.Type == ContingencyType.OneUpdatesOther ? TradeStationOrderGroupRequest.Bracket : TradeStationOrderGroupRequest.OneCancelsOther;
    }

    /// <summary>
    /// Places an order using TradeStation.
    /// </summary>
    /// <param name="orders">The collection orders to be placed.</param>
    /// <param name="isSubmittedEvent">Indicates if the order submission event should be triggered.</param>
    /// <returns>A response from TradeStation after placing the order.</returns>
    private TradeStationPlaceOrderResponse? PlaceTradeStationOrder(IReadOnlyCollection<Order> orders, bool isSubmittedEvent = true)
    {
        var order = orders.First();
        switch (order.Type)
        {
            case OrderType.ComboMarket:
            case OrderType.ComboLimit:
                return PlaceOrderCommon(orders, order.Type, order.TimeInForce, 0m, "", "", order.GetLimitPrice(_priceMapper), 0m, null, null, isSubmittedEvent);
            case OrderType.MarketOnOpen:
            case OrderType.MarketOnClose:
            case OrderType.Market:
            case OrderType.Limit:
            case OrderType.StopMarket:
            case OrderType.StopLimit:
            case OrderType.TrailingStop:
                var response = default(TradeStationPlaceOrderResponse?);
                var holdingQuantity = SecurityProvider.GetHoldingsQuantity(order.Symbol);
                var isPlaceCrossOrder = TryCrossZeroPositionOrder(order, holdingQuantity);
                // If TryCrossZeroPositionOrder returned null we should place the simple order.
                // A non-null result means the cross-zero (part 1) was already submitted.
                if (isPlaceCrossOrder == null)
                {
                    var symbol = _symbolMapper.GetBrokerageSymbol(order.Symbol);
                    var tradeAction = ConvertDirection(order.SecurityType, order.Direction, holdingQuantity);
                    var (trailingAmount, trailingAsPercentage) = order.GetTrailingStopInfo();
                    response = PlaceOrderCommon(orders, order.Type, order.TimeInForce, order.AbsoluteQuantity, tradeAction, symbol,
                        order.GetLimitPrice(_priceMapper), order.GetStopPrice(_priceMapper), trailingAmount, trailingAsPercentage, isSubmittedEvent);
                }
                return response;
            default:
                throw new NotSupportedException($"{nameof(TradeStationBrokerage)}.{nameof(PlaceTradeStationOrder)}:" +
                    $" The order type '{order.Type}' is not supported for conversion to TradeStation order type.");
        }
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

        // First-part call (isPlaceOrderWithLeanEvent == true) is invoked synchronously from PlaceOrder,
        // which already holds _messageHandler.WithLockedStream — re-entering would throw LockRecursionException
        // because the underlying ReaderWriterLockSlim is created with LockRecursionPolicy.NoRecursion.
        // Second-part call (isPlaceOrderWithLeanEvent == false) runs from Brokerage.TryHandleRemainingCrossZeroOrder
        // on a Task.Run with no outer stream lock, so we must take the lock here so the new BrokerId and
        // _skipWebSocketUpdatesForLeanOrders are registered before any WS event for that brokerage ID is dispatched.
        if (isPlaceOrderWithLeanEvent)
        {
            return PlaceCrossZeroOrderInternal(crossZeroOrderRequest, symbol, tradeAction, isPlaceOrderWithLeanEvent);
        }

        var crossZeroOrderResponse = default(CrossZeroOrderResponse);
        _messageHandler.WithLockedStream(() =>
        {
            crossZeroOrderResponse = PlaceCrossZeroOrderInternal(crossZeroOrderRequest, symbol, tradeAction, isPlaceOrderWithLeanEvent);
        });
        return crossZeroOrderResponse;
    }

    /// <summary>
    /// Submits the cross-zero order leg to TradeStation and shapes the response.
    /// </summary>
    /// <remarks>
    /// Caller is responsible for holding <c>_messageHandler.WithLockedStream</c> when needed
    /// (see <see cref="PlaceCrossZeroOrder"/>). PlaceOrderCommon will not check the order type.
    /// </remarks>
    private CrossZeroOrderResponse PlaceCrossZeroOrderInternal(CrossZeroFirstOrderRequest crossZeroOrderRequest, string symbol, string tradeAction, bool isPlaceOrderWithLeanEvent)
    {
        var (trailingAmount, trailingAsPercentage) = crossZeroOrderRequest.LeanOrder.GetTrailingStopInfo();
        var response = PlaceOrderCommon(new List<Order> { crossZeroOrderRequest.LeanOrder }, crossZeroOrderRequest.OrderType, crossZeroOrderRequest.LeanOrder.TimeInForce,
            crossZeroOrderRequest.AbsoluteOrderQuantity, tradeAction, symbol, crossZeroOrderRequest.LeanOrder.GetLimitPrice(_priceMapper), crossZeroOrderRequest.LeanOrder.GetStopPrice(_priceMapper),
            trailingAmount, trailingAsPercentage, isPlaceOrderWithLeanEvent);

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
    /// <param name="orders">The collection orders to be placed.</param>
    /// <param name="orderType">The type of the order.</param>
    /// <param name="timeInForce">The time in force for the order.</param>
    /// <param name="quantity">The quantity of the order.</param>
    /// <param name="tradeAction">The trade action (BUY/SELL) of the order.</param>
    /// <param name="symbol">The symbol for the order.</param>
    /// <param name="limitPrice">The limit price for the order, if applicable.</param>
    /// <param name="stopPrice">The stop price for the order, if applicable.</param>
    /// <param name="trailingAmount">The trailing amount to be used to update the stop price.
    /// If a trailing amount is passed, and stop price is passed as well, the stop price is ignored b the brokerage</param>
    /// <param name="trailingAsPercentage">Whether the <paramref name="trailingAmount"/> is a percentage or an absolute currency value</param>
    /// <param name="isSubmittedEvent">Indicates if the order submission event should be triggered.</param>
    /// <returns>A response from TradeStation after placing the order.</returns>
    private TradeStationPlaceOrderResponse? PlaceOrderCommon(IReadOnlyCollection<Order> orders, OrderType orderType, TimeInForce timeInForce, decimal quantity, string tradeAction,
        string symbol, decimal? limitPrice, decimal? stopPrice, decimal? trailingAmount, bool? trailingAsPercentage, bool isSubmittedEvent)
    {
        var response = default(TradeStationPlaceOrderResponse);

        var tradeStationOrderProperties = orders.First().Properties as OrderProperties;

        if (!GetTradeStationOrderRouteIdByOrderSecurityTypes(tradeStationOrderProperties, orders.Select(x => x.SecurityType).ToList(), out var routeId))
        {
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, -1,
                $"Failed to find a valid TradeStation route for exchange '{tradeStationOrderProperties.Exchange.Name}' with the security types: {string.Join(", ", orders.Select(order => order.SecurityType))}." +
                $"Please verify that the exchange and security types are supported."));
            return null;
        }

        if (!string.IsNullOrEmpty(routeId))
        {
            Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(PlaceOrderCommon)}: Using Route ID '{routeId}' for the following Order(s): {string.Join(',', orders.Select(x => x.ToString()))}");
        }

        if (orders.Count == 1)
        {
            response = _tradeStationApiClient.PlaceOrder(orderType, timeInForce, quantity, tradeAction, symbol,
                limitPrice: limitPrice, stopPrice: stopPrice, trailingAmount: trailingAmount,
                trailingAsPercentage: trailingAsPercentage, routeId: routeId,
                tradeStationOrderProperties: tradeStationOrderProperties as TradeStationOrderProperties).SynchronouslyAwaitTaskResult();
        }
        else
        {
            var orderLegs = CreateOrderLegs(orders);
            response = _tradeStationApiClient.PlaceOrder(orderType, timeInForce, legs: orderLegs, limitPrice: limitPrice,
                routeId: routeId, tradeStationOrderProperties: tradeStationOrderProperties as TradeStationOrderProperties).SynchronouslyAwaitTaskResult();
        }

        foreach (var brokerageOrder in response.Orders)
        {
            var exceptOneFailed = default(bool);
            foreach (var order in orders)
            {
                // Check if the order failed due to an existing position. Reason: [EC601,EC602,EC701,EC702]: You are long/short N shares.
                if (!string.IsNullOrEmpty(brokerageOrder.Error))
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

                _skipWebSocketUpdatesForLeanOrders[brokerageOrder.OrderID] = true;
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
        // Lean pushes only the leg whose ticket changed, the rest of the combo shares its group order manager
        order.TryGetGroupOrders(OrderProvider.GetOrderById, out var orders);

        // Always use the first order in the group, as combo orders determine direction based on the first order's details.
        order = orders.First();
        var brokerageOrderId = order.BrokerId.Last();

        decimal quantity;
        if (order.GroupOrderManager == null)
        {
            if (!TryGetUpdateCrossZeroOrderQuantity(order, out var orderQuantity))
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1, $"{nameof(TradeStationBrokerage)}.{nameof(UpdateOrder)}: Unable to modify order quantities."));
                return false;
            }
            quantity = Math.Abs(orderQuantity);
        }
        else
        {
            // TradeStation multiplies the leg ratios by the replace quantity, the same group quantity Lean reads back in TryConvertToLeanOrder
            quantity = GroupOrderExtensions.GetGroupQuantityByEachLegQuantity(orders.Select(groupOrder => groupOrder.Quantity), OrderDirection.Buy);
        }

        var (trailingAmount, trailingAsPercentage) = order.GetTrailingStopInfo();
        var update = (Quantity: quantity, LimitPrice: order.GetLimitPrice(_priceMapper), StopPrice: order.GetStopPrice(_priceMapper),
            TrailingAmount: trailingAmount, TrailingAsPercentage: trailingAsPercentage);

        var response = default(bool);
        _messageHandler.WithLockedStream(() =>
        {
            // Every leg of a combo produces this same request, so submit it once
            if (_lastSubmittedUpdateByBrokerageOrderId.TryGetValue(brokerageOrderId, out var lastSubmittedUpdate) && lastSubmittedUpdate == update)
            {
                response = true;
                return;
            }

            try
            {
                ReplaceBrokerageOrder(brokerageOrderId, order.Type, quantity, update.LimitPrice, update.StopPrice, trailingAmount, trailingAsPercentage);

                _lastSubmittedUpdateByBrokerageOrderId[brokerageOrderId] = update;
                foreach (var groupOrder in orders)
                {
                    OnOrderEvent(new OrderEvent(groupOrder, DateTime.UtcNow, OrderFee.Zero, $"{nameof(TradeStationBrokerage)}.{nameof(UpdateOrder)} Order Event")
                    {
                        Status = OrderStatus.UpdateSubmitted
                    });
                }
                response = true;
                _updateSubmittedResponseResultByBrokerageID[brokerageOrderId] = true;
            }
            catch (Exception exception) when (_updateOrderSoftRejects.TryGetValue(exception.Message, out var softReject))
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, softReject.Code, $"Failed to update Order: OrderId: {order.Id} (BrokerId: {brokerageOrderId}) for {order.Symbol}, {softReject.Reason}"));
            }
            catch (Exception exception) when (exception.Message.Contains("prior stop already triggered", StringComparison.InvariantCultureIgnoreCase))
            {
                if (!_priorStopTriggeredWarningFired)
                {
                    _priorStopTriggeredWarningFired = true;
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UpdatePriorStopTriggered", $"Failed to update Order: OrderId: {order.Id} (BrokerId: {brokerageOrderId}) for {order.Symbol}, the prior stop has already triggered"));
                }
            }
            catch (Exception exception)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, "UpdateOrderInvalid", exception.Message));
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

        var result = default(bool);
        _messageHandler.WithLockedStream(() =>
        {
            // A combo is a single TradeStation order: cancelling any leg cancels all of them, so send one cancel per brokerage order
            if (_pendingCancelByBrokerageOrderId.ContainsKey(brokerageOrderId))
            {
                result = true;
                return;
            }

            try
            {
                if (CancelBrokerageOrder(brokerageOrderId))
                {
                    _pendingCancelByBrokerageOrderId[brokerageOrderId] = true;
                    result = true;
                    if (!_contingentOrderCancelWarningSent && order.GetSiblingLink() != null)
                    {
                        // TradeStation cancels the rest of an order group when one of them fills, but not when one of them is canceled
                        _contingentOrderCancelWarningSent = true;
                        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "ContingentOrderCancel",
                            "TradeStation does not cancel the rest of the orders of a one cancels other or one updates other group when one of them is canceled, they keep working."));
                    }
                }
            }
            catch (Exception ex) when (ex.Message.Contains("after cancel has been attempted", StringComparison.InvariantCultureIgnoreCase))
            {
                // A cancel is already in flight; report success and let the stream deliver the terminal event
                _pendingCancelByBrokerageOrderId[brokerageOrderId] = true;
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "CancelAfterCancelAttempted", $"Failed to cancel Order: OrderId: {order.Id} (BrokerId: {brokerageOrderId}) for {order.Symbol}, a cancel has already been attempted on the order"));
                result = true;
            }
            catch (Exception ex) when (_cancelOrderSoftRejects.TryGetValue(ex.Message, out var softReject))
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, softReject.Code, $"Failed to cancel Order: OrderId: {order.Id} (BrokerId: {brokerageOrderId}) for {order.Symbol}, {softReject.Reason}"));

                // The order is no longer active at the brokerage: close the whole group so the transaction
                // handler stops retrying the cancel (a false return would loop in CancelPending forever)
                order.TryGetGroupOrders(OrderProvider.GetOrderById, out var groupOrders);
                foreach (var groupOrder in groupOrders)
                {
                    OnOrderEvent(new OrderEvent(groupOrder, DateTime.UtcNow, OrderFee.Zero, softReject.Reason)
                    {
                        Status = OrderStatus.Canceled
                    });
                }
                result = true;
            }
            catch (Exception ex)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, "CancelOrderInvalid", ex.Message));
            }
        });
        return result;
    }

    /// <summary>
    /// Sends the cancel request for the given brokerage order id to TradeStation. Extracted as a seam so the
    /// soft-reject handling in <see cref="CancelOrder"/> can be unit tested without a live API connection.
    /// </summary>
    /// <param name="brokerageOrderId">The brokerage order id to cancel.</param>
    /// <returns>True if TradeStation accepted the cancel request.</returns>
    protected virtual bool CancelBrokerageOrder(string brokerageOrderId)
    {
        return _tradeStationApiClient.CancelOrder(brokerageOrderId).SynchronouslyAwaitTaskResult();
    }

    /// <summary>
    /// Sends the replace request for the given brokerage order id to TradeStation. Extracted as a seam so the
    /// requests <see cref="UpdateOrder"/> sends can be unit tested without a live API connection.
    /// </summary>
    /// <param name="brokerageOrderId">The brokerage order id to replace.</param>
    /// <param name="orderType">The Lean order type.</param>
    /// <param name="quantity">The new quantity, a multiplier of the leg ratios for a combo order.</param>
    /// <param name="limitPrice">The new limit price.</param>
    /// <param name="stopPrice">The new stop price.</param>
    /// <param name="trailingAmount">The new trailing amount.</param>
    /// <param name="trailingAsPercentage">Whether the <paramref name="trailingAmount"/> is a percentage.</param>
    protected virtual void ReplaceBrokerageOrder(string brokerageOrderId, OrderType orderType, decimal quantity, decimal? limitPrice, decimal? stopPrice,
        decimal? trailingAmount, bool? trailingAsPercentage)
    {
        _tradeStationApiClient.ReplaceOrder(brokerageOrderId, orderType, quantity, limitPrice, stopPrice, trailingAmount, trailingAsPercentage).SynchronouslyAwaitTaskResult();
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
    /// Handles brokerage message events by invoking the appropriate message processing logic.
    /// </summary>
    /// <param name="_">The sender of the event, typically unused in this implementation.</param>
    /// <param name="brokerageMessageEvent">The event data containing details of the brokerage message, such as type and content.</param>
    private void OnBrokerageMessageEventHandler(object _, BrokerageMessageEvent brokerageMessageEvent) => OnMessage(brokerageMessageEvent);

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
    internal void HandleTradeStationMessage(string json)
    {
        if (OrderProvider == null)
        {
            // we are used as a data source only, not a brokerage
            return;
        }

        try
        {
            var jObj = JObject.Parse(json);
            if (jObj["AccountID"] != null)
            {
                // Order frame. The frames before the first EndSnapshot are the initial snapshot, which
                // we ignore (Lean's starting order state is loaded by the REST setup handler). Once that
                // has completed, process live frames normally and treat a reconnect's re-sent snapshot
                // as a reconciliation to recover terminal events (e.g. fills) missed while disconnected.
                if (!_isSubscribeOnStreamOrderUpdate && !_initialSnapshotCompleted)
                {
                    return;
                }

                if (Log.DebuggingEnabled)
                {
                    Log.Debug($"{nameof(TradeStationBrokerage)}.{nameof(HandleTradeStationMessage)}.WebSocket.JSON: {json}");
                }

                var brokerageOrder = jObj.ToObject<TradeStationOrder>();

                // The live flag is still false until EndSnapshot, so reaching here with it unset means
                // this frame is part of a reconnect's re-sent snapshot. We only want to recover MISSED
                // terminal/progress events (fills, cancels, rejects) for orders Lean placed and still
                // considers open.
                if (!_isSubscribeOnStreamOrderUpdate)
                {
                    // Skip working acknowledgements (Ack/Don/Stp/Rjr): they carry no terminal progress and
                    // would otherwise re-emit a spurious UpdateSubmitted, or repeat the rejected-replace
                    // warning, for every open order on each reconnect. Genuine fill deltas (Fpr/Fll/...)
                    // and cancels/rejects still flow through.
                    if (brokerageOrder.Status is TradeStationOrderStatusType.Ack
                        or TradeStationOrderStatusType.Don
                        or TradeStationOrderStatusType.Stp
                        or TradeStationOrderStatusType.Rjr)
                    {
                        return;
                    }

                    // Reconcile only Lean orders that are still open; recovering a missed fill/cancel for
                    // them. Skip everything else (no matching order, or all already terminal) so we don't
                    // re-notify external orders or re-emit events for orders already in sync. Already-counted
                    // fill quantity is deduplicated downstream via _orderIdToFillQuantity (a re-sent partial
                    // yields FillQuantity 0).
                    if (OrderProvider.GetOrdersByBrokerageId(brokerageOrder.OrderID)?.All(o => o.Status.IsClosed()) ?? true)
                    {
                        return;
                    }
                }

                var globalLeanOrderStatus = default(OrderStatus);
                var eventMessage = string.Empty;
                switch (brokerageOrder.Status)
                {
                    case TradeStationOrderStatusType.Ack:
                    case TradeStationOrderStatusType.Don: // The event occurs during extended market hours
                        // Remove the order entry when the order is acknowledged (indicating successful submission)
                        if (_updateSubmittedResponseResultByBrokerageID.TryRemove(new(brokerageOrder.OrderID, true))
                            || _skipWebSocketUpdatesForLeanOrders.TryRemove(brokerageOrder.OrderID, out _))
                        {
                            return;
                        }
                        // An ack we did not initiate (e.g. modified from another client): the last sent values
                        // no longer reflect the working order, allow re-sending them
                        _lastSubmittedUpdateByBrokerageOrderId.TryRemove(brokerageOrder.OrderID, out _);
                        // Handle manually submitted order by TradeStation clients
                        globalLeanOrderStatus = OrderStatus.Submitted;
                        break;
                    // Sometimes, a filled event is received without the ClosedDateTime property set.
                    // Subsequently, another event is received with the ClosedDateTime property correctly populated.
                    case TradeStationOrderStatusType.Fll:
                    case TradeStationOrderStatusType.Brf:
                        globalLeanOrderStatus = OrderStatus.Filled;
                        break;
                    case TradeStationOrderStatusType.Fpr:
                        globalLeanOrderStatus = OrderStatus.PartiallyFilled;
                        break;
                    // A rejected replace leaves the original order working, so don't invalidate: warn and allow retrying the same values.
                    // Not keyed on the update flag, an Ack of the replace request consumes it before the rejection arrives
                    case TradeStationOrderStatusType.Rjr when OrderProvider.GetOrdersByBrokerageId(brokerageOrder.OrderID) is { Count: > 0 } rejectedOrders
                        && rejectedOrders.Any(rejectedOrder => !rejectedOrder.Status.IsClosed()):
                        _updateSubmittedResponseResultByBrokerageID.TryRemove(brokerageOrder.OrderID, out _);
                        _lastSubmittedUpdateByBrokerageOrderId.TryRemove(brokerageOrder.OrderID, out _);
                        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UpdateOrderRejected",
                            $"TradeStation rejected the update (BrokerId: {brokerageOrder.OrderID}): {brokerageOrder.RejectReason}. " +
                            "TradeStation still works the order with the values before the update; Lean's order shows the new ones."));
                        return;
                    case TradeStationOrderStatusType.Rej:
                    case TradeStationOrderStatusType.Tsc:
                    case TradeStationOrderStatusType.Rjr:
                    case TradeStationOrderStatusType.Bro:
                        eventMessage = brokerageOrder.RejectReason;
                        globalLeanOrderStatus = OrderStatus.Invalid;
                        break;
                    case TradeStationOrderStatusType.Exp:
                        eventMessage = "Expired";
                        globalLeanOrderStatus = OrderStatus.Canceled;
                        break;
                    case TradeStationOrderStatusType.FLP:
                        eventMessage = "PartiallyFilled (Canceled)";
                        globalLeanOrderStatus = OrderStatus.Canceled;
                        break;
                    // canceled by TradeStation, like the orders sent by a canceled order (OSO)
                    case TradeStationOrderStatusType.Can:
                        globalLeanOrderStatus = OrderStatus.Canceled;
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
                        globalLeanOrderStatus = OrderStatus.Canceled;
                        break;
                    case TradeStationOrderStatusType.Stp:
                        return;
                    default:
                        Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(HandleTradeStationMessage)}.TradeStationStreamStatus: {json}");
                        return;
                }

                if (globalLeanOrderStatus.IsClosed())
                {
                    _lastSubmittedUpdateByBrokerageOrderId.TryRemove(brokerageOrder.OrderID, out _);
                    _pendingCancelByBrokerageOrderId.TryRemove(brokerageOrder.OrderID, out _);
                }

                var leanOrders = new List<Order>();
                if (!TryGetOrRemoveCrossZeroOrder(brokerageOrder.OrderID, globalLeanOrderStatus, out var crossZeroLeanOrder))
                {
                    leanOrders = OrderProvider.GetOrdersByBrokerageId(brokerageOrder.OrderID);

                    if (leanOrders == null || leanOrders.Count == 0)
                    {
                        if (TryConvertToLeanOrder(brokerageOrder, out leanOrders))
                        {
                            var shouldSubmittedOrderEvents = new List<OrderEvent>();
                            foreach (var order in leanOrders)
                            {
                                OnNewBrokerageOrderNotification(new NewBrokerageOrderNotificationEventArgs(order));

                                if (order.Id == 0)
                                {
                                    shouldSubmittedOrderEvents.Clear();
                                    leanOrders = null;
                                    break;
                                }

                                shouldSubmittedOrderEvents.Add(
                                    new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, $"Order was submitted outside Lean")
                                    { Status = OrderStatus.Submitted });
                            }

                            if (globalLeanOrderStatus != OrderStatus.Submitted
                                && shouldSubmittedOrderEvents.Count > 0)
                            {
                                OnOrderEvents(shouldSubmittedOrderEvents);
                            }
                        }
                    }
                    else
                    {
                        // Applies only to user-placed TWS orders.
                        // Lean-managed orders are handled in PlaceOrder() / UpdateOrder().
                        // If TradeStation sends Ack/Done for an existing Lean order, handle it as an update
                        // to keep the Lean order lifecycle consistent.
                        if (brokerageOrder.Status is TradeStationOrderStatusType.Ack or TradeStationOrderStatusType.Don)
                        {
                            // Skip any Ack/Don that already has fill data. The final FLL event
                            // carries the same fill, so we wait for it. If we let this Ack pass,
                            // it becomes UpdateSubmitted (which Lean does not apply to holdings)
                            // and the later Filled event ends up with FillQuantity = 0 (#79, #84).
                            if (brokerageOrder.Legs.Any(l => l.ExecQuantity > 0))
                            {
                                return;
                            }
                            globalLeanOrderStatus = OrderStatus.UpdateSubmitted;
                        }
                    }
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

                var sendFeesOnce = default(bool);
                foreach (var leg in brokerageOrder.Legs.DistinctBy(x => x.Symbol))
                {
                    var legOrderStatus = globalLeanOrderStatus;
                    // Manually update the order status to 'Filled' because one of the combo order legs is fully filled.
                    // This prevents excessive event generation in Lean by avoiding repeated 'PartiallyFilled' updates.
                    if (legOrderStatus != OrderStatus.Filled && legOrderStatus == OrderStatus.PartiallyFilled && leg.QuantityRemaining == 0)
                    {
                        legOrderStatus = OrderStatus.Filled;
                    }

                    Order leanOrder;
                    if (leanOrders.Count == 1)
                    {
                        // If there is only one order, use it directly
                        leanOrder = leanOrders[0];
                    }
                    else
                    {
                        // If there are multiple orders, find the one that matches the leg's symbol
                        if (!_symbolMapper.TryGetLeanSymbol(leg.Symbol, leg.AssetType, leg.ExpirationDate, out var leanSymbol))
                        {
                            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, -1, $"{nameof(TradeStationBrokerage)}.{nameof(HandleTradeStationMessage)}: " +
                                $"Failed to map a Lean Symbol using the following details:: {leg} "));
                            return;
                        }

                        // Ensure there is an order with the specific symbol in leanOrders.
                        leanOrder = leanOrders.FirstOrDefault(order => order.Symbol == leanSymbol);

                        if (leanOrder == null)
                        {
                            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, -1, $"Error in {nameof(TradeStationBrokerage)}.{nameof(HandleTradeStationMessage)}: " +
                                $"Could not find order with symbol '{leanSymbol}' in leanOrders. " +
                                $"Brokerage Order ID: {brokerageOrder.OrderID}. Leg details - {leg}" +
                                $"Please verify that the order was correctly added to leanOrders."));
                            return;
                        }
                    }

                    // TradeStation may occasionally send duplicate event messages where the only difference is the order of the legs.
                    // If the order status is 'Filled', skip processing this message to avoid handling the same event multiple times.
                    if (leanOrder.Status == OrderStatus.Filled)
                    {
                        // The closing message of a leg that filled ahead of the combo is skipped here, so drop its entry now
                        if (globalLeanOrderStatus.IsClosed())
                        {
                            _orderIdToFillQuantity.TryRemove(leanOrder.Id, out _);
                        }
                        continue;
                    }

                    // TradeStation sends the accumulative filled quantity but we need the partial amount for our event
                    _orderIdToFillQuantity.TryGetValue(leanOrder.Id, out var previousExecutionAmount);
                    var accumulativeFilledQuantity = _orderIdToFillQuantity[leanOrder.Id] = leg.BuyOrSell.IsShort() ? decimal.Negate(leg.ExecQuantity) : leg.ExecQuantity;

                    if (globalLeanOrderStatus.IsClosed())
                    {
                        _orderIdToFillQuantity.TryRemove(leanOrder.Id, out _);
                    }

                    var orderEvent = new OrderEvent(
                        leanOrder,
                        DateTime.UtcNow,
                        OrderFee.Zero,
                        brokerageOrder.RejectReason)
                    {
                        Status = legOrderStatus,
                        FillPrice = _priceMapper.GetLeanPrice(leanOrder.Symbol, leg.ExecutionPrice),
                        FillQuantity = accumulativeFilledQuantity - previousExecutionAmount,
                        Message = eventMessage
                    };

                    // When updating a combo order with multiple legs, each leg's update is received separately via WebSocket.
                    // However, it's possible for one leg to be partially filled while another leg is still waiting to be filled.
                    // In these cases, to avoid generating unnecessary events in Lean (and causing spam),
                    // we skip processing if the current leg's update does not include any new fill quantity (i.e., the leg has not had any additional quantity filled).
                    if ((legOrderStatus == OrderStatus.PartiallyFilled || leanOrder.Status == OrderStatus.Filled) && orderEvent.FillQuantity == 0)
                    {
                        continue;
                    }

                    // Fees should only be sent once when the order is fully filled.
                    // The sendFeesOnce flag ensures that we don't send the OrderFee multiple times,
                    // especially for ComboOrders with multiple legs where each leg might trigger an update.
                    if (!sendFeesOnce && globalLeanOrderStatus == OrderStatus.Filled)
                    {
                        sendFeesOnce = true;
                        orderEvent.OrderFee = new OrderFee(new CashAmount(brokerageOrder.CommissionFee, Currencies.USD));
                    }

                    // if we filled the order and have another contingent order waiting, submit it
                    if (!TryHandleRemainingCrossZeroOrder(leanOrder, orderEvent))
                    {
                        OnOrderEvent(orderEvent);

                        // contingent orders: the orders sent by the one which filled are no longer held
                        OnContingentOrdersTriggered([orderEvent], OrderProvider);
                    }
                }

                // Sometimes, TradeStation returns incorrect responses with a duplicate leg symbol or without the leg being fully executed quantity.
                // This issue occurs only when dealing with OrderType.ComboMarket or OrderType.ComboLimit Orders.
                if (globalLeanOrderStatus == OrderStatus.Filled && leanOrders.Any(x => x.GroupOrderManager != null))
                {
                    leanOrders = OrderProvider.GetOrdersByBrokerageId(brokerageOrder.OrderID);
                    foreach (var leanOrder in leanOrders)
                    {
                        if (leanOrder.Status != OrderStatus.Filled)
                        {
                            // if we don't fill our order from TradeStation's response, we can keep the quantity in collection to calculate the correct holdings.
                            _orderIdToFillQuantity.TryRemove(leanOrder.Id, out var previousExecutionAmount);
                            var orderEvent = new OrderEvent(leanOrder, DateTime.UtcNow, OrderFee.Zero, brokerageOrder.RejectReason)
                            {
                                Status = OrderStatus.Filled,
                                FillQuantity = leanOrder.Quantity - previousExecutionAmount
                            };
                            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1, $"Detected missing fill event for OrderID: {leanOrder.Id} creating inferred filled event."));
                            OnOrderEvent(orderEvent);
                        }
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
                        _initialSnapshotCompleted = true;
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
            case SecurityType.IndexOption:
                switch (orderPosition)
                {
                    // Increasing existing long position or opening new long position from zero
                    case OrderPosition.BuyToOpen:
                        tradeAction = securityType.IsOption() ? TradeStationTradeActionType.BuyToOpen : TradeStationTradeActionType.Buy;
                        break;
                    // Decreasing existing short position or opening new short position from zero
                    case OrderPosition.SellToOpen:
                        tradeAction = securityType.IsOption() ? TradeStationTradeActionType.SellToOpen : TradeStationTradeActionType.SellShort;
                        break;
                    // Buying from an existing short position (reducing, closing or flipping)
                    case OrderPosition.BuyToClose:
                        tradeAction = securityType.IsOption() ? TradeStationTradeActionType.BuyToClose : TradeStationTradeActionType.BuyToCover;
                        break;
                    // Selling from an existing long position (reducing, closing or flipping)
                    case OrderPosition.SellToClose:
                        tradeAction = securityType.IsOption() ? TradeStationTradeActionType.SellToClose : TradeStationTradeActionType.Sell;
                        break;
                    // This should never happen
                    default:
                        throw new NotSupportedException("The specified order position is not supported.");
                }
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
    private bool TryCreateLeanOrder(TradeStationOrder order, Models.Leg leg, out Order leanOrder, GroupOrderManager groupOrderManager = null)
    {
        var orderQuantity = leg.BuyOrSell.IsShort() ? decimal.Negate(leg.QuantityOrdered) : leg.QuantityOrdered;

        leanOrder = default;
        if (!_symbolMapper.TryGetLeanSymbol(leg.Symbol, leg.AssetType, leg.ExpirationDate, out var leanSymbol))
        {
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, 1, $"The asset type '{leg.AssetType}' for symbol '{leg.Symbol}' is not supported. This position has been skipped."));
            return false;
        }

        var orderProperties = new TradeStationOrderProperties();
        if (!orderProperties.GetLeanTimeInForce(order.Duration, order.GoodTillDate))
        {
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1, $"Detected unsupported Lean TimeInForce of '{order.Duration}', ignoring. Using default: TimeInForce.GoodTilCanceled"));
        }

        if (!string.IsNullOrEmpty(order.AdvancedOptions))
        {
            var advancedOptions = order.AdvancedOptions.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var postOnlyChecked = false;
            foreach (var option in advancedOptions)
            {
                var key = option.Split('=', StringSplitOptions.RemoveEmptyEntries)[0];
                switch (key)
                {
                    case "AON":
                        orderProperties.AllOrNone = true;
                        break;
                    case "BKO" or "PSO" when !postOnlyChecked:
                        orderProperties.PostOnly = advancedOptions.Contains("BKO") && advancedOptions.Contains("PSO");
                        postOnlyChecked = true;
                        break;
                    // Ignore trailing stop option, the order's AdvancedOptions property has it
                    case "TRL":
                        break;
                    // STPTRG = Stop Trigger type (how a stop is armed). e.g: STT (Single Trade Tick, default)
                    // Docs: https://help.tradestation.com/09_05/eng/tradestationhelp/ob/oe_pref_all_triggers.htm
                    case "STPTRG":
                        break;
                    default:
                        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1, $" Detected unsupported Lean.TradeStationOrderProperties: {option}, ignoring"));
                        break;
                }
            }
        }

        // "Intelligent" is the default routing strategy for TradeStation orders.
        if (!string.IsNullOrEmpty(order.Routing) && !order.Routing.Equals("Intelligent", StringComparison.InvariantCultureIgnoreCase))
        {
            if (!_tradeStationRouteToLeanExchange.TryGetValue(order.Routing, out var mappedExchangeName))
            {
                mappedExchangeName = Exchanges.GetPrimaryExchange(order.Routing, leanSymbol.SecurityType);
            }
            orderProperties.Exchange = mappedExchangeName;
        }

        switch (order.OrderType)
        {
            case TradeStationOrderType.Market:
                switch (order.Duration)
                {
                    case TradeStationDuration.Close:
                        leanOrder = new MarketOnCloseOrder(leanSymbol, orderQuantity, order.OpenedDateTime, properties: orderProperties);
                        break;
                    case TradeStationDuration.Opening:
                        leanOrder = new MarketOnOpenOrder(leanSymbol, orderQuantity, order.OpenedDateTime, properties: orderProperties);
                        break;
                    default:
                        if (groupOrderManager == null)
                        {
                            leanOrder = new MarketOrder(leanSymbol, orderQuantity, order.OpenedDateTime, properties: orderProperties);
                        }
                        else
                        {
                            leanOrder = new ComboMarketOrder(leanSymbol, orderQuantity, order.OpenedDateTime, groupOrderManager, properties: orderProperties);
                        }
                        break;
                }
                break;
            case TradeStationOrderType.Limit:
                if (groupOrderManager == null)
                {
                    leanOrder = new LimitOrder(leanSymbol, orderQuantity, _priceMapper.GetLeanPrice(leanSymbol, order.LimitPrice), order.OpenedDateTime, properties: orderProperties);
                }
                else
                {
                    leanOrder = new ComboLimitOrder(leanSymbol, orderQuantity, _priceMapper.GetLeanPrice(leanSymbol, order.LimitPrice), order.OpenedDateTime, groupOrderManager, properties: orderProperties);
                }
                break;
            case TradeStationOrderType.StopMarket:
                if (order.TrailingStop.TryGetValue(out var trailingAmount, out var trailingAsPercentage))
                {
                    var leanTrailingAmount = trailingAsPercentage ? trailingAmount : _priceMapper.GetLeanPrice(leanSymbol, trailingAmount);
                    leanOrder = new TrailingStopOrder(leanSymbol, orderQuantity, leanTrailingAmount, trailingAsPercentage, order.OpenedDateTime, properties: orderProperties);
                }
                else
                {
                    leanOrder = new StopMarketOrder(leanSymbol, orderQuantity, _priceMapper.GetLeanPrice(leanSymbol, order.StopPrice), order.OpenedDateTime, properties: orderProperties);
                }
                break;
            case TradeStationOrderType.StopLimit:
                leanOrder = new StopLimitOrder(leanSymbol, orderQuantity, _priceMapper.GetLeanPrice(leanSymbol, order.StopPrice), _priceMapper.GetLeanPrice(leanSymbol, order.LimitPrice), order.OpenedDateTime, properties: orderProperties);
                break;
            default:
                throw new NotSupportedException($"Unsupported order type: {order.OrderType}");
        }

        leanOrder = leanOrder.SetOrderStatusAndBrokerId(order, leg);

        return true;
    }

    /// <summary>
    /// Attempts to retrieve the TradeStation route ID based on the specified exchange and security types.
    /// </summary>
    /// <param name="orderProperties">
    /// The order properties containing information about the TradeStation exchange.
    /// If no exchange is provided, the method will return <c>true</c> as no specific routing is required.
    /// </param>
    /// <param name="securityTypes">
    /// A collection of security types to be used for determining the correct TradeStation route.
    /// The route ID is determined by matching the exchange with one of the security types.
    /// </param>
    /// <param name="routeId">
    /// When this method returns, contains the route ID for the specified exchange and security types,
    /// or <c>null</c> if no matching route was found.
    /// </param>
    /// <returns>
    /// <c>true</c> if either the exchange is not provided, indicating that no routing is required,
    /// or if a valid route ID is found; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This method will return <c>true</c> when no exchange is set in the <paramref name="orderProperties"/>,
    /// since this implies that no specific routing is needed. The route ID is determined by attempting to match
    /// the provided exchange with a route for one of the security types.
    /// </remarks>
    protected bool GetTradeStationOrderRouteIdByOrderSecurityTypes(OrderProperties orderProperties, IReadOnlyCollection<SecurityType> securityTypes, out string routeId)
    {
        routeId = default;

        // If no exchange is set in tradeStationOrderProperties, return true.
        // This indicates that the user didn't specify an exchange, so no specific routing is required.
        if (orderProperties?.Exchange == null)
        {
            return true;
        }

        if (!_leanExchangeToTradeStationRoute.TryGetValue(orderProperties.Exchange, out var mappedExchangeName))
        {
            mappedExchangeName = orderProperties.Exchange.Name;
        }

        foreach (var securityType in securityTypes)
        {
            routeId = _routes.Value[securityType].FirstOrDefault(r => r.Name.Equals(mappedExchangeName, StringComparison.InvariantCultureIgnoreCase)).Id;

            if (routeId != null)
            {
                break;
            }
        }

        return !string.IsNullOrEmpty(routeId);
    }

    /// <summary>
    /// Dispose of the brokerage allocations
    /// </summary>
    public override void Dispose()
    {
        _aggregator.DisposeSafely();
        _tradeStationApiClient.DisposeSafely();
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
            // Create HTTP request
            using var request = ApiUtils.CreateJsonPostRequest("modules/license/read", information);
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