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
using System.Threading;
using NUnit.Framework;
using QuantConnect.Tests;
using QuantConnect.Orders;
using QuantConnect.Securities;
using System.Collections.Generic;
using QuantConnect.Tests.Brokerages;
using QuantConnect.Brokerages.CrossZero;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    [TestFixture]
    public class TradeStationBrokerageCancelOrderTests
    {
        // When TradeStation reports the order is already gone, CancelOrder must treat it as a non-fatal warning and
        // transition the still-pending order to a terminal Canceled state (instead of crashing or looping in CancelPending).
        [TestCase("Not an open order.", "CancelNotOpenOrder", "the order is already closed")]
        [TestCase("Invalid order ID.", "CancelOrderInvalid", "the order no longer exists at the brokerage")]
        public void SoftRejectMarksCancelPendingOrderCanceled(string brokerMessage, string expectedCode, string expectedReason)
        {
            var orderProvider = new OrderProvider();
            var brokerage = CreateBrokerage(orderProvider, _ => throw new Exception(brokerMessage));

            var order = CreateCancelPendingOrder(orderProvider, "957819089");

            var orderEvents = new List<OrderEvent>();
            BrokerageMessageEvent message = null;
            brokerage.OrdersStatusChanged += (_, events) => orderEvents.AddRange(events);
            brokerage.Message += (_, msg) => message = msg;

            var result = brokerage.CancelOrder(order);

            Assert.IsTrue(result);

            // A non-fatal warning is emitted with the expected code and message.
            Assert.IsNotNull(message);
            Assert.That(message.Type, Is.EqualTo(BrokerageMessageType.Warning));
            Assert.That(message.Code, Is.EqualTo(expectedCode));
            Assert.That(message.Message, Is.EqualTo(
                $"Failed to cancel Order: OrderId: {order.Id} (BrokerId: 957819089) for {order.Symbol}, {expectedReason}"));

            // The order is transitioned to a terminal Canceled state.
            Assert.That(orderEvents, Has.Count.EqualTo(1));
            Assert.That(orderEvents[0].Status, Is.EqualTo(OrderStatus.Canceled));
            Assert.That(orderEvents[0].OrderId, Is.EqualTo(order.Id));
            Assert.That(orderEvents[0].Message, Is.EqualTo(expectedReason));
        }

        // A cancel rejected because the order already filled closes it as canceled: the fill a reconnect snapshot
        // reports for it must still reach Lean, once.
        [Test]
        public void ReconnectSnapshotRecoversFillOfOrderClosedByCancelSoftReject()
        {
            var orderProvider = new OrderProvider();
            var brokerage = CreateBrokerage(orderProvider, _ => throw new Exception("Not an open order."));
            var order = CreateCancelPendingOrder(orderProvider, "957819089");

            var orderEvents = new List<OrderEvent>();
            brokerage.OrdersStatusChanged += (_, events) =>
            {
                orderEvents.AddRange(events);
                events.ForEach(orderProvider.HandleOrderEvent);
            };

            brokerage.HandleTradeStationMessage(@"{""StreamStatus"": ""EndSnapshot""}");
            TradeStationBrokerageStreamEventsTests.EnterReconnectSnapshotWindow(brokerage);

            Assert.IsTrue(brokerage.CancelOrder(order));
            Assert.AreEqual(OrderStatus.Canceled, order.Status);

            var filledSnapshotJson = @"{
                ""AccountID"": ""SIM2784990M"",
                ""ClosedDateTime"": ""2026-06-23T16:06:00Z"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""DAY"",
                ""FilledPrice"": ""100"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""10"",
                        ""ExecutionPrice"": ""100"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""10"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""AAPL""
                    }
                ],
                ""OpenedDateTime"": ""2026-06-23T16:06:00Z"",
                ""OrderID"": ""957819089"",
                ""OrderType"": ""StopMarket"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""FLL"",
                ""StatusDescription"": ""Filled"",
                ""UnbundledRouteFee"": ""0""
            }";
            brokerage.HandleTradeStationMessage(filledSnapshotJson);

            Assert.AreEqual(10m, orderEvents.Sum(e => e.FillQuantity));
            Assert.AreEqual(OrderStatus.Filled, order.Status);

            // A later reconnect snapshot must not report the same fill again
            TradeStationBrokerageStreamEventsTests.EnterReconnectSnapshotWindow(brokerage);
            brokerage.HandleTradeStationMessage(filledSnapshotJson);

            Assert.AreEqual(10m, orderEvents.Sum(e => e.FillQuantity));
        }

        // An order a soft-rejected cancel closed, which the brokerage reports as canceled, emits no second cancel
        [Test]
        public void ReconnectSnapshotSkipsCancelOfOrderClosedByCancelSoftReject()
        {
            var orderProvider = new OrderProvider();
            var brokerage = CreateBrokerage(orderProvider, _ => throw new Exception("Not an open order."));
            var order = CreateCancelPendingOrder(orderProvider, "957819089");

            var orderEvents = new List<OrderEvent>();
            brokerage.OrdersStatusChanged += (_, events) =>
            {
                orderEvents.AddRange(events);
                events.ForEach(orderProvider.HandleOrderEvent);
            };

            brokerage.HandleTradeStationMessage(@"{""StreamStatus"": ""EndSnapshot""}");
            Assert.IsTrue(brokerage.CancelOrder(order));
            orderEvents.Clear();

            TradeStationBrokerageStreamEventsTests.EnterReconnectSnapshotWindow(brokerage);
            brokerage.HandleTradeStationMessage(@"{
                ""AccountID"": ""SIM2784990M"",
                ""ClosedDateTime"": ""2026-06-23T16:06:00Z"",
                ""CommissionFee"": ""0"",
                ""Currency"": ""USD"",
                ""Duration"": ""DAY"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Buy"",
                        ""ExecQuantity"": ""0"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""10"",
                        ""QuantityRemaining"": ""10"",
                        ""Symbol"": ""AAPL""
                    }
                ],
                ""OpenedDateTime"": ""2026-06-23T16:06:00Z"",
                ""OrderID"": ""957819089"",
                ""OrderType"": ""StopMarket"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""OUT"",
                ""StatusDescription"": ""UROut"",
                ""UnbundledRouteFee"": ""0""
            }");

            Assert.IsEmpty(orderEvents);
            Assert.AreEqual(OrderStatus.Canceled, order.Status);
        }

        // A cross-zero order whose first half filled before the cancel: the soft-rejected cancel drops the pending
        // second half, so the fill a reconnect snapshot recovers closes the order instead of placing the second half
        [Test]
        public void ReconnectSnapshotFillOfCrossZeroOrderClosedByCancelSoftRejectDoesNotPlaceSecondHalf()
        {
            var orderProvider = new OrderProvider();
            var brokerage = CreateBrokerage(orderProvider, _ => throw new Exception("Not an open order."));

            var order = new MarketOrder(Symbols.AAPL, -10, DateTime.UtcNow) { Status = OrderStatus.Submitted };
            orderProvider.Add(order);

            // Holding +5: the sell of 10 places a sell of 5 and keeps the short 5 for when it fills
            Assert.IsTrue(brokerage.PlaceCrossZeroOrder(order, 5m));
            Assert.AreEqual(1, brokerage.CrossZeroPlacements.Count);
            Assert.AreEqual(-5m, brokerage.CrossZeroPlacements[0].OrderQuantity);
            order.Status = OrderStatus.CancelPending;

            var orderEvents = new List<OrderEvent>();
            brokerage.OrdersStatusChanged += (_, events) =>
            {
                orderEvents.AddRange(events);
                events.ForEach(orderProvider.HandleOrderEvent);
            };

            brokerage.HandleTradeStationMessage(@"{""StreamStatus"": ""EndSnapshot""}");
            TradeStationBrokerageStreamEventsTests.EnterReconnectSnapshotWindow(brokerage);

            Assert.IsTrue(brokerage.CancelOrder(order));
            Assert.AreEqual(OrderStatus.Canceled, order.Status);

            brokerage.HandleTradeStationMessage(@"{
                ""AccountID"": ""SIM2784990M"",
                ""ClosedDateTime"": ""2026-06-23T16:06:00Z"",
                ""CommissionFee"": ""1"",
                ""Currency"": ""USD"",
                ""Duration"": ""DAY"",
                ""FilledPrice"": ""100"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""Sell"",
                        ""ExecQuantity"": ""5"",
                        ""ExecutionPrice"": ""100"",
                        ""OpenOrClose"": ""Close"",
                        ""QuantityOrdered"": ""5"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""AAPL""
                    }
                ],
                ""OpenedDateTime"": ""2026-06-23T16:06:00Z"",
                ""OrderID"": ""957819089"",
                ""OrderType"": ""Market"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""FLL"",
                ""StatusDescription"": ""Filled"",
                ""UnbundledRouteFee"": ""0""
            }");

            // The second half would be placed on a background task
            Thread.Sleep(500);
            Assert.AreEqual(1, brokerage.CrossZeroPlacements.Count, "The second half must not be placed after the cancel");
            Assert.AreEqual(-5m, orderEvents.Sum(e => e.FillQuantity));
            Assert.AreEqual(OrderStatus.Filled, orderEvents.Last().Status);
            Assert.AreEqual(OrderStatus.Filled, order.Status);
        }

        [Test]
        public void UnexpectedCancelErrorIsReportedAsError()
        {
            var orderProvider = new OrderProvider();
            var brokerage = CreateBrokerage(orderProvider, _ => throw new Exception("Some other failure."));

            var order = CreateCancelPendingOrder(orderProvider, "957819089");

            var orderEvents = new List<OrderEvent>();
            BrokerageMessageEvent message = null;
            brokerage.OrdersStatusChanged += (_, events) => orderEvents.AddRange(events);
            brokerage.Message += (_, msg) => message = msg;

            var result = brokerage.CancelOrder(order);

            // An unrecognized failure is not swallowed: it is surfaced as an error and the cancel is reported as failed.
            Assert.IsFalse(result);
            Assert.IsNotNull(message);
            Assert.That(message.Type, Is.EqualTo(BrokerageMessageType.Error));
            Assert.That(message.Code, Is.EqualTo("CancelOrderInvalid"));
            Assert.That(message.Message, Is.EqualTo("Some other failure."));
            Assert.That(orderEvents, Is.Empty);
        }

        private static StopMarketOrder CreateCancelPendingOrder(OrderProvider orderProvider, string brokerageOrderId)
        {
            var order = new StopMarketOrder(Symbols.AAPL, 10, 100m, DateTime.UtcNow) { Status = OrderStatus.CancelPending };
            order.BrokerId.Add(brokerageOrderId);
            orderProvider.Add(order);
            return order;
        }

        private static CancelOrderStubBrokerage CreateBrokerage(IOrderProvider orderProvider, Func<string, bool> cancelHandler)
        {
            // Dummy credentials: construction does not authenticate (the account id is resolved lazily), and the cancel
            // call is intercepted by the stub, so no live API request is ever made.
            return new CancelOrderStubBrokerage("clientId", "clientSecret", "https://sim-api.tradestation.com", string.Empty,
                string.Empty, "refreshToken", "Margin", orderProvider, null)
            {
                CancelOrderHandler = cancelHandler
            };
        }

        /// <summary>
        /// Test brokerage that intercepts the TradeStation cancel request so the soft-reject handling in
        /// <see cref="TradeStationBrokerage.CancelOrder"/> can be exercised deterministically without a live connection.
        /// </summary>
        private class CancelOrderStubBrokerage : TradeStationBrokerageTest
        {
            public Func<string, bool> CancelOrderHandler { get; set; }

            public CancelOrderStubBrokerage(string apiKey, string apiKeySecret, string restApiUrl, string redirectUrl,
                string authorizationCode, string refreshToken, string accountType, IOrderProvider orderProvider, ISecurityProvider securityProvider)
                : base(apiKey, apiKeySecret, restApiUrl, redirectUrl, authorizationCode, refreshToken, accountType, orderProvider, securityProvider)
            { }

            protected override bool CancelBrokerageOrder(string brokerageOrderId)
            {
                return CancelOrderHandler != null ? CancelOrderHandler(brokerageOrderId) : base.CancelBrokerageOrder(brokerageOrderId);
            }

            public List<CrossZeroFirstOrderRequest> CrossZeroPlacements { get; } = new();

            public bool? PlaceCrossZeroOrder(Order order, decimal holdingQuantity)
            {
                return TryCrossZeroPositionOrder(order, holdingQuantity);
            }

            protected override CrossZeroOrderResponse PlaceCrossZeroOrder(CrossZeroFirstOrderRequest crossZeroOrderRequest, bool isPlaceOrderWithLeanEvent)
            {
                CrossZeroPlacements.Add(crossZeroOrderRequest);
                return new CrossZeroOrderResponse(CrossZeroPlacements.Count == 1 ? "957819089" : "957819090", true);
            }
        }
    }
}
