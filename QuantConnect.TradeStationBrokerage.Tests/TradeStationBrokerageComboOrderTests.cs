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
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Tests.Brokerages;

namespace QuantConnect.Brokerages.TradeStation.Tests;

/// <summary>
/// Requests the brokerage sends on update/cancel, recorded instead of reaching TradeStation. No TradeStation account
/// needed, but the QC subscription validation still requires valid api credentials in config.
/// </summary>
[TestFixture]
public class TradeStationBrokerageComboOrderTests
{
    private const string BrokerageOrderId = "123456789";

    /// <summary>
    /// Issue #106: Lean pushes only the updated leg; waiting for the remaining legs meant the replace was never sent.
    /// </summary>
    [Test]
    public void UpdatesComboOrderWhenOnlyOneLegIsUpdated()
    {
        var orderProvider = new OrderProvider();
        var comboOrders = CreateComboLimitOrderGroup(orderProvider, limitPrice: 1.5m);

        using var brokerage = new RecordingBrokerage(orderProvider);

        var orderEvents = new List<OrderEvent>();
        brokerage.OrdersStatusChanged += (_, events) => orderEvents.AddRange(events);

        // The new price reaches every leg through the shared group order manager
        var updatedLeg = comboOrders[0];
        updatedLeg.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, updatedLeg.Id, new() { LimitPrice = 2.25m }));

        Assert.IsTrue(brokerage.UpdateOrder(updatedLeg));

        Assert.AreEqual(1, brokerage.Replaces.Count);
        Assert.AreEqual(BrokerageOrderId, brokerage.Replaces[0].BrokerageOrderId);
        Assert.AreEqual(2.25m, brokerage.Replaces[0].LimitPrice);
        // TradeStation takes the quantity as a multiplier of the leg ratios, not a leg quantity
        Assert.AreEqual(8m, brokerage.Replaces[0].Quantity);

        // Every leg of the combo must be reported as updated, not just the one whose ticket Lean pushed.
        CollectionAssert.AreEquivalent(comboOrders.Select(order => order.Id),
            orderEvents.Where(orderEvent => orderEvent.Status == OrderStatus.UpdateSubmitted).Select(orderEvent => orderEvent.OrderId));

        // An algorithm that updates every leg's ticket produces the same request again, which must not be re-sent
        Assert.IsTrue(brokerage.UpdateOrder(comboOrders[1]));
        Assert.AreEqual(1, brokerage.Replaces.Count);

        // A combo quantity update resizes every leg through the group order manager
        updatedLeg.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, updatedLeg.Id, new() { Quantity = 3 }));

        Assert.IsTrue(brokerage.UpdateOrder(updatedLeg));

        Assert.AreEqual(2, brokerage.Replaces.Count);
        Assert.AreEqual(3m, brokerage.Replaces[1].Quantity);
    }

    /// <summary>
    /// Cancelling one leg's ticket - all Lean pushes - must cancel the whole combo.
    /// </summary>
    [Test]
    public void CancelsComboOrderWhenOnlyOneLegIsCancelled()
    {
        var orderProvider = new OrderProvider();
        var comboOrders = CreateComboLimitOrderGroup(orderProvider, limitPrice: 1.5m);

        using var brokerage = new RecordingBrokerage(orderProvider);

        Assert.IsTrue(brokerage.CancelOrder(comboOrders[0]));
        // Cancelling the remaining legs too must not send a second cancel for the same brokerage order
        Assert.IsTrue(brokerage.CancelOrder(comboOrders[1]));

        CollectionAssert.AreEqual(new[] { BrokerageOrderId }, brokerage.Cancels);
    }

    /// <summary>
    /// A rejected replace leaves TradeStation working the order with its previous values, so the order stays open,
    /// the algorithm is warned and the same update can be sent again.
    /// </summary>
    [Test]
    public void WarnsAndKeepsTheOrderOpenWhenTheReplaceIsRejected()
    {
        var orderProvider = new OrderProvider();
        var comboOrders = CreateComboLimitOrderGroup(orderProvider, limitPrice: 1.5m);

        using var brokerage = new RecordingBrokerage(orderProvider);

        var orderEvents = new List<OrderEvent>();
        brokerage.OrdersStatusChanged += (_, events) => orderEvents.AddRange(events);
        var messages = new List<BrokerageMessageEvent>();
        brokerage.Message += (_, message) => messages.Add(message);

        // Marks the stream as live, the frames before it are the initial snapshot and are ignored
        brokerage.HandleTradeStationMessage(@"{ ""StreamStatus"": ""EndSnapshot"" }");

        var updatedLeg = comboOrders[0];
        updatedLeg.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, updatedLeg.Id, new() { LimitPrice = 2.25m }));
        Assert.IsTrue(brokerage.UpdateOrder(updatedLeg));
        Assert.AreEqual(1, brokerage.Replaces.Count);

        brokerage.HandleTradeStationMessage($$"""
            {
                "AccountID": "SIM2784990M",
                "OrderID": "{{BrokerageOrderId}}",
                "OrderType": "Limit",
                "LimitPrice": "1.5",
                "Status": "RJR",
                "StatusDescription": "Change Request Rejected",
                "RejectReason": "Order price is outside of the allowed range"
            }
            """);

        Assert.IsFalse(orderEvents.Any(orderEvent => orderEvent.Status == OrderStatus.Invalid));
        Assert.AreEqual(1, messages.Count(message => message.Type == BrokerageMessageType.Warning && message.Code == "UpdateOrderRejected"));

        // The rejection cleared the last sent values, so the same request goes out again
        Assert.IsTrue(brokerage.UpdateOrder(updatedLeg));
        Assert.AreEqual(2, brokerage.Replaces.Count);
    }

    /// <summary>
    /// Single leg orders keep replacing both their quantity and their price.
    /// </summary>
    [Test]
    public void SendsQuantityAndPriceWhenReplacingASingleLegOrder()
    {
        var orderProvider = new OrderProvider();
        var limitOrder = new LimitOrder(Symbol.Create("AAPL", SecurityType.Equity, Market.USA), 10, 200m, DateTime.UtcNow)
        {
            Status = OrderStatus.Submitted
        };
        limitOrder.BrokerId.Add(BrokerageOrderId);
        orderProvider.Add(limitOrder);

        using var brokerage = new RecordingBrokerage(orderProvider);

        limitOrder.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, limitOrder.Id, new() { LimitPrice = 210m }));
        Assert.IsTrue(brokerage.UpdateOrder(limitOrder));

        Assert.AreEqual(1, brokerage.Replaces.Count);
        Assert.AreEqual(10m, brokerage.Replaces[0].Quantity);
        Assert.AreEqual(210m, brokerage.Replaces[0].LimitPrice);
    }

    /// <summary>
    /// Builds a two leg combo limit order group, registering every leg with the order provider the way Lean does.
    /// </summary>
    /// <param name="orderProvider">The order provider the legs are registered with.</param>
    /// <param name="limitPrice">The initial limit price of the group.</param>
    /// <returns>The legs of the group.</returns>
    private static List<ComboLimitOrder> CreateComboLimitOrderGroup(OrderProvider orderProvider, decimal limitPrice)
    {
        var underlying = Symbol.Create("AAPL", SecurityType.Equity, Market.USA);
        var expiry = new DateTime(2026, 9, 18);
        (Symbol Symbol, decimal Ratio)[] legs =
        [
            (Symbol.CreateOption(underlying, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 220m, expiry), -1m),
            (Symbol.CreateOption(underlying, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 230m, expiry), 1m)
        ];

        var groupOrderManager = new GroupOrderManager(1, legCount: legs.Length, quantity: 8, limitPrice: limitPrice);

        List<ComboLimitOrder> comboOrders = [];
        foreach (var (symbol, ratio) in legs)
        {
            var comboOrder = new ComboLimitOrder(symbol, ratio.GetOrderLegGroupQuantity(groupOrderManager), limitPrice, DateTime.UtcNow, groupOrderManager)
            {
                Status = OrderStatus.Submitted
            };
            comboOrder.BrokerId.Add(BrokerageOrderId);
            orderProvider.Add(comboOrder);
            groupOrderManager.OrderIds.Add(comboOrder.Id);
            comboOrders.Add(comboOrder);
        }

        return comboOrders;
    }

    /// <summary>
    /// Records the replace and cancel requests instead of sending them to TradeStation.
    /// </summary>
    private class RecordingBrokerage(IOrderProvider orderProvider)
        : TradeStationBrokerageTest("client-id", "client-secret", "https://api.test", "http://localhost", string.Empty, "refresh-token", "Margin",
            orderProvider, securityProvider: null)
    {
        public List<(string BrokerageOrderId, decimal Quantity, decimal? LimitPrice)> Replaces { get; } = [];

        public List<string> Cancels { get; } = [];

        protected override void ReplaceBrokerageOrder(string brokerageOrderId, OrderType orderType, decimal quantity, decimal? limitPrice, decimal? stopPrice,
            decimal? trailingAmount, bool? trailingAsPercentage)
        {
            Replaces.Add((brokerageOrderId, quantity, limitPrice));
        }

        protected override bool CancelBrokerageOrder(string brokerageOrderId)
        {
            Cancels.Add(brokerageOrderId);
            return true;
        }
    }
}
