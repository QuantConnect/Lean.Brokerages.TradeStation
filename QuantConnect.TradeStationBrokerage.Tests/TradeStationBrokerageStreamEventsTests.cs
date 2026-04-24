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
using QuantConnect.Orders;
using System.Collections.Generic;
using QuantConnect.Tests.Brokerages;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    [TestFixture]
    public class TradeStationBrokerageStreamEventsTests
    {
        [Test]
        public void EmitsFillQuantityOnFilledEventAfterUpdateSubmittedFromTradeStation()
        {
            var orderProvider = new OrderProvider();
            var ts = TestSetup.CreateBrokerageStub(orderProvider, default);

            var capturedEvents = new List<OrderEvent>();
            var filledEventReceived = new AutoResetEvent(false);

            ts.OrdersStatusChanged += (_, orderEvents) =>
            {
                capturedEvents.AddRange(orderEvents);
                if (orderEvents.Any(e => e.Status == OrderStatus.Filled))
                {
                    filledEventReceived.Set();
                }
            };

            // init flag _isSubscribeOnStreamOrderUpdate which allow use WS msg updates
            ts.Connect();

            var stopOrder = new StopMarketOrder(Symbol.Create("IONQ", SecurityType.Equity, Market.USA), 1, 43.02m, DateTime.UtcNow);
            stopOrder.BrokerId.Add("948679459");
            orderProvider.Add(stopOrder);

            // Stub's PlaceOrder emits Submitted and seeds _skipWebSocketUpdatesForLeanOrders.
            ts.PlaceOrder(stopOrder);

            // Frame 1: STP ("Stop Hit") - the stop price was touched and the resting order is being armed as a market order.
            var stpJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""ClosedDateTime"": ""2026-04-23T17:26:21Z"",
                ""CommissionFee"": ""0"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""0"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""0"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""1"",
                        ""QuantityRemaining"": ""1"",
                        ""Symbol"": ""IONQ""
                    }
                ],
                ""OpenedDateTime"": ""2026-04-23T17:26:03Z"",
                ""OrderID"": ""948679459"",
                ""OrderType"": ""StopMarket"",
                ""PriceUsedForBuyingPower"": ""43.03"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""STP"",
                ""StatusDescription"": ""Stop Hit"",
                ""StopPrice"": ""43.02"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(stpJson);

            // Frame 2: ACK arriving after the stop triggered - carries the non-zero leg.ExecQuantity and ExecutionPrice.
            var ackJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""43.03"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""1"",
                        ""ExecutionPrice"": ""43.03"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""1"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""IONQ""
                    }
                ],
                ""OpenedDateTime"": ""2026-04-23T17:26:03Z"",
                ""OrderID"": ""948679459"",
                ""OrderType"": ""StopMarket"",
                ""PriceUsedForBuyingPower"": ""43.03"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""ACK"",
                ""StatusDescription"": ""Received"",
                ""StopPrice"": ""43.02"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(ackJson);

            // Frame 3: FLL - Filled event with ExecQuantity/ExecutionPrice.
            var fllJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""ClosedDateTime"": ""2026-04-23T17:26:22Z"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""43.03"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""1"",
                        ""ExecutionPrice"": ""43.03"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""1"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""IONQ""
                    }
                ],
                ""OpenedDateTime"": ""2026-04-23T17:26:03Z"",
                ""OrderID"": ""948679459"",
                ""OrderType"": ""StopMarket"",
                ""PriceUsedForBuyingPower"": ""43.03"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""FLL"",
                ""StatusDescription"": ""Filled"",
                ""StopPrice"": ""43.02"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(fllJson);

            Assert.IsTrue(filledEventReceived.WaitOne(TimeSpan.FromSeconds(1)), "Did not receive a Filled order event.");

            // A fill-carrying ACK/DON must not be rewritten to UpdateSubmitted — the terminal FLL
            // owns the fill delta. UpdateSubmitted stays reserved for Ack/Don frames with
            // ExecQuantity == 0 (user place/modify from the TradeStation UI).
            Assert.IsFalse(capturedEvents.Any(e => e.Status == OrderStatus.UpdateSubmitted), "Fill-carrying ACK must not be rewritten to UpdateSubmitted (#79).");

            var filled = capturedEvents.SingleOrDefault(e => e.Status == OrderStatus.Filled);
            Assert.IsNotNull(filled, "Expected a Filled event after FLL frame.");
            Assert.AreEqual(1m, filled.FillQuantity, "Filled event must carry the full fill quantity.");
            Assert.AreEqual(43.03m, filled.FillPrice, "Filled event must carry the fill price.");
        }

        [Test]
        public void EmitsFillQuantityOnFilledEventAfterStopLimitUpdateSubmittedFromTradeStation()
        {
            var orderProvider = new OrderProvider();
            var ts = TestSetup.CreateBrokerageStub(orderProvider, default);

            var capturedEvents = new List<OrderEvent>();
            var filledEventReceived = new AutoResetEvent(false);

            ts.OrdersStatusChanged += (_, orderEvents) =>
            {
                capturedEvents.AddRange(orderEvents);
                if (orderEvents.Any(e => e.Status == OrderStatus.Filled))
                {
                    filledEventReceived.Set();
                }
            };

            // init flag _isSubscribeOnStreamOrderUpdate which allow use WS msg updates
            ts.Connect();

            var stopLimitOrder = new StopLimitOrder(Symbol.Create("GOOGL", SecurityType.Equity, Market.USA), 2, 339.25m, 339.32m, DateTime.UtcNow);
            stopLimitOrder.BrokerId.Add("948728275");
            orderProvider.Add(stopLimitOrder);

            // Stub's PlaceOrder emits Submitted and seeds _skipWebSocketUpdatesForLeanOrders.
            ts.PlaceOrder(stopLimitOrder);

            // Frame 1: STP ("Stop Hit") - the stop price was touched and the limit order is now live. No-op via the Stp switch case.
            var stpJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""ClosedDateTime"": ""2026-04-23T19:52:50Z"",
                ""CommissionFee"": ""0"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""0"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""0"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""2"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""GOOGL""
                    }
                ],
                ""LimitPrice"": ""339.32"",
                ""OpenedDateTime"": ""2026-04-23T19:52:07Z"",
                ""OrderID"": ""948728275"",
                ""OrderType"": ""StopLimit"",
                ""PriceUsedForBuyingPower"": ""339.25"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""STP"",
                ""StatusDescription"": ""Stop Hit"",
                ""StopPrice"": ""339.25"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(stpJson);

            // Frame 2: ACK arriving after the stop triggered on a StopLimit - carries the non-zero leg.ExecQuantity and ExecutionPrice.
            var ackJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""339.25"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""2"",
                        ""ExecutionPrice"": ""339.25"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""2"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""GOOGL""
                    }
                ],
                ""LimitPrice"": ""339.32"",
                ""OpenedDateTime"": ""2026-04-23T19:52:07Z"",
                ""OrderID"": ""948728275"",
                ""OrderType"": ""StopLimit"",
                ""PriceUsedForBuyingPower"": ""339.32"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""ACK"",
                ""StatusDescription"": ""Received"",
                ""StopPrice"": ""339.25"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(ackJson);

            // Frame 3: FLL - Filled event with ExecQuantity/ExecutionPrice.
            var fllJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""ClosedDateTime"": ""2026-04-23T19:52:50Z"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""339.25"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""2"",
                        ""ExecutionPrice"": ""339.25"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""2"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""GOOGL""
                    }
                ],
                ""LimitPrice"": ""339.32"",
                ""OpenedDateTime"": ""2026-04-23T19:52:07Z"",
                ""OrderID"": ""948728275"",
                ""OrderType"": ""StopLimit"",
                ""PriceUsedForBuyingPower"": ""339.32"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""FLL"",
                ""StatusDescription"": ""Filled"",
                ""StopPrice"": ""339.25"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(fllJson);

            Assert.IsTrue(filledEventReceived.WaitOne(TimeSpan.FromSeconds(1)), "Did not receive a Filled order event.");

            // StopLimit follows the same post-fill re-ack pattern as StopMarket: STP -> ACK (with fills) -> FLL.
            // The fill-carrying ACK must not become UpdateSubmitted, otherwise the terminal Filled event loses its fill delta (#79).
            Assert.IsFalse(capturedEvents.Any(e => e.Status == OrderStatus.UpdateSubmitted), "Fill-carrying ACK on a StopLimit must not be rewritten to UpdateSubmitted (#79).");

            var filled = capturedEvents.SingleOrDefault(e => e.Status == OrderStatus.Filled);
            Assert.IsNotNull(filled, "Expected a Filled event after FLL frame.");
            Assert.AreEqual(2m, filled.FillQuantity, "Filled event must carry the full fill quantity.");
            Assert.AreEqual(339.25m, filled.FillPrice, "Filled event must carry the fill price.");
        }

        [Test]
        public void HandlesStopLimitUserStopPriceEditAndStopTriggeredFillFromTradeStation()
        {
            var orderProvider = new OrderProvider();
            var ts = TestSetup.CreateBrokerageStub(orderProvider, default);

            var capturedEvents = new List<OrderEvent>();
            var filledEventReceived = new AutoResetEvent(false);

            ts.OrdersStatusChanged += (_, orderEvents) =>
            {
                capturedEvents.AddRange(orderEvents);
                if (orderEvents.Any(e => e.Status == OrderStatus.Filled))
                {
                    filledEventReceived.Set();
                }
            };

            // init flag _isSubscribeOnStreamOrderUpdate which allow use WS msg updates
            ts.Connect();

            var stopLimitOrder = new StopLimitOrder(Symbol.Create("GOOGL", SecurityType.Equity, Market.USA), 2, 339.25m, 339.32m, DateTime.UtcNow);
            stopLimitOrder.BrokerId.Add("948728275");
            orderProvider.Add(stopLimitOrder);

            // Stub's PlaceOrder emits Submitted and seeds _skipWebSocketUpdatesForLeanOrders.
            ts.PlaceOrder(stopLimitOrder);

            // Frame 0: initial post-PlaceOrder ACK (ExecQuantity = 0, StopPrice = 339.25) — drained
            // by the skip marker the stub seeded. Next ACK frames must fall through to the inner else.
            var initialAckJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""CommissionFee"": ""0"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""0"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""0"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""2"",
                        ""QuantityRemaining"": ""2"",
                        ""Symbol"": ""GOOGL""
                    }
                ],
                ""LimitPrice"": ""339.32"",
                ""OpenedDateTime"": ""2026-04-23T19:52:07Z"",
                ""OrderID"": ""948728275"",
                ""OrderType"": ""StopLimit"",
                ""PriceUsedForBuyingPower"": ""339.32"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""ACK"",
                ""StatusDescription"": ""Received"",
                ""StopPrice"": ""339.25"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(initialAckJson);

            // Frame 1: user edits the StopPrice from 339.25 -> 339.27 via the TradeStation UI.
            // ExecQuantity stays 0. Must emit UpdateSubmitted so Lean's lifecycle tracks the change.
            var userEditAckJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""CommissionFee"": ""0"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""0"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""0"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""2"",
                        ""QuantityRemaining"": ""2"",
                        ""Symbol"": ""GOOGL""
                    }
                ],
                ""LimitPrice"": ""339.32"",
                ""OpenedDateTime"": ""2026-04-23T19:52:07Z"",
                ""OrderID"": ""948728275"",
                ""OrderType"": ""StopLimit"",
                ""PriceUsedForBuyingPower"": ""339.32"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""ACK"",
                ""StatusDescription"": ""Received"",
                ""StopPrice"": ""339.27"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(userEditAckJson);

            // Frame 2: STP - stop triggered. No-op via the Stp switch case.
            var stpJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""ClosedDateTime"": ""2026-04-23T19:52:50Z"",
                ""CommissionFee"": ""0"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""0"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""0"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""2"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""GOOGL""
                    }
                ],
                ""LimitPrice"": ""339.32"",
                ""OpenedDateTime"": ""2026-04-23T19:52:07Z"",
                ""OrderID"": ""948728275"",
                ""OrderType"": ""StopLimit"",
                ""PriceUsedForBuyingPower"": ""339.25"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""STP"",
                ""StatusDescription"": ""Stop Hit"",
                ""StopPrice"": ""339.25"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(stpJson);

            // Frame 3: post-stop ACK re-ack with ExecQuantity = 2. Must be dropped; FLL owns the fill.
            var postFillAckJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""339.25"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""2"",
                        ""ExecutionPrice"": ""339.25"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""2"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""GOOGL""
                    }
                ],
                ""LimitPrice"": ""339.32"",
                ""OpenedDateTime"": ""2026-04-23T19:52:07Z"",
                ""OrderID"": ""948728275"",
                ""OrderType"": ""StopLimit"",
                ""PriceUsedForBuyingPower"": ""339.32"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""ACK"",
                ""StatusDescription"": ""Received"",
                ""StopPrice"": ""339.25"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(postFillAckJson);

            // Frame 4: FLL - terminal Filled with ExecQuantity/ExecutionPrice.
            var fllJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""ClosedDateTime"": ""2026-04-23T19:52:50Z"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""339.25"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""2"",
                        ""ExecutionPrice"": ""339.25"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""2"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""GOOGL""
                    }
                ],
                ""LimitPrice"": ""339.32"",
                ""OpenedDateTime"": ""2026-04-23T19:52:07Z"",
                ""OrderID"": ""948728275"",
                ""OrderType"": ""StopLimit"",
                ""PriceUsedForBuyingPower"": ""339.32"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""FLL"",
                ""StatusDescription"": ""Filled"",
                ""StopPrice"": ""339.25"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(fllJson);

            Assert.IsTrue(filledEventReceived.WaitOne(TimeSpan.FromSeconds(1)), "Did not receive a Filled order event.");

            // The user-edit ACK on a StopLimit (ExecQuantity = 0) must become UpdateSubmitted so the
            // algorithm sees the external change. The post-stop re-ack (ExecQuantity > 0) must be
            // dropped so the terminal Filled event owns the full fill delta (#79).
            var updateSubmittedEvents = capturedEvents.Where(e => e.Status == OrderStatus.UpdateSubmitted).ToList();
            Assert.AreEqual(1, updateSubmittedEvents.Count, "Expected exactly one UpdateSubmitted event (the user UI edit).");
            Assert.AreEqual(0m, updateSubmittedEvents[0].FillQuantity, "UpdateSubmitted from a user UI edit must not carry fill quantity.");
            Assert.AreEqual(0m, updateSubmittedEvents[0].FillPrice, "UpdateSubmitted from a user UI edit must not carry fill price.");

            var filled = capturedEvents.SingleOrDefault(e => e.Status == OrderStatus.Filled);
            Assert.IsNotNull(filled, "Expected a Filled event after FLL frame.");
            Assert.AreEqual(2m, filled.FillQuantity, "Filled event must carry the full fill quantity.");
            Assert.AreEqual(339.25m, filled.FillPrice, "Filled event must carry the fill price.");
        }

        [Test]
        public void EmitsFilledEventForStopMarketWithoutStpFrameFromTradeStation()
        {
            var orderProvider = new OrderProvider();
            var ts = TestSetup.CreateBrokerageStub(orderProvider, default);

            var capturedEvents = new List<OrderEvent>();
            var filledEventReceived = new AutoResetEvent(false);

            ts.OrdersStatusChanged += (_, orderEvents) =>
            {
                capturedEvents.AddRange(orderEvents);
                if (orderEvents.Any(e => e.Status == OrderStatus.Filled))
                {
                    filledEventReceived.Set();
                }
            };

            // init flag _isSubscribeOnStreamOrderUpdate which allow use WS msg updates
            ts.Connect();

            var stopOrder = new StopMarketOrder(Symbol.Create("UNH", SecurityType.Equity, Market.USA), 1, 352.87m, DateTime.UtcNow);
            stopOrder.BrokerId.Add("948677825");
            orderProvider.Add(stopOrder);

            // Stub's PlaceOrder emits Submitted and seeds _skipWebSocketUpdatesForLeanOrders.
            ts.PlaceOrder(stopOrder);

            // TradeStation sometimes omits the STP frame and streams the StopMarket lifecycle as
            // ACK (ExecQuantity > 0) -> FLL. The initial post-PlaceOrder ACK (ExecQuantity = 0) is
            // drained by the skip marker that the stub's PlaceOrder seeded, so the flow below
            // picks up from the post-fill re-ack.

            // Frame 1: post-fill ACK re-ack with ExecQuantity = 1. Must be dropped; FLL owns the fill.
            var postFillAckJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""352.87"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""1"",
                        ""ExecutionPrice"": ""352.87"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""1"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""UNH""
                    }
                ],
                ""OpenedDateTime"": ""2026-04-23T17:22:05Z"",
                ""OrderID"": ""948677825"",
                ""OrderType"": ""StopMarket"",
                ""PriceUsedForBuyingPower"": ""352.85"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""ACK"",
                ""StatusDescription"": ""Received"",
                ""StopPrice"": ""352.87"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(postFillAckJson);

            // Frame 2: FLL - terminal Filled with ExecQuantity/ExecutionPrice.
            var fllJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""ClosedDateTime"": ""2026-04-23T17:22:56Z"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""352.87"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""1"",
                        ""ExecutionPrice"": ""352.87"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""1"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""UNH""
                    }
                ],
                ""OpenedDateTime"": ""2026-04-23T17:22:05Z"",
                ""OrderID"": ""948677825"",
                ""OrderType"": ""StopMarket"",
                ""PriceUsedForBuyingPower"": ""352.85"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""FLL"",
                ""StatusDescription"": ""Filled"",
                ""StopPrice"": ""352.87"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(fllJson);

            Assert.IsTrue(filledEventReceived.WaitOne(TimeSpan.FromSeconds(1)), "Did not receive a Filled order event.");

            // Fill-carrying ACK on a StopMarket must not become UpdateSubmitted - the terminal FLL
            // owns the fill delta (#79).
            Assert.IsFalse(capturedEvents.Any(e => e.Status == OrderStatus.UpdateSubmitted), "Fill-carrying ACK on a StopMarket must not be rewritten to UpdateSubmitted (#79).");

            var filled = capturedEvents.SingleOrDefault(e => e.Status == OrderStatus.Filled);
            Assert.IsNotNull(filled, "Expected a Filled event after FLL frame.");
            Assert.AreEqual(1m, filled.FillQuantity, "Filled event must carry the full fill quantity.");
            Assert.AreEqual(352.87m, filled.FillPrice, "Filled event must carry the fill price.");
        }

        [Test]
        public void EmitsFilledEventForStopMarketDirectFllFromTradeStation()
        {
            var orderProvider = new OrderProvider();
            var ts = TestSetup.CreateBrokerageStub(orderProvider, default);

            var capturedEvents = new List<OrderEvent>();
            var filledEventReceived = new AutoResetEvent(false);

            ts.OrdersStatusChanged += (_, orderEvents) =>
            {
                capturedEvents.AddRange(orderEvents);
                if (orderEvents.Any(e => e.Status == OrderStatus.Filled))
                {
                    filledEventReceived.Set();
                }
            };

            // init flag _isSubscribeOnStreamOrderUpdate which allow use WS msg updates
            ts.Connect();

            var stopOrder = new StopMarketOrder(Symbol.Create("BE", SecurityType.Equity, Market.USA), 1, 233.83m, DateTime.UtcNow);
            stopOrder.BrokerId.Add("948678503");
            orderProvider.Add(stopOrder);

            // Stub's PlaceOrder emits Submitted and seeds _skipWebSocketUpdatesForLeanOrders.
            ts.PlaceOrder(stopOrder);

            // Happy-path StopMarket lifecycle: the skip marker that the stub's PlaceOrder seeded
            // would drain an initial post-PlaceOrder ACK in production; the stream jumps straight
            // to the terminal FLL here — no STP, no post-fill ACK re-ack. The Filled event must
            // still carry the full fill.
            var fllJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""AdvancedOptions"": ""STPTRG=STT;"",
                ""ClosedDateTime"": ""2026-04-23T17:24:09Z"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""233.96"",
                ""GoodTillDate"": ""2026-07-22T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""1"",
                        ""ExecutionPrice"": ""233.96"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""1"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""BE""
                    }
                ],
                ""OpenedDateTime"": ""2026-04-23T17:24:03Z"",
                ""OrderID"": ""948678503"",
                ""OrderType"": ""StopMarket"",
                ""PriceUsedForBuyingPower"": ""233.8"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""FLL"",
                ""StatusDescription"": ""Filled"",
                ""StopPrice"": ""233.83"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(fllJson);

            Assert.IsTrue(filledEventReceived.WaitOne(TimeSpan.FromSeconds(1)), "Did not receive a Filled order event.");

            Assert.IsFalse(capturedEvents.Any(e => e.Status == OrderStatus.UpdateSubmitted), "A direct FLL lifecycle must not emit an UpdateSubmitted event.");

            var filled = capturedEvents.SingleOrDefault(e => e.Status == OrderStatus.Filled);
            Assert.IsNotNull(filled, "Expected a Filled event after FLL frame.");
            Assert.AreEqual(1m, filled.FillQuantity, "Filled event must carry the full fill quantity.");
            Assert.AreEqual(233.96m, filled.FillPrice, "Filled event must carry the fill price.");
        }

        [Test]
        public void EmitsFilledEventForLimitOrderAfterLeanUpdateDrainsFillCarryingAckFromTradeStation()
        {
            var orderProvider = new OrderProvider();
            var ts = TestSetup.CreateBrokerageStub(orderProvider, default);

            var capturedEvents = new List<OrderEvent>();
            var filledEventReceived = new AutoResetEvent(false);

            ts.OrdersStatusChanged += (_, orderEvents) =>
            {
                capturedEvents.AddRange(orderEvents);
                if (orderEvents.Any(e => e.Status == OrderStatus.Filled))
                {
                    filledEventReceived.Set();
                }
            };

            // init flag _isSubscribeOnStreamOrderUpdate which allow use WS msg updates
            ts.Connect();

            var limitOrder = new LimitOrder(Symbol.Create("AAPL", SecurityType.Equity, Market.USA), 100, 272m, DateTime.UtcNow);
            limitOrder.BrokerId.Add("948825753");
            orderProvider.Add(limitOrder);

            // Stub's PlaceOrder emits Submitted and seeds _skipWebSocketUpdatesForLeanOrders so Frame 1 is drained.
            ts.PlaceOrder(limitOrder);

            // Frame 1: initial post-PlaceOrder ACK (ExecQuantity = 0) - drained by _skipWebSocketUpdatesForLeanOrders.
            var initialAckJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""CommissionFee"": ""0"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""0"",
                ""GoodTillDate"": ""2026-07-23T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""0"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""100"",
                        ""QuantityRemaining"": ""100"",
                        ""Symbol"": ""AAPL""
                    }
                ],
                ""LimitPrice"": ""272"",
                ""OpenedDateTime"": ""2026-04-24T14:43:14Z"",
                ""OrderID"": ""948825753"",
                ""OrderType"": ""Limit"",
                ""PriceUsedForBuyingPower"": ""272"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""ACK"",
                ""StatusDescription"": ""Received"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(initialAckJson);

            // Lean calls UpdateOrder (limit price 272 -> 272.3). The stub override emits UpdateSubmitted
            // and re-seeds _skipWebSocketUpdatesForLeanOrders so the next fill-carrying ACK is drained.
            ts.UpdateOrder(limitOrder);

            // Frame 2: post-update ACK carrying the fill (ExecQuantity = 100, ExecutionPrice = 272.22).
            // Must be drained by the skip marker UpdateOrder just populated — otherwise it would emit an
            // extra UpdateSubmitted and consume the fill delta, leaving the terminal FLL at FillQuantity = 0.
            var postUpdateAckJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""272.22"",
                ""GoodTillDate"": ""2026-07-23T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""100"",
                        ""ExecutionPrice"": ""272.22"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""100"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""AAPL""
                    }
                ],
                ""LimitPrice"": ""272.3"",
                ""OpenedDateTime"": ""2026-04-24T14:43:14Z"",
                ""OrderID"": ""948825753"",
                ""OrderType"": ""Limit"",
                ""PriceUsedForBuyingPower"": ""272.3"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""ACK"",
                ""StatusDescription"": ""Received"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(postUpdateAckJson);

            // Frame 3: FLL - terminal Filled.
            var fllJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""ClosedDateTime"": ""2026-04-24T14:43:16Z"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""272.22"",
                ""GoodTillDate"": ""2026-07-23T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""100"",
                        ""ExecutionPrice"": ""272.22"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""100"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""AAPL""
                    }
                ],
                ""LimitPrice"": ""272.3"",
                ""OpenedDateTime"": ""2026-04-24T14:43:14Z"",
                ""OrderID"": ""948825753"",
                ""OrderType"": ""Limit"",
                ""PriceUsedForBuyingPower"": ""272.3"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""FLL"",
                ""StatusDescription"": ""Filled"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(fllJson);

            Assert.IsTrue(filledEventReceived.WaitOne(TimeSpan.FromSeconds(1)), "Did not receive a Filled order event.");

            // Expected lifecycle: Submitted (from stubbed PlaceOrder), UpdateSubmitted (from stubbed
            // UpdateOrder), Filled (from Frame 3). Frames 1 and 2 are drained by _skipWebSocketUpdatesForLeanOrders.
            Assert.AreEqual(3, capturedEvents.Count, "Expected Submitted + UpdateSubmitted + Filled; ACK frames should be drained.");

            var submitted = capturedEvents.SingleOrDefault(e => e.Status == OrderStatus.Submitted);
            Assert.IsNotNull(submitted, "Expected a Submitted event from the Lean PlaceOrder call.");

            var updateSubmitted = capturedEvents.SingleOrDefault(e => e.Status == OrderStatus.UpdateSubmitted);
            Assert.IsNotNull(updateSubmitted, "Expected an UpdateSubmitted event from the Lean UpdateOrder call.");

            var filled = capturedEvents.SingleOrDefault(e => e.Status == OrderStatus.Filled);
            Assert.IsNotNull(filled, "Expected a Filled event after FLL frame.");
            Assert.AreEqual(100m, filled.FillQuantity, "Filled event must carry the full fill quantity.");
            Assert.AreEqual(272.22m, filled.FillPrice, "Filled event must carry the fill price.");
        }

        [Test]
        public void EmitsFilledEventForLimitOrderAfterMultipleUserEditsAndLeanUpdateFromTradeStation()
        {
            var orderProvider = new OrderProvider();
            var ts = TestSetup.CreateBrokerageStub(orderProvider, default);

            var capturedEvents = new List<OrderEvent>();
            var filledEventReceived = new AutoResetEvent(false);

            ts.OrdersStatusChanged += (_, orderEvents) =>
            {
                capturedEvents.AddRange(orderEvents);
                if (orderEvents.Any(e => e.Status == OrderStatus.Filled))
                {
                    filledEventReceived.Set();
                }
            };

            // init flag _isSubscribeOnStreamOrderUpdate which allow use WS msg updates
            ts.Connect();

            var limitOrder = new LimitOrder(Symbol.Create("WULF", SecurityType.Equity, Market.USA), 100, 19.6m, DateTime.UtcNow);
            limitOrder.BrokerId.Add("948851974");
            orderProvider.Add(limitOrder);

            // PlaceOrder stub: emits Submitted + seeds _skipWebSocketUpdatesForLeanOrders so Frame 1 is drained.
            ts.PlaceOrder(limitOrder);

            // Frame 1: initial post-PlaceOrder ACK (ExecQuantity = 0, LimitPrice = 19.6) - drained by skip marker.
            var initialAckJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""CommissionFee"": ""0"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""0"",
                ""GoodTillDate"": ""2026-07-23T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""0"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""100"",
                        ""QuantityRemaining"": ""100"",
                        ""Symbol"": ""WULF""
                    }
                ],
                ""LimitPrice"": ""19.6"",
                ""OpenedDateTime"": ""2026-04-24T15:39:00Z"",
                ""OrderID"": ""948851974"",
                ""OrderType"": ""Limit"",
                ""PriceUsedForBuyingPower"": ""19.6"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""ACK"",
                ""StatusDescription"": ""Received"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(initialAckJson);

            // Frame 2: user manually edits LimitPrice 19.6 -> 19.7 from the TradeStation UI.
            // ExecQuantity = 0, skip marker already drained — inner else rewrites to UpdateSubmitted.
            var firstUserEditJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""CommissionFee"": ""0"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""0"",
                ""GoodTillDate"": ""2026-07-23T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""0"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""100"",
                        ""QuantityRemaining"": ""100"",
                        ""Symbol"": ""WULF""
                    }
                ],
                ""LimitPrice"": ""19.7"",
                ""OpenedDateTime"": ""2026-04-24T15:39:00Z"",
                ""OrderID"": ""948851974"",
                ""OrderType"": ""Limit"",
                ""PriceUsedForBuyingPower"": ""19.7"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""ACK"",
                ""StatusDescription"": ""Received"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(firstUserEditJson);

            // Frame 3: user edits LimitPrice 19.7 -> 19.8 from the TradeStation UI. Must also emit UpdateSubmitted.
            var secondUserEditJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""CommissionFee"": ""0"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""0"",
                ""GoodTillDate"": ""2026-07-23T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""0"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""100"",
                        ""QuantityRemaining"": ""100"",
                        ""Symbol"": ""WULF""
                    }
                ],
                ""LimitPrice"": ""19.8"",
                ""OpenedDateTime"": ""2026-04-24T15:39:00Z"",
                ""OrderID"": ""948851974"",
                ""OrderType"": ""Limit"",
                ""PriceUsedForBuyingPower"": ""19.8"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""ACK"",
                ""StatusDescription"": ""Received"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(secondUserEditJson);

            // Lean calls UpdateOrder (limit -> 20.57). Stub emits UpdateSubmitted + re-seeds skip marker.
            ts.UpdateOrder(limitOrder);

            // Frame 4: FLL - terminal Filled at the new limit price.
            var fllJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""ClosedDateTime"": ""2026-04-24T15:41:01Z"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""GTC"",
                ""FilledPrice"": ""20.57"",
                ""GoodTillDate"": ""2026-07-23T00:00:00Z"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""100"",
                        ""ExecutionPrice"": ""20.57"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""100"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""WULF""
                    }
                ],
                ""LimitPrice"": ""20.57"",
                ""OpenedDateTime"": ""2026-04-24T15:39:00Z"",
                ""OrderID"": ""948851974"",
                ""OrderType"": ""Limit"",
                ""PriceUsedForBuyingPower"": ""20.57"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""FLL"",
                ""StatusDescription"": ""Filled"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(fllJson);

            Assert.IsTrue(filledEventReceived.WaitOne(TimeSpan.FromSeconds(1)), "Did not receive a Filled order event.");

            // Expected lifecycle (EventIDs 1..5 in the production log):
            //   1. Submitted           (stubbed PlaceOrder)
            //   2. UpdateSubmitted     (Frame 2 - first UI edit)
            //   3. UpdateSubmitted     (Frame 3 - second UI edit)
            //   4. UpdateSubmitted     (stubbed UpdateOrder)
            //   5. Filled              (Frame 4 FLL)
            Assert.AreEqual(5, capturedEvents.Count, "Expected Submitted + 3x UpdateSubmitted + Filled.");
            Assert.AreEqual(1, capturedEvents.Count(e => e.Status == OrderStatus.Submitted), "Expected one Submitted event from PlaceOrder.");
            Assert.AreEqual(3, capturedEvents.Count(e => e.Status == OrderStatus.UpdateSubmitted), "Expected three UpdateSubmitted events: two UI edits + one Lean UpdateOrder.");

            var filled = capturedEvents.SingleOrDefault(e => e.Status == OrderStatus.Filled);
            Assert.IsNotNull(filled, "Expected a Filled event after FLL frame.");
            Assert.AreEqual(100m, filled.FillQuantity, "Filled event must carry the full fill quantity.");
            Assert.AreEqual(20.57m, filled.FillPrice, "Filled event must carry the fill price.");
        }
    }
}
