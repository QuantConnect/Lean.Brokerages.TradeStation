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
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace QuantConnect.Brokerages.TradeStation.Models.Enums;

/// <summary>
/// Represents the duration of a placed order, determining how long the order remains active.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum PlaceOrderDuration
{
    /// <summary>
    /// Day, valid until the end of the regular trading session.
    /// </summary>
    [EnumMember(Value = "DAY")]
    Day = 0,

    /// <summary>
    /// Day Plus; valid until the end of the extended trading session.
    /// </summary>
    [EnumMember(Value = "DYP")]
    DayPlus = 1,

    /// <summary>
    /// Good till canceled. Maximum lifespan is 90 calendar days.
    /// </summary>
    [EnumMember(Value = "GTC")]
    GoodTillCanceled = 2,

    /// <summary>
    /// Good till canceled plus. Maximum lifespan is 90 calendar days.
    /// </summary>
    [EnumMember(Value = "GCP")]
    GoodTillCanceledPlus = 3,

    /// <summary>
    /// Good through date. Maximum lifespan is 90 calendar days.
    /// </summary>
    [EnumMember(Value = "GTD")]
    GoodThroughDate = 4,

    /// <summary>
    /// Good through date plus. Maximum lifespan is 90 calendar days.
    /// </summary>
    [EnumMember(Value = "GDP")]
    GoodThroughDatePlus = 5,

    /// <summary>
    /// At the opening; only valid for listed stocks at the opening session Price.
    /// </summary>
    [EnumMember(Value = "OPG")]
    Opening = 6,

    /// <summary>
    /// On Close; orders that target the closing session of an exchange.
    /// </summary>
    [EnumMember(Value = "CLO")]
    Close = 7,
}
