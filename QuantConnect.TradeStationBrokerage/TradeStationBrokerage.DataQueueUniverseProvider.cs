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

using QuantConnect.Interfaces;
using QuantConnect.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuantConnect.Brokerages.TradeStation;

/// <summary>
/// Represents the TradeStation Brokerage's IDataQueueUniverseProvider implementation.
/// </summary>
public partial class TradeStationBrokerage : IDataQueueUniverseProvider
{
    /// <summary>
    /// Collection of pre-defined option rights.
    /// Initialized for performance optimization as the API only returns strike price without indicating the right.
    /// </summary>
    private readonly IEnumerable<OptionRight> _optionRights = new[] { OptionRight.Call, OptionRight.Put };

    /// <summary>
    /// Method returns a collection of Symbols that are available at the data source.
    /// </summary>
    /// <param name="symbol">Symbol to lookup</param>
    /// <param name="includeExpired">Include expired contracts</param>
    /// <param name="securityCurrency">Expected security currency(if any)</param>
    /// <returns>Enumerable of Symbols, that are associated with the provided Symbol</returns>
    public IEnumerable<Symbol> LookupSymbols(Symbol symbol, bool includeExpired, string securityCurrency = null)
    {
        if (!symbol.SecurityType.IsOption())
        {
            Log.Error("The provided symbol is not an option. SecurityType: " + symbol.SecurityType);
            return Enumerable.Empty<Symbol>();
        }
        var blockingOptionCollection = new BlockingCollection<Symbol>();

        Task.Run(async () =>
        {
            var underlying = symbol.Underlying.Value;
            await foreach (var optionParameters in _tradeStationApiClient.GetOptionExpirationsAndStrikes(underlying))
            {
                foreach (var optionStrike in optionParameters.strikes)
                {
                    foreach (var right in _optionRights)
                    {
                        blockingOptionCollection.Add(_symbolMapper.GetLeanSymbol(underlying, SecurityType.Option, Market.USA,
                            optionParameters.expirationDate, optionStrike, right));
                    }
                }
            }
        }).ContinueWith(_ => blockingOptionCollection.CompleteAdding());

        var options = blockingOptionCollection.GetConsumingEnumerable();

        // Validate if the collection contains at least one successful response from history.
        if (!options.Any())
        {
            return null;
        }

        return options;
    }

    /// <summary>
    /// Returns whether selection can take place or not.
    /// </summary>
    /// <remarks>This is useful to avoid a selection taking place during invalid times, for example IB reset times or when not connected,
    /// because if allowed selection would fail since IB isn't running and would kill the algorithm</remarks>
    /// <returns>True if selection can take place</returns>
    public bool CanPerformSelection()
    {
        return IsConnected;
    }
}
