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

[JsonConverter(typeof(StringEnumConverter))]
public enum TradeStationDuration
{
    /// <summary>
    /// Day, valid until the end of the regular trading session.
    /// </summary>
    [EnumMember(Value = "DAY")]
    Day = 0,

    /// <summary>
    /// Good through date. Maximum lifespan is 90 calendar days.
    /// </summary>
    [EnumMember(Value = "GTD")]
    GoodThroughDate = 1,

    /// <summary>
    /// Good till canceled. Maximum lifespan is 90 calendar days.
    /// </summary>
    [EnumMember(Value = "GTC")]
    GoodTillCanceled = 2,

    /// <summary>
    /// At the opening; only valid for listed stocks at the opening session Price.
    /// </summary>
    [EnumMember(Value = "OPG")]
    Opening = 3,

    /// <summary>
    /// On Close; orders that target the closing session of an exchange.
    /// </summary>
    [EnumMember(Value = "CLO")]
    Close = 4,

    /// <summary>
    /// Valid until the end of the extended trading session.
    /// </summary>
    [EnumMember(Value = "DAY+")]
    DayPlus = 5,

    /// <summary>
    /// Good till Canceled Plus
    /// </summary>
    [EnumMember(Value = "GTC+")]
    GoodTillCanceledPlus = 6,

    /// <summary>
    /// Good till Date Plus
    /// </summary>
    [EnumMember(Value = "GTD+")]
    GoodThroughDatePlus = 7,

    /// <summary>
    /// (Immediate or Cancel) IOC orders are filled immediately or canceled. Partial fills are accepted when using this order duration.
    /// </summary>
    [EnumMember(Value = "IOC")]
    ImmediateOrCancel = 8,

    /// <summary>
    /// (Fill or Kill) FOK orders are filled in their entirety or canceled. Partial fills are not accepted when using this order duration.
    /// </summary>
    [EnumMember(Value = "FOK")]
    FillOrKill = 9,

    /// <summary>
    /// Expire after 1 minutes;
    /// </summary>
    [EnumMember(Value = "1 min")]
    OneMinute = 11,

    /// <summary>
    /// Expire after 3 minutes;
    /// </summary>
    [EnumMember(Value = "3 min")]
    ThreeMinute = 12,

    /// <summary>
    /// Expire after 5 minutes;
    /// </summary>
    [EnumMember(Value = "5 min")]
    FiveMinute = 13,
}
