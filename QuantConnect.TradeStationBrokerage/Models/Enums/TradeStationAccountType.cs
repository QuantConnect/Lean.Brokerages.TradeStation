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

using Newtonsoft.Json;

namespace QuantConnect.Brokerages.TradeStation.Models.Enums;

/// <summary>
/// Specifies the type of account on TradeStation.
/// </summary>
[JsonConverter(typeof(AccountTypeEnumConverter))]
public enum TradeStationAccountType
{
    /// <summary>
    /// Undocumented trade station account types, we've seen 'forex' for example
    /// </summary>
    Unknown,

    /// <summary>
    /// Cash account type. Trades are executed using only the funds available in the account.
    /// </summary>
    Cash,

    /// <summary>
    /// Margin account type. Allows borrowing funds from the broker to trade securities, 
    /// increasing trading potential but also carrying higher risks.
    /// </summary>
    Margin,

    /// <summary>
    /// Futures account type. Specifically designed for trading futures contracts.
    /// </summary>
    Futures,

    /// <summary>
    /// Delivery versus Payment (DVP) account type. Typically used in financial transactions 
    /// where securities are exchanged for cash with simultaneous delivery.
    /// </summary>
    DVP
}
