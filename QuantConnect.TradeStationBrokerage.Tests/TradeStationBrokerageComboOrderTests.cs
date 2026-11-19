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
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using QuantConnect.Orders;
using QuantConnect.Brokerages.TradeStation.Api;
using QuantConnect.Securities;
using QuantConnect.Tests.Brokerages;

namespace QuantConnect.Brokerages.TradeStation.Tests;

/// <summary>
/// Cancels the brokerage sends, recorded instead of reaching TradeStation. No TradeStation account
/// needed, but the QC subscription validation still requires valid api credentials in config.
/// </summary>
[TestFixture]
public class TradeStationBrokerageComboOrderTests
{
    private const string BrokerageOrderId = "123456789";

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

        CollectionAssert.AreEqual(new[] { BrokerageOrderId }, brokerage.Cancels);
    }

    /// <summary>
    /// A replace with unchanged values gets no stream frame, so its pending update flag must not swallow a later cancel.
    /// </summary>
    [Test]
    public void CancelsAfterARepeatedUpdateWithoutStreamFrame()
    {
        var orderProvider = new OrderProvider();
        var limitOrder = new LimitOrder(Symbol.Create("AAPL", SecurityType.Equity, Market.USA), 1, 100m, DateTime.UtcNow)
        {
            Status = OrderStatus.Submitted
        };
        limitOrder.BrokerId.Add(BrokerageOrderId);
        orderProvider.Add(limitOrder);

        using var brokerage = new RecordingBrokerage(orderProvider);

        var orderEvents = new List<OrderEvent>();
        brokerage.OrdersStatusChanged += (_, events) => orderEvents.AddRange(events);
        brokerage.HandleTradeStationMessage(@"{ ""StreamStatus"": ""EndSnapshot"" }");

        // What UpdateOrder leaves behind after a PUT with unchanged values, which gets no stream frame to clear it
        brokerage.MarkUpdateSubmitted(BrokerageOrderId);
        Assert.IsTrue(brokerage.CancelOrder(limitOrder));

        brokerage.HandleTradeStationMessage($$"""
            {
                "AccountID": "SIM2784990M",
                "OrderID": "{{BrokerageOrderId}}",
                "OrderType": "Limit",
                "LimitPrice": "100",
                "Status": "OUT",
                "StatusDescription": "UROut",
                "ClosedDateTime": "2026-09-29T14:16:58Z",
                "Legs": [{ "BuyOrSell": "Buy", "QuantityOrdered": "1", "ExecQuantity": "0", "Symbol": "AAPL", "AssetType": "STOCK" }]
            }
            """);

        Assert.IsTrue(orderEvents.Any(orderEvent => orderEvent.OrderId == limitOrder.Id && orderEvent.Status == OrderStatus.Canceled));
    }

    /// <summary>
    /// A cancel TradeStation rejects because the order is gone closes only the legs still open: a filled leg stays filled.
    /// </summary>
    [Test]
    public void CancelSoftRejectKeepsClosedComboLegs()
    {
        var orderProvider = new OrderProvider();
        var comboOrders = CreateComboLimitOrderGroup(orderProvider, limitPrice: 1.5m);
        comboOrders[0].Status = OrderStatus.Filled;
        comboOrders[1].Status = OrderStatus.CancelPending;

        using var brokerage = new RecordingBrokerage(orderProvider) { CancelException = new Exception("Invalid order ID.") };
        brokerage.OrdersStatusChanged += (_, events) => events.ForEach(orderProvider.HandleOrderEvent);

        Assert.IsTrue(brokerage.CancelOrder(comboOrders[1]));

        Assert.AreEqual(OrderStatus.Filled, comboOrders[0].Status);
        Assert.AreEqual(OrderStatus.Canceled, comboOrders[1].Status);
    }

    /// <summary>
    /// A replace TradeStation accepts reports the update only for the legs still open: a filled leg stays filled.
    /// </summary>
    [Test]
    public void UpdateKeepsClosedComboLegs()
    {
        var orderProvider = new OrderProvider();
        var comboOrders = CreateComboLimitOrderGroup(orderProvider, limitPrice: 1.5m);
        comboOrders[0].Status = OrderStatus.Filled;

        using var brokerage = new RecordingBrokerage(orderProvider);
        brokerage.AnswerRequestsWith($@"{{ ""OrderID"": ""{BrokerageOrderId}"", ""Message"": ""Cancel/Replace order sent."" }}");
        brokerage.OrdersStatusChanged += (_, events) => events.ForEach(orderProvider.HandleOrderEvent);

        Assert.IsTrue(brokerage.UpdateOrder(comboOrders[1]));

        Assert.AreEqual(OrderStatus.Filled, comboOrders[0].Status);
        Assert.AreEqual(OrderStatus.UpdateSubmitted, comboOrders[1].Status);
    }

    /// <summary>
    /// A filled leg reopens when an accepted replace increases the combo quantity, so its further fills still reach Lean.
    /// </summary>
    [Test]
    public void UpdateReopensFilledComboLegWhenQuantityIncreases()
    {
        var orderProvider = new OrderProvider();
        var comboOrders = CreateComboLimitOrderGroup(orderProvider, limitPrice: 1.5m);

        using var brokerage = new RecordingBrokerage(orderProvider);
        brokerage.AnswerRequestsWith($@"{{ ""OrderID"": ""{BrokerageOrderId}"", ""Message"": ""Cancel/Replace order sent."" }}");
        var fillQuantities = new Dictionary<int, decimal>();
        brokerage.OrdersStatusChanged += (_, events) => events.ForEach(orderEvent =>
        {
            fillQuantities[orderEvent.OrderId] = fillQuantities.GetValueOrDefault(orderEvent.OrderId) + orderEvent.FillQuantity;
            orderProvider.HandleOrderEvent(orderEvent);
        });
        brokerage.HandleTradeStationMessage(@"{ ""StreamStatus"": ""EndSnapshot"" }");

        // The sold leg fills all 8 while the bought leg waits
        brokerage.HandleTradeStationMessage(CreateComboMessage("FPR", "Partial Fill (Alive)", soldExecuted: 8, boughtExecuted: 0, quantity: 8));
        Assert.AreEqual(OrderStatus.Filled, comboOrders[0].Status);

        comboOrders[1].ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, comboOrders[1].Id, new UpdateOrderFields { Quantity = 10 }));
        Assert.IsTrue(brokerage.UpdateOrder(comboOrders[1]));
        Assert.AreEqual(OrderStatus.UpdateSubmitted, comboOrders[0].Status);

        brokerage.HandleTradeStationMessage(CreateComboMessage("FLL", "Filled", soldExecuted: 10, boughtExecuted: 10, quantity: 10));

        Assert.AreEqual(-10m, fillQuantities[comboOrders[0].Id]);
        Assert.AreEqual(10m, fillQuantities[comboOrders[1].Id]);
        Assert.AreEqual(OrderStatus.Filled, comboOrders[0].Status);
        Assert.AreEqual(OrderStatus.Filled, comboOrders[1].Status);
    }

    /// <summary>
    /// Builds a stream message for the combo of <see cref="CreateComboLimitOrderGroup"/> with the cumulative executions of its legs.
    /// </summary>
    private static string CreateComboMessage(string status, string statusDescription, decimal soldExecuted, decimal boughtExecuted, decimal quantity)
    {
        return $$"""
            {
                "AccountID": "SIM2784990M",
                "OrderID": "{{BrokerageOrderId}}",
                "OrderType": "Limit",
                "LimitPrice": "1.5",
                "Status": "{{status}}",
                "StatusDescription": "{{statusDescription}}",
                "CommissionFee": "0",
                "Legs": [
                    { "AssetType": "STOCKOPTION", "BuyOrSell": "SellToOpen", "ExecQuantity": "{{soldExecuted}}", "ExecutionPrice": "3",
                      "ExpirationDate": "2026-09-18T00:00:00Z", "QuantityOrdered": "{{quantity}}", "QuantityRemaining": "{{quantity - soldExecuted}}",
                      "Symbol": "AAPL 260918C220" },
                    { "AssetType": "STOCKOPTION", "BuyOrSell": "BuyToOpen", "ExecQuantity": "{{boughtExecuted}}", "ExecutionPrice": "1.5",
                      "ExpirationDate": "2026-09-18T00:00:00Z", "QuantityOrdered": "{{quantity}}", "QuantityRemaining": "{{quantity - boughtExecuted}}",
                      "Symbol": "AAPL 260918C230" }
                ]
            }
            """;
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
    /// Records the cancel requests instead of sending them to TradeStation.
    /// </summary>
    private class RecordingBrokerage(IOrderProvider orderProvider)
        : TradeStationBrokerageTest("client-id", "client-secret", "https://api.test", "http://localhost", string.Empty, "refresh-token", "Margin",
            orderProvider, securityProvider: null)
    {
        public List<string> Cancels { get; } = [];

        public Exception CancelException { get; init; }

        /// <summary>
        /// Answers every TradeStation request with the given body instead of sending it.
        /// </summary>
        public void AnswerRequestsWith(string responseBody)
        {
            var handler = new TestHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            }));
            var httpClient = new HttpClientRetryWrapper("https://api.test", handler, 1, TimeSpan.FromSeconds(10), TimeSpan.Zero);
            typeof(TradeStationBrokerage).GetField("_tradeStationApiClient", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(this, new TradeStationApiClient(httpClient, "SIM2784990M", null));
        }

        public void MarkUpdateSubmitted(string brokerageOrderId) => _updateSubmittedResponseResultByBrokerageID[brokerageOrderId] = true;

        protected override bool CancelBrokerageOrder(string brokerageOrderId)
        {
            Cancels.Add(brokerageOrderId);
            if (CancelException != null)
            {
                throw CancelException;
            }
            return true;
        }
    }
}
