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
/// Represents the response for a replace order operation in TradeStation.
/// </summary>
public class TradeStationReplaceOrderResponse
{
    /// <summary>
    /// Represents an error that occurred during the retrieval of trading account information.
    /// </summary>
    public string Error { get; }

    /// <summary>
    /// The order message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the ID of the order.
    /// </summary>
    public string OrderID { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationReplaceOrderResponse"/> class.
    /// </summary>
    /// <param name="error">The error message, if any, encountered during the operation.</param>
    /// <param name="message">The informational message associated with the operation.</param>
    /// <param name="orderID">The unique identifier of the order.</param>
    [JsonConstructor]
    public TradeStationReplaceOrderResponse(string error, string message, string orderID)
    {
        Error = error;
        Message = message;
        OrderID = orderID;
    }
}
