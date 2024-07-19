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
using Newtonsoft.Json;
using System.Collections.Generic;
using Newtonsoft.Json.Converters;
using QuantConnect.Brokerages.TradeStation.Models.Enums;
using QuantConnect.Brokerages.TradeStation.Models.Interfaces;

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents a collection of positions retrieved from TradeStation, along with any associated errors.
/// </summary>
public readonly struct TradeStationPosition : ITradeStationError
{
    /// <summary>
    /// Gets the collection of positions.
    /// </summary>
    [JsonProperty("Positions")]
    public List<Position> Positions { get; }

    /// <summary>
    /// Represents an error that occurred during the retrieval of trading account information.
    /// </summary>
    [JsonProperty("Errors")]
    public List<TradeStationError> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationPosition"/> struct.
    /// </summary>
    /// <param name="positions">Enumerable collection of positions.</param>
    /// <param name="errors">Enumerable collection of errors related to the positions.</param>
    [JsonConstructor]
    public TradeStationPosition(List<Position> positions, List<TradeStationError> errors)
    {
        Positions = positions;
        Errors = errors;
    }
}

/// <summary>
/// Represents a position in a trading account.
/// </summary>
public readonly struct Position
{
    /// <summary>
    /// TradeStation Account ID.
    /// </summary>
    public string AccountID { get; }

    /// <summary>
    /// The average price of the position currently held.
    /// </summary>
    public decimal AveragePrice { get; }

    /// <summary>
    /// Indicates the asset type of the position.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public TradeStationAssetType AssetType { get; }

    /// <summary>
    /// The last price at which the symbol traded.
    /// </summary>
    public decimal Last { get; }

    /// <summary>
    /// The highest price a prospective buyer is prepared to pay at a particular time for a trading unit of a given symbol.
    /// </summary>
    public decimal Bid { get; }

    /// <summary>
    /// The price at which a security, futures contract, or other financial instrument is offered for sale.
    /// </summary>
    public decimal Ask { get; }

    /// <summary>
    /// The UTC formatted expiration date of the future or option symbol, in the country the contract is traded in. 
    /// The time portion of the value should be ignored.
    /// </summary>
    public DateTime ExpirationDate { get; }

    /// <summary>
    /// The currency conversion rate that is used in order to convert from the currency of the symbol to the currency of the account.
    /// </summary>
    public int ConversionRate { get; }

    /// <summary>
    /// (Futures) DayTradeMargin used on open positions. 
    /// Currently only calculated for futures positions. 
    /// Other asset classes will have a 0 for this value.
    /// </summary>
    public decimal DayTradeRequirement { get; }

    /// <summary>
    /// Only applies to future and option positions. 
    /// The margin account balance denominated in the symbol currency required for entering a position on margin.
    /// </summary>
    public string InitialRequirement { get; }

    /// <summary>
    /// A unique identifier for the position.
    /// </summary>
    public string PositionID { get; }

    /// <summary>
    /// Specifies if the position is Long or Short.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public TradeStationPositionDirection LongShort { get; }

    /// <summary>
    /// The number of shares or contracts for a particular position. This value is negative for short positions.
    /// </summary>
    public int Quantity { get; }

    /// <summary>
    /// Symbol of the position.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Time the position was entered.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// The total cost denominated in the account currency of the open position.
    /// </summary>
    public decimal TotalCost { get; }

    /// <summary>
    /// The actual market value denominated in the symbol currency of the open position. This value is updated in real-time.
    /// </summary>
    public decimal MarketValue { get; }

    /// <summary>
    /// The unrealized profit or loss denominated in the symbol currency on the position held, calculated based on the average price of the position.
    /// </summary>
    public decimal UnrealizedProfitLoss { get; }

    /// <summary>
    /// The unrealized profit or loss on the position expressed as a percentage of the initial value of the position.
    /// </summary>
    public decimal UnrealizedProfitLossPercent { get; }

    /// <summary>
    /// The unrealized profit or loss denominated in the account currency divided by the number of shares, contracts or units held.
    /// </summary>
    public decimal UnrealizedProfitLossQty { get; }

    /// <summary>
    /// Only applies to equity and option positions. 
    /// This value will be included in the payload to convey the unrealized profit or loss denominated in the account currency on the position held, 
    /// calculated using the <see cref="MarkToMarketPrice"/>.
    /// </summary>
    public decimal TodaysProfitLoss { get; }

    /// <summary>
    /// Only applies to equity and option positions. 
    /// The MarkToMarketPrice value is the weighted average of the previous close price for the position quantity held overnight and 
    /// the purchase price of the position quantity opened during the current market session.
    /// This value is used to calculate <see cref="TodaysProfitLoss"/>.
    /// </summary>
    public decimal MarkToMarketPrice { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Position"/> struct.
    /// </summary>
    /// <param name="accountID">The TradeStation Account ID.</param>
    /// <param name="averagePrice">The average price of the position currently held.</param>
    /// <param name="assetType">Indicates the asset type of the position.</param>
    /// <param name="last">The last price at which the symbol traded.</param>
    /// <param name="bid">The highest price a prospective buyer is prepared to pay at a particular time for a trading unit of a given symbol.</param>
    /// <param name="ask">The price at which a security, futures contract, or other financial instrument is offered for sale.</param>
    /// <param name="expirationDate">The UTC formatted expiration date of the future or option symbol, in the country the contract is traded in.</param>
    /// <param name="conversionRate">The currency conversion rate that is used in order to convert from the currency of the symbol to the currency of the account.</param>
    /// <param name="dayTradeRequirement">(Futures) DayTradeMargin used on open positions. Currently only calculated for futures positions. Other asset classes will have a 0 for this value.</param>
    /// <param name="initialRequirement">Only applies to future and option positions. The margin account balance denominated in the symbol currency required for entering a position on margin.</param>
    /// <param name="positionID">A unique identifier for the position.</param>
    /// <param name="longShort">Specifies if the position is Long or Short.</param>
    /// <param name="quantity">The number of shares or contracts for a particular position. This value is negative for short positions.</param>
    /// <param name="symbol">Symbol of the position.</param>
    /// <param name="timestamp">Time the position was entered.</param>
    /// <param name="totalCost">The total cost denominated in the account currency of the open position.</param>
    /// <param name="marketValue">The actual market value denominated in the symbol currency of the open position. This value is updated in real-time.</param>
    /// <param name="unrealizedProfitLoss">The unrealized profit or loss denominated in the symbol currency on the position held, calculated based on the average price of the position.</param>
    /// <param name="unrealizedProfitLossPercent">The unrealized profit or loss on the position expressed as a percentage of the initial value of the position.</param>
    /// <param name="unrealizedProfitLossQty">The unrealized profit or loss denominated in the account currency divided by the number of shares, contracts or units held.</param>
    /// <param name="todaysProfitLoss">This value will be included in the payload to convey the unrealized profit or loss denominated in the account currency on the position held</param>
    /// <param name="markToMarketPrice">The MarkToMarketPrice value is the weighted average of the previous close price for the position quantity</param>
    [JsonConstructor]
    public Position(string accountID, decimal averagePrice, TradeStationAssetType assetType, decimal last, decimal bid, decimal ask, DateTime expirationDate,
        int conversionRate, decimal dayTradeRequirement, string initialRequirement, string positionID, TradeStationPositionDirection longShort, int quantity,
        string symbol, DateTime timestamp, decimal totalCost, decimal marketValue, decimal unrealizedProfitLoss, decimal unrealizedProfitLossPercent,
        decimal unrealizedProfitLossQty, decimal todaysProfitLoss, decimal markToMarketPrice)
    {
        AccountID = accountID;
        AveragePrice = averagePrice;
        AssetType = assetType;
        Last = last;
        Bid = bid;
        Ask = ask;
        ExpirationDate = expirationDate;
        ConversionRate = conversionRate;
        DayTradeRequirement = dayTradeRequirement;
        InitialRequirement = initialRequirement;
        PositionID = positionID;
        LongShort = longShort;
        Quantity = quantity;
        Symbol = symbol;
        Timestamp = timestamp;
        TotalCost = totalCost;
        MarketValue = marketValue;
        UnrealizedProfitLoss = unrealizedProfitLoss;
        UnrealizedProfitLossPercent = unrealizedProfitLossPercent;
        UnrealizedProfitLossQty = unrealizedProfitLossQty;
        TodaysProfitLoss = todaysProfitLoss;
        MarkToMarketPrice = markToMarketPrice;
    }
}
