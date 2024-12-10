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
using QuantConnect.Securities;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using QuantConnect.Securities.IndexOption;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation;

/// <summary>
/// Provides the mapping between Lean symbols and brokerage specific symbols.
/// </summary>
public class TradeStationSymbolMapper : ISymbolMapper
{
    /// <summary>
    /// Regular expression pattern for parsing a position option symbol.
    /// </summary>
    private readonly string _optionPatternRegex = @"^(?<symbol>[A-Z]+)\s(?<expiryDate>\d{6})(?<optionRight>[CP])(?<strikePrice>\d+(\.\d+)?)$";

    /// <summary>
    /// Represents a set of supported security types.
    /// </summary>
    /// <remarks>
    /// This HashSet contains the supported security types that are allowed within the system.
    /// </remarks>
    public readonly HashSet<SecurityType> SupportedSecurityType = new() { SecurityType.Equity, SecurityType.Option, SecurityType.Future, SecurityType.Index, SecurityType.IndexOption };

    /// <summary>
    /// Converts a Lean symbol instance to a brokerage symbol
    /// </summary>
    /// <param name="symbol">A Lean symbol instance</param>
    /// <returns> The brokerage symbol</returns>
    /// <exception cref="NotImplementedException">The lean security type is not implemented.</exception>
    public string GetBrokerageSymbol(Symbol symbol)
    {
        switch (symbol.SecurityType)
        {
            case SecurityType.Equity:
                return symbol.Value;
            case SecurityType.Index:
                return "$" + symbol.Value + ".X";
            case SecurityType.Option:
            case SecurityType.IndexOption:
                return GenerateBrokerageOption(symbol);
            case SecurityType.Future:
                return GenerateBrokerageFuture(symbol);
            default:
                throw new NotImplementedException($"{nameof(TradeStationSymbolMapper)}.{nameof(GetBrokerageSymbol)}: " +
                    $"The security type '{symbol.SecurityType}' is not supported.");
        }
    }

    /// <summary>
    /// Generates a brokerage future string based on the provided symbol.
    /// </summary>
    /// <param name="symbol">The symbol object containing information about the future.</param>
    /// <returns>A string representing the brokerage future.</returns>
    /// <example>{ESZ24}</example>
    private string GenerateBrokerageFuture(Symbol symbol)
    {
        return $"{symbol.ID.Symbol}{SymbolRepresentation.FuturesMonthLookup[symbol.ID.Date.Month]}{symbol.ID.Date:yy}";
    }

    /// <summary>
    /// Generates a brokerage option string based on the Lean symbol.
    /// </summary>
    /// <param name="symbol">The symbol object containing information about the option.</param>
    /// <returns>A string representing the brokerage option.</returns>
    /// <example>{AAPL 240510C167.5}</example>
    private string GenerateBrokerageOption(Symbol symbol)
    {
        return $"{symbol.ID.Symbol} {symbol.ID.Date:yyMMdd}{symbol.ID.OptionRight.ToString()[0]}{symbol.ID.StrikePrice}";
    }

    /// <summary>
    /// Converts a brokerage symbol to a Lean symbol instance
    /// </summary>
    /// <param name="brokerageSymbol">The brokerage symbol</param>
    /// <param name="securityType">The security type</param>
    /// <param name="market">The market</param>
    /// <param name="expirationDate">Expiration date of the security(if applicable)</param>
    /// <param name="strike">The strike of the security (if applicable)</param>
    /// <param name="optionRight">The option right of the security (if applicable)</param>
    /// <returns>A new Lean Symbol instance</returns>
    /// <exception cref="NotImplementedException">The security type is not implemented or not supported.</exception>
    public Symbol GetLeanSymbol(string brokerageSymbol, SecurityType securityType, string market = Market.USA, DateTime expirationDate = default, decimal strike = 0, OptionRight optionRight = OptionRight.Call)
    {
        switch (securityType)
        {
            case SecurityType.Equity:
                return Symbol.Create(brokerageSymbol, SecurityType.Equity, market);
            case SecurityType.Index:
                return Symbol.Create(ConvertIndexBrokerageTickerInLeanTicker(brokerageSymbol), SecurityType.Index, market);
            case SecurityType.Option:
                var underlying = Symbol.Create(brokerageSymbol, SecurityType.Equity, market);
                return Symbol.CreateOption(underlying, underlying.ID.Market, SecurityType.Option.DefaultOptionStyle(), optionRight, strike, expirationDate);
            case SecurityType.IndexOption:
                return GetIndexOptionByBrokerageSymbol(brokerageSymbol, securityType, market, expirationDate, strike, optionRight);
            case SecurityType.Future:
                var parsedFutures = SymbolRepresentation.ParseFutureTicker(brokerageSymbol).Underlying;
                if (!SymbolPropertiesDatabase.FromDataFolder().TryGetMarket(parsedFutures, SecurityType.Future, out market))
                {
                    market = DefaultBrokerageModel.DefaultMarketMap[SecurityType.Future];
                }
                return Symbol.CreateFuture(parsedFutures, market, expirationDate);
            default:
                throw new NotImplementedException($"{nameof(TradeStationSymbolMapper)}.{nameof(GetLeanSymbol)}: " +
                    $"The security type '{securityType}' with brokerage symbol '{brokerageSymbol}' is not supported.");
        }
    }

    /// <summary>
    /// Parses a position option symbol into its components.
    /// </summary>
    /// <param name="optionSymbol">The option symbol to parse.</param>
    /// <returns>
    /// A tuple containing the parsed components of the option symbol:
    /// - symbol: The stock symbol.
    /// - expiryDate: The expiry date of the option.
    /// - optionRight: The option right (Call or Put).
    /// - strikePrice: The strike price of the option.
    /// </returns>
    /// <exception cref="FormatException">Thrown when the option symbol has an invalid format.</exception>
    public (string symbol, DateTime expiryDate, OptionRight optionRight, decimal strikePrice) ParsePositionOptionSymbol(string optionSymbol)
    {
        // Match the pattern against the option symbol
        var match = Regex.Match(optionSymbol, _optionPatternRegex);

        if (!match.Success)
        {
            throw new FormatException($"{nameof(TradeStationSymbolMapper)}.{nameof(ParsePositionOptionSymbol)}: Invalid option symbol format: {optionSymbol}");
        }

        // Extract matched groups
        var symbol = match.Groups["symbol"].Value;
        var expiryDate = DateTime.ParseExact(match.Groups["expiryDate"].Value, "yyMMdd", null);
        var optionRight = match.Groups["optionRight"].Value[0] switch
        {
            'C' => OptionRight.Call,
            'P' => OptionRight.Put,
            _ => throw new ArgumentException($"{nameof(TradeStationSymbolMapper)}.{nameof(ParsePositionOptionSymbol)}: Invalid option right '{match.Groups["optionRight"].Value[0]}'. Expected 'C' or 'P'.")
        };

        var strikePrice = decimal.Parse(match.Groups["strikePrice"].Value);

        return (symbol, expiryDate, optionRight, strikePrice);
    }

    /// <summary>
    /// Maps a brokerage index option symbol to a Lean <see cref="Symbol"/> object.
    /// </summary>
    /// <param name="brokerageSymbol">The brokerage-specific symbol for the index option, expected to start with '$'.</param>
    /// <param name="securityType">The type of security. Must be <see cref="SecurityType.IndexOption"/>.</param>
    /// <param name="market">The market in which the security is traded.</param>
    /// <param name="expirationDate">The expiration date of the option.</param>
    /// <param name="strike">The strike price of the option.</param>
    /// <param name="optionRight">The option type: <see cref="OptionRight.Call"/> or <see cref="OptionRight.Put"/>.</param>
    /// <returns>A Lean <see cref="Symbol"/> representing the index option.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="securityType"/> is not <see cref="SecurityType.IndexOption"/>.</exception>
    private Symbol GetIndexOptionByBrokerageSymbol(string brokerageSymbol, SecurityType securityType, string market, DateTime expirationDate, decimal strike, OptionRight optionRight)
    {
        if (securityType != SecurityType.IndexOption)
        {
            throw new ArgumentException($"{nameof(TradeStationSymbolMapper)}.{nameof(GetIndexOptionByBrokerageSymbol)}: Expected {SecurityType.IndexOption}, but received {securityType}.");
        }

        // Remove the leading '$' character from the brokerage symbol
        var leanTicker = ConvertIndexBrokerageTickerInLeanTicker(brokerageSymbol);

        var underlyingIndex = Symbol.Create(IndexOptionSymbol.MapToUnderlying(leanTicker), SecurityType.Index, market);
        return Symbol.CreateOption(underlyingIndex, leanTicker, underlyingIndex.ID.Market, SecurityType.IndexOption.DefaultOptionStyle(), optionRight, strike, expirationDate);
    }

    /// <summary>
    /// Converts an index brokerage ticker to a Lean-compatible ticker by removing specific characters.
    /// </summary>
    /// <param name="indexBrokerageTicker">The brokerage-specific ticker for an index, typically containing special characters like '$' and '.X'.</param>
    /// <returns>
    /// A Lean-compatible ticker string with the '$' and '.X' characters removed.
    /// </returns>
    /// <example>
    /// Input: "$RUTW.X"  
    /// Output: "RUTW"
    /// </example>
    private static string ConvertIndexBrokerageTickerInLeanTicker(string indexBrokerageTicker)
    {
        return indexBrokerageTicker.Replace("$", string.Empty).Replace(".X", string.Empty);
    }
}
