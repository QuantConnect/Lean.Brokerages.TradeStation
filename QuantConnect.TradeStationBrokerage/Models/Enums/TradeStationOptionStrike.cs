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
using System.Text.Json.Serialization;

namespace QuantConnect.Brokerages.TradeStation.Models.Enums;

/// <summary>
/// Represents an option strike configuration for TradeStation with a specific spread type and a collection of strike prices.
/// </summary>
public readonly struct TradeStationOptionStrike
{
    /// <summary>
    /// Gets the type of the spread.
    /// </summary>
    public string SpreadType { get; }

    /// <summary>
    /// Gets the collection of strike prices. Each inner collection represents a set of strike prices.
    /// </summary>
    public IEnumerable<IEnumerable<decimal>> Strikes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationOptionStrike"/> struct with the specified spread type and strikes.
    /// </summary>
    /// <param name="spreadType">The type of the spread.</param>
    /// <param name="strikes">The collection of strike prices.</param>
    [JsonConstructor]
    public TradeStationOptionStrike(string spreadType, IEnumerable<IEnumerable<decimal>> strikes) => (SpreadType, Strikes) = (spreadType, strikes); 
}