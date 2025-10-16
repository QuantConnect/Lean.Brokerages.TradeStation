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
/// Represents a snapshot of quotes retrieved from TradeStation, along with any associated errors.
/// </summary>
public readonly struct TradeStationQuoteSnapshot
{
    /// <summary>
    /// Gets the collection of quotes.
    /// </summary>
    public List<Quote> Quotes { get; }

    /// <summary>
    /// Gets the collection of errors, if any.
    /// </summary>
    public List<QuoteError> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationQuoteSnapshot"/> struct.
    /// </summary>
    /// <param name="quotes">The collection of quotes.</param>
    /// <param name="errors">The collection of errors, if any.</param>
    [JsonConstructor]
    public TradeStationQuoteSnapshot(List<Quote> quotes, List<QuoteError> errors)
    {
        Quotes = quotes;
        Errors = errors;
    }
}

/// <summary>
/// Represents an error associated with a quote retrieval operation.
/// </summary>
public readonly struct QuoteError
{
    /// <summary>
    /// Gets the symbol for which the error occurred.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Gets the description of the error.
    /// </summary>
    public string Error { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="QuoteError"/> struct.
    /// </summary>
    /// <param name="symbol">The symbol for which the error occurred.</param>
    /// <param name="error">The description of the error.</param>
    [JsonConstructor]
    public QuoteError(string symbol, string error) => (Symbol, Error) = (symbol, error); 
}

/// <summary>
/// Represents a financial quote containing various data points related to a security, futures contract, or other financial instrument.
/// </summary>
public class Quote
{
    /// <summary>
    /// The price at which a security, futures contract, or other financial instrument is offered for sale.
    /// </summary>
    public decimal? Ask { get; init; }

    /// <summary>
    /// The number of trading units that prospective sellers are prepared to sell.
    /// </summary>
    public decimal? AskSize { get; init; }

    /// <summary>
    /// The highest price a prospective buyer is prepared to pay at a particular time for a trading unit of a given symbol.
    /// </summary>
    public decimal? Bid { get; init; }

    /// <summary>
    /// The number of trading units that prospective buyers are prepared to purchase for a symbol.
    /// </summary>
    public decimal? BidSize { get; init; }

    /// <summary>
    /// The total number of open or outstanding (not closed or delivered) options and/or futures contracts that exist on a given day, delivered on a particular day.
    /// </summary>
    public decimal? DailyOpenInterest { get; init; }

    /// <summary>
    /// The last price at which the symbol traded.
    /// </summary>
    public decimal? Last { get; init; }

    /// <summary>
    /// Market specific information for a symbol.
    /// </summary>
    public MarketFlag MarketFlags { get; init; }

    /// <summary>
    /// The name identifying the financial instrument for which the data is displayed.
    /// </summary>
    public string Symbol { get; init; }

    /// <summary>
    /// Time of the last trade.
    /// </summary>
    public DateTime? TradeTime { get; init; }

    /// <summary>
    /// Number of contracts/shares last traded.
    /// </summary>
    public decimal? LastSize { get; init; }

    public override string ToString()
    {
        return $"{Symbol}: Ask: {Ask}@{AskSize}, Bid: {Bid}@{BidSize}, Last Trade[{TradeTime}]: {Last}@{LastSize}, Daily Open Interest={DailyOpenInterest}";
    }
}

/// <summary>
/// Market specific information for a symbol.
/// </summary>
public readonly struct MarketFlag
{
    /// <summary>
    /// Is Bats.
    /// </summary>
    public bool IsBats { get; }

    /// <summary>
    /// Is delayed.
    /// </summary>
    public bool? IsDelayed { get; }

    /// <summary>
    /// Is halted.
    /// </summary>
    public bool IsHalted { get; }

    /// <summary>
    /// Is hard to borrow.
    /// </summary>
    public bool IsHardToBorrow { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MarketFlag"/> struct with the specified market flags.
    /// </summary>
    /// <param name="isBats">A value indicating whether the market is BATS.</param>
    /// <param name="isDelayed">A nullable value indicating whether the market data is delayed.</param>
    /// <param name="isHalted">A value indicating whether the market is halted.</param>
    /// <param name="isHardToBorrow">A value indicating whether the symbol is hard to borrow.</param>
    public MarketFlag(bool isBats, bool? isDelayed, bool isHalted, bool isHardToBorrow)
    {
        IsBats = isBats;
        IsDelayed = isDelayed;
        IsHalted = isHalted;
        IsHardToBorrow = isHardToBorrow;
    }
}