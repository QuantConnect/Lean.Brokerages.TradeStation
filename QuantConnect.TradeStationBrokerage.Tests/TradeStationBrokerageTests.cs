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
using System.Collections.Generic;
using QuantConnect.Tests.Brokerages;
using QuantConnect.Orders.TimeInForces;
using QuantConnect.Securities.IndexOption;

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
            return TestSetup.CreateBrokerage(orderProvider, securityProvider);
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
            var lastPrice = _brokerage.GetPrice(symbol);

            return IsLongOrder ? lastPrice.Ask : lastPrice.Bid;
        }

        private static OrderTestParameters GetOrderTestParameters(OrderType orderType, Symbol symbol, decimal highLimit = 0m, decimal lowLimit = 0m)
        {
            return orderType switch
            {
                OrderType.Market => new MarketOrderTestParameters(symbol),
                OrderType.Limit => new CustomLimitOrderTestParameters(symbol, highLimit, lowLimit),
                OrderType.StopMarket => new CustomStopMarketOrderTestParameters(symbol, highLimit, lowLimit),
                OrderType.StopLimit => new CustomStopLimitOrderTestParameters(symbol, highLimit, lowLimit),
                _ => throw new NotImplementedException($"{nameof(TradeStationBrokerageTests)}.{nameof(GetOrderTestParameters)}: The specified order type '{orderType}' is not supported.")
            };
        }

        private static IEnumerable<OrderTestMetaData> OrderTestParameters
        {
            get
            {
                var INTL = Symbol.Create("INTL", SecurityType.Equity, Market.USA);
                yield return new OrderTestMetaData(OrderType.Market, INTL);
                yield return new OrderTestMetaData(OrderType.Limit, INTL);
                yield return new OrderTestMetaData(OrderType.StopMarket, INTL);
                yield return new OrderTestMetaData(OrderType.StopLimit, INTL);

                var AAPLOption = Symbol.CreateOption(Symbols.AAPL, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 200m, new DateTime(2024, 12, 13));
                yield return new OrderTestMetaData(OrderType.Market, AAPLOption);
                yield return new OrderTestMetaData(OrderType.Limit, AAPLOption);
                yield return new OrderTestMetaData(OrderType.StopMarket, AAPLOption);
                yield return new OrderTestMetaData(OrderType.StopLimit, AAPLOption);

                var index = Symbol.Create("VIX", SecurityType.Index, Market.USA);
                var VIXOption = Symbol.CreateOption(index, Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Call, 13m, new DateTime(2024, 12, 18));
                yield return new OrderTestMetaData(OrderType.Market, VIXOption);
                yield return new OrderTestMetaData(OrderType.Limit, VIXOption);
                yield return new OrderTestMetaData(OrderType.StopMarket, VIXOption);
                yield return new OrderTestMetaData(OrderType.StopLimit, VIXOption);

                // At the first, setup specific `trade-station-account-type` in config file.
                //Config.Set("trade-station-account-type", "Future");
                //var COTTON = Symbol.CreateFuture(Futures.Softs.Cotton2, Market.ICE, new DateTime(2024, 10, 1));
                //yield return new OrderTestMetaData(OrderType.Market, COTTON);
                //yield return new OrderTestMetaData(OrderType.Limit, COTTON);
                //yield return new OrderTestMetaData(OrderType.StopMarket, COTTON);
                //yield return new OrderTestMetaData(OrderType.StopLimit, COTTON);
            }
        }

        [TestCaseSource(nameof(OrderTestParameters))]
        public void CancelOrders(OrderTestMetaData orderTestMetaData)
        {
            var symbolPrice = _brokerage.GetPrice(orderTestMetaData.Symbol);

            var (highPrice, lowPrice) = orderTestMetaData.OrderType switch
            {
                OrderType.Market => (0m, 0m),
                OrderType.Limit or OrderType.StopMarket => (AddAndRound(symbolPrice.Ask, 0.1m), SubtractAndRound(symbolPrice.Bid, 0.1m)),
                // StopLimit : Limit price must be at or above StopMarketPrice
                OrderType.StopLimit => (AddAndRound(symbolPrice.Bid, 0.1m), AddAndRound(symbolPrice.Ask, 0.2m)),
                _ => throw new NotImplementedException("Not supported type of order")
            };

            Log.Trace($"CANCEL ORDERS: OrderType = {orderTestMetaData.OrderType}, Symbol = {orderTestMetaData.Symbol}, HighPrice = {highPrice}, LowPrice = {lowPrice}");

            var parameters = GetOrderTestParameters(orderTestMetaData.OrderType, orderTestMetaData.Symbol, highPrice, lowPrice);
            base.CancelOrders(parameters);
        }

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public void LongFromZero(OrderTestMetaData orderTestMetaData)
        {
            var symbolPrice = _brokerage.GetPrice(orderTestMetaData.Symbol);

            var (highPrice, lowPrice) = orderTestMetaData.OrderType switch
            {
                OrderType.Market => (0m, 0m),
                OrderType.Limit => (AddAndRound(symbolPrice.Ask, 0.01m), SubtractAndRound(symbolPrice.Bid, 0.01m)),
                // StopMarket: Stop Price - Stop Price must be above current market.
                OrderType.StopMarket => (AddAndRound(symbolPrice.Ask, 0.06m), SubtractAndRound(symbolPrice.Bid, 0.01m)),
                // StopLimit: Limit price must be at or above StopMarketPrice || Invalid Stop Price - Stop Price must be above current market.
                OrderType.StopLimit => (AddAndRound(symbolPrice.Bid, 0.1m), AddAndRound(symbolPrice.Ask, 0.1m)),
                _ => throw new NotImplementedException("Not supported type of order")
            };
            Log.Trace($"LONG FROM ZERO ORDERS: OrderType = {orderTestMetaData.OrderType}, Symbol = {orderTestMetaData.Symbol}, HighPrice = {highPrice}, LowPrice = {lowPrice}");

            var parameters = GetOrderTestParameters(orderTestMetaData.OrderType, orderTestMetaData.Symbol, highPrice, lowPrice);

            base.LongFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public void CloseFromLong(OrderTestMetaData orderTestMetaData)
        {
            IsLongOrder = false;
            var symbolPrice = _brokerage.GetPrice(orderTestMetaData.Symbol);

            var (highPrice, lowPrice) = orderTestMetaData.OrderType switch
            {
                OrderType.Market => (0m, 0m),
                OrderType.Limit or OrderType.StopMarket => (AddAndRound(symbolPrice.Ask, 0.2m), SubtractAndRound(symbolPrice.Bid, 0.2m)),
                // StopLimit: Invalid Limit Price - Limit Price must be at or below Stop Price.
                OrderType.StopLimit => (SubtractAndRound(symbolPrice.Bid, 0.3m), SubtractAndRound(symbolPrice.Ask, 0.2m)),
                _ => throw new NotImplementedException("Not supported type of order")
            };

            Log.Trace($"CLOSE FROM LONG: OrderType = {orderTestMetaData.OrderType}, Symbol = {orderTestMetaData.Symbol}, HighPrice = {highPrice}, LowPrice = {lowPrice}");

            var parameters = GetOrderTestParameters(orderTestMetaData.OrderType, orderTestMetaData.Symbol, highPrice, lowPrice);
            base.CloseFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public void ShortFromZero(OrderTestMetaData orderTestMetaData)
        {
            var symbolPrice = _brokerage.GetPrice(orderTestMetaData.Symbol);

            var (highPrice, lowPrice) = orderTestMetaData.OrderType switch
            {
                OrderType.Market => (0m, 0m),
                OrderType.Limit => (AddAndRound(symbolPrice.Ask, 0.01m), SubtractAndRound(symbolPrice.Bid, 0.01m)),
                // StopMarket: Stop Price - Stop Price must be above current market.
                OrderType.StopMarket => (AddAndRound(symbolPrice.Ask, 0.06m), SubtractAndRound(symbolPrice.Bid, 0.01m)),
                // StopLimit: Limit price must be at or above StopMarketPrice || Message: Invalid Stop Price - Stop Price must be below current market.
                OrderType.StopLimit => (SubtractAndRound(symbolPrice.Bid, 0.1m), SubtractAndRound(symbolPrice.Ask, 0.1m)),
                _ => throw new NotImplementedException("Not supported type of order")
            };
            Log.Trace($"SHORT FROM ZERO: OrderType = {orderTestMetaData.OrderType}, Symbol = {orderTestMetaData.Symbol}, HighPrice = {highPrice}, LowPrice = {lowPrice}");

            var parameters = GetOrderTestParameters(orderTestMetaData.OrderType, orderTestMetaData.Symbol, highPrice, lowPrice);

            base.ShortFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderTestParameters))]
        public void CloseFromShort(OrderTestMetaData orderTestMetaData)
        {
            IsLongOrder = true;

            var symbolPrice = _brokerage.GetPrice(orderTestMetaData.Symbol);

            var (highPrice, lowPrice) = orderTestMetaData.OrderType switch
            {
                OrderType.Market => (0m, 0m),
                OrderType.Limit => (AddAndRound(symbolPrice.Ask, 0.01m), SubtractAndRound(symbolPrice.Bid, 0.01m)),
                // StopMarket: Stop Price - Stop Price must be above current market.
                OrderType.StopMarket => (AddAndRound(symbolPrice.Ask, 0.06m), SubtractAndRound(symbolPrice.Bid, 0.01m)),
                // StopLimit: Limit price must be at or above StopMarketPrice || Invalid Stop Price - Stop Price must be above current market.
                OrderType.StopLimit => (AddAndRound(symbolPrice.Bid, 0.1m), AddAndRound(symbolPrice.Ask, 0.1m)),
                _ => throw new NotImplementedException("Not supported type of order")
            };
            Log.Trace($"SHORT FROM ZERO: OrderType = {orderTestMetaData.OrderType}, Symbol = {orderTestMetaData.Symbol}, HighPrice = {highPrice}, LowPrice = {lowPrice}");

            var parameters = GetOrderTestParameters(orderTestMetaData.OrderType, orderTestMetaData.Symbol, highPrice, lowPrice);

            base.CloseFromShort(parameters);
        }

        private static IEnumerable<OrderTestMetaData> MostActiveOrderTestParameters
        {
            get
            {
                var NVDA = Symbol.Create("NVDA", SecurityType.Equity, Market.USA);
                yield return new OrderTestMetaData(OrderType.Limit, NVDA);
                yield return new OrderTestMetaData(OrderType.StopMarket, NVDA);
                yield return new OrderTestMetaData(OrderType.StopLimit, NVDA);

                var NVDAOption = Symbol.CreateOption(NVDA, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 139m, new DateTime(2024, 12, 13));
                yield return new OrderTestMetaData(OrderType.Limit, NVDAOption);
                yield return new OrderTestMetaData(OrderType.StopMarket, NVDAOption);
                yield return new OrderTestMetaData(OrderType.StopLimit, NVDAOption);

                var index = Symbol.Create("VIX", SecurityType.Index, Market.USA);
                var VIXOption = Symbol.CreateOption(index, Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Call, 15m, new DateTime(2024, 12, 18));
                yield return new OrderTestMetaData(OrderType.Limit, VIXOption);
                yield return new OrderTestMetaData(OrderType.StopMarket, VIXOption);
                yield return new OrderTestMetaData(OrderType.StopLimit, VIXOption);

                var VIXWeeklyOption = Symbol.CreateOption(index, "VIXW", Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Call, 14.5m, new DateTime(2024, 12, 11));
                yield return new OrderTestMetaData(OrderType.Limit, VIXWeeklyOption);
                yield return new OrderTestMetaData(OrderType.StopMarket, VIXWeeklyOption);
                yield return new OrderTestMetaData(OrderType.StopLimit, VIXWeeklyOption);
            }
        }

        [Test, TestCaseSource(nameof(MostActiveOrderTestParameters))]
        public void ShortFromLong(OrderTestMetaData orderTestMetaData)
        {
            IsLongOrder = false;
            var symbolPrice = _brokerage.GetPrice(orderTestMetaData.Symbol);

            var (highPrice, lowPrice) = orderTestMetaData.OrderType switch
            {
                OrderType.Limit or OrderType.StopMarket => (AddAndRound(symbolPrice.Ask, 0.2m), SubtractAndRound(symbolPrice.Bid, 0.2m)),
                // StopLimit: Invalid Limit Price - Limit Price must be at or below Stop Price.
                OrderType.StopLimit => (SubtractAndRound(symbolPrice.Bid, 0.05m), SubtractAndRound(symbolPrice.Ask, 0.04m)),
                _ => throw new NotImplementedException("Not supported type of order")
            };

            Log.Trace($"SHORT FROM LONG: OrderType = {orderTestMetaData.OrderType}, Symbol = {orderTestMetaData.Symbol}, HighPrice = {highPrice}, LowPrice = {lowPrice}");

            var parameters = GetOrderTestParameters(orderTestMetaData.OrderType, orderTestMetaData.Symbol, highPrice, lowPrice);
            base.ShortFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(MostActiveOrderTestParameters))]
        public void LongFromShort(OrderTestMetaData orderTestMetaData)
        {
            IsLongOrder = true;
            var symbolPrice = _brokerage.GetPrice(orderTestMetaData.Symbol);

            var (highPrice, lowPrice) = orderTestMetaData.OrderType switch
            {
                OrderType.Limit or OrderType.StopMarket => (AddAndRound(symbolPrice.Ask, 0.2m), SubtractAndRound(symbolPrice.Bid, 0.2m)),
                // StopLimit: Invalid Limit Price - Limit Price must be at or below Stop Price.
                OrderType.StopLimit => (AddAndRound(symbolPrice.Bid, 0.2m), AddAndRound(symbolPrice.Ask, 0.3m)),
                _ => throw new NotImplementedException("Not supported type of order")
            };

            Log.Trace($"LONG FROM SHORT: OrderType = {orderTestMetaData.OrderType}, Symbol = {orderTestMetaData.Symbol}, HighPrice = {highPrice}, LowPrice = {lowPrice}");

            var parameters = GetOrderTestParameters(orderTestMetaData.OrderType, orderTestMetaData.Symbol, highPrice, lowPrice);
            base.LongFromShort(parameters);
        }

        [TestCase("AAPL", SecurityType.Option)]
        [TestCase("SPY", SecurityType.Option)]
        [TestCase("SPX", SecurityType.IndexOption)]
        [TestCase("VIX", SecurityType.IndexOption)]
        [TestCase("VIXW", SecurityType.IndexOption)]
        [TestCase("NDX", SecurityType.IndexOption)]
        [TestCase("NDXP", SecurityType.IndexOption)]
        public void LookupSymbols(string ticker, SecurityType securityType)
        {
            var option = default(Symbol);
            switch (securityType)
            {
                case SecurityType.Option:
                    var underlying = Symbol.Create(ticker, SecurityType.Equity, Market.USA);
                    option = Symbol.CreateCanonicalOption(underlying);
                    break;
                case SecurityType.IndexOption:
                    underlying = Symbol.Create(IndexOptionSymbol.MapToUnderlying(ticker), SecurityType.Index, Market.USA);
                    option = Symbol.CreateCanonicalOption(underlying, targetOption: ticker);
                    break;
                default:
                    throw new NotImplementedException($"{nameof(TradeStationBrokerageTests)}.{nameof(LookupSymbols)}: Not support SecurityType = {securityType} by Ticker = {ticker}");
            }

            var options = (Brokerage as IDataQueueUniverseProvider).LookupSymbols(option, false).ToList();
            Assert.IsNotNull(options);
            Assert.True(options.Any());
            Assert.Greater(options.Count, 0);
            Assert.That(options.Distinct().ToList().Count, Is.EqualTo(options.Count));

            var mainSymbol = option.ID.Symbol;
            Assert.IsTrue(options.All(x => x.ID.Symbol == mainSymbol));
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
        [TestCase(Futures.Softs.Cotton2, SecurityType.Future, Market.ICE, "2024/10/1", 4), Explicit("Pay attention on Config:trade-station-account-type")]
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

        [Test]
        public void UpdateAlreadyCanceledOrder()
        {
            Log.Trace("UPDATE ALREADY CANCELED ORDER");
            var symbol = Symbols.AAPL;
            var lastPrice = _brokerage.GetPrice(symbol).Last;
            var limitPrice = SubtractAndRound(lastPrice, 0.5m);
            var limitOrder = new LimitOrder(Symbols.AAPL, 1, limitPrice, DateTime.UtcNow);
            OrderProvider.Add(limitOrder);

            var submittedResetEvent = new AutoResetEvent(false);
            var cancelledResetEvent = new AutoResetEvent(false);

            Brokerage.Message += (_, brokerageMessage) =>
            {
                Assert.AreEqual(BrokerageMessageType.Warning, brokerageMessage.Type);
                Assert.IsTrue(brokerageMessage.Message.StartsWith("Failed to update Order: OrderId:", StringComparison.InvariantCultureIgnoreCase));
            };

            Brokerage.OrdersStatusChanged += (_, orderEvents) =>
            {
                var orderEvent = orderEvents[0];

                Log.Trace("");
                Log.Trace($"{nameof(UpdateAlreadyCanceledOrder)}.OrderEvent.Status: {orderEvent.Status}");
                Log.Trace("");

                switch (orderEvent.Status)
                {
                    case OrderStatus.Submitted:
                        submittedResetEvent.Set();
                        break;
                    case OrderStatus.Canceled:
                        cancelledResetEvent.Set();
                        break;
                }
            };

            if (!Brokerage.PlaceOrder(limitOrder))
            {
                Assert.Fail("Brokerage failed to place the order: " + limitOrder);
            }

            if (!submittedResetEvent.WaitOne(TimeSpan.FromSeconds(5)))
            {
                Assert.Fail($"{nameof(PlaceLimitOrderAndUpdate)}: the brokerage doesn't return {OrderStatus.Submitted}");
            }

            var order = OrderProvider.GetOrderById(1);
            if (!Brokerage.CancelOrder(order))
            {
                Assert.Fail("Brokerage failed to cancel the order: " + order);
            }

            if (!cancelledResetEvent.WaitOne(TimeSpan.FromSeconds(5)))
            {
                Assert.Fail($"{nameof(PlaceLimitOrderAndUpdate)}: the brokerage doesn't return {OrderStatus.Canceled}");
            }

            order.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, order.Id, new() { LimitPrice = limitPrice - 0.01m }));

            if (Brokerage.UpdateOrder(order))
            {
                Assert.Fail("Brokerage can not update already cancelled order: " + order);
            }
        }

        [TestCase("Day")]
        [TestCase("GoodTilDate")]
        [TestCase("GoodTilCanceled")]
        public void PlaceOutsideMarketHours(string timeInForceName)
        {
            Log.Trace("PLACE LONG OUTSIDE MARKET HOURS LIMIT ORDER");
            var symbol = Symbols.AAPL;
            var lastPrice = _brokerage.GetPrice(symbol).Last;
            var limitPrice = SubtractAndRound(lastPrice, 0.5m);
            var limitOrder = new LimitOrder(Symbols.AAPL, 1, limitPrice, DateTime.UtcNow, properties: new TradeStationOrderProperties() { OutsideRegularTradingHours = true });

            var orderExpiryDate = default(DateTime);
            switch (timeInForceName)
            {
                case "GoodTilCanceled":
                    limitOrder.Properties.TimeInForce = TimeInForce.GoodTilCanceled;
                    break;
                case "Day":
                    limitOrder.Properties.TimeInForce = TimeInForce.Day;
                    break;
                case "GoodTilDate":
                    orderExpiryDate = DateTime.UtcNow.AddDays(1);
                    limitOrder.Properties.TimeInForce = TimeInForce.GoodTilDate(orderExpiryDate);
                    break;
                default:
                    throw new NotSupportedException($"{nameof(TradeStationBrokerageTests)}.{nameof(PlaceOutsideMarketHours)}: The specified TimeInForce '{timeInForceName}' is not supported.");
            }

            OrderProvider.Add(limitOrder);

            var submittedResetEvent = new AutoResetEvent(false);
            var cancelledResetEvent = new AutoResetEvent(false);

            Brokerage.Message += (_, brokerageMessage) =>
            {
                Assert.AreEqual(BrokerageMessageType.Warning, brokerageMessage.Type);
                Assert.IsTrue(brokerageMessage.Message.StartsWith("Failed to update Order: OrderId:", StringComparison.InvariantCultureIgnoreCase));
            };

            Brokerage.OrdersStatusChanged += (_, orderEvents) =>
            {
                var orderEvent = orderEvents[0];

                Log.Trace("");
                Log.Trace($"{nameof(UpdateAlreadyCanceledOrder)}.OrderEvent.Status: {orderEvent.Status}");
                Log.Trace("");

                switch (orderEvent.Status)
                {
                    case OrderStatus.Submitted:
                        submittedResetEvent.Set();
                        break;
                    case OrderStatus.Canceled:
                        cancelledResetEvent.Set();
                        break;
                }
            };

            if (!Brokerage.PlaceOrder(limitOrder))
            {
                Assert.Fail("Brokerage failed to place the order: " + limitOrder);
            }

            if (!submittedResetEvent.WaitOne(TimeSpan.FromSeconds(5)))
            {
                Assert.Fail($"{nameof(PlaceLimitOrderAndUpdate)}: the brokerage doesn't return {OrderStatus.Submitted}");
            }

            var openOrder = Brokerage.GetOpenOrders().Single();

            var orderProperties = openOrder.Properties as TradeStationOrderProperties;
            Assert.True(orderProperties.OutsideRegularTradingHours);

            switch (timeInForceName)
            {
                case "Day":
                    Assert.That(orderProperties.TimeInForce, Is.TypeOf<DayTimeInForce>());
                    break;
                case "GoodTilCanceled":
                    Assert.That(orderProperties.TimeInForce, Is.TypeOf<GoodTilCanceledTimeInForce>());
                    break;
                case "GoodTilDate":
                    Assert.That(orderProperties.TimeInForce, Is.TypeOf<GoodTilDateTimeInForce>());
                    Assert.AreEqual(orderExpiryDate.Date, (orderProperties.TimeInForce as GoodTilDateTimeInForce).Expiry.Date);
                    break;
            }

            var order = OrderProvider.GetOrderById(1);
            if (!Brokerage.CancelOrder(order))
            {
                Assert.Fail("Brokerage failed to cancel the order: " + order);
            }

            if (!cancelledResetEvent.WaitOne(TimeSpan.FromSeconds(5)))
            {
                Assert.Fail($"{nameof(PlaceLimitOrderAndUpdate)}: the brokerage doesn't return {OrderStatus.Canceled}");
            }
        }

        /// <summary>
        /// Adds a value to a base number and rounds the result to the nearest specified increment.
        /// </summary>
        /// <param name="number">The base number to which the value will be added.</param>
        /// <param name="valueToAdd">The value to add to the base number.</param>
        /// <param name="increment">The increment to which the result should be rounded (default is 0.05).</param>
        /// <returns>The adjusted and rounded result.</returns>
        private static decimal AddAndRound(decimal number, decimal valueToAdd, decimal increment = 0.05m) =>
            AdjustAndRound(number + valueToAdd, increment);

        /// <summary>
        /// Subtracts a value from a base number and rounds the result to the nearest specified increment.
        /// Ensures that the result does not go below zero unless the base number itself is negative or zero.
        /// </summary>
        /// <param name="number">The base number from which the value will be subtracted.</param>
        /// <param name="valueToSubtract">The value to subtract from the base number.</param>
        /// <param name="increment">The increment to which the result should be rounded (default is 0.05).</param>
        /// <returns>The adjusted and rounded result.</returns>
        private static decimal SubtractAndRound(decimal number, decimal valueToSubtract, decimal increment = 0.05m)
        {
            var adjustedValue = number - valueToSubtract <= 0 ? number : number - valueToSubtract;
            return AdjustAndRound(adjustedValue, increment);
        }

        /// <summary>
        /// Rounds a given value to the nearest specified increment and ensures it is rounded to two decimal places.
        /// </summary>
        /// <param name="value">The value to adjust and round.</param>
        /// <param name="increment">The increment to which the value should be rounded.</param>
        /// <returns>The rounded value, adjusted to two decimal places.</returns>
        private static decimal AdjustAndRound(decimal value, decimal increment) => Math.Round(Math.Round(value / increment) * increment, 2);
    }
}