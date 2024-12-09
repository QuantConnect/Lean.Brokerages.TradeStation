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

using QuantConnect.Orders;

namespace QuantConnect.Brokerages.TradeStation.Tests;

/// <summary>
/// Represents the parameters required for testing an order, including order type, symbol, and price limits.
/// </summary>
/// <param name="OrderType">The type of order being tested (e.g., Market, Limit, Stop).</param>
/// <param name="Symbol">The financial symbol for the order, such as a stock or option ticker.</param>
public record OrderTestMetaData(OrderType OrderType, Symbol Symbol);
