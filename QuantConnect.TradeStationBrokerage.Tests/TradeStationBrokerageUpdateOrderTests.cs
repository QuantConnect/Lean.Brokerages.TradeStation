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

        // TradeStation purged the order id (e.g. a DAY order on a later trading session): the update must not stop the
        // algorithm, and the Lean order must be closed, since no stream event will ever close it
        [Test]
        public void InvalidOrderIdMarksOrderCanceled()
        {
            var orderProvider = new OrderProvider();
            var brokerage = new ReplaceRejectingBrokerage(orderProvider, "Failed to Cancel/Replace order: Invalid order ID.");
            var order = CreateSubmittedLimitOrder(orderProvider, Symbol.Create("SAZ", SecurityType.Equity, Market.USA));

            var (orderEvents, messages) = Record(brokerage);

            Assert.IsTrue(brokerage.UpdateOrder(order));

            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0].Type, Is.EqualTo(BrokerageMessageType.Warning));
            Assert.That(messages[0].Code, Is.EqualTo("UpdateOrderInvalid"));
            Assert.That(messages[0].Message, Is.EqualTo(
                $"Failed to update Order: OrderId: {order.Id} (BrokerId: {BrokerageOrderId}) for {order.Symbol}, the order no longer exists at the brokerage"));

            Assert.That(orderEvents, Has.Count.EqualTo(1));
            Assert.That(orderEvents[0].OrderId, Is.EqualTo(order.Id));
            Assert.That(orderEvents[0].Status, Is.EqualTo(OrderStatus.Canceled));
            Assert.That(orderEvents[0].Message, Is.EqualTo("the order no longer exists at the brokerage"));
        }

        [Test]
        public void InvalidOrderIdMarksEveryComboLegCanceled()
        {
            var orderProvider = new OrderProvider();
            var brokerage = new ReplaceRejectingBrokerage(orderProvider, "Failed to Cancel/Replace order: Invalid order ID.");
            var comboOrders = TradeStationBrokerageComboOrderTests.CreateComboLimitOrderGroup(orderProvider, limitPrice: 1.5m);

            var (orderEvents, _) = Record(brokerage);

            // Lean pushes only the leg whose ticket changed
            Assert.IsTrue(brokerage.UpdateOrder(comboOrders[1]));

            CollectionAssert.AreEquivalent(comboOrders.Select(order => order.Id), orderEvents.Select(orderEvent => orderEvent.OrderId));
            Assert.That(orderEvents.All(orderEvent => orderEvent.Status == OrderStatus.Canceled));
        }

        // The order still exists at TradeStation and its own stream events close it, so the update only warns
        [Test]
        public void NotAnOpenOrderOnlyWarns()
        {
            var orderProvider = new OrderProvider();
            var brokerage = new ReplaceRejectingBrokerage(orderProvider, "Failed to Cancel/Replace order: Not an open order.");
            var order = CreateSubmittedLimitOrder(orderProvider, Symbol.Create("AAPL", SecurityType.Equity, Market.USA));

            var (orderEvents, messages) = Record(brokerage);

            Assert.IsFalse(brokerage.UpdateOrder(order));

            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0].Type, Is.EqualTo(BrokerageMessageType.Warning));
            Assert.That(messages[0].Code, Is.EqualTo("UpdateNotOpenOrder"));
            Assert.That(orderEvents, Is.Empty);
        }

        [Test]
        public void UnexpectedUpdateErrorIsReportedAsError()
        {
            var orderProvider = new OrderProvider();
            var brokerage = new ReplaceRejectingBrokerage(orderProvider, "Some other failure.");
            var order = CreateSubmittedLimitOrder(orderProvider, Symbol.Create("AAPL", SecurityType.Equity, Market.USA));

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

        private static LimitOrder CreateSubmittedLimitOrder(OrderProvider orderProvider, Symbol symbol)
        {
            var order = new LimitOrder(symbol, 4, 24.99m, DateTime.UtcNow) { Status = OrderStatus.Submitted };
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
