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

using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents a request to replace an order on TradeStation.
/// </summary>
public class TradeStationReplaceOrderRequest
{
    /// <summary>
    /// The limit price for this order.
    /// </summary>
    public string LimitPrice { get; set; }

    /// <summary>
    /// The stop price for this order.
    /// </summary>
    public string StopPrice { get; set; }

    /// <summary>
    /// The quantity of this order.
    /// </summary>
    public string Quantity { get; }

    /// <summary>
    /// The order type of this order. Order type can only be updated to <c>Market</c>.
    /// </summary>
    public TradeStationOrderType? OrderType { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationReplaceOrderRequest"/> class with the specified quantity.
    /// </summary>
    /// <param name="quantity">The quantity of the order.</param>
    /// <param name="orderType">The order type of this order.</param>
    public TradeStationReplaceOrderRequest(string quantity)
    {
        Quantity = quantity;
    }
}
