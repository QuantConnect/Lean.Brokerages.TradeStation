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
using System.Collections.Generic;
using QuantConnect.Brokerages.TradeStation.Models.Enums;
using QuantConnect.Brokerages.TradeStation.Models.Interfaces;

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents the balances and errors retrieved from TradeStation.
/// </summary>
public readonly struct TradeStationBalance : ITradeStationError
{
    /// <summary>
    /// Gets the balances information.
    /// </summary>
    public List<Balance> Balances { get; }

    /// <summary>
    /// Represents an error that occurred during the retrieval of trading account information.
    /// </summary>
    public List<TradeStationError> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationBalance"/> struct.
    /// </summary>
    /// <param name="balances">The balances information.</param>
    /// <param name="errors">The errors occurred during the retrieval.</param>
    [JsonConstructor]
    public TradeStationBalance(List<Balance> balances, List<TradeStationError> errors)
    {
        Balances = balances;
        Errors = errors;
    }
}

/// <summary>
/// Represents the balance information for a trading account.
/// </summary>
public readonly struct Balance
{
    /// <summary>
    /// TradeStation Account ID.
    /// </summary>
    public string AccountID { get; }

    /// <summary>
    /// The type of the account. Valid values are: Cash, Margin, Futures and DVP.
    /// </summary>
    public TradeStationAccountType AccountType { get; }

    /// <summary>
    /// Indicates the value of real-time cash balance.
    /// </summary>
    public string CashBalance { get; }

    /// <summary>
    /// Buying Power available in the account.
    /// </summary>
    public string BuyingPower { get; }

    /// <summary>
    /// The real-time equity of the account.
    /// </summary>
    public string Equity { get; }

    /// <summary>
    /// Market value of open positions.
    /// </summary>
    public string MarketValue { get; }

    /// <summary>
    /// Unrealized profit and loss, for the current trading day, of all open positions.
    /// </summary>
    public string TodaysProfitLoss { get; }

    /// <summary>
    /// The total of uncleared checks received by Tradestation for deposit.
    /// </summary>
    public string UnclearedDeposit { get; }

    /// <summary>
    /// Contains real-time balance information that varies according to account type.
    /// </summary>
    public BalanceDetail BalanceDetail { get; }

    /// <summary>
    /// Only applies to <c>futures</c>. Collection of properties that describe balance characteristics in different currencies.
    /// </summary>
    public List<CurrencyDetail> CurrencyDetails { get; }

    /// <summary>
    /// The brokerage commission cost and routing fees (if applicable) for a trade based on the number of shares or contracts.
    /// </summary>
    public string Commission { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Balance"/> struct.
    /// </summary>
    /// <param name="accountID">The account ID.</param>
    /// <param name="accountType">The type of the trading account.</param>
    /// <param name="cashBalance">The cash balance in the account.</param>
    /// <param name="buyingPower">The buying power of the account.</param>
    /// <param name="equity">The equity of the account.</param>
    /// <param name="marketValue">The market value of the account.</param>
    /// <param name="todayProfitLoss">The profit or loss for the day.</param>
    /// <param name="unclearedDeposit">The uncleared deposit amount.</param>
    /// <param name="balanceDetail">The detailed balance information.</param>
    /// <param name="currencyDetails">The details of currency balances.</param>
    /// <param name="commission">The commission amount.</param>
    [JsonConstructor]
    public Balance(string accountID, TradeStationAccountType accountType, string cashBalance, string buyingPower, string equity, string marketValue,
        string todayProfitLoss, string unclearedDeposit, BalanceDetail balanceDetail, List<CurrencyDetail> currencyDetails, string commission)
    {
        AccountID = accountID;
        AccountType = accountType;
        CashBalance = cashBalance;
        BuyingPower = buyingPower;
        Equity = equity;
        MarketValue = marketValue;
        TodaysProfitLoss = todayProfitLoss;
        UnclearedDeposit = unclearedDeposit;
        BalanceDetail = balanceDetail;
        CurrencyDetails = currencyDetails;
        Commission = commission;
    }
}

/// <summary>
/// Represents the details of currency balances.
/// </summary>
public readonly struct CurrencyDetail
{
    /// <summary>
    /// Gets the currency.
    /// </summary>
    public string Currency { get; }

    /// <summary>
    /// Gets the commission amount.
    /// </summary>
    public string Commission { get; }

    /// <summary>
    /// Indicates the value of real-time cash balance.
    /// </summary>
    public string CashBalance { get; }

    /// <summary>
    /// Indicates the value of real-time account realized profit or loss.
    /// </summary>
    public string RealizedProfitLoss { get; }

    /// <summary>
    /// Indicates the value of real-time account unrealized profit or loss.
    /// </summary>
    public string UnrealizedProfitLoss { get; }

    /// <summary>
    /// Gets the initial margin in the specified currency.
    /// </summary>
    public string InitialMargin { get; }

    /// <summary>
    /// Gets the maintenance margin in the specified currency.
    /// </summary>
    public string MaintenanceMargin { get; }

    /// <summary>
    /// Gets the account conversion rate for the currency.
    /// </summary>
    public string AccountConversionRate { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CurrencyDetail"/> struct.
    /// </summary>
    /// <param name="currency">The currency.</param>
    /// <param name="commission">The commission amount.</param>
    /// <param name="cashBalance">The cash balance in the specified currency.</param>
    /// <param name="realizedProfitLoss">The realized profit or loss in the specified currency.</param>
    /// <param name="unrealizedProfitLoss">The unrealized profit or loss in the specified currency.</param>
    /// <param name="initialMargin">The initial margin in the specified currency.</param>
    /// <param name="maintenanceMargin">The maintenance margin in the specified currency.</param>
    /// <param name="accountConversionRate">The account conversion rate for the currency.</param>
    [JsonConstructor]
    public CurrencyDetail(string currency, string commission, string cashBalance, string realizedProfitLoss, string unrealizedProfitLoss,
        string initialMargin, string maintenanceMargin, string accountConversionRate)
    {
        Currency = currency;
        Commission = commission;
        CashBalance = cashBalance;
        RealizedProfitLoss = realizedProfitLoss;
        UnrealizedProfitLoss = unrealizedProfitLoss;
        InitialMargin = initialMargin;
        MaintenanceMargin = maintenanceMargin;
        AccountConversionRate = accountConversionRate;
    }
}

/// <summary>
/// Contains real-time balance information that varies according to account type.
/// </summary>
public readonly struct BalanceDetail
{
    /// <summary>
    /// (Equities): (Buying Power Available - Buying Power Used) / Buying Power Multiplier. (Futures): (Cash + UnrealizedGains) - Buying Power Used.
    /// </summary>
    public string DayTradeExcess { get; }

    /// <summary>
    /// Gets the realized profit or loss.
    /// </summary>
    public string RealizedProfitLoss { get; }

    /// <summary>
    /// Gets the unrealized profit or loss.
    /// </summary>
    public string UnrealizedProfitLoss { get; }

    /// <summary>
    /// (Futures) Money field representing the current amount of money reserved for open orders.
    /// </summary>
    public string DayTradeOpenOrderMargin { get; }

    /// <summary>
    /// (Futures) The dollar amount of Open Order Margin for the given futures account.
    /// </summary>
    public string OpenOrderMargin { get; }

    /// <summary>
    /// (Futures) Money field representing the current total amount of futures day trade margin.
    /// </summary>
    public string DayTradeMargin { get; }

    /// <summary>
    /// (Futures) Sum (Initial Margins of all positions in the given account).
    /// </summary>
    public string InitialMargin { get; }

    /// <summary>
    /// (Futures) Indicates the value of real-time maintenance margin.
    /// </summary>
    public string MaintenanceMargin { get; }

    /// <summary>
    /// (Futures) The dollar amount of unrealized profit and loss for the given futures account. Same value as RealTimeUnrealizedGains.
    /// </summary>
    public string TradeEquity { get; }

    /// <summary>
    /// (Futures) The value of special securities that are deposited by the customer with the clearing firm for the sole purpose of increasing 
    /// purchasing power in their trading account. This number will be reset daily by the account balances clearing file. 
    /// The entire value of this field will increase purchasing power.
    /// </summary>
    public string SecurityOnDeposit { get; }

    /// <summary>
    /// (Futures) The unrealized P/L for today. Unrealized P/L - BODOpenTradeEquity.
    /// </summary>
    public string TodayRealTimeTradeEquity { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BalanceDetail"/> struct.
    /// </summary>
    /// <param name="dayTradeExcess">The day trade excess.</param>
    /// <param name="realizedProfitLoss">The realized profit or loss.</param>
    /// <param name="unrealizedProfitLoss">The unrealized profit or loss.</param>
    /// <param name="dayTradeOpenOrderMargin">The day trade open order margin.</param>
    /// <param name="openOrderMargin">The open order margin.</param>
    /// <param name="dayTradeMargin">The day trade margin.</param>
    /// <param name="initialMargin">The initial margin.</param>
    /// <param name="maintenanceMargin">The maintenance margin.</param>
    /// <param name="tradeEquity">The trade equity.</param>
    /// <param name="securityOnDeposit">The security on deposit.</param>
    /// <param name="todayRealTimeTradeEquity">The today's real-time trade equity.</param>
    [JsonConstructor]
    public BalanceDetail(string dayTradeExcess, string realizedProfitLoss, string unrealizedProfitLoss, string dayTradeOpenOrderMargin,
        string openOrderMargin, string dayTradeMargin, string initialMargin, string maintenanceMargin, string tradeEquity, string securityOnDeposit,
        string todayRealTimeTradeEquity)
    {
        DayTradeExcess = dayTradeExcess;
        RealizedProfitLoss = realizedProfitLoss;
        UnrealizedProfitLoss = unrealizedProfitLoss;
        DayTradeOpenOrderMargin = dayTradeOpenOrderMargin;
        OpenOrderMargin = openOrderMargin;
        DayTradeMargin = dayTradeMargin;
        InitialMargin = initialMargin;
        MaintenanceMargin = maintenanceMargin;
        TradeEquity = tradeEquity;
        SecurityOnDeposit = securityOnDeposit;
        TodayRealTimeTradeEquity = todayRealTimeTradeEquity;
    }
}