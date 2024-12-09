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
using QuantConnect.Orders;
using QuantConnect.Interfaces;
using QuantConnect.Tests.Brokerages;

namespace QuantConnect.Brokerages.TradeStation.Tests;

public class CustomStopMarketOrderTestParameters : StopMarketOrderTestParameters
{
    public CustomStopMarketOrderTestParameters(Symbol symbol, decimal highLimit, decimal lowLimit, IOrderProperties properties = null, OrderSubmissionData orderSubmissionData = null) : base(symbol, highLimit, lowLimit, properties, orderSubmissionData)
    {
    }

    public override bool ModifyOrderToFill(IBrokerage brokerage, Order order, decimal lastMarketPrice)
    {
        var newStopPrice = default(decimal);
        var previousStop = (order as StopMarketOrder).StopPrice;
        if (order.Quantity > 0)
        {
            newStopPrice = lastMarketPrice + 0.01m;
        }
        else
        {
            newStopPrice = lastMarketPrice - 0.01m;
        }

        newStopPrice = order.SecurityType == SecurityType.IndexOption ? Round(newStopPrice) : newStopPrice;

        order.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, order.Id, new UpdateOrderFields() { StopPrice = newStopPrice }));

        return newStopPrice != previousStop;
    }

    private static decimal Round(decimal price, decimal increment = 0.05m)
    {
        return Math.Round(price / increment) * increment;
    }
}