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
/// Indicates the asset type of the position.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum TradeStationAssetType
{
    [EnumMember(Value = "UNKNOWN")]
    Unknown = 0,

    [EnumMember(Value = "STOCK")]
    Stock = 1,

    [EnumMember(Value = "STOCKOPTION")]
    StockOption = 2,

    [EnumMember(Value = "FUTURE")]
    Future = 3,

    [EnumMember(Value = "FUTUREOPTION")]
    FutureOption = 4,

    [EnumMember(Value = "FOREX")]
    Forex = 5,

    [EnumMember(Value = "CURRENCYOPTION")]
    CurrencyOption = 6,

    [EnumMember(Value = "INDEX")]
    Index = 7,

    [EnumMember(Value = "INDEXOPTION")]
    IndexOption = 8
}
