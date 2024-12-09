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
using static QLNet.Callability;

namespace QuantConnect.Brokerages.TradeStation.Tests;

public class CustomStopLimitOrderTestParameters : StopLimitOrderTestParameters
{
    public CustomStopLimitOrderTestParameters(Symbol symbol, decimal highLimit, decimal lowLimit, IOrderProperties properties = null, OrderSubmissionData orderSubmissionData = null) : base(symbol, highLimit, lowLimit, properties, orderSubmissionData)
    {
    }

    public override bool ModifyOrderToFill(IBrokerage brokerage, Order order, decimal lastMarketPrice)
    {
        var symbolProperties = SPDB.GetSymbolProperties(order.Symbol.ID.Market, order.Symbol, order.SecurityType, order.PriceCurrency);
        var roundOffPlaces = symbolProperties.MinimumPriceVariation.GetDecimalPlaces();

        var newStopPrice = default(decimal);
        var newLimitPrice = default(decimal);
        var previousStopPrice = (order as StopLimitOrder).StopPrice;
        if (order.Quantity > 0)
        {

            // Invalid Stop Price - Stop Price must be above current market.
            newStopPrice = lastMarketPrice + 0.02m;
            // Invalid Limit Price - Limit Price must be at or above Stop Price.
            newLimitPrice = newStopPrice + 0.03m;
        }
        else
        {
            // for stop sells we need to increase the stop price
            newStopPrice = lastMarketPrice - 0.02m;
            newLimitPrice = newStopPrice - 0.03m;
        }

        newLimitPrice = order.SecurityType == SecurityType.IndexOption ? Round(newLimitPrice) : newLimitPrice;
        newStopPrice = order.SecurityType == SecurityType.IndexOption ? Round(newStopPrice) : newStopPrice;

        order.ApplyUpdateOrderRequest(
            new UpdateOrderRequest(
                DateTime.UtcNow,
                order.Id,
                new UpdateOrderFields()
                {
                    LimitPrice = newLimitPrice,
                    StopPrice = newStopPrice
                }));

        return newStopPrice != previousStopPrice;
    }

    private static decimal Round(decimal price, decimal increment = 0.05m)
    {
        return Math.Round(price / increment) * increment;
    }
}
