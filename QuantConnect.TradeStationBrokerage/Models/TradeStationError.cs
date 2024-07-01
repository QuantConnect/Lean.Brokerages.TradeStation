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

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents an error that occurred during the retrieval of trading account information.
/// </summary>
public readonly struct TradeStationError
{
    /// <summary>
    /// The AccountID of the error, may contain multiple Account IDs in comma separated format.
    /// </summary>
    public string AccountID { get; }

    /// <summary>
    /// The Error.
    /// </summary>
    public string Error { get; }

    /// <summary>
    /// The error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationError"/> struct.
    /// </summary>
    /// <param name="accountID">The ID of the account associated with the error.</param>
    /// <param name="error">The type of the error.</param>
    /// <param name="message">The error message.</param>
    [JsonConstructor]
    public TradeStationError(string accountID, string error, string message)
    {
        AccountID = accountID;
        Error = error;
        Message = message;
    }
}
