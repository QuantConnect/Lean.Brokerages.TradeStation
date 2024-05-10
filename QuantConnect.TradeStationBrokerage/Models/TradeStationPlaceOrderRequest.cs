﻿/*
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

using System.Text.Json.Serialization;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents an order placement request for TradeStation.
/// </summary>
public class TradeStationPlaceOrderRequest
{
    /// <summary>
    /// TradeStation Account ID.
    /// </summary>
    /// <value>The TradeStation Account ID.</value>
    public string AccountID { get; }

    /// <summary>
    /// Gets the type of the order.
    /// </summary>
    /// <value>The type of the order.</value>
    /// <seealso cref="TradeStationOrderType"/>
    public TradeStationOrderType OrderType { get; }

    /// <summary>
    /// The quantity of the order.
    /// </summary>
    /// <value>The quantity of the order.</value>
    public string Quantity { get; }

    /// <summary>
    /// The symbol used for this order.
    /// </summary>
    /// <value>The symbol used for this order.</value>
    public string Symbol { get; }

    /// <summary>
    /// Gets the time in force for the order.
    /// </summary>
    /// <value>The time in force for the order.</value>
    /// <seealso cref="Models.TimeInForce"/>
    public TimeInForce TimeInForce { get; }

    /// <summary>
    /// Represents the trade action for the order.
    /// </summary>
    /// <value>The trade action for the order.</value>
    /// <remarks>
    /// <para>Valid values:</para>
    /// <list type="bullet">
    /// <item><description>BUY - for equities and futures</description></item>
    /// <item><description>SELL - for equities and futures</description></item>
    /// </list>
    /// </remarks>
    public string TradeAction { get; }

    /// <summary>
    /// The limit price for this order.
    /// </summary>
    public string LimitPrice { get; set; }

    /// <summary>
    /// The stop price for this order.
    /// </summary>
    public string StopPrice { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationPlaceOrderRequest"/> struct.
    /// </summary>
    /// <param name="accountID">The TradeStation Account ID.</param>
    /// <param name="orderType">The type of the order.</param>
    /// <param name="quantity">The quantity of the order.</param>
    /// <param name="symbol">The symbol used for this order.</param>
    /// <param name="timeInForce">The time in force for the order.</param>
    /// <param name="tradeAction">The trade action for the order.</param>
    [JsonConstructor]
    public TradeStationPlaceOrderRequest(string accountID, TradeStationOrderType orderType, string quantity, string symbol, TimeInForce timeInForce,
        string tradeAction)
    {
        AccountID = accountID;
        OrderType = orderType;
        Quantity = quantity;
        Symbol = symbol;
        TimeInForce = timeInForce;
        TradeAction = tradeAction;
    }
}

/// <summary>
/// TimeInForce defines the duration and expiration timestamp.
/// </summary>
public readonly struct TimeInForce
{
    /// <summary>
    /// The length of time for which an order will remain valid in the market.
    /// Available values are: DAY, DYP, GTC, GCP, GTD, GDP, OPG, CLO, IOC, FOK, 1, 3, and 5.
    /// Different asset classes and routes may have restrictions on the durations they accept.
    /// </summary>
    public string Duration { get; }

    /// <summary>
    /// Timestamp represented as an RFC3339 formatted date, a profile of the ISO 8601 date standard.
    /// Only applicable to GTD and GDP orders. The full timestamp is required, but only the date portion is relevant.
    /// </summary>
    /// <example>2023-01-01T23:30:30Z.</example>
    public string Expiration { get; }

    [JsonConstructor]
    public TimeInForce(string duration, string expiration)
    {
        Duration = duration;
        Expiration = expiration;
    }
}
