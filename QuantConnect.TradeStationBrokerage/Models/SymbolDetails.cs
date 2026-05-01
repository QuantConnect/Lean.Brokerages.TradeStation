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

using QuantConnect.Brokerages.TradeStation.Models.Interfaces;
using System.Collections.Generic;

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Response wrapper for the <c>/v3/marketdata/symbols/{symbol}</c> endpoint.
/// </summary>
public class SymbolDetailsResponse : ITradeStationError
{
    /// <summary>
    /// Symbol metadata records returned for the requested ticker(s).
    /// </summary>
    public IReadOnlyList<SymbolDetails> Symbols { get; set; }

    /// <summary>
    /// Represents an error that occurred during the retrieval of trading account information.
    /// </summary>
    public List<TradeStationError> Errors { get; set; }
}

/// <summary>
/// Per-symbol metadata returned by TradeStation, carrying the price format used on the wire.
/// </summary>
public class SymbolDetails
{
    /// <summary>
    /// Wire-format description of the symbol's price (increment and point value).
    /// </summary>
    public PriceFormat PriceFormat { get; set; }
}

/// <summary>
/// Describes how TradeStation quotes prices for a given symbol on the wire.
/// </summary>
public class PriceFormat
{
    /// <summary>
    /// Minimum price increment in TradeStation wire units (e.g. <c>0.25</c> cents per bushel for Corn).
    /// </summary>
    public decimal Increment { get; set; }

    /// <summary>
    /// Dollar value of moving the price by <c>1.0</c> in TradeStation wire units
    /// (e.g. <c>50</c> USD per cent on a Corn contract).
    /// </summary>
    public decimal PointValue { get; set; }
}
