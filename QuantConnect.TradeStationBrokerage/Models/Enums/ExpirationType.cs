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

namespace QuantConnect.Brokerages.TradeStation.Models.Enums;

/// <summary>
/// Represents the different types of expiration cycles for financial instruments.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum ExpirationType
{
    /// <summary>
    /// Represents an expiration type that does not fit into standard categories.
    /// </summary>
    Other = 0,

    /// <summary>
    /// Represents a weekly expiration cycle.
    /// </summary>
    Weekly = 1,

    /// <summary>
    /// Represents a monthly expiration cycle.
    /// </summary>
    Monthly = 2,

    /// <summary>
    /// Represents a quarterly expiration cycle.
    /// </summary>
    Quarterly = 3,

    /// <summary>
    /// Represents an End-of-Month (EOM) expiration cycle.
    /// </summary>
    EOM = 4
}
