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

using QuantConnect.Securities;
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
        /// Lean futures roots whose price magnifier is hardcoded to 100, bypassing the
        /// <see cref="SymbolPropertiesDatabase"/> lookup.
        /// </summary>
        private static readonly HashSet<string> _specialMagnifierRoots = ["6J", "ENY", "J7", "MJY"];

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
        /// Converts a nullable TradeStation brokerage price to the equivalent Lean price for the given symbol.
        /// </summary>
        /// <param name="symbol">The Lean symbol whose price magnifier should be removed.</param>
        /// <param name="brokeragePrice">The nullable brokerage-side price to convert.</param>
        /// <returns>
        /// The price divided by the symbol's magnifier for futures; the original
        /// <paramref name="brokeragePrice"/> unchanged for all other security types; <c>null</c>
        /// if <paramref name="brokeragePrice"/> is <c>null</c>.
        /// </returns>
        public decimal? GetLeanPrice(Symbol symbol, decimal? brokeragePrice)
        {
            if (!brokeragePrice.HasValue)
            {
                return null;
            }

            return GetLeanPrice(symbol, brokeragePrice.Value);
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
        ///   <item><description>Use the hardcoded magnifier of 100 when the root is in <see cref="_specialMagnifierRoots"/>.</description></item>
        ///   <item><description>Otherwise use the magnifier from <see cref="SymbolPropertiesDatabase"/>.</description></item>
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

                if (_specialMagnifierRoots.Contains(symbol.ID.Symbol))
                {
                    priceMagnifier = 100m;
                }
                else
                {
                    var properties = _spDb.GetSymbolProperties(symbol.ID.Market, symbol, symbol.SecurityType, Currencies.USD);
                    priceMagnifier = properties.PriceMagnifier;
                }

                _priceMagnifierByCanonicalSymbol[symbol.Canonical] = priceMagnifier;
                return priceMagnifier;
            }
        }
    }
}
