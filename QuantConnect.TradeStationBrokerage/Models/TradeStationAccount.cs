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
using System.Collections.Generic;

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents a TradeStation account containing a collection of accounts.
/// </summary>
public readonly struct TradeStationAccount
{
    /// <summary>
    /// Gets the collection of accounts associated with the TradeStation account.
    /// </summary>
    public IEnumerable<Account> Accounts { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationAccount"/> struct
    /// with the specified collection of accounts.
    /// </summary>
    /// <param name="accounts">The collection of accounts to be associated with the TradeStation account.</param>
    [JsonConstructor]
    public TradeStationAccount(IEnumerable<Account> accounts)
    {
        Accounts = accounts;
    }
}

/// <summary>
/// Represents an account within TradeStation.
/// </summary>
public readonly struct Account
{
    /// <summary>
    /// Gets the unique identifier of the account.
    /// </summary>
    public string AccountID { get; }

    /// <summary>
    /// Gets the currency associated with the account.
    /// </summary>
    public string Currency { get; }

    /// <summary>
    /// Gets the status of the account.
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// Gets the type of the account.
    /// </summary>
    public string AccountType { get; }

    /// <summary>
    /// Gets the detailed information of the account, if available.
    /// </summary>
    public AccountDetail? AccountDetail { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Account"/> struct
    /// with the specified parameters.
    /// </summary>
    /// <param name="accountID">The unique identifier of the account.</param>
    /// <param name="currency">The currency associated with the account.</param>
    /// <param name="status">The status of the account.</param>
    /// <param name="accountType">The type of the account.</param>
    /// <param name="accountDetail">The detailed information of the account, if available.</param>
    [JsonConstructor]
    public Account(string accountID, string currency, string status, string accountType, AccountDetail? accountDetail)
    {
        AccountID = accountID;
        Currency = currency;
        Status = status;
        AccountType = accountType;
        AccountDetail = accountDetail;
    }
}

/// <summary>
/// Represents detailed information about an account within TradeStation.
/// </summary>
public readonly struct AccountDetail
{
    /// <summary>
    /// Gets a value indicating whether the account is eligible for stock locate.
    /// </summary>
    public bool IsStockLocateEligible { get; }

    /// <summary>
    /// Gets a value indicating whether the account is enrolled in the Regulation T (Reg T) program.
    /// </summary>
    public bool EnrolledInRegTProgram { get; }

    /// <summary>
    /// Gets a value indicating whether the account requires buying power warning.
    /// </summary>
    public bool RequiresBuyingPowerWarning { get; }

    /// <summary>
    /// Gets a value indicating whether the account is enabled for cryptocurrency trading.
    /// </summary>
    public bool CryptoEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether the account is qualified for day trading.
    /// </summary>
    public bool DayTradingQualified { get; }

    /// <summary>
    /// Gets the option approval level of the account.
    /// </summary>
    public int OptionApprovalLevel { get; }

    /// <summary>
    /// Gets a value indicating whether the account is classified as a pattern day trader.
    /// </summary>
    public bool PatternDayTrader { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountDetail"/> struct
    /// with the specified parameters.
    /// </summary>
    /// <param name="isStockLocateEligible">Whether the account is eligible for stock locate.</param>
    /// <param name="enrolledInRegTProgram">Whether the account is enrolled in the Regulation T (Reg T) program.</param>
    /// <param name="requiresBuyingPowerWarning">Whether the account requires buying power warning.</param>
    /// <param name="cryptoEnabled">Whether the account is enabled for cryptocurrency trading.</param>
    /// <param name="dayTradingQualified">Whether the account is qualified for day trading.</param>
    /// <param name="optionApprovalLevel">The option approval level of the account.</param>
    /// <param name="patternDayTrader">Whether the account is classified as a pattern day trader.</param>
    [JsonConstructor]
    public AccountDetail(bool isStockLocateEligible, bool enrolledInRegTProgram, bool requiresBuyingPowerWarning,
        bool cryptoEnabled, bool dayTradingQualified, int optionApprovalLevel, bool patternDayTrader)
    {
        IsStockLocateEligible = isStockLocateEligible;
        EnrolledInRegTProgram = enrolledInRegTProgram;
        RequiresBuyingPowerWarning = requiresBuyingPowerWarning;
        CryptoEnabled = cryptoEnabled;
        DayTradingQualified = dayTradingQualified;
        OptionApprovalLevel = optionApprovalLevel;
        PatternDayTrader = patternDayTrader;
    }
}
