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
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents a collection of option expirations in TradeStation.
/// </summary>
public readonly struct TradeStationOptionExpiration
{
    /// <summary>
    /// Gets the collection of expirations.
    /// </summary>
    public IEnumerable<Expiration> Expirations { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationOptionExpiration"/> struct.
    /// </summary>
    /// <param name="expirations">The collection of expirations.</param>
    [JsonConstructor]
    public TradeStationOptionExpiration(IEnumerable<Expiration> expirations) => Expirations = expirations;
}

/// <summary>
/// Represents a single option expiration.
/// </summary>
public readonly struct Expiration
{
    /// <summary>
    /// Gets the date of the expiration.
    /// </summary>
    public DateTime Date { get; }

    /// <summary>
    /// Gets the type of the expiration.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Expiration"/> struct.
    /// </summary>
    /// <param name="date">The date of the expiration.</param>
    /// <param name="type">The type of the expiration.</param>
    [JsonConstructor]
    public Expiration(DateTime date, string type) => (Date, Type) = (date, type); 
}
