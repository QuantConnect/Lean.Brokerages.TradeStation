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
using QuantConnect.Data;
using QuantConnect.Logging;
using QuantConnect.Data.Market;
using System.Collections.Generic;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation;

/// <summary>
/// Represents the TradeStation Brokerage's HistoryProvider implementation.
/// </summary>
public partial class TradeStationBrokerage
{
    /// <summary>
    /// Indicates whether the warning for invalid <see cref="SecurityType"/> has been fired.
    /// </summary>
    private volatile bool _unsupportedSecurityTypeWarningFired;

    /// <summary>
    /// Indicates whether the warning for invalid <see cref="Resolution"/> has been fired.
    /// </summary>
    private volatile bool _unsupportedResolutionTypeWarningFired;

    /// <summary>
    /// Indicates whether the warning for invalid <see cref="TickType"/> has been fired.
    /// </summary>
    private volatile bool _unsupportedTickTypeTypeWarningFired;

    /// <summary>
    /// Gets the history for the requested security
    /// </summary>
    /// <param name="request">The historical data request</param>
    /// <returns>An enumerable of bars covering the span specified in the request</returns>
    public override IEnumerable<BaseData> GetHistory(HistoryRequest request)
    {
        if (!CanSubscribe(request.Symbol))
        {
            if (!_unsupportedSecurityTypeWarningFired)
            {
                _unsupportedSecurityTypeWarningFired = true;
                Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(GetHistory)}: Unsupported SecurityType '{request.Symbol.SecurityType}' for symbol '{request.Symbol}'");
            }

            return null;
        }

        if (request.Resolution <= Resolution.Second)
        {
            if (!_unsupportedResolutionTypeWarningFired)
            {
                _unsupportedResolutionTypeWarningFired = true;
                Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(GetHistory)}: Unsupported Resolution '{request.Resolution}'");
            }

            return null;
        }

        if (request.TickType != TickType.Trade)
        {
            if (!_unsupportedTickTypeTypeWarningFired)
            {
                _unsupportedTickTypeTypeWarningFired = true;
                Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(GetHistory)}: Unsupported TickType '{request.TickType}'");
            }

            return null;
        }

        return GetTradeStationHistory(request);
    }

    private IEnumerable<BaseData> GetTradeStationHistory(HistoryRequest request)
    {
        var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

        var brokerageUnitTime = request.Resolution switch
        {
            Resolution.Minute => TradeStationUnitTimeIntervalType.Minute,
            Resolution.Hour => TradeStationUnitTimeIntervalType.Hour,
            Resolution.Daily => TradeStationUnitTimeIntervalType.Daily,
            _ => throw new NotSupportedException($"{nameof(TradeStationBrokerage)}.{nameof(GetHistory)}: Unsupported time Resolution type '{request.Resolution}'")
        };

        var period = request.Resolution.ToTimeSpan();

        foreach (var bar in _tradeStationApiClient.GetBars(brokerageSymbol, brokerageUnitTime, request.StartTimeUtc, request.EndTimeUtc).ToEnumerable())
        {
            var tradeBar = new TradeBar(bar.TimeStamp.ConvertFromUtc(request.ExchangeHours.TimeZone), request.Symbol, bar.Open, bar.High, bar.Low, bar.Close, bar.TotalVolume, period);

            if (request.ExchangeHours.IsOpen(tradeBar.Time, tradeBar.EndTime, request.IncludeExtendedMarketHours))
            {
                yield return tradeBar;
            }
        }
    }
}
