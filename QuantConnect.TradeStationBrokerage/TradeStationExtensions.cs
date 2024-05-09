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
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation;

/// <summary>
/// Provides extension methods.
/// </summary>
public static class TradeStationExtensions
{
    /// <summary>
    /// Converts a <see cref="TradeStationOptionType"/> to an <see cref="OptionRight"/>.
    /// </summary>
    /// <param name="optionType">The <see cref="TradeStationOptionType"/> to convert.</param>
    /// <returns>The corresponding <see cref="OptionRight"/> value.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the <paramref name="optionType"/> is not supported.
    /// </exception>
    public static OptionRight ConvertOptionTypeToOptionRight(this TradeStationOptionType optionType) => optionType switch
    {
        TradeStationOptionType.Put => OptionRight.Put,
        TradeStationOptionType.Call => OptionRight.Call,
        _ => throw new NotSupportedException($"{nameof(TradeStationBrokerage)}.{nameof(ConvertOptionTypeToOptionRight)}: " +
            $"The optionType '{optionType}' is not supported.")
    };

    /// <summary>
    /// Converts a <see cref="TradeStationAssetType"/> to a <see cref="SecurityType"/>.
    /// </summary>
    /// <param name="assetType">The <see cref="TradeStationAssetType"/> to convert.</param>
    /// <returns>The corresponding <see cref="SecurityType"/> value.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the <paramref name="assetType"/> is not supported.
    /// </exception>
    public static SecurityType ConvertAssetTypeToSecurityType(this TradeStationAssetType assetType) => assetType switch
    {
        TradeStationAssetType.Stock => SecurityType.Equity,
        TradeStationAssetType.StockOption => SecurityType.Option,
        TradeStationAssetType.Future => SecurityType.Future,
        TradeStationAssetType.FutureOption => SecurityType.FutureOption,
        TradeStationAssetType.Forex => SecurityType.Forex,
        TradeStationAssetType.Index => SecurityType.Index,
        TradeStationAssetType.IndexOption => SecurityType.IndexOption,
        _ => throw new NotSupportedException($"{nameof(TradeStationBrokerage)}.{nameof(ConvertAssetTypeToSecurityType)}: " +
            $"The AssetType '{assetType}' is not supported.")
    };
}
