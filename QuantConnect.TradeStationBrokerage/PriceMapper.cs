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

using QuantConnect.Brokerages.TradeStation.Api;
using QuantConnect.Logging;
using QuantConnect.Securities;
using System;
using System.Collections.Generic;
using System.Threading;

namespace QuantConnect.Brokerages.TradeStation
{
    /// <summary>
    /// Translates prices between Lean's internal representation and TradeStation's brokerage format
    /// for futures contracts by applying a per-symbol price magnifier.
    /// </summary>
    /// <remarks>
    /// For non-futures symbols the price passes through unchanged. For futures the magnifier is
    /// derived first from <see cref="SymbolPropertiesDatabase"/>; if the database value is not
    /// meaningful (≤ 1) it is computed from live symbol details fetched via the TradeStation API,
    /// cross-checking the increment-derived and point-value-derived results and logging a warning
    /// when they disagree. Computed magnifiers are cached by canonical symbol.
    /// </remarks>
    public class PriceMapper
    {
        /// <summary>
        /// Shared symbol-properties database used to look up the price magnifier and minimum price
        /// variation for a given symbol.
        /// </summary>
        private static readonly SymbolPropertiesDatabase _spDb = SymbolPropertiesDatabase.FromDataFolder();

        /// <summary>
        /// TradeStation API client used to fetch live symbol details when the database magnifier is
        /// not meaningful (≤ 1).
        /// </summary>
        private readonly TradeStationApiClient _apiClient;

        /// <summary>
        /// Symbol mapper used to convert a Lean <see cref="Symbol"/> to a TradeStation brokerage
        /// ticker string before querying the API.
        /// </summary>
        private readonly TradeStationSymbolMapper _symbolMapper;

        /// <summary>
        /// Cache of resolved price magnifiers keyed by canonical symbol, populated on first access
        /// and reused on subsequent calls for the same contract root.
        /// </summary>
        private readonly Dictionary<Symbol, decimal> _priceMagnifierByCanonicalSymbol = [];

        /// <summary>
        /// Protects concurrent read/write access to <see cref="_priceMagnifierByCanonicalSymbol"/>.
        /// </summary>
        private readonly Lock _magnifierLock = new();

        /// <summary>
        /// Creates a price mapper backed by the supplied TradeStation API client and symbol mapper.
        /// </summary>
        /// <param name="apiClient">TradeStation API client for live symbol-detail lookups.</param>
        /// <param name="symbolMapper">Symbol mapper for converting Lean symbols to brokerage tickers.</param>
        public PriceMapper(TradeStationApiClient apiClient, TradeStationSymbolMapper symbolMapper)
        {
            _apiClient = apiClient;
            _symbolMapper = symbolMapper;
        }

        /// <summary>
        /// Converts a Lean price to the equivalent TradeStation brokerage price for the given symbol.
        /// </summary>
        /// <param name="symbol">The Lean symbol whose price magnifier should be applied.</param>
        /// <param name="price">The Lean-side price to convert.</param>
        /// <returns>
        /// The price scaled by the symbol's magnifier for futures; the original
        /// <paramref name="price"/> unchanged for all other security types.
        /// </returns>
        public decimal GetBrokeragePrice(Symbol symbol, decimal price)
        {
            if (symbol.SecurityType != SecurityType.Future)
            {
                return price;
            }

            return price * GetMagnifier(symbol);
        }

        /// <summary>
        /// Converts a TradeStation brokerage price to the equivalent Lean price for the given symbol.
        /// </summary>
        /// <param name="symbol">The Lean symbol whose price magnifier should be removed.</param>
        /// <param name="brokeragePrice">The brokerage-side price to convert.</param>
        /// <returns>
        /// The price divided by the symbol's magnifier for futures; the original
        /// <paramref name="brokeragePrice"/> unchanged for all other security types.
        /// </returns>
        public decimal GetLeanPrice(Symbol symbol, decimal brokeragePrice)
        {
            if (symbol.SecurityType != SecurityType.Future)
            {
                return brokeragePrice;
            }

            return brokeragePrice / GetMagnifier(symbol);
        }

        /// <summary>
        /// Returns the price magnifier for the given futures symbol, fetching and caching it on first
        /// access.
        /// </summary>
        /// <param name="symbol">The futures symbol to look up.</param>
        /// <returns>The price magnifier to multiply (brokerage) or divide (Lean) prices by.</returns>
        /// <remarks>
        /// Resolution order:
        /// <list type="number">
        ///   <item><description>Return the cached value if already computed for the canonical symbol.</description></item>
        ///   <item><description>Use <see cref="SymbolPropertiesDatabase"/> when its magnifier is greater than 1.</description></item>
        ///   <item><description>Otherwise fetch live symbol details from the TradeStation API and derive the
        ///   magnifier from both the price increment and the point value. A warning is logged when the two
        ///   derivations disagree by more than 0.000001; the increment-derived value is used.</description></item>
        /// </list>
        /// Thread-safe via <see cref="_magnifierLock"/>.
        /// </remarks>
        private decimal GetMagnifier(Symbol symbol)
        {
            lock (_magnifierLock)
            {
                if (_priceMagnifierByCanonicalSymbol.TryGetValue(symbol.Canonical, out var priceMagnifier))
                {
                    return priceMagnifier;
                }

                var properties = _spDb.GetSymbolProperties(symbol.ID.Market, symbol, symbol.SecurityType, Currencies.USD);

                priceMagnifier = properties.PriceMagnifier;
                if (priceMagnifier <= 1)
                {
                    var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);
                    var symbolDetail = _apiClient.GetSymbolDetailsAsync(brokerageSymbol).SynchronouslyAwaitTaskResult();

                    var fromIncrement = symbolDetail.PriceFormat.Increment / properties.MinimumPriceVariation;
                    var fromPointValue = properties.ContractMultiplier / symbolDetail.PriceFormat.PointValue;

                    if (Math.Abs(fromIncrement - fromPointValue) > 0.000001m)
                    {
                        Log.Error($"{nameof(PriceMapper)}.{nameof(GetMagnifier)}: magnifier disagreement for {symbol} — " +
                            $"increment-derived={fromIncrement}, point-value-derived={fromPointValue}. " +
                            $"Using increment-derived; investigate symbol-properties-database.csv row.");
                    }

                    priceMagnifier = fromIncrement;
                }

                _priceMagnifierByCanonicalSymbol[symbol.Canonical] = priceMagnifier;
                return priceMagnifier;
            }
        }
    }
}
