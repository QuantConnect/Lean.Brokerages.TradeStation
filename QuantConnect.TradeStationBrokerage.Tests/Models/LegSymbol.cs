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

using QuantConnect.Brokerages.TradeStation.Models;

namespace QuantConnect.Brokerages.TradeStation.Tests;

/// <summary>
/// Combines a TradeStation <see cref="Models.Leg"/> with its corresponding Lean <see cref="QuantConnect.Symbol"/>.
/// </summary>
/// <param name="Leg">The financial instrument or component (e.g., option leg).</param>
/// <param name="Symbol">The corresponding Lean symbol.</param>
public sealed record LegSymbol(Leg Leg, Symbol Symbol)
{
    /// <summary>
    /// Returns a detailed string representation of the leg and its associated symbol.
    /// </summary>
    /// <returns>A formatted string summarizing the leg's details and the Lean symbol.</returns>
    public override string ToString()
    {
        return $"Leg: {Leg.AssetType}, Symbol = {Leg.Symbol}, Underlying = {Leg.Underlying}, LeanSymbol = {Symbol}";
    }
}
