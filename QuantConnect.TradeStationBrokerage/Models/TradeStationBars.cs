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

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents a collection of TradeStation bars.
/// </summary>
public readonly struct TradeStationBars
{
    /// <summary>
    /// Gets the collection of TradeStation bars.
    /// </summary>
    public readonly IEnumerable<TradeStationBar> Bars { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationBars"/> struct.
    /// </summary>
    /// <param name="bars">The collection of TradeStation bars.</param>
    public TradeStationBars(IEnumerable<TradeStationBar> bars) => Bars = bars;
}

/// <summary>
/// Represents a single bar of financial data.
/// </summary>
public readonly struct TradeStationBar
{
    /// <summary>
    /// The high price of the current bar.
    /// </summary>
    public readonly decimal High { get; }

    /// <summary>
    /// The low price of the current bar.
    /// </summary>
    public readonly decimal Low { get; }

    /// <summary>
    /// The open price of the current bar.
    /// </summary>
    public readonly decimal Open { get; }

    /// <summary>
    /// The close price of the current bar.
    /// </summary>
    public readonly decimal Close { get; }

    /// <summary>
    /// Timestamp represented as an RFC3339 formatted date.
    /// </summary>
    public readonly DateTime TimeStamp { get; }

    /// <summary>
    /// The sum of up volume and down volume.
    /// </summary>
    public readonly decimal TotalVolume { get; }

    /// <summary>
    /// A trade made at a price less than or equal to the previous trade price.
    /// </summary>
    public readonly ulong DownTicks { get; }

    /// <summary>
    /// Volume traded on downticks.
    /// </summary>
    public readonly ulong DownVolume { get; }

    /// <summary>
    /// Number of open contracts for Options or Futures.
    /// </summary>
    public readonly string OpenInterest { get; }

    /// <summary>
    /// Set when there is data in the bar and the data is being built in "real time".
    /// </summary>
    public readonly bool IsRealtime { get; }

    /// <summary>
    /// Conveys that all historical bars in the request have been delivered.
    /// </summary>
    public readonly bool IsEndOfHistory { get; }

    /// <summary>
    /// Total number of ticks (upticks and downticks together).
    /// </summary>
    public readonly ulong TotalTicks { get; }

    /// <summary>
    /// This field is deprecated and its value will always be zero.
    /// </summary>
    public readonly ulong UnchangedTicks { get; }

    /// <summary>
    /// This field is deprecated and its value will always be zero.
    /// </summary>
    public readonly ulong UnchangedVolume { get; }

    /// <summary>
    /// A trade made at a price greater than or equal to the previous trade price.
    /// </summary>
    public readonly ulong UpTicks { get; }

    /// <summary>
    /// Volume traded on upticks.
    /// </summary>
    public readonly ulong UpVolume { get; }

    /// <summary>
    /// The Epoch time.
    /// </summary>
    public readonly long Epoch { get; }

    /// <summary>
    /// Indicates if bar is Open or Closed.
    /// </summary>
    public readonly string BarStatus { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationBar"/> struct.
    /// </summary>
    public TradeStationBar(
        decimal high, decimal low, decimal open, decimal close, DateTime timeStamp,
        decimal totalVolume, ulong downTicks, ulong downVolume, string openInterest,
        bool isRealtime, bool isEndOfHistory, ulong totalTicks, ulong unchangedTicks,
        ulong unchangedVolume, ulong upTicks, ulong upVolume, long epoch, string barStatus)
    {
        High = high;
        Low = low;
        Open = open;
        Close = close;
        TimeStamp = timeStamp;
        TotalVolume = totalVolume;
        DownTicks = downTicks;
        DownVolume = downVolume;
        OpenInterest = openInterest;
        IsRealtime = isRealtime;
        IsEndOfHistory = isEndOfHistory;
        TotalTicks = totalTicks;
        UnchangedTicks = unchangedTicks;
        UnchangedVolume = unchangedVolume;
        UpTicks = upTicks;
        UpVolume = upVolume;
        Epoch = epoch;
        BarStatus = barStatus;
    }
}
