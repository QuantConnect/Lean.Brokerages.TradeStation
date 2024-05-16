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
using QuantConnect.Brokerages.TradeStation.Api;
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
    /// Represents the type of account used in TradeStation.
    /// For <see cref="TradeStationAccountType.Cash"/> accounts, it is used for trading <seealso cref="SecurityType.Equity"/> and <seealso cref="SecurityType.Option"/>.
    /// For <seealso cref="TradeStationAccountType.Futures"/> accounts, it is used for trading <seealso cref="SecurityType.Future"/> contracts.
    /// </summary>
    private readonly TradeStationAccountType _accountType;

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
    /// <param name="authorizationCodeFromUrl">The authorization code obtained from the URL.</param>
    /// <param name="accountType">The type of TradeStation account for the current session.</param>
    /// <param name="algorithm">The algorithm instance is required to retrieve account type</param>
    /// <param name="useProxy">Boolean value indicating whether to use a proxy for TradeStation API requests. Default is false.</param>
    public TradeStationBrokerage(string apiKey, string apiKeySecret, string restApiUrl, string authorizationCodeFromUrl, string accountType,
        IAlgorithm algorithm, bool useProxy = false)
        : this(apiKey, apiKeySecret, restApiUrl, authorizationCodeFromUrl, accountType, algorithm?.Portfolio?.Transactions, useProxy)
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
    /// <param name="accountType">The type of TradeStation account for the current session.</param>
    /// <param name="orderProvider">The order provider.</param>
    /// <param name="useProxy">Optional. Specifies whether to use a proxy for TradeStation API requests. Default is false.</param>
    public TradeStationBrokerage(string apiKey, string apiKeySecret, string restApiUrl, string authorizationCodeFromUrl, string accountType,
        IOrderProvider orderProvider, bool useProxy = false)
        : base("TradeStation")
    {
        OrderProvider = orderProvider;
        _symbolMapper = new TradeStationSymbolMapper();
        _tradeStationApiClient = new TradeStationApiClient(apiKey, apiKeySecret, restApiUrl, authorizationCodeFromUrl, useProxy: useProxy);

        if (!Enum.TryParse(accountType, out _accountType) || !Enum.IsDefined(typeof(TradeStationAccountType), _accountType))
        {
            throw new ArgumentException($"An error occurred while parsing the account type '{accountType}'. Please ensure that the provided account type is valid and supported by the system.");
    }

    #region Brokerage

    /// <summary>
    /// Gets all open orders on the account.
    /// NOTE: The order objects returned do not have QC order IDs.
    /// </summary>
    /// <returns>The open orders returned from IB</returns>
    public override List<Order> GetOpenOrders()
    {
        var orders = _tradeStationApiClient.GetAllAccountOrders().SynchronouslyAwaitTaskResult();

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
        var positions = _tradeStationApiClient.GetAllAccountPositions().SynchronouslyAwaitTaskResult();

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
        var balances = _tradeStationApiClient.GetAllAccountBalances().SynchronouslyAwaitTaskResult();

        foreach (var balanceError in balances.Errors)
        {
            Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(GetCashBalance)}: Error encountered in Account ID: {balanceError.AccountID}. Type: {balanceError.Error}. Message: {balanceError.Message}");
        }

        var cashBalance = new List<CashAmount>();
        foreach (var balance in balances.Balances)
        {
            if (balance.AccountType == _accountType )
            {
                cashBalance.Add(new CashAmount(decimal.Parse(balance.CashBalance, CultureInfo.InvariantCulture), Currencies.USD));
            }
            }

        if (cashBalance.Count == 0)
        {
            throw new Exception($"Unable to retrieve cash balance for {_accountType}. No suitable account was found. Please select one of the following account types: {string.Join(',', balances.Balances.Select(x => x.AccountType))}");
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
        var result = _tradeStationApiClient.PlaceOrder(order, symbol).SynchronouslyAwaitTaskResult();

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
            var result = _tradeStationApiClient.ReplaceOrder(order).SynchronouslyAwaitTaskResult();

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
        if (_tradeStationApiClient.CancelOrder(orderID).SynchronouslyAwaitTaskResult())
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
        return IsConnected;
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
    /// Subscribes to order updates.
    /// </summary>
    /// <returns>True if the subscription was successful; otherwise, false.</returns>
    private bool SubscribeOnOrderUpdate()
    {
        Task.Factory.StartNew(async () =>
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await foreach (var json in _tradeStationApiClient.StreamOrders())
                    {
                        var jObj = JObject.Parse(json);
                        if (IsConnected && jObj["AccountID"] != null)
                        {
                            var brokerageOrder = jObj.ToObject<Models.Order>();
                            var leanOrder = OrderProvider.GetOrdersByBrokerageId(brokerageOrder.OrderID).FirstOrDefault();
                            if (leanOrder == null)
                            {
                                Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnOrderUpdate)}. order id not found: {brokerageOrder.OrderID}");
                                continue;
                            }

                            var leg = brokerageOrder.Legs.First();
                            // TODO: Where may we take Market ?
                            var leanSymbol = _symbolMapper.GetLeanSymbol(leg.Underlying ?? leg.Symbol, leg.AssetType.ConvertAssetTypeToSecurityType(), Market.USA,
                                leg.ExpirationDate, leg.StrikePrice, leg.OptionType.ConvertOptionTypeToOptionRight());

                            switch (brokerageOrder.Status)
                            {
                                case TradeStationOrderStatusType.Fll:
                                case TradeStationOrderStatusType.Brf:
                                    leanOrder.Status = OrderStatus.Filled;
                                    break;
                                case TradeStationOrderStatusType.Fpr:
                                    leanOrder.Status = OrderStatus.PartiallyFilled;
                                    break;
                                case TradeStationOrderStatusType.Rej:
                                case TradeStationOrderStatusType.Tsc:
                                case TradeStationOrderStatusType.Rjr:
                                case TradeStationOrderStatusType.Bro:
                                    leanOrder.Status = OrderStatus.Invalid;
                                    break;
                                default:
                                    continue;
                            };

                            var orderEvent = new OrderEvent(
                                leanOrder.Id,
                                leanSymbol,
                                brokerageOrder.OpenedDateTime,
                                leanOrder.Status,
                                leg.BuyOrSell == "Buy" ? OrderDirection.Buy : OrderDirection.Sell,
                                leg.ExecutionPrice,
                                leg.ExecQuantity,
                                new OrderFee(new CashAmount(brokerageOrder.CommissionFee, Currencies.USD)));

                            OnOrderEvent(orderEvent);

                        }
                        else if (jObj["StreamStatus"] != null)
                        {
                            var status = jObj.ToObject<Models.TradeStationStreamStatus>();
                            switch (status.StreamStatus)
                            {
                                case "EndSnapshot":
                                    _autoResetEvent.Set();
                                    break;
                                default:
                                    Log.Debug($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnOrderUpdate)}.TradeStationStreamStatus: {json}");
                                    break;
                            }
                        }
                        else
                        {
                            Log.Debug($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnOrderUpdate)}.Response: {json}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnOrderUpdate)}.Exception: {ex}");
                }
                _cancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            }
        }, _cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        return _autoResetEvent.WaitOne(TimeSpan.FromSeconds(10), _cancellationTokenSource.Token);
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
