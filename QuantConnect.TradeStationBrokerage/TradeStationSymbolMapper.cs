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

namespace QuantConnect.Brokerages.TradeStation;

/// <summary>
/// Provides the mapping between Lean symbols and brokerage specific symbols.
/// </summary>
public class TradeStationSymbolMapper : ISymbolMapper
{
    /// <summary>
    /// Represents a set of supported security types.
    /// </summary>
    /// <remarks>
    /// This HashSet contains the supported security types that are allowed within the system.
    /// </remarks>
    public readonly HashSet<SecurityType> SupportedSecurityType = new (){ SecurityType.Equity, SecurityType.Option, SecurityType.Future };

    /// <inheritdoc cref="ISymbolMapper.GetBrokerageSymbol(Symbol)"/>
    public string GetBrokerageSymbol(Symbol symbol)
    {
        switch (symbol.SecurityType)
        {
            case SecurityType.Equity:
                return symbol.Value;
            case SecurityType.Option:
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
        return $"{symbol.ID.Symbol}{SymbolRepresentation.FuturesMonthLookup[symbol.ID.Date.Month]}{symbol.ID.Date.ToString("yy")}";
    }

    /// <summary>
    /// Generates a brokerage option string based on the Lean symbol.
    /// </summary>
    /// <param name="symbol">The symbol object containing information about the option.</param>
    /// <returns>A string representing the brokerage option.</returns>
    /// <example>{AAPL 240510C167.5}</example>
    private string GenerateBrokerageOption(Symbol symbol)
    {
        return $"{symbol.Underlying.Value} {symbol.ID.Date.ToString("yyMMdd")}{symbol.ID.OptionRight.ToString()[0]}{symbol.ID.StrikePrice}";
    }

    /// <inheritdoc cref="ISymbolMapper.GetLeanSymbol(string, SecurityType, string, DateTime, decimal, OptionRight)"/>
    public Symbol GetLeanSymbol(string brokerageSymbol, SecurityType securityType, string market, DateTime expirationDate = default, decimal strike = 0, OptionRight optionRight = OptionRight.Call)
    {
        switch (securityType)
        {
            case SecurityType.Equity:
                return Symbol.Create(brokerageSymbol, SecurityType.Equity, market);
            case SecurityType.Option:
                var underlying = Symbol.Create(brokerageSymbol, SecurityType.Equity, market);
                return Symbol.CreateOption(underlying, underlying.ID.Market, SecurityType.Option.DefaultOptionStyle(), optionRight, strike, expirationDate);
            case SecurityType.Future:
                return Symbol.CreateFuture(brokerageSymbol, market, expirationDate);
            default:
                throw new NotImplementedException($"{nameof(TradeStationSymbolMapper)}.{nameof(GetLeanSymbol)}: " +
                    $"The security type '{securityType}' with brokerage symbol '{brokerageSymbol}' is not supported.");
        }
    }
}
