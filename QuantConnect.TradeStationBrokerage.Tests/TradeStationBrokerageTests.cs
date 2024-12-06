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
using System.Linq;
using NUnit.Framework;
using System.Threading;
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
        protected TradeStationBrokerageTest _brokerage => Brokerage as TradeStationBrokerageTest;

        protected override Symbol Symbol { get; } = Symbols.AAPL;

        protected override SecurityType SecurityType { get; }

        protected override IBrokerage CreateBrokerage(IOrderProvider orderProvider, ISecurityProvider securityProvider)
        {
            return TestSetup.CreateBrokerage(orderProvider, securityProvider, forceCreateBrokerageInstance: true);
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
            var lastPrice = _brokerage.GetPrice(symbol).Last;
            if (IsLongOrder)
            {
                return AddAndRound(lastPrice, 0.03m);
            }
            return SubtractAndRound(lastPrice, 0.03m);
        }

        /// <summary>
        /// Provides the data required to test each order type in various cases
        /// </summary>
        private static IEnumerable<TestCaseData> EquityMarketOrderParameters
        {
            get
            {
                var EPU = Symbol.Create("AAPL", SecurityType.Equity, Market.USA);
                yield return new TestCaseData(new MarketOrderTestParameters(EPU));
            }
        }

        [Test, TestCaseSource(nameof(EquityMarketOrderParameters))]
        public void ShortFromZeroEquity(OrderTestParameters parameters)
        {
            base.ShortFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(EquityMarketOrderParameters))]
        public void ShortFromLongEquity(OrderTestParameters parameters)
        {
            base.ShortFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(EquityMarketOrderParameters))]
        public void LongFromShortEquity(OrderTestParameters parameters)
        {
            base.LongFromShort(parameters);
        }

        private static IEnumerable<TestCaseData> OrderTestParameters
        {
            get
            {
                TestGlobals.Initialize();
                var INTL = Symbol.Create("INTL", SecurityType.Equity, Market.USA);
                yield return new TestCaseData(new LimitOrderTestParameters(INTL, 23m, 22m)).SetCategory("Equity").SetName("INTL|EQUITY|LIMIT");
                yield return new TestCaseData(new StopMarketOrderTestParameters(INTL, 23m, 22m)).SetCategory("Equity").SetName("INTL|EQUITY|STOPMARKET");
                yield return new TestCaseData(new StopLimitOrderTestParameters(INTL, 23m, 23m)).SetCategory("Equity").SetName("INTL|EQUITY|STOPLIMIT");

                var AAPLOption = Symbol.CreateOption(Symbols.AAPL, Market.USA, OptionStyle.American, OptionRight.Call, 100m, new DateTime(2024, 9, 6));
                yield return new TestCaseData(new LimitOrderTestParameters(AAPLOption, 15.85m, 14.85m)).SetCategory("Option").SetName("AAPL|OPTION|LIMIT");
                yield return new TestCaseData(new StopMarketOrderTestParameters(AAPLOption, 15.1m, 15.1m)).SetCategory("Option").SetName("AAPL|OPTION|STOPMARKET");
                yield return new TestCaseData(new StopLimitOrderTestParameters(AAPLOption, 15.1m, 15.1m)).SetCategory("Option").SetName("AAPL|OPTION|STOPLIMIT");

                var COTTON = Symbol.CreateFuture(Futures.Softs.Cotton2, Market.ICE, new DateTime(2024, 10, 1));
                yield return new TestCaseData(new LimitOrderTestParameters(COTTON, 72m, 70m)).SetCategory("Future").SetName("COTTON|FUTURE|LIMIT").Explicit("At the first, setup specific `trade-station-account-type` in config file.");
                yield return new TestCaseData(new StopMarketOrderTestParameters(COTTON, 72m, 70m)).SetCategory("Future").SetName("COTTON|FUTURE|STOPMARKET").Explicit("At the first, setup specific `trade-station-account-type` in config file.");
                yield return new TestCaseData(new StopLimitOrderTestParameters(COTTON, 72m, 72m)).SetCategory("Future").SetName("COTTON|FUTURE|STOPLIMIT").Explicit("At the first, setup specific `trade-station-account-type` in config file.");
            }
        }

        [TestCaseSource(nameof(OrderTestParameters))]
        public override void CancelOrders(OrderTestParameters parameters)
        {
            parameters = GetLastPriceForLongOrder(parameters);
            base.CancelOrders(parameters);
        }

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public override void LongFromZero(OrderTestParameters parameters)
        {
            parameters = GetLastPriceForLongOrder(parameters);
            base.LongFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public override void CloseFromLong(OrderTestParameters parameters)
        {
            IsLongOrder = false;
            parameters = GetLastPriceForShortOrder(parameters);
            base.CloseFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public override void ShortFromZero(OrderTestParameters parameters)
        {
            parameters = GetLastPriceForShortOrder(parameters);
            base.ShortFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public override void CloseFromShort(OrderTestParameters parameters)
        {
            IsLongOrder = true;
            parameters = GetLastPriceForLongOrder(parameters);
            base.CloseFromShort(parameters);
        }

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public override void ShortFromLong(OrderTestParameters parameters)
        {
            IsLongOrder = false;
            parameters = GetLastPriceForShortOrder(parameters);
            base.ShortFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public override void LongFromShort(OrderTestParameters parameters)
        {
            IsLongOrder = true;
            parameters = GetLastPriceForLongOrder(parameters);
            base.LongFromShort(parameters);
        }

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public void ShortFromShort(OrderTestParameters parameters)
        {
            IsLongOrder = false;
            parameters = GetLastPriceForShortOrder(parameters);

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

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public virtual void LongFromLong(OrderTestParameters parameters)
        {
            IsLongOrder = true;
            parameters = GetLastPriceForLongOrder(parameters);

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
        [TestCase("AAPL", SecurityType.Equity, Market.USA, null, 4)]
        [TestCase(Futures.Softs.Cotton2, SecurityType.Future, Market.ICE, "2024/10/1", 4, Description = "Pay attention on Config:trade-station-account-type")]
        public void MarketCrossZeroLongFromShort(string ticker, SecurityType securityType, string market, DateTime expireDate, decimal longQuantityMultiplayer)
        {
            Log.Trace($"TEST MARKET CROSS ZERO LONG FROM SHORT OF {securityType.ToString().ToUpperInvariant()}");
            var symbol = TradeStationBrokerageHistoryProviderTests.CreateSymbol(ticker, securityType, expirationDate: expireDate, market: market);
            var expectedOrderStatusChangedOrdering = new[] { OrderStatus.Submitted, OrderStatus.PartiallyFilled, OrderStatus.Filled };
            var actualCrossZeroOrderStatusOrdering = new Queue<OrderStatus>();

            // create market order to holding something
            var marketOrder = new MarketOrderTestParameters(symbol, properties: new OrderProperties() { TimeInForce = TimeInForce.Day });

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

        [Test]
        public void PartialLongMarketOrder()
        {
            var marketOrder = new MarketOrderTestParameters(Symbols.SPY, properties: new OrderProperties() { TimeInForce = TimeInForce.Day });
            PlaceOrderWaitForStatus(marketOrder.CreateLongMarketOrder(-1797), OrderStatus.Filled, secondsTimeout: 120);
        }

        private static IEnumerable<TestCaseData> ExchangesTestParameters
        {
            get
            {
                yield return new TestCaseData(Symbols.AAPL, Exchange.CBOE, true);
                yield return new TestCaseData(Symbols.AAPL, Exchange.AMEX, false);
                yield return new TestCaseData(Symbols.AAPL, Exchange.IEX, false);
                yield return new TestCaseData(Symbols.AAPL, Exchange.MIAX_SAPPHIRE, true);
                yield return new TestCaseData(Symbols.AAPL, Exchange.ISE_GEMINI, true);
                var option = Symbol.CreateOption(Symbols.AAPL, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 100m, new DateTime(2024, 9, 6));
                yield return new TestCaseData(option, Exchange.ISE_GEMINI, false);
                yield return new TestCaseData(option, Exchange.AMEX_Options, false);
                yield return new TestCaseData(option, Exchange.AMEX, true);
            }
        }

        [TestCaseSource(nameof(ExchangesTestParameters))]
        public void PlaceMarketOrderWithDifferentExchanges(Symbol symbol, Exchange exchange, bool isShouldThrow)
        {
            var marketOrder = new MarketOrderTestParameters(symbol, properties: new TradeStationOrderProperties() { Exchange = exchange });

            if (isShouldThrow)
            {
                Assert.Throws<AssertionException>(() => PlaceOrderWaitForStatus(marketOrder.CreateLongMarketOrder(1), OrderStatus.Invalid));
            }
            else
            {
                PlaceOrderWaitForStatus(marketOrder.CreateLongMarketOrder(1), OrderStatus.Filled);
            }
        }

        [Test]
        public void PlaceLimitOrderAndUpdate()
        {
            Log.Trace("PLACE LIMIT ORDER AND UPDATE");
            var symbol = Symbols.AAPL;
            var lastPrice = _brokerage.GetPrice(symbol).Last;
            var limitPrice = SubtractAndRound(lastPrice, 0.5m);
            var limitOrder = new LimitOrder(Symbols.AAPL, 1, limitPrice, DateTime.UtcNow);

            var submittedResetEvent = new AutoResetEvent(false);
            var updateSubmittedResetEvent = new AutoResetEvent(false);
            var filledResetEvent = new ManualResetEvent(false);

            Brokerage.OrdersStatusChanged += (_, orderEvents) =>
            {
                var orderEvent = orderEvents[0];

                Log.Trace("");
                Log.Trace($"{nameof(PlaceLimitOrderAndUpdate)}.OrderEvent.Status: {orderEvent.Status}");
                Log.Trace("");

                if (orderEvent.Status == OrderStatus.Submitted)
                {
                    submittedResetEvent.Set();
                }

                if (orderEvent.Status == OrderStatus.UpdateSubmitted)
                {
                    updateSubmittedResetEvent.Set();
                }

                if (orderEvent.Status == OrderStatus.Filled)
                {
                    filledResetEvent.Set();
                }
            };

            OrderProvider.Add(limitOrder);

            if (!Brokerage.PlaceOrder(limitOrder))
            {
                Assert.Fail("Brokerage failed to place the order: " + limitOrder);
            }

            if (!submittedResetEvent.WaitOne(TimeSpan.FromSeconds(5)))
            {
                Assert.Fail($"{nameof(PlaceLimitOrderAndUpdate)}: the brokerage doesn't return {OrderStatus.Submitted}");
            }

            var order = OrderProvider.GetOrderById(1);

            var subtractor = 0.1m;
            var subtraction = 0.3m;
            do
            {
                subtraction -= subtractor;
                var newLastPrice = _brokerage.GetPrice(symbol).Last;
                var newLimitPrice = Math.Round(newLastPrice - subtraction, 2);

                order.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, order.Id, new() { LimitPrice = newLimitPrice }));

                if (!Brokerage.UpdateOrder(order))
                {
                    if (filledResetEvent.WaitOne(TimeSpan.FromSeconds(10)))
                    {
                        break;
                    }

                    Assert.Fail("Brokerage failed to update the order: " + order);
                }

                if (!updateSubmittedResetEvent.WaitOne(TimeSpan.FromSeconds(10)))
                {
                    Assert.Fail($"{nameof(PlaceLimitOrderAndUpdate)}: the brokerage doesn't return {OrderStatus.UpdateSubmitted}");
                }
            } while (subtraction > -subtractor);

            if (!filledResetEvent.WaitOne(TimeSpan.FromSeconds(10)))
            {
                Assert.Fail($"{nameof(PlaceLimitOrderAndUpdate)}: the brokerage doesn't return {OrderStatus.Filled}");
            }
        }

        private static IEnumerable<TestCaseData> MarketOpenCloseOrderTypeParameters
        {
            get
            {
                var symbol = Symbols.AAPL;
                yield return new TestCaseData(new MarketOnOpenOrder(symbol, 1m, DateTime.UtcNow), !symbol.IsMarketOpen(DateTime.UtcNow, false));
                yield return new TestCaseData(new MarketOnCloseOrder(symbol, 1m, DateTime.UtcNow), symbol.IsMarketOpen(DateTime.UtcNow, false));
            }
        }

        [TestCaseSource(nameof(MarketOpenCloseOrderTypeParameters))]
        public void PlaceMarketOpenCloseOrder(Order order, bool marketIsOpen)
        {
            Log.Trace($"PLACE {order.Type} ORDER TEST");

            var submittedResetEvent = new AutoResetEvent(false);
            var invalidResetEvent = new AutoResetEvent(false);

            OrderProvider.Add(order);

            Brokerage.OrdersStatusChanged += (_, orderEvents) =>
            {
                var orderEvent = orderEvents[0];

                Log.Trace("");
                Log.Trace($"{nameof(PlaceMarketOpenCloseOrder)}.OrderEvent.Status: {orderEvent.Status}");
                Log.Trace("");

                if (orderEvent.Status == OrderStatus.Submitted)
                {
                    submittedResetEvent.Set();
                }
                else if (orderEvent.Status == OrderStatus.Invalid)
                {
                    invalidResetEvent.Set();
                }
            };

            Assert.IsTrue(Brokerage.PlaceOrder(order));

            if (marketIsOpen)
            {
                if (!submittedResetEvent.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    Assert.Fail($"{nameof(PlaceLimitOrderAndUpdate)}: the brokerage doesn't return {OrderStatus.Submitted}");
                }

                var openOrders = Brokerage.GetOpenOrders();

                Assert.IsNotEmpty(openOrders);
                Assert.That(openOrders.Count, Is.EqualTo(1));
                Assert.That(openOrders[0].Type, Is.EqualTo(order.Type));
                Assert.IsTrue(Brokerage.CancelOrder(order));
            }
            else
            {
                if (!invalidResetEvent.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    Assert.Fail($"{nameof(PlaceLimitOrderAndUpdate)}: the brokerage doesn't return {OrderStatus.Invalid}");
                }
            }
        }

        [Test]
        public void PlaceComboMarketOrder()
        {
            var underlyingSymbol = Symbols.AAPL;
            var legs = new List<(Symbol symbol, decimal quantity)>
            {
                (underlyingSymbol, 16),
                (Symbol.CreateOption(underlyingSymbol, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 100m, new DateTime(2024, 9, 6)), -1),
                (Symbol.CreateOption(underlyingSymbol, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 125m, new DateTime(2024, 9, 6)), 1)
            };

            var groupOrderManager = new GroupOrderManager(1, legCount: legs.Count, quantity: 8);

            var comboOrders = PlaceComboOrder(
                legs,
                null,
                (optionContract, quantity, price, groupOrderManager) =>
                new ComboMarketOrder(optionContract, quantity, DateTime.UtcNow, groupOrderManager),
                groupOrderManager);

            AssertComboOrderPlacedSuccessfully(comboOrders);
        }

        [TestCase(150, OrderDirection.Buy)]
        public void PlaceComboLimitOrder(decimal comboLimitPrice, OrderDirection comboDirection)
        {
            var underlyingSymbol = Symbols.AAPL;
            var optionContracts = new List<(Symbol symbol, decimal quantity)>
            {
                (Symbol.CreateOption(underlyingSymbol, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 100m, new DateTime(2024, 9, 6)), 10),
                (Symbol.CreateOption(underlyingSymbol, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 125m, new DateTime(2024, 9, 6)), 1)
            };

            var groupOrderManager = new GroupOrderManager(1, legCount: optionContracts.Count, quantity: comboDirection == OrderDirection.Buy ? 2 : -2);

            var comboOrders = PlaceComboOrder(
                optionContracts,
                comboLimitPrice,
                (optionContract, quantity, price, groupOrderManager) =>
                    new ComboLimitOrder(optionContract, quantity.GetOrderLegGroupQuantity(groupOrderManager), price.Value, DateTime.UtcNow, groupOrderManager, properties: new TradeStationOrderProperties() { AllOrNone = true }),
                groupOrderManager);

            AssertComboOrderPlacedSuccessfully(comboOrders);
            CancelComboOpenOrders(comboOrders);
        }

        [TestCase(70, 80)]
        public void PlaceComboLimitOrderAndUpdateLimitPrice(decimal comboLimitPrice, decimal newComboLimitPrice)
        {
            var underlyingSymbol = Symbols.AAPL;
            var optionContracts = new List<(Symbol symbol, decimal quantity)>
            {
                (Symbol.CreateOption(underlyingSymbol, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 100m, new DateTime(2024, 9, 6)), -1),
                (Symbol.CreateOption(underlyingSymbol, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 125m, new DateTime(2024, 9, 6)), 1)
            };

            var groupOrderManager = new GroupOrderManager(1, legCount: optionContracts.Count, quantity: 8);

            var comboOrders = PlaceComboOrder(
                optionContracts,
                comboLimitPrice,
                (optionContract, quantity, price, groupOrderManager) =>
                    new ComboLimitOrder(optionContract, quantity, price.Value, DateTime.UtcNow, groupOrderManager, properties: new TradeStationOrderProperties() { AllOrNone = true }),
                groupOrderManager);

            AssertComboOrderPlacedSuccessfully(comboOrders);

            using var manualResetEvent = new ManualResetEvent(false);
            var orderStatusCallback = HandleComboOrderStatusChange(comboOrders, manualResetEvent, OrderStatus.UpdateSubmitted);

            Brokerage.OrdersStatusChanged += orderStatusCallback;

            foreach (var comboOrder in comboOrders)
            {
                comboOrder.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, comboOrder.Id, new() { LimitPrice = newComboLimitPrice }));
                Assert.IsTrue(Brokerage.UpdateOrder(comboOrder));
            }

            Assert.IsTrue(manualResetEvent.WaitOne(TimeSpan.FromSeconds(60)));

            Brokerage.OrdersStatusChanged -= orderStatusCallback;

            CancelComboOpenOrders(comboOrders);
        }

        [TestCase(70, 10)]
        public void PlaceSeveralComboLimitOrder(decimal firstLimitPrice, decimal secondLimitPrice)
        {
            var submittedComboOrders = new List<ComboLimitOrder>();
            var symbolQuantityContractsByGroupOrderManager = new Dictionary<(GroupOrderManager groupOrderManager, decimal limitPrice), List<(Symbol symbol, decimal quantity)>>();
            var underlyingSymbol = Symbols.AAPL;
            var firstOptionContracts = new List<(Symbol symbol, decimal quantity)>
            {
                (Symbol.CreateOption(underlyingSymbol, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 100m, new DateTime(2024, 9, 6)), -1),
                (Symbol.CreateOption(underlyingSymbol, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 125m, new DateTime(2024, 9, 6)), 1)
            };

            symbolQuantityContractsByGroupOrderManager[(new GroupOrderManager(1, legCount: firstOptionContracts.Count, quantity: 8), firstLimitPrice)] = firstOptionContracts;

            var secondOptionContracts = new List<(Symbol symbol, decimal quantity)>
            {
                (underlyingSymbol, 16),
                (Symbol.CreateOption(underlyingSymbol, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 105m, new DateTime(2024, 9, 6)), -1),
                (Symbol.CreateOption(underlyingSymbol, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 110m, new DateTime(2024, 9, 6)), 1)
            };

            symbolQuantityContractsByGroupOrderManager[(new GroupOrderManager(2, legCount: secondOptionContracts.Count, quantity: 8), secondLimitPrice)] = secondOptionContracts;

            foreach (var ((groupOrderManager, limitPrice), optionContracts) in symbolQuantityContractsByGroupOrderManager)
            {
                var comboOrders = PlaceComboOrder(
                    optionContracts,
                    limitPrice,
                    (optionContract, quantity, price, groupOrderManager) =>
                    new ComboLimitOrder(optionContract, quantity, price.Value, DateTime.UtcNow, groupOrderManager, properties: new TradeStationOrderProperties()
                    {
                        Exchange = Exchange.CBOE
                    }),
                    groupOrderManager);

                AssertComboOrderPlacedSuccessfully(comboOrders);

                submittedComboOrders.AddRange(comboOrders);
            }

            CancelComboOpenOrders(submittedComboOrders);
        }

        private IReadOnlyCollection<T> PlaceComboOrder<T>(
            IReadOnlyCollection<(Symbol symbol, decimal quantity)> legs,
            decimal? orderLimitPrice,
            Func<Symbol, decimal, decimal?, GroupOrderManager, T> orderType, GroupOrderManager groupOrderManager) where T : ComboOrder
        {
            var comboOrders = legs
                .Select(optionContract => orderType(optionContract.symbol, optionContract.quantity, orderLimitPrice, groupOrderManager))
                .ToList().AsReadOnly();

            var manualResetEvent = new ManualResetEvent(false);
            var orderStatusCallback = HandleComboOrderStatusChange(comboOrders, manualResetEvent, OrderStatus.Submitted);

            Brokerage.OrdersStatusChanged += orderStatusCallback;

            foreach (var comboOrder in comboOrders)
            {
                OrderProvider.Add(comboOrder);
                groupOrderManager.OrderIds.Add(comboOrder.Id);
                Assert.IsTrue(Brokerage.PlaceOrder(comboOrder));
            }

            Assert.IsTrue(manualResetEvent.WaitOne(TimeSpan.FromSeconds(60)));

            Brokerage.OrdersStatusChanged -= orderStatusCallback;

            return comboOrders;
        }

        private void CancelComboOpenOrders(IReadOnlyCollection<ComboLimitOrder> comboLimitOrders)
        {
            using var manualResetEvent = new ManualResetEvent(false);

            var orderStatusCallback = HandleComboOrderStatusChange(comboLimitOrders, manualResetEvent, OrderStatus.Canceled);

            Brokerage.OrdersStatusChanged += orderStatusCallback;

            var openOrders = OrderProvider.GetOpenOrders(order => order.Type == OrderType.ComboLimit);
            foreach (var openOrder in openOrders)
            {
                Assert.IsTrue(Brokerage.CancelOrder(openOrder));
            }

            if (openOrders.Count > 0)
            {
                Assert.IsTrue(manualResetEvent.WaitOne(TimeSpan.FromSeconds(60)));
            }

            Brokerage.OrdersStatusChanged -= orderStatusCallback;
        }

        private void AssertComboOrderPlacedSuccessfully<T>(IReadOnlyCollection<T> comboOrders) where T : ComboOrder
        {
            Assert.IsTrue(comboOrders.All(o => o.Status.IsClosed() || o.Status == OrderStatus.Submitted));
        }

        private static EventHandler<List<OrderEvent>> HandleComboOrderStatusChange<T>(
            IReadOnlyCollection<T> comboOrders,
            ManualResetEvent manualResetEvent,
            OrderStatus expectedOrderStatus) where T : ComboOrder
        {
            return (_, orderEvents) =>
            {

                foreach (var order in comboOrders)
                {
                    foreach (var orderEvent in orderEvents)
                    {
                        if (orderEvent.OrderId == order.Id)
                        {
                            order.Status = orderEvent.Status;
                        }
                    }

                    if (comboOrders.All(o => o.Status.IsClosed()) || comboOrders.All(o => o.Status == expectedOrderStatus))
                    {
                        manualResetEvent.Set();
                    }
                }
            };
        }

        /// <summary>
        /// Retrieves the last price for a short order based on the provided order test parameters.
        /// </summary>
        /// <param name="orderTestParameters">The order test parameters.</param>
        /// <returns>An instance of <see cref="QuantConnect.Tests.Brokerages.OrderTestParameters"/> with the last price for a short order.</returns>
        private OrderTestParameters GetLastPriceForShortOrder(OrderTestParameters orderTestParameters)
        {
            if (orderTestParameters is MarketOrderTestParameters)
            {
                return orderTestParameters;
            }
            var orderType = GetOrderTypeByOrderTestParameters(orderTestParameters);
            return GetLastPriceForShortOrder(orderTestParameters.Symbol, orderType);
        }

        /// <summary>
        /// Retrieves the last price for a long order based on the provided order test parameters.
        /// </summary>
        /// <param name="orderTestParameters">The order test parameters.</param>
        /// <returns>An instance of <see cref="QuantConnect.Tests.Brokerages.OrderTestParameters"/> with the last price for a long order.</returns>
        private OrderTestParameters GetLastPriceForLongOrder(OrderTestParameters orderTestParameters)
        {
            if (orderTestParameters is MarketOrderTestParameters)
            {
                return orderTestParameters;
            }
            var orderType = GetOrderTypeByOrderTestParameters(orderTestParameters);
            return GetLastPriceForLongOrder(orderTestParameters.Symbol, orderType);
        }

        /// <summary>
        /// Determines the order type based on the provided order test parameters.
        /// </summary>
        /// <param name="orderTestParameters">The order test parameters.</param>
        /// <returns>The determined <see cref="OrderType"/>.</returns>
        /// <exception cref="NotImplementedException">Thrown when the order type is not implemented.</exception>
        private static OrderType GetOrderTypeByOrderTestParameters(OrderTestParameters orderTestParameters) => orderTestParameters switch
        {
            LimitOrderTestParameters => OrderType.Limit,
            StopMarketOrderTestParameters => OrderType.StopMarket,
            StopLimitOrderTestParameters => OrderType.StopLimit,
            _ => throw new NotImplementedException($"The order type '{orderTestParameters.GetType().Name}' is not implemented.")
        };

        /// <summary>
        /// Retrieves the last price for a short order based on the symbol and order type.
        /// </summary>
        /// <param name="symbol">The symbol for the order.</param>
        /// <param name="orderType">The type of the order.</param>
        /// <returns>An instance of <see cref="QuantConnect.Tests.Brokerages.OrderTestParameters"/> with the last price for a short order.</returns>
        /// <exception cref="NotImplementedException">Thrown when the order type is not supported.</exception>
        private OrderTestParameters GetLastPriceForShortOrder(Symbol symbol, OrderType orderType)
        {
            var lastPrice = _brokerage.GetPrice(symbol).Last;
            return orderType switch
            {
                OrderType.Limit => new LimitOrderTestParameters(symbol, AddAndRound(lastPrice, 0.03m), SubtractAndRound(lastPrice, 0.03m)),
                OrderType.StopMarket => new StopMarketOrderTestParameters(symbol, SubtractAndRound(lastPrice, 0.03m), SubtractAndRound(lastPrice, 0.06m)),
                OrderType.StopLimit => new StopLimitOrderTestParameters(symbol, SubtractAndRound(lastPrice, 0.03m), SubtractAndRound(lastPrice, 0.03m)),
                _ => throw new NotImplementedException("Not supported type of order")
            };
        }

        /// <summary>
        /// Retrieves the last price for a long order based on the symbol and order type.
        /// </summary>
        /// <param name="symbol">The symbol for the order.</param>
        /// <param name="orderType">The type of the order.</param>
        /// <returns>An instance of <see cref="QuantConnect.Tests.Brokerages.OrderTestParameters"/> with the last price for a long order.</returns>
        /// <exception cref="NotImplementedException">Thrown when the order type is not supported.</exception>
        private OrderTestParameters GetLastPriceForLongOrder(Symbol symbol, OrderType orderType)
        {
            var lastPrice = _brokerage.GetPrice(symbol).Last;
            return orderType switch
            {
                OrderType.Limit => new LimitOrderTestParameters(symbol, AddAndRound(lastPrice, 0.02m), SubtractAndRound(lastPrice, 0.02m)),
                OrderType.StopMarket => new StopMarketOrderTestParameters(symbol, AddAndRound(lastPrice, 0.04m), AddAndRound(lastPrice, 0.06m)),
                OrderType.StopLimit => new StopLimitOrderTestParameters(symbol, AddAndRound(lastPrice, 0.04m), AddAndRound(lastPrice, 0.06m)),
                _ => throw new NotImplementedException("Not supported type of order")
            };
        }

        public static decimal AddAndRound(decimal number, decimal valueToAdd)
        {
            decimal result = number + valueToAdd;
            return Math.Round(result, 2);
        }

        public static decimal SubtractAndRound(decimal number, decimal valueToSubtract)
        {
            decimal result = number - valueToSubtract;
            return Math.Round(result, 2);
        }

        [TestCase("CBOE", SecurityType.Equity, new SecurityType[] { SecurityType.Equity, SecurityType.Option }, "CBOE")]
        [TestCase("IEX", SecurityType.Equity, new SecurityType[] { SecurityType.Equity }, "IEXG")]
        [TestCase("BOSTON", SecurityType.Equity, new SecurityType[] { SecurityType.Equity }, "NQBX")]
        [TestCase("NASDAQ", SecurityType.Equity, new SecurityType[] { SecurityType.Equity }, "NSDQ")]
        [TestCase("ARCX", SecurityType.Option, new SecurityType[] { SecurityType.Option }, "NYOP")]
        [TestCase("ISE MERCURY", SecurityType.Option, new SecurityType[] { SecurityType.Option }, "MCRY")]
        [TestCase("MIAX_PEARL", SecurityType.Option, new SecurityType[] { SecurityType.Option }, "MPRL")]
        [TestCase("MIAX_SAPPHIRE", SecurityType.Option, new SecurityType[] { SecurityType.Option }, "SPHR")]
        [TestCase("BATS_Y", SecurityType.Equity, new SecurityType[] { SecurityType.Option }, "SPHR")]
        [TestCase("BYX", SecurityType.Equity, new SecurityType[] { SecurityType.Equity }, "BYX")]
        [TestCase("MEMX", SecurityType.Equity, new SecurityType[] { SecurityType.Equity }, "MXOP")]
        [TestCase("NASDAQ_BX", SecurityType.Equity, new SecurityType[] { SecurityType.Equity }, "NOBO")]
        [TestCase("NASDAQ_BX", SecurityType.Equity, new SecurityType[] { SecurityType.Equity }, "NOBO")]
        [TestCase("MIAX_EMERALD", SecurityType.Option, new SecurityType[] { SecurityType.Option }, "EMLD")]
        [TestCase("MIAX_EMERALD", SecurityType.Option, new SecurityType[] { SecurityType.Option }, "EMLD")]
        [TestCase("ISE_GEMINI", SecurityType.Option, new SecurityType[] { SecurityType.Option }, "GMNI")]
        [TestCase("AMEX", SecurityType.Option, new SecurityType[] { SecurityType.Option }, "AMOP")]
        [TestCase("TWAP-ALGO", SecurityType.Equity, new SecurityType[] { SecurityType.Equity }, "TWAP", true)]
        [TestCase("SweepPI-ALGO", SecurityType.Equity, new SecurityType[] { SecurityType.Equity }, "WEEB", true)]
        [TestCase("NQBX", SecurityType.Equity, new SecurityType[] { SecurityType.Equity }, "NQBX", true)]
        public void GetCorrectRouteIdWithExchangeNameAndSecurityTypes(string exchangeName, SecurityType exchangeSecurityType, SecurityType[] orderSecurityTypes, string expectedRouteId, bool useCustomExchange = false)
        {
            var exchange = useCustomExchange
                ? new Exchange(exchangeName, exchangeName, exchangeName, exchangeSecurityType.ToString())
                : Exchanges.GetPrimaryExchange(exchangeName, exchangeSecurityType);

            var tradeStationOrderProperties = new TradeStationOrderProperties() { Exchange = exchange };

            if (_brokerage.GetTradeStationOrderRouteIdByOrder(tradeStationOrderProperties, orderSecurityTypes, out var routeId))
            {
                Assert.That(routeId, Is.EqualTo(expectedRouteId));
            }
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
            /// <param name="authorizationCode">The authorization code obtained from the URL.</param>
            /// <param name="refreshToken">The refresh token used to obtain new access tokens for authentication.</param>
            /// <param name="accountType">The type of TradeStation account for the current session.
            /// For <see cref="TradeStationAccountType.Cash"/> or <seealso cref="TradeStationAccountType.Margin"/> accounts, it is used for trading <seealso cref="SecurityType.Equity"/> and <seealso cref="SecurityType.Option"/>.
            /// For <seealso cref="TradeStationAccountType.Futures"/> accounts, it is used for trading <seealso cref="SecurityType.Future"/> contracts.</param>
            /// <param name="orderProvider">The order provider.</param>
            /// <param name="accountId">The specific user account id.</param>
            public TradeStationBrokerageTest(string apiKey, string apiKeySecret, string restApiUrl, string redirectUrl,
                string authorizationCode, string refreshToken, string accountType, IOrderProvider orderProvider, ISecurityProvider securityProvider, string accountId = "")
                : base(apiKey, apiKeySecret, restApiUrl, redirectUrl, authorizationCode, refreshToken, accountType, orderProvider, securityProvider, accountId)
            { }

            /// <summary>
            /// Retrieves the last price of the specified symbol.
            /// </summary>
            /// <param name="symbol">The symbol for which to retrieve the last price.</param>
            /// <returns>The last price of the specified symbol as a decimal.</returns>
            public Models.Quote GetPrice(Symbol symbol)
            {
                return GetQuote(symbol).Quotes.Single();
            }

            public bool GetTradeStationOrderRouteIdByOrder(TradeStationOrderProperties tradeStationOrderProperties, IReadOnlyCollection<SecurityType> securityTypes, out string routeId)
            {
                routeId = default;
                return GetTradeStationOrderRouteIdByOrderSecurityTypes(tradeStationOrderProperties, securityTypes, out routeId);
            }
        }
    }
}