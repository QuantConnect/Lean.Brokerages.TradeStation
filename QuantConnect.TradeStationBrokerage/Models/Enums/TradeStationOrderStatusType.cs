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
public enum TradeStationOrderStatusType
{
    #region Open
    /// <summary>
    /// Received - The TradeStation Order Execution Network has routed the order to the appropriate market participant 
    /// (such as an ECN, Market Maker, or NYSE Specialist) and confirmation has been received.
    /// </summary>
    [EnumMember(Value = "ACK")]
    Ack,

    /// <summary>
    /// Option Assignment
    /// </summary>
    [EnumMember(Value = "ASS")]
    Ass,

    /// <summary>
    /// Bracket Canceled
    /// </summary>
    [EnumMember(Value = "BRC")]
    Brc,

    /// <summary>
    /// Bracket Filled
    /// </summary>
    [EnumMember(Value = "BRF")]
    Brf,

    /// <summary>
    /// Broken - Executed trade was manually broken by TradeStation.
    /// </summary>
    [EnumMember(Value = "BRO")]
    Bro,

    /// <summary>
    /// Change
    /// </summary>
    [EnumMember(Value = "CHG")]
    Chg,

    /// <summary>
    /// Condition Met
    /// </summary>
    [EnumMember(Value = "CND")]
    Cnd,

    /// <summary>
    /// Fill Corrected
    /// </summary>
    [EnumMember(Value = "COR")]
    Cor,

    /// <summary>
    /// Cancel Sent
    /// </summary>
    [EnumMember(Value = "CSN")]
    Csn,

    /// <summary>
    /// Dispatched
    /// </summary>
    [EnumMember(Value = "DIS")]
    Dis,

    /// <summary>
    /// Dead
    /// </summary>
    [EnumMember(Value = "DOA")]
    Doa,

    /// <summary>
    /// Queued - An order that is held on the TradeStation Order Execution Network and 
    /// that will be placed automatically at the correct time (often the open of the next trading session).
    /// </summary>
    [EnumMember(Value = "DON")]
    Don,

    /// <summary>
    /// Expiration Cancel Request
    /// </summary>
    [EnumMember(Value = "ECN")]
    Ecn,

    /// <summary>
    /// Option Exercise
    /// </summary>
    [EnumMember(Value = "EXE")]
    Exe,

    /// <summary>
    /// Partial Fill (Alive)
    /// </summary>
    [EnumMember(Value = "FPR")]
    Fpr,

    /// <summary>
    /// Too Late to Cancel
    /// </summary>
    [EnumMember(Value = "LAT")]
    Lat,

    /// <summary>
    /// Sent
    /// </summary>
    [EnumMember(Value = "OPN")]
    Opn,

    /// <summary>
    /// OSO Order
    /// </summary>
    [EnumMember(Value = "OSO")]
    Oso,

    /// <summary>
    /// OrderStatus not mapped
    /// </summary>
    [EnumMember(Value = "OTHER")]
    Other,

    /// <summary>
    /// Sending
    /// </summary>
    [EnumMember(Value = "PLA")]
    Pla,

    /// <summary>
    /// Big Brother Recall Request
    /// </summary>
    [EnumMember(Value = "REC")]
    Rec,

    /// <summary>
    /// Cancel Request Rejected
    /// </summary>
    [EnumMember(Value = "RJC")]
    Rjc,

    /// <summary>
    /// Replace Pending
    /// </summary>
    [EnumMember(Value = "RPD")]
    Rpd,

    /// <summary>
    /// Replace Sent
    /// </summary>
    [EnumMember(Value = "RSN")]
    Rsn,

    /// <summary>
    /// Stop Hit
    /// </summary>
    [EnumMember(Value = "STP")]
    Stp,

    /// <summary>
    /// OrderStatus Message
    /// </summary>
    [EnumMember(Value = "STT")]
    Stt,

    /// <summary>
    /// Suspended
    /// </summary>
    [EnumMember(Value = "SUS")]
    Sus,

    /// <summary>
    /// Cancel Sent
    /// </summary>
    [EnumMember(Value = "UCN")]
    Ucn,

    #endregion
    #region Canceled

    /// <summary>
    /// Canceled - Queued GTC order that has been manually cancelled by TradeStation.
    /// </summary>
    [EnumMember(Value = "CAN")]
    Can,

    /// <summary>
    /// Expired - The order is no longer active because the order was not filled within the specified time duration.
    /// </summary>
    [EnumMember(Value = "EXP")]
    Exp,

    /// <summary>
    /// UROut - The order is no longer active.
    /// </summary>
    [EnumMember(Value = "OUT")]
    Out,

    /// <summary>
    /// Change Request Rejected
    /// </summary>
    [EnumMember(Value = "RJR")]
    Rjr,

    /// <summary>
    /// Big Brother Recall
    /// </summary>
    [EnumMember(Value = "SCN")]
    Scn,

    /// <summary>
    /// Trade Server Canceled
    /// </summary>
    [EnumMember(Value = "TSC")]
    Tsc,

    /// <summary>
    /// Replaced
    /// </summary>
    [EnumMember(Value = "UCH")]
    Uch,

    #endregion

    #region Rejected

    /// <summary>
    /// Rejected - The TradeStation Order Execution Network was unable to send the order, or the market participant was unable to accept the order.
    /// </summary>
    [EnumMember(Value = "REJ")]
    Rej,

    #endregion

    #region Filled

    /// <summary>
    /// Filled - The market participant has filled the order in its entirety.
    /// </summary>
    [EnumMember(Value = "FLL")]
    Fll,

    /// <summary>
    /// Partial Fill (UROut)
    /// </summary>
    [EnumMember(Value = "FLP")]
    FLP,

    #endregion
}
