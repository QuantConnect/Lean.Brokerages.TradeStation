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
using QuantConnect.Brokerages.TradeStation.Models.Interfaces;

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents a response from TradeStation after placing an order.
/// </summary>
public readonly struct TradeStationPlaceOrderResponse : ITradeStationError
{
    /// <summary>
    /// Represents an error that occurred during the retrieval of trading account information.
    /// </summary>
    public List<TradeStationError> Errors { get; }

    /// <summary>
    /// Gets the collection of orders that were placed successfully.
    /// </summary>
    public List<OrderResponse> Orders { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationPlaceOrderResponse"/> struct.
    /// </summary>
    /// <param name="errors">The collection of errors occurred during the order placement process.</param>
    /// <param name="orders">The collection of orders that were placed successfully.</param>
    [JsonConstructor]
    public TradeStationPlaceOrderResponse(List<TradeStationError> errors, List<OrderResponse> orders)
    {
        Errors = errors;
        Orders = orders;
    }
}

/// <summary>
/// Represents a response for an individual order.
/// </summary>
public class OrderResponse
{
    /// <summary>
    /// The order message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the ID of the order.
    /// </summary>
    public string OrderID { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderResponse"/> struct.
    /// </summary>
    /// <param name="message">The order message, if any, related to the order.</param>
    /// <param name="orderID">The ID of the order.</param>
    [JsonConstructor]
    public OrderResponse(string message, string orderID)
    {
        Message = message;
        OrderID = orderID;
    }
}
