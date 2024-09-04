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
    /// <item><description>BUYTOCOVER - for equities</description></item>
    /// <item><description>SELLSHORT - for equities</description></item>
    /// <item><description>BUYTOOPEN - for options</description></item>
    /// <item><description>BUYTOCLOSE - for options</description></item>
    /// <item><description>SELLTOOPEN - for options</description></item>
    /// <item><description>SELLTOCLOSE - for options</description></item>
    /// </list>
    /// </remarks>
    public string TradeAction { get; }

    /// <summary>
    /// Gets or sets the collection of order legs to be placed through TradeStation.
    /// </summary>
    public IReadOnlyCollection<TradeStationPlaceOrderLeg> Legs { get; }

    /// <summary>
    /// The limit price for this order.
    /// </summary>
    public string LimitPrice { get; set; }

    /// <summary>
    /// The stop price for this order.
    /// </summary>
    public string StopPrice { get; set; }

    /// <summary>
    /// The TradeStation Advanced Options of Orders
    /// </summary>
    public TradeStationAdvancedOptions? AdvancedOptions { get; set; } = null;

    /// <summary>
    /// The route of the order. For Stocks and Options, Route value will default to Intelligent if no value is set.
    /// </summary>
    public string Route { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationPlaceOrderRequest"/> class for single leg orders.
    /// </summary>
    /// <param name="accountID">The TradeStation Account ID.</param>
    /// <param name="orderType">The type of the order.</param>
    /// <param name="quantity">The quantity of the order.</param>
    /// <param name="symbol">The symbol used for this order.</param>
    /// <param name="timeInForce">The time in force for the order.</param>
    /// <param name="tradeAction">The trade action for the order.</param>
    [JsonConstructor]
    public TradeStationPlaceOrderRequest(
        string accountID,
        TradeStationOrderType orderType,
        string quantity,
        string symbol,
        TimeInForce timeInForce,
        string tradeAction,
        IReadOnlyCollection<TradeStationPlaceOrderLeg> legs = null,
        string limitPrice = null,
        string stopPrice = null,
        TradeStationAdvancedOptions? advancedOptions = null)
    {
        AccountID = accountID;
        OrderType = orderType;
        Quantity = quantity;
        Symbol = symbol;
        TimeInForce = timeInForce;
        TradeAction = tradeAction;
        Legs = legs;
        LimitPrice = limitPrice;
        StopPrice = stopPrice;
        AdvancedOptions = advancedOptions;
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

/// <summary>
/// Represents a single leg of an order to be placed through TradeStation.
/// </summary>
/// <remarks>
/// This struct encapsulates the necessary information for executing an individual leg of a combo order
/// on TradeStation. Each leg includes the quantity of the asset, the symbol representing the asset,
/// and the specific trade action to be performed.
/// </remarks>
public readonly struct TradeStationPlaceOrderLeg
{
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
    /// Represents the trade action for the order.
    /// </summary>
    /// <value>The trade action for the order.</value>
    /// <remarks>
    /// <para>Valid values:</para>
    /// <list type="bullet">
    /// <item><description>BUY - for equities and futures</description></item>
    /// <item><description>SELL - for equities and futures</description></item>
    /// <item><description>BUYTOCOVER - for equities</description></item>
    /// <item><description>SELLSHORT - for equities</description></item>
    /// <item><description>BUYTOOPEN - for options</description></item>
    /// <item><description>BUYTOCLOSE - for options</description></item>
    /// <item><description>SELLTOOPEN - for options</description></item>
    /// <item><description>SELLTOCLOSE - for options</description></item>
    /// </list>
    /// </remarks>
    public string TradeAction { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationPlaceOrderLeg"/> struct.
    /// </summary>
    /// <param name="quantity">The quantity of the asset to be traded.</param>
    /// <param name="symbol">The symbol for the asset.</param>
    /// <param name="tradeAction">The trade action to be executed.</param>
    [JsonConstructor]
    public TradeStationPlaceOrderLeg(string quantity, string symbol, string tradeAction) => (Quantity, Symbol, TradeAction) = (quantity, symbol, tradeAction);
}

/// <summary>
/// Represents advanced options for orders placed through TradeStation.
/// </summary>
public readonly struct TradeStationAdvancedOptions
{
    /// <summary>
    /// Gets a value indicating whether the "All or None" feature is enabled.
    /// </summary>
    /// <value>
    /// A boolean value that, when set to true, ensures the order will only be filled completely or not at all. 
    /// If set to false, the order can be partially filled. This option is applicable to both equities and options.
    /// </value>
    /// <remarks>
    /// The "All or None" feature is particularly useful when the trader wants to avoid partial executions, 
    /// which can result in multiple transactions and potentially higher fees.
    /// </remarks>
    public bool AllOrNone { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationAdvancedOptions"/> struct.
    /// </summary>
    /// <param name="allOrNone">Specifies whether to enable the "All or None" feature for the order.</param>
    public TradeStationAdvancedOptions(bool allOrNone)
    {
        AllOrNone = allOrNone;
    }
}
