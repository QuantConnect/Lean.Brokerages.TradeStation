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
using Newtonsoft.Json;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using QuantConnect.Tests;
using QuantConnect.Orders;
using System.Collections.Generic;
using QuantConnect.Brokerages.TradeStation.Models;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    /// <summary>
    /// Hermetic tests of the contingent orders (OCO, OTO, OUO, brackets) support: order groups and order sends order
    /// </summary>
    [TestFixture]
    public class TradeStationBrokerageContingentOrdersTests
    {
        private static readonly DateTime Time = new DateTime(2024, 1, 2, 15, 0, 0);

        [Test]
        public void RebuildsBracketFromOpenOrders()
        {
            var entry = Open("1", new LimitOrder(Symbols.SPY, 100, 100, Time), ("OSO", "2"), ("OSO", "3"));
            var takeProfit = Open("2", new LimitOrder(Symbols.SPY, -100, 110, Time), ("OSP", "1"), ("OCO", "3"));
            var stopLoss = Open("3", new StopMarketOrder(Symbols.SPY, -100, 90, Time), ("OSP", "1"), ("OCO", "2"));
            var plain = Open("4", new LimitOrder(Symbols.AAPL, 10, 100, Time));
            var openOrders = new List<(TradeStationOrder, List<Order>)> { entry, takeProfit, stopLoss, plain };

            TradeStationBrokerage.SetContingencies(openOrders);

            var bracket = openOrders.Take(3).Select(x => x.Item2[0]).ToList();
            // the set is shared
            Assert.AreEqual(1, bracket.Select(x => x.Contingency.OrderIds).Distinct().Count());
            Assert.AreEqual(3, bracket[0].Contingency.Count);
            Assert.AreEqual(ContingencyRole.Parent, bracket[0].Contingency.Links.Single().Role);
            foreach (var exit in bracket.Skip(1))
            {
                Assert.IsTrue(exit.IsWaitingForTrigger());
                Assert.AreEqual(2, exit.Contingency.Links.Count);
                Assert.AreEqual(bracket[0].Contingency.Links[0].Id, exit.GetContingencyLink(ContingencyRole.Child).Id);
                Assert.AreEqual(ContingencyType.OneCancelsOther, exit.GetSiblingLink().Type);
            }
            Assert.AreEqual(takeProfit.Item2[0].GetSiblingLink().Id, stopLoss.Item2[0].GetSiblingLink().Id);
            Assert.IsNull(plain.Item2[0].Contingency);
        }

        [Test]
        public void FilledParentLeavesWorkingBracketGroup()
        {
            // the parent is gone, the exits are working and reduce each other (BRK)
            var takeProfit = Open("2", new LimitOrder(Symbols.SPY, -100, 110, Time), ("OSP", "1"), ("BRK", "3"));
            var stopLoss = Open("3", new StopMarketOrder(Symbols.SPY, -100, 90, Time), ("OSP", "1"), ("BRK", "2"));
            var openOrders = new List<(TradeStationOrder, List<Order>)> { takeProfit, stopLoss };

            TradeStationBrokerage.SetContingencies(openOrders);

            foreach (var exit in openOrders.Select(x => x.Item2[0]))
            {
                Assert.IsFalse(exit.IsWaitingForTrigger());
                Assert.AreEqual(2, exit.Contingency.Count);
                Assert.AreEqual(ContingencyType.OneUpdatesOther, exit.Contingency.Links.Single().Type);
            }
        }

        [Test]
        public void OrderGroupRequestSerialization()
        {
            var request = new TradeStationOrderGroupRequest(TradeStationOrderGroupRequest.OneCancelsOther, new List<TradeStationPlaceOrderRequest>
            {
                new("account", TradeStationOrderType.Limit, "100", "SPY", new Models.TimeInForce(PlaceOrderDuration.GoodTillCanceled, null), "SELL") { LimitPrice = "110" },
                new("account", TradeStationOrderType.StopMarket, "100", "SPY", new Models.TimeInForce(PlaceOrderDuration.GoodTillCanceled, null), "SELL") { StopPrice = "90" }
            });

            var json = JsonConvert.SerializeObject(request, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            StringAssert.Contains("\"Type\":\"OCO\"", json);
            StringAssert.Contains("\"LimitPrice\":\"110\"", json);
            StringAssert.Contains("\"StopPrice\":\"90\"", json);
            StringAssert.DoesNotContain("OSOs", json);
        }

        private static (TradeStationOrder, List<Order>) Open(string orderId, Order leanOrder, params (string Relationship, string OrderId)[] linked)
        {
            var conditionalOrders = string.Join(",", linked.Select(x => $"{{\"AccountID\":\"account\",\"Relationship\":\"{x.Relationship}\",\"OrderID\":\"{x.OrderId}\"}}"));
            var brokerageOrder = JsonConvert.DeserializeObject<TradeStationOrder>($"{{\"OrderID\":\"{orderId}\",\"ConditionalOrders\":[{conditionalOrders}]}}");
            leanOrder.BrokerId.Add(orderId);
            return (brokerageOrder, new List<Order> { leanOrder });
        }
    }
}
