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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using QuantConnect.Brokerages.TradeStation.Models;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation.Converters;

public class TradeStationPositionConverter : JsonConverter<Position>
{
    /// <summary>
    /// Gets a value indicating whether this <see cref="JsonConverter"/> can write JSON.
    /// </summary>
    /// <value><c>true</c> if this <see cref="JsonConverter"/> can write JSON; otherwise, <c>false</c>.</value>
    public override bool CanWrite => false;

    /// <summary>
    /// Gets a value indicating whether this <see cref="JsonConverter"/> can read JSON.
    /// </summary>
    /// <value><c>true</c> if this <see cref="JsonConverter"/> can read JSON; otherwise, <c>false</c>.</value>
    public override bool CanRead => true;

    /// <summary>
    /// Reads the JSON representation of the object.
    /// </summary>
    /// <param name="reader">The <see cref="JsonReader"/> to read from.</param>
    /// <param name="objectType">Type of the object.</param>
    /// <param name="existingValue">The existing value of object being read.</param>
    /// <param name="hasExistingValue">The existing value has a value.</param>
    /// <param name="serializer">The calling serializer.</param>
    /// <returns>The object value.</returns>
    public override Position ReadJson(JsonReader reader, Type objectType, Position existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var token = JToken.Load(reader);
        if (token.Type != JTokenType.Object /*|| token.Count() != 20*/) throw new Exception($"{nameof(TradeStationPositionConverter)}.{nameof(ReadJson)}: Invalid token type or count. Expected a JSON array with exactly four elements.");

        return new Position(
            accountID: token["AccountID"].Value<string>(),
            averagePrice: token["AveragePrice"].Value<decimal>(),
            assetType: Enum.TryParse<TradeStationAssetType>(token["AssetType"].Value<string>(), true, out var val) ? val : default,
            last: token["Last"].Value<decimal>(),
            bid: token["Bid"].Value<decimal>(),
            ask: token["Ask"].Value<decimal>(),
            expirationDate: DateTime.Parse((token as JObject).TryGetPropertyValue<string>("ExpirationDate") ?? default(DateTime).ToString(), CultureInfo.InvariantCulture),
            conversionRate: token["ConversionRate"].Value<int>(),
            dayTradeRequirement: token["DayTradeRequirement"].Value<int>(),
            initialRequirement: token["InitialRequirement"].Value<int>(),
            positionID: token["PositionID"].Value<string>(),
            longShort: token["LongShort"].Value<string>(),
            quantity: token["Quantity"].Value<int>(),
            symbol: token["Symbol"].Value<string>(),
            timestamp: DateTime.Parse(token["Timestamp"].Value<string>(), CultureInfo.InvariantCulture),
            totalCost: token["TotalCost"].Value<decimal>(),
            marketValue: token["MarketValue"].Value<decimal>(),
            unrealizedProfitLoss: token["UnrealizedProfitLoss"].Value<decimal>(),
            unrealizedProfitLossPercent: token["UnrealizedProfitLossPercent"].Value<decimal>(),
            unrealizedProfitLossQty: token["UnrealizedProfitLossQty"].Value<decimal>(),
            todaysProfitLoss: (token as JObject).TryGetPropertyValue<decimal>("TodaysProfitLoss"),
            markToMarketPrice: (token as JObject).TryGetPropertyValue<decimal>("MarkToMarketPrice")
            );
    }

    /// <summary>
    /// Writes the JSON representation of the object.
    /// </summary>
    /// <param name="writer">The <see cref="JsonWriter"/> to write to.</param>
    /// <param name="value">The value.</param>
    /// <param name="serializer">The calling serializer.</param>
    public override void WriteJson(JsonWriter writer, Position value, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }
}
