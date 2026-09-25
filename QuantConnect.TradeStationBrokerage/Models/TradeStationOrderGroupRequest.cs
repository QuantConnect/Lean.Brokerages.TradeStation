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

using System.Collections.Generic;

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents a group of related orders: submitted as a group order or as the orders sent by
/// another order once it fills, an order sends order (OSO).
/// </summary>
public class TradeStationOrderGroupRequest
{
    /// <summary>
    /// The orders are independent of each other
    /// </summary>
    public const string Normal = "NORMAL";

    /// <summary>
    /// Order cancels order: if one of the orders is filled or partially filled the rest are canceled
    /// </summary>
    public const string OneCancelsOther = "OCO";

    /// <summary>
    /// Bracket: if one of the orders is filled or partially filled the rest are reduced by the same amount
    /// </summary>
    public const string Bracket = "BRK";

    /// <summary>
    /// The group order type. Valid values are: BRK, OCO, and NORMAL.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// The orders in the group
    /// </summary>
    public List<TradeStationPlaceOrderRequest> Orders { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationOrderGroupRequest"/> class.
    /// </summary>
    /// <param name="type">The group order type. Valid values are: BRK, OCO, and NORMAL.</param>
    /// <param name="orders">The orders in the group</param>
    public TradeStationOrderGroupRequest(string type, List<TradeStationPlaceOrderRequest> orders)
    {
        Type = type;
        Orders = orders;
    }
}
