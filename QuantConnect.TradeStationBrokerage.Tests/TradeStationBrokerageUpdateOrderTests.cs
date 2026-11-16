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
using QuantConnect.Orders;
using QuantConnect.Securities;
using System.Collections.Generic;
using QuantConnect.Tests.Brokerages;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    [TestFixture]
    public class TradeStationBrokerageUpdateOrderTests
    {
        private const string BrokerageOrderId = "1337909273";

        // TradeStation no longer holds the order id (e.g. a DAY order on a later trading session) or no longer holds it
        // open: the update must not stop the algorithm, and must leave the order's state to the stream's own events
        [TestCase("Failed to Cancel/Replace order: Invalid order ID.", "UpdateOrderInvalid", "the order no longer exists at the brokerage")]
        [TestCase("Failed to Cancel/Replace order: Not an open order.", "UpdateNotOpenOrder", "the order is already closed")]
        public void SoftRejectOnlyWarns(string brokerMessage, string expectedCode, string expectedReason)
        {
            var orderProvider = new OrderProvider();
            using var brokerage = new ReplaceRejectingBrokerage(orderProvider, brokerMessage);
            var order = CreateSubmittedLimitOrder(orderProvider);

            var (orderEvents, messages) = Record(brokerage);

            Assert.IsFalse(brokerage.UpdateOrder(order));

            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0].Type, Is.EqualTo(BrokerageMessageType.Warning));
            Assert.That(messages[0].Code, Is.EqualTo(expectedCode));
            Assert.That(messages[0].Message, Is.EqualTo(
                $"Failed to update Order: OrderId: {order.Id} (BrokerId: {BrokerageOrderId}) for {order.Symbol}, {expectedReason}"));
            Assert.That(orderEvents, Is.Empty);
            Assert.That(orderProvider.GetOrderById(order.Id).Status, Is.EqualTo(OrderStatus.Submitted));
        }

        // The rejection closes nothing, so a fill TradeStation reports for the order afterwards still reaches Lean
        [Test]
        public void FillAfterInvalidOrderIdIsStillApplied()
        {
            var orderProvider = new OrderProvider();
            using var brokerage = new ReplaceRejectingBrokerage(orderProvider, "Failed to Cancel/Replace order: Invalid order ID.");
            var order = CreateSubmittedLimitOrder(orderProvider);

            var (orderEvents, _) = Record(brokerage);
            brokerage.HandleTradeStationMessage(@"{ ""StreamStatus"": ""EndSnapshot"" }");

            Assert.IsFalse(brokerage.UpdateOrder(order));

            brokerage.HandleTradeStationMessage($$"""
                {
                    "AccountID": "SIM2784990M",
                    "OrderID": "{{BrokerageOrderId}}",
                    "OrderType": "Limit",
                    "LimitPrice": "24.99",
                    "FilledPrice": "24.99",
                    "CommissionFee": "0",
                    "Status": "FLL",
                    "StatusDescription": "Filled",
                    "ClosedDateTime": "2026-11-10T00:59:01Z",
                    "Legs": [{ "BuyOrSell": "Buy", "QuantityOrdered": "4", "ExecQuantity": "4", "ExecutionPrice": "24.99", "Symbol": "SAZ", "AssetType": "STOCK" }]
                }
                """);

            Assert.That(orderEvents, Has.Count.EqualTo(1));
            Assert.That(orderEvents[0].OrderId, Is.EqualTo(order.Id));
            Assert.That(orderEvents[0].Status, Is.EqualTo(OrderStatus.Filled));
            Assert.That(orderEvents[0].FillQuantity, Is.EqualTo(4m));
        }

        // Lean pushes only the leg whose ticket changed: a leg that already filled must keep its state
        [Test]
        public void InvalidOrderIdLeavesComboLegsUntouched()
        {
            var orderProvider = new OrderProvider();
            using var brokerage = new ReplaceRejectingBrokerage(orderProvider, "Failed to Cancel/Replace order: Invalid order ID.");
            var comboOrders = TradeStationBrokerageComboOrderTests.CreateComboLimitOrderGroup(orderProvider, limitPrice: 1.5m);
            comboOrders[0].Status = OrderStatus.Filled;

            var (orderEvents, messages) = Record(brokerage);

            Assert.IsFalse(brokerage.UpdateOrder(comboOrders[1]));

            Assert.That(messages.Single().Type, Is.EqualTo(BrokerageMessageType.Warning));
            Assert.That(orderEvents, Is.Empty);
            Assert.That(orderProvider.GetOrderById(comboOrders[0].Id).Status, Is.EqualTo(OrderStatus.Filled));
            Assert.That(orderProvider.GetOrderById(comboOrders[1].Id).Status, Is.EqualTo(OrderStatus.Submitted));
        }

        [Test]
        public void UnexpectedUpdateErrorIsReportedAsError()
        {
            var orderProvider = new OrderProvider();
            using var brokerage = new ReplaceRejectingBrokerage(orderProvider, "Some other failure.");
            var order = CreateSubmittedLimitOrder(orderProvider);

            var (orderEvents, messages) = Record(brokerage);

            Assert.IsFalse(brokerage.UpdateOrder(order));

            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0].Type, Is.EqualTo(BrokerageMessageType.Error));
            Assert.That(messages[0].Code, Is.EqualTo("UpdateOrderInvalid"));
            Assert.That(messages[0].Message, Is.EqualTo("Some other failure."));
            Assert.That(orderEvents, Is.Empty);
        }

        private static (List<OrderEvent> OrderEvents, List<BrokerageMessageEvent> Messages) Record(TradeStationBrokerage brokerage)
        {
            var orderEvents = new List<OrderEvent>();
            var messages = new List<BrokerageMessageEvent>();
            brokerage.OrdersStatusChanged += (_, events) => orderEvents.AddRange(events);
            brokerage.Message += (_, message) => messages.Add(message);
            return (orderEvents, messages);
        }

        private static LimitOrder CreateSubmittedLimitOrder(OrderProvider orderProvider)
        {
            var order = new LimitOrder(Symbol.Create("SAZ", SecurityType.Equity, Market.USA), 4, 24.99m, DateTime.UtcNow) { Status = OrderStatus.Submitted };
            order.BrokerId.Add(BrokerageOrderId);
            orderProvider.Add(order);
            return order;
        }

        /// <summary>
        /// Rejects every replace request with the given TradeStation message instead of sending it. Its dummy credentials
        /// make no API request: construction does not authenticate, and the replace is intercepted.
        /// </summary>
        private class ReplaceRejectingBrokerage(IOrderProvider orderProvider, string brokerMessage)
            : TradeStationBrokerageTest("clientId", "clientSecret", "https://sim-api.tradestation.com", string.Empty, string.Empty, "refreshToken", "Margin",
                orderProvider, securityProvider: null)
        {
            protected override void ReplaceBrokerageOrder(string brokerageOrderId, Order order, decimal quantity)
            {
                throw new Exception(brokerMessage);
            }
        }
    }
}
