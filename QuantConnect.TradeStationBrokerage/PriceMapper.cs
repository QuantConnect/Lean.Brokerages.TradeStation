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
    public class PriceMapper
    {
        private static readonly SymbolPropertiesDatabase _spDb = SymbolPropertiesDatabase.FromDataFolder();

        private readonly TradeStationApiClient _apiClient;

        private readonly TradeStationSymbolMapper _symbolMapper;

        private readonly Dictionary<Symbol, decimal> _priceMagnifierByCanonicalSymbol = [];

        private readonly Lock _magnifierLock = new();

        /// <summary>
        /// Creates a price mapper backed by the supplied TradeStation API client and symbol mapper.
        /// </summary>
        public PriceMapper(TradeStationApiClient apiClient, TradeStationSymbolMapper symbolMapper)
        {
            _apiClient = apiClient;
            _symbolMapper = symbolMapper;
        }

        public decimal GetBrokeragePrice(Symbol symbol, decimal price)
        {
            if (symbol.SecurityType != SecurityType.Future)
            {
                return price;
            }

            return price * GetMagnifier(symbol);
        }

        public decimal GetLeanPrice(Symbol symbol, decimal brokeragePrice)
        {
            if (symbol.SecurityType != SecurityType.Future)
            {
                return brokeragePrice;
            }

            return brokeragePrice / GetMagnifier(symbol);
        }

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
