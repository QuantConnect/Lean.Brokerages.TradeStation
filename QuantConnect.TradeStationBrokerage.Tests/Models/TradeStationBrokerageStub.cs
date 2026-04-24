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
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Securities;

namespace QuantConnect.Brokerages.TradeStation.Tests;

/// <summary>
/// Test-only brokerage subclass that stubs Lean-facing broker actions without hitting the
/// TradeStation REST API, and exposes helpers for seeding internal state used by the stream
/// event path. Extend this class (rather than <see cref="TradeStationBrokerageTest"/>) when a
/// test needs to drive lifecycle flows locally.
/// </summary>
public class TradeStationBrokerageStub : TradeStationBrokerageTest
{
    public TradeStationBrokerageStub(string apiKey, string apiKeySecret, string restApiUrl, string redirectUrl,
        string authorizationCode, string refreshToken, string accountType, IOrderProvider orderProvider, ISecurityProvider securityProvider, string accountId = "")
        : base(apiKey, apiKeySecret, restApiUrl, redirectUrl, authorizationCode, refreshToken, accountType, orderProvider, securityProvider, accountId)
    { }

    /// <summary>
    /// Locally replays Lean's PlaceOrder side effects without issuing a REST call: populates
    /// <c>_skipWebSocketUpdatesForLeanOrders</c> so the initial post-PlaceOrder ACK stream
    /// frame is drained at the outer switch, and emits the Submitted order event. Assumes
    /// the order's <see cref="Order.BrokerId"/> has already been populated by the test.
    /// </summary>
    public override bool PlaceOrder(Order order)
    {
        var brokerageOrderId = order.BrokerId.Last();
        _skipWebSocketUpdatesForLeanOrders[brokerageOrderId] = true;
        OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, "Stub")
        {
            Status = OrderStatus.Submitted
        });
        return true;
    }

    /// <summary>
    /// Locally replays Lean's UpdateOrder side effects without issuing a REST call: populates
    /// <c>_skipWebSocketUpdatesForLeanOrders</c> so the next ACK (which may carry a fill) is
    /// drained at the outer switch, and emits the UpdateSubmitted order event.
    /// </summary>
    public override bool UpdateOrder(Order order)
    {
        var brokerageOrderId = order.BrokerId.Last();
        _skipWebSocketUpdatesForLeanOrders[brokerageOrderId] = true;
        OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, $"Stub")
        {
            Status = OrderStatus.UpdateSubmitted
        });
        return true;
    }
}
