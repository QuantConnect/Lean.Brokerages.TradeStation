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
    public IEnumerable<Quote> Quotes { get; }

    /// <summary>
    /// Gets the collection of errors, if any.
    /// </summary>
    public IEnumerable<QuoteError> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationQuoteSnapshot"/> struct.
    /// </summary>
    /// <param name="quotes">The collection of quotes.</param>
    /// <param name="errors">The collection of errors, if any.</param>
    [JsonConstructor]
    public TradeStationQuoteSnapshot(IEnumerable<Quote> quotes, IEnumerable<QuoteError> errors)
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
public readonly struct Quote
{
    /// <summary>
    /// The price at which a security, futures contract, or other financial instrument is offered for sale.
    /// </summary>
    public decimal Ask { get; }

    /// <summary>
    /// The number of trading units that prospective sellers are prepared to sell.
    /// </summary>
    public decimal AskSize { get; }

    /// <summary>
    /// The highest price a prospective buyer is prepared to pay at a particular time for a trading unit of a given symbol.
    /// </summary>
    public decimal Bid { get; }

    /// <summary>
    /// The number of trading units that prospective buyers are prepared to purchase for a symbol.
    /// </summary>
    public decimal BidSize { get; }

    /// <summary>
    /// The closing price of the day.
    /// </summary>
    public decimal Close { get; }

    /// <summary>
    /// The total number of open or outstanding (not closed or delivered) options and/or futures contracts that exist on a given day, delivered on a particular day.
    /// </summary>
    public string DailyOpenInterest { get; }

    /// <summary>
    /// The highest price of the day.
    /// </summary>
    public decimal High { get; }

    /// <summary>
    /// The lowest price of the day.
    /// </summary>
    public decimal Low { get; }

    /// <summary>
    /// The highest price of the past 52 weeks.
    /// </summary>
    public decimal High52Week { get; }

    /// <summary>
    /// Date of the highest price in the past 52 week.
    /// </summary>
    public string High52WeekTimestamp { get; }

    /// <summary>
    /// The last price at which the symbol traded.
    /// </summary>
    public decimal Last { get; }

    /// <summary>
    /// The minimum price a commodity futures contract may be traded for the current session.
    /// </summary>
    public decimal MinPrice { get; }

    /// <summary>
    /// The maximum price a commodity futures contract may be traded for the current session.
    /// </summary>
    public decimal MaxPrice { get; }

    /// <summary>
    /// The day after which an investor who has purchased a futures contract may be required to take physical delivery of the contracts underlying commodity.
    /// </summary>
    public string FirstNoticeDate { get; }

    /// <summary>
    /// The final day that a futures contract may trade or be closed out before the delivery of the underlying asset or cash settlement must occur.
    /// </summary>
    public string LastTradingDate { get; }

    /// <summary>
    /// The lowest price of the past 52 weeks.
    /// </summary>
    public decimal Low52Week { get; }

    /// <summary>
    /// Date of the lowest price of the past 52 weeks.
    /// </summary>
    public string Low52WeekTimestamp { get; }

    /// <summary>
    /// Market specific information for a symbol.
    /// </summary>
    public MarketFlag MarketFlags { get; }

    /// <summary>
    /// The difference between the last displayed price and the previous day's close.
    /// </summary>
    public string NetChange { get; }

    /// <summary>
    /// The percentage difference between the current price and previous day's close, expressed as a percentage.
    /// For example, a price change from 100 to 103.5 would be expressed as "3.5".
    /// </summary>
    public string NetChangePct { get; }

    /// <summary>
    /// The opening price of the day.
    /// </summary>
    public decimal Open { get; }

    /// <summary>
    /// The closing price of the previous day.
    /// </summary>
    public decimal PreviousClose { get; }

    /// <summary>
    /// Daily volume of the previous day.
    /// </summary>
    public decimal PreviousVolume { get; }

    /// <summary>
    /// Restriction if any returns array.
    /// </summary>
    public string[] Restrictions { get; }

    /// <summary>
    /// The name identifying the financial instrument for which the data is displayed.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Trading increment based on a level group.
    /// </summary>
    public string TickSizeTier { get; }

    /// <summary>
    /// Time of the last trade.
    /// </summary>
    public DateTime TradeTime { get; }

    /// <summary>
    /// Daily volume in shares/contracts.
    /// </summary>
    public decimal Volume { get; }

    /// <summary>
    /// Number of contracts/shares last traded.
    /// </summary>
    public decimal LastSize { get; }

    /// <summary>
    /// Exchange name of last trade.
    /// </summary>
    public string LastVenue { get; }

    /// <summary>
    /// VWAP (Volume Weighted Average Price) is a measure of the price at which the majority of a given day's trading in a given security took place.
    /// It is calculated by adding the dollars traded for the average price of the bar throughout the day ("avgprice" x "number of shares traded" per bar) 
    /// and dividing by the total shares traded for the day. The VWAP is calculated throughout the day by the TradeStation data-network.
    /// </summary>
    public string VWAP { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Quote"/> struct.
    /// </summary>
    /// <param name="ask">The price at which a security, futures contract, or other financial instrument is offered for sale.</param>
    /// <param name="askSize">The number of trading units that prospective sellers are prepared to sell.</param>
    /// <param name="bid">The highest price a prospective buyer is prepared to pay at a particular time for a trading unit of a given symbol.</param>
    /// <param name="bidSize">The number of trading units that prospective buyers are prepared to purchase for a symbol.</param>
    /// <param name="close">The closing price of the day.</param>
    /// <param name="dailyOpenInterest">The total number of open or outstanding options and/or futures contracts that exist on a given day, delivered on a particular day.</param>
    /// <param name="high">The highest price of the day.</param>
    /// <param name="low">The lowest price of the day.</param>
    /// <param name="high52Week">The highest price of the past 52 weeks.</param>
    /// <param name="high52WeekTimestamp">Date of the highest price in the past 52 weeks.</param>
    /// <param name="last">The last price at which the symbol traded.</param>
    /// <param name="minPrice">The minimum price a commodity futures contract may be traded for the current session.</param>
    /// <param name="maxPrice">The maximum price a commodity futures contract may be traded for the current session.</param>
    /// <param name="firstNoticeDate">The day after which an investor who has purchased a futures contract may be required to take physical delivery of the contracts underlying commodity.</param>
    /// <param name="lastTradingDate">The final day that a futures contract may trade or be closed out before the delivery of the underlying asset or cash settlement must occur.</param>
    /// <param name="low52Week">The lowest price of the past 52 weeks.</param>
    /// <param name="low52WeekTimestamp">Date of the lowest price of the past 52 weeks.</param>
    /// <param name="marketFlags">Flags indicating various market conditions.</param>
    /// <param name="netChange">The difference between the last displayed price and the previous day's close.</param>
    /// <param name="netChangePct">The percentage difference between the current price and previous day's close, expressed as a percentage.</param>
    /// <param name="open">The opening price of the day.</param>
    /// <param name="previousClose">The closing price of the previous day.</param>
    /// <param name="previousVolume">Daily volume of the previous day.</param>
    /// <param name="restrictions">Restrictions applicable to the quote.</param>
    /// <param name="symbol">The name identifying the financial instrument for which the data is displayed.</param>
    /// <param name="tickSizeTier">Trading increment based on a level group.</param>
    /// <param name="tradeTime">Time of the last trade.</param>
    /// <param name="volume">Daily volume in shares/contracts.</param>
    /// <param name="lastSize">Number of contracts/shares last traded.</param>
    /// <param name="lastVenue">Exchange name of last trade.</param>
    /// <param name="VWAP">VWAP (Volume Weighted Average Price) is a measure of the price at which the majority of a given day's trading in a given security took place.</param>
    [JsonConstructor]
    public Quote(decimal ask, decimal askSize, decimal bid, decimal bidSize, decimal close, string dailyOpenInterest, decimal high, decimal low,
        decimal high52Week, string high52WeekTimestamp, decimal last, decimal minPrice, decimal maxPrice, string firstNoticeDate, string lastTradingDate,
        decimal low52Week, string low52WeekTimestamp, MarketFlag marketFlags, string netChange, string netChangePct, decimal open, decimal previousClose,
        decimal previousVolume, string[] restrictions, string symbol, string tickSizeTier, DateTime tradeTime, decimal volume, decimal lastSize,
        string lastVenue, string VWAP)
    {
        Ask = ask;
        AskSize = askSize;
        Bid = bid;
        BidSize = bidSize;
        Close = close;
        DailyOpenInterest = dailyOpenInterest;
        High = high;
        Low = low;
        High52Week = high52Week;
        High52WeekTimestamp = high52WeekTimestamp;
        Last = last;
        MinPrice = minPrice;
        MaxPrice = maxPrice;
        FirstNoticeDate = firstNoticeDate;
        LastTradingDate = lastTradingDate;
        Low52Week = low52Week;
        Low52WeekTimestamp = low52WeekTimestamp;
        MarketFlags = marketFlags;
        NetChange = netChange;
        NetChangePct = netChangePct;
        Open = open;
        PreviousClose = previousClose;
        PreviousVolume = previousVolume;
        Restrictions = restrictions;
        Symbol = symbol;
        TickSizeTier = tickSizeTier;
        TradeTime = tradeTime;
        Volume = volume;
        LastSize = lastSize;
        LastVenue = lastVenue;
        this.VWAP = VWAP;
    }

    public override string ToString()
    {
        return $"{Symbol}: Ask={Ask} (Size={AskSize}), Bid={Bid} (Size={BidSize}), Open={Open}, High={High}, Low={Low}, Close={Close}, Last Price={Last} (Size={LastSize}), Trade Time={TradeTime}, Daily Open Interest={DailyOpenInterest}";
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
    public bool IsDelayed { get; }

    /// <summary>
    /// Is halted.
    /// </summary>
    public bool IsHalted { get; }

    /// <summary>
    /// Is hard to borrow.
    /// </summary>
    public bool IsHardToBorrow { get; }
}