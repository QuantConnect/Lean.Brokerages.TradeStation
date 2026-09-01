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
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QuantConnect.Brokerages.TradeStation.Api;
using QuantConnect.Orders;
using QuantConnect.Tests.Brokerages;

namespace QuantConnect.Brokerages.TradeStation.Tests;

/// <summary>
/// Offline coverage of the replace order request the brokerage sends when Lean updates an order,
/// driven through a fake HTTP handler so no TradeStation account is required.
/// </summary>
[TestFixture]
public class TradeStationBrokerageUpdateOrderTests
{
    private const string BrokerageOrderId = "123456789";

    /// <summary>
    /// Regression test for issue #106: Lean applies a combo update to the group order manager shared by every
    /// leg and only hands the brokerage the leg whose ticket was updated. Waiting for the remaining legs (the way
    /// the place order path does) meant the replace request was never sent, so <see cref="TradeStationBrokerage.UpdateOrder"/>
    /// reported success while the order stayed at its original limit price at TradeStation.
    /// </summary>
    [Test]
    public void UpdatesComboOrderWhenOnlyOneLegIsUpdated()
    {
        var orderProvider = new OrderProvider();
        var comboOrders = CreateComboLimitOrderGroup(orderProvider, limitPrice: 1.5m);

        using var brokerage = CreateBrokerage(orderProvider, out var requests);

        var orderEvents = new List<OrderEvent>();
        brokerage.OrdersStatusChanged += (_, events) => orderEvents.AddRange(events);

        // Lean's own ComboLimitOrderAlgorithm updates a single leg's ticket: the new price reaches every leg
        // through the shared group order manager.
        var updatedLeg = comboOrders[0];
        updatedLeg.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, updatedLeg.Id, new() { LimitPrice = 2.25m }));

        Assert.IsTrue(brokerage.UpdateOrder(updatedLeg));

        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual(HttpMethod.Put, requests[0].Method);
        Assert.AreEqual($"/v3/orderexecution/orders/{BrokerageOrderId}", requests[0].Path);
        Assert.AreEqual("2.25", requests[0].Body["LimitPrice"]?.Value<string>());

        // Every leg of the combo must be reported as updated, not just the one whose ticket Lean pushed.
        CollectionAssert.AreEquivalent(comboOrders.Select(order => order.Id),
            orderEvents.Where(orderEvent => orderEvent.Status == OrderStatus.UpdateSubmitted).Select(orderEvent => orderEvent.OrderId));
    }

    /// <summary>
    /// A TradeStation order is leg keyed - a multi-leg order carries its size on each leg and the replace request
    /// has no leg data - so there is no single quantity to replace. Lean's TradeStationBrokerageModel refuses combo
    /// quantity updates as well, so the request must only carry the group level price.
    /// </summary>
    [Test]
    public void DoesNotSendAQuantityWhenReplacingAComboOrder()
    {
        var orderProvider = new OrderProvider();
        var comboOrders = CreateComboLimitOrderGroup(orderProvider, limitPrice: 1.5m);

        using var brokerage = CreateBrokerage(orderProvider, out var requests);

        comboOrders[0].ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, comboOrders[0].Id, new() { LimitPrice = 2.25m }));
        Assert.IsTrue(brokerage.UpdateOrder(comboOrders[0]));

        Assert.AreEqual(1, requests.Count);
        Assert.IsNull(requests[0].Body["Quantity"], $"The replace request should carry no quantity for a combo order: {requests[0].Body}");
    }

    /// <summary>
    /// A combo is a single TradeStation order, so an algorithm that updates every leg's ticket must still produce
    /// a single replace request: a repeated one would be acknowledged again and reported back to Lean as a new
    /// submission of the same order.
    /// </summary>
    [Test]
    public void ReplacesTheComboOrderOnceWhenEveryLegIsUpdated()
    {
        var orderProvider = new OrderProvider();
        var comboOrders = CreateComboLimitOrderGroup(orderProvider, limitPrice: 1.5m);

        using var brokerage = CreateBrokerage(orderProvider, out var requests);

        foreach (var comboOrder in comboOrders)
        {
            comboOrder.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, comboOrder.Id, new() { LimitPrice = 2.25m }));
            Assert.IsTrue(brokerage.UpdateOrder(comboOrder));
        }

        Assert.AreEqual(1, requests.Count);

        // A later, different price is a new update and must reach TradeStation.
        comboOrders[0].ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, comboOrders[0].Id, new() { LimitPrice = 3.5m }));
        Assert.IsTrue(brokerage.UpdateOrder(comboOrders[0]));

        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual("3.5", requests[1].Body["LimitPrice"]?.Value<string>());
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

        using var brokerage = CreateBrokerage(orderProvider, out var requests);

        limitOrder.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, limitOrder.Id, new() { LimitPrice = 210m }));
        Assert.IsTrue(brokerage.UpdateOrder(limitOrder));

        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual("10", requests[0].Body["Quantity"]?.Value<string>());
        Assert.AreEqual("210", requests[0].Body["LimitPrice"]?.Value<string>());
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
        var legs = new[]
        {
            (symbol: Symbol.CreateOption(underlying, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 220m, expiry), ratio: -1m),
            (symbol: Symbol.CreateOption(underlying, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 230m, expiry), ratio: 1m)
        };

        var groupOrderManager = new GroupOrderManager(1, legCount: legs.Length, quantity: 8, limitPrice: limitPrice);

        var comboOrders = new List<ComboLimitOrder>();
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
    /// Creates a brokerage whose api client is backed by a fake HTTP handler recording every request it is given.
    /// </summary>
    /// <param name="orderProvider">The order provider the brokerage resolves Lean orders from.</param>
    /// <param name="requests">The recorded requests.</param>
    /// <returns>The brokerage to drive.</returns>
    private static TradeStationBrokerageTest CreateBrokerage(OrderProvider orderProvider, out List<CapturedRequest> requests)
    {
        var capturedRequests = requests = [];

        var handler = new TestHttpMessageHandler(async (request, _) =>
        {
            var body = request.Content == null ? new JObject() : JObject.Parse(await request.Content.ReadAsStringAsync());
            capturedRequests.Add(new CapturedRequest(request.Method, request.RequestUri.AbsolutePath, body));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"Message\": \"Order replaced\", \"OrderID\": \"" + BrokerageOrderId + "\" }")
            };
        });

        var httpClient = new HttpClientRetryWrapper("https://api.test", handler, maxRetries: 1,
            ctsAttemptTimeout: TimeSpan.FromSeconds(10), backOffDelay: TimeSpan.Zero);

        var brokerage = new TradeStationBrokerageTest("client-id", "client-secret", "https://api.test", "http://localhost",
            string.Empty, "refresh-token", "Margin", orderProvider, securityProvider: null);
        brokerage.SetApiClient(new TradeStationApiClient(httpClient, accountId: "SIM123456M", messageReceived: null));
        return brokerage;
    }

    /// <summary>
    /// An HTTP request the brokerage sent to TradeStation.
    /// </summary>
    /// <param name="Method">The HTTP method used.</param>
    /// <param name="Path">The absolute path requested.</param>
    /// <param name="Body">The parsed JSON body, empty when the request carried none.</param>
    private record CapturedRequest(HttpMethod Method, string Path, JObject Body);
}
