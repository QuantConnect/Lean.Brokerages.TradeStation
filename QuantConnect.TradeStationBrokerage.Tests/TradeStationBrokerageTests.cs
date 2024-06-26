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

using Moq;
using System;
using System.Linq;
using NUnit.Framework;
using QuantConnect.Tests;
using QuantConnect.Orders;
using QuantConnect.Logging;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Configuration;
using System.Collections.Generic;
using QuantConnect.Tests.Brokerages;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    [TestFixture]
    public partial class TradeStationBrokerageTests : BrokerageTests
    {
        /// <summary>
        /// Gets the TradeStationBrokerageTest instance from the Brokerage.
        /// </summary>
        private TradeStationBrokerageTest _brokerage => Brokerage as TradeStationBrokerageTest;

        protected override Symbol Symbol { get; } = Symbols.AAPL;

        protected override SecurityType SecurityType { get; }

        protected override IBrokerage CreateBrokerage(IOrderProvider orderProvider, ISecurityProvider securityProvider)
        {
            var algorithm = new Mock<IAlgorithm>();

            var apiKey = Config.Get("trade-station-api-key");
            var apiSecret = Config.Get("trade-station-api-secret");
            var apiUrl = Config.Get("trade-station-api-url");
            var authorizationCodeFromUrl = Config.Get("trade-station-code-from-url");
            var accountType = Config.Get("trade-station-account-type");
            var redirectUrl = Config.Get("trade-station-redirect-url");

            if (new string[] { apiKey, apiSecret, apiUrl }.Any(string.IsNullOrEmpty))
            {
                throw new ArgumentException("API key, secret, and URL cannot be empty or null. Please ensure these values are correctly set in the configuration file.");
            }

            return new TradeStationBrokerageTest(apiKey, apiSecret, apiUrl, redirectUrl, authorizationCodeFromUrl, accountType, orderProvider, securityProvider, useProxy: true);
        }
        protected override bool IsAsync()
        {
            return false;
        }

        /// <summary>
        /// Indicates whether the order is a long order. This field is used in the <see cref="GetAskPrice(Symbol)"/> method
        /// to determine the ask price. If it is a long position, the stop price must be greater. If it is a short position,
        /// the stop price must be lower.
        /// </summary>
        private bool IsLongOrder = false;

        /// <summary>
        /// Gets the ask price for a given symbol. If the order is a long order, 
        /// it returns the last price plus 0.1, rounded to 2 decimal places. 
        /// Otherwise, it returns the last price minus 0.1, rounded to 2 decimal places.
        /// </summary>
        /// <param name="symbol">The symbol for which to get the ask price.</param>
        /// <returns>The ask price for the specified symbol.</returns>
        protected override decimal GetAskPrice(Symbol symbol)
        {
            if (IsLongOrder)
            {
                return Math.Round(_brokerage.GetLastPrice(symbol) + 0.1m, 2);
            }

            return Math.Round(_brokerage.GetLastPrice(symbol) - 0.1m, 2);
        }

        /// <summary>
        /// Provides the data required to test each order type in various cases
        /// </summary>
        private static IEnumerable<TestCaseData> OrderMoreRealToLiveParameters
        {
            get
            {
                var INTL = Symbol.Create("INTL", SecurityType.Equity, Market.USA);
                yield return new TestCaseData(new LimitOrderTestParameters(INTL, 23m, 22m));
                yield return new TestCaseData(new StopMarketOrderTestParameters(INTL, 22.61m, 23m));
                yield return new TestCaseData(new StopLimitOrderTestParameters(INTL, 22.61m, 28.65m));
            }
        }

        private static IEnumerable<TestCaseData> OrderSimpleParameters
        {
            get
            {
                var AAPLOption = Symbol.CreateOption(Symbols.AAPL, Market.USA, OptionStyle.American, OptionRight.Call, 205m, new DateTime(2024, 6, 28));
                yield return new TestCaseData(new LimitOrderTestParameters(AAPLOption, 7m, 6.5m)).SetCategory("Option").SetName("AAPL Option Limit");
                yield return new TestCaseData(new StopMarketOrderTestParameters(AAPLOption, 7.25m, 7.20m)).SetCategory("Option").SetName("AAPL Option StopMarket");
                yield return new TestCaseData(new StopLimitOrderTestParameters(AAPLOption, 7.25m, 7.25m)).SetCategory("Option").SetName("AAPL Option StopLimit");

                var INTL = Symbol.Create("INTL", SecurityType.Equity, Market.USA);
                yield return new TestCaseData(new LimitOrderTestParameters(INTL, 23m, 22m)).SetCategory("Equity").SetName("INTL Limit");
                yield return new TestCaseData(new StopMarketOrderTestParameters(INTL, 23m, 22m)).SetCategory("Equity").SetName("INTL StopMarket");
                yield return new TestCaseData(new StopLimitOrderTestParameters(INTL, 23m, 23m)).SetCategory("Equity").SetName("INTL StopLimit");

                var COTTON = Symbol.CreateFuture(Futures.Softs.Cotton2, Market.USA, new DateTime(2024, 7, 1));
                yield return new TestCaseData(new LimitOrderTestParameters(COTTON, 72m, 70m)).SetCategory("Future").SetName("COTTON Future Limit");
                yield return new TestCaseData(new StopMarketOrderTestParameters(COTTON, 72m, 70m)).SetCategory("Future").SetName("COTTON Future StopMarket");
                yield return new TestCaseData(new StopLimitOrderTestParameters(COTTON, 72m, 70m)).SetCategory("Future").SetName("COTTON Future StopLimit");
            }
        }

        private static IEnumerable<TestCaseData> SymbolOrderTypeParameters
        {
            get
            {
                var INTL = Symbol.Create("INTL", SecurityType.Equity, Market.USA);
                yield return new TestCaseData(INTL, OrderType.Limit);
                yield return new TestCaseData(INTL, OrderType.StopMarket);
                yield return new TestCaseData(INTL, OrderType.StopLimit);
            }
        }

        [Test, TestCaseSource(nameof(OrderSimpleParameters))]
        public override void CancelOrders(OrderTestParameters parameters)
        {
            base.CancelOrders(parameters);
        }

        [Test, TestCaseSource(nameof(OrderMoreRealToLiveParameters))]
        public override void LongFromZero(OrderTestParameters parameters)
        {
            base.LongFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderMoreRealToLiveParameters))]
        public override void CloseFromLong(OrderTestParameters parameters)
        {
            base.CloseFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderMoreRealToLiveParameters))]
        public override void ShortFromZero(OrderTestParameters parameters)
        {
            base.ShortFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderMoreRealToLiveParameters))]
        public override void CloseFromShort(OrderTestParameters parameters)
        {
            base.CloseFromShort(parameters);
        }

        [Test, TestCaseSource(nameof(OrderMoreRealToLiveParameters))]
        public override void ShortFromLong(OrderTestParameters parameters)
        {
            IsLongOrder = false;
            base.ShortFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderSimpleParameters))]
        public override void LongFromShort(OrderTestParameters parameters)
        {
            IsLongOrder = true;
            base.LongFromShort(parameters);
        }

        [Test, TestCaseSource(nameof(SymbolOrderTypeParameters))]
        public void ShortFromShort(Symbol symbol, OrderType orderType)
        {
            IsLongOrder = false;
            var lastPrice = _brokerage.GetLastPrice(symbol);
            OrderTestParameters parameters = orderType switch
            {
                OrderType.Limit => new LimitOrderTestParameters(symbol, Math.Round(lastPrice + 0.5m, 2), Math.Round(lastPrice - 0.5m)),
                OrderType.StopMarket => new StopMarketOrderTestParameters(symbol, Math.Round(lastPrice - 0.2m, 2), Math.Round(lastPrice - 0.4m, 2)),
                OrderType.StopLimit => new StopLimitOrderTestParameters(symbol, Math.Round(lastPrice - 0.05m, 2), Math.Round(lastPrice - 0.02m, 2)),
                _ => throw new NotImplementedException("Not supported type of order")
            };

            Log.Trace("");
            Log.Trace("SHORT FROM SHORT");
            Log.Trace("");
            // first go short
            PlaceOrderWaitForStatus(parameters.CreateShortMarketOrder(-GetDefaultQuantity()), OrderStatus.Filled);

            // now go short again
            var order = PlaceOrderWaitForStatus(parameters.CreateShortOrder(-2 * GetDefaultQuantity()), parameters.ExpectedStatus);

            if (parameters.ModifyUntilFilled)
            {
                ModifyOrderUntilFilled(order, parameters);
            }
        }

        [Test, TestCaseSource(nameof(SymbolOrderTypeParameters))]
        public virtual void LongFromLong(Symbol symbol, OrderType orderType)
        {
            IsLongOrder = true;
            var lastPrice = _brokerage.GetLastPrice(symbol);
            OrderTestParameters parameters = orderType switch
            {
                OrderType.Limit => new LimitOrderTestParameters(symbol, Math.Round(lastPrice + 0.5m, 2), Math.Round(lastPrice - 0.5m)),
                OrderType.StopMarket => new StopMarketOrderTestParameters(symbol, Math.Round(lastPrice + 0.5m, 2), Math.Round(lastPrice + 0.6m, 2)),
                OrderType.StopLimit => new StopLimitOrderTestParameters(symbol, Math.Round(lastPrice + 0.5m, 2), Math.Round(lastPrice + 0.6m, 2)),
                _ => throw new NotImplementedException("Not supported type of order")
            };

            Log.Trace("");
            Log.Trace("LONG FROM LONG");
            Log.Trace("");
            // first go long
            PlaceOrderWaitForStatus(parameters.CreateLongMarketOrder(GetDefaultQuantity()));

            // now go long again
            var order = PlaceOrderWaitForStatus(parameters.CreateLongOrder(2 * GetDefaultQuantity()), parameters.ExpectedStatus);

            if (parameters.ModifyUntilFilled)
            {
                ModifyOrderUntilFilled(order, parameters);
            }
        }

        [Test]
        public void LookupSymbols()
        {
            var option = Symbol.CreateCanonicalOption(Symbols.AAPL);

            var options = (Brokerage as IDataQueueUniverseProvider).LookupSymbols(option, false).ToList();
            Assert.IsNotNull(options);
            Assert.True(options.Any());
            Assert.Greater(options.Count, 0);
            Assert.That(options.Distinct().ToList().Count, Is.EqualTo(options.Count));
        }
        /// <summary>
        /// Tests the scenario where a market order transitions from a short position to a long position,
        /// crossing zero in the process. This test ensures the order status change events occur in the expected
        /// sequence: Submitted, PartiallyFilled, and Filled.
        /// 
        /// The method performs the following steps:
        /// <list type="number">
        /// <item>Creates a market order for the AAPL symbol with a TimeInForce property set to Day.</item>
        /// <item>Places a short market order to establish a short position of at least -1 quantity.</item>
        /// <item>Subscribes to the order status change events and records the status changes.</item>
        /// <item>Places a long market order that crosses zero, effectively transitioning from short to long.</item>
        /// <item>Asserts that the order is not null, has a BrokerId, and the status change events match the expected sequence.</item>
        /// </list>
        /// </summary>
        /// <param name="longQuantityMultiplayer">The multiplier for the long order quantity, relative to the default quantity.</param>
        [TestCase(4)]
        public void MarketCrossZeroLongFromShort(decimal longQuantityMultiplayer)
        {
            var expectedOrderStatusChangedOrdering = new[] { OrderStatus.Submitted, OrderStatus.PartiallyFilled, OrderStatus.Filled };
            var actualCrossZeroOrderStatusOrdering = new Queue<OrderStatus>();

            // create market order to holding something
            var marketOrder = new MarketOrderTestParameters(Symbols.AAPL, properties: new OrderProperties() { TimeInForce = TimeInForce.Day });

            // place short position to holding at least -1 quantity to run of cross zero order
            PlaceOrderWaitForStatus(marketOrder.CreateLongMarketOrder(GetDefaultQuantity()), OrderStatus.Filled, secondsTimeout: 120);

            // validate ordering of order status change events
            Brokerage.OrdersStatusChanged += (_, orderEvents) => actualCrossZeroOrderStatusOrdering.Enqueue(orderEvents[0].Status);

            // Place Order with crossZero processing
            var order = PlaceOrderWaitForStatus(marketOrder.CreateShortMarketOrder(longQuantityMultiplayer * -GetDefaultQuantity()), OrderStatus.Filled, 120);

            Assert.IsNotNull(order);
            Assert.Greater(order.BrokerId.Count, 0);
            CollectionAssert.AreEquivalent(expectedOrderStatusChangedOrdering, actualCrossZeroOrderStatusOrdering);
        }

        public class TradeStationBrokerageTest : TradeStationBrokerage
        {
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
            public TradeStationBrokerageTest(string apiKey, string apiKeySecret, string restApiUrl, string redirectUrl,
                string authorizationCodeFromUrl, string accountType, IOrderProvider orderProvider, ISecurityProvider securityProvider, bool useProxy = false)
                : base(apiKey, apiKeySecret, restApiUrl, redirectUrl, authorizationCodeFromUrl, accountType, orderProvider, securityProvider, useProxy)
            { }

            /// <summary>
            /// Retrieves the last price of the specified symbol.
            /// </summary>
            /// <param name="symbol">The symbol for which to retrieve the last price.</param>
            /// <returns>The last price of the specified symbol as a decimal.</returns>
            public decimal GetLastPrice(Symbol symbol)
            {
                return GetQuote(symbol).Quotes.Single().Last;
            }
        }
    }
}