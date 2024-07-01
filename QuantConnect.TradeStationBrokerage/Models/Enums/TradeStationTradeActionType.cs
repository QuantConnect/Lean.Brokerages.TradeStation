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

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace QuantConnect.Brokerages.TradeStation.Models.Enums;

/// <summary>
/// Represents the different trade action types available in TradeStation.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum TradeStationTradeActionType
{
    /// <summary>
    /// Buy action for equities and futures.
    /// </summary>
    [EnumMember(Value = "BUY")]
    Buy = 0,

    /// <summary>
    /// Buy to cover action for equities.
    /// </summary>
    [EnumMember(Value = "BUYTOCOVER")]
    BuyToCover = 1,

    /// <summary>
    /// Buy to close action for options.
    /// </summary>
    [EnumMember(Value = "BUYTOCLOSE")]
    BuyToClose = 2,

    /// <summary>
    /// Buy to open action for options.
    /// </summary>
    [EnumMember(Value = "BUYTOOPEN")]
    BuyToOpen = 3,

    /// <summary>
    /// Sell action for equities and futures.
    /// </summary>
    [EnumMember(Value = "SELL")]
    Sell = 4,

    /// <summary>
    /// Sell short action for equities.
    /// </summary>
    [EnumMember(Value = "SELLSHORT")]
    SellShort = 5,

    /// <summary>
    /// Sell to open action for options.
    /// </summary>
    [EnumMember(Value = "SELLTOOPEN")]
    SellToOpen = 6,

    /// <summary>
    /// Sell to close action for options.
    /// </summary>
    [EnumMember(Value = "SELLTOCLOSE")]
    SellToClose = 7,
}
