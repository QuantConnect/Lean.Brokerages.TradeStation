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

public class CustomLimitOrderTestParameters : LimitOrderTestParameters
{
    public CustomLimitOrderTestParameters(Symbol symbol, decimal highLimit, decimal lowLimit, IOrderProperties properties = null, OrderSubmissionData orderSubmissionData = null) : base(symbol, highLimit, lowLimit, properties, orderSubmissionData)
    {
    }

    public override bool ModifyOrderToFill(IBrokerage brokerage, Order order, decimal lastMarketPrice)
    {
        var symbolProperties = SPDB.GetSymbolProperties(order.Symbol.ID.Market, order.Symbol, order.SecurityType, order.PriceCurrency);
        var roundOffPlaces = symbolProperties.MinimumPriceVariation.GetDecimalPlaces();

        var price = default(decimal);
        if (order.Quantity > 0)
        {
            // for limit buys we need to increase the limit price
            price = Math.Round(lastMarketPrice * 1.02m, roundOffPlaces);
        }
        else
        {
            // for limit sells we need to decrease the limit price
            price = Math.Round(lastMarketPrice / 1.02m, roundOffPlaces);
        }

        price = order.SecurityType == SecurityType.IndexOption ? Round(price) : Round(lastMarketPrice);

        order.ApplyUpdateOrderRequest(new UpdateOrderRequest(DateTime.UtcNow, order.Id, new UpdateOrderFields() { LimitPrice = Math.Round(price, roundOffPlaces) }));

        return true;
    }

    private static decimal Round(decimal price, decimal increment = 0.05m)
    {
        return Math.Round(price / increment) * increment;
    }
}
