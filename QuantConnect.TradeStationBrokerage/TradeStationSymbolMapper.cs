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

namespace QuantConnect.Brokerages.TradeStation;

/// <summary>
/// Provides the mapping between Lean symbols and brokerage specific symbols.
/// </summary>
public class TradeStationSymbolMapper : ISymbolMapper
{
    /// <inheritdoc cref="ISymbolMapper.GetBrokerageSymbol(Symbol)"/>
    public string GetBrokerageSymbol(Symbol symbol)
    {
        throw new NotImplementedException();
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
