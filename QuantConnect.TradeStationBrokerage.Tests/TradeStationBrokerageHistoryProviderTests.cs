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
using NodaTime;
using System.Linq;
using NUnit.Framework;
using QuantConnect.Util;
using QuantConnect.Data;
using QuantConnect.Logging;
using QuantConnect.Securities;
using QuantConnect.Data.Market;
using System.Collections.Generic;
using static QuantConnect.Brokerages.TradeStation.Tests.TradeStationBrokerageTests;

namespace QuantConnect.Brokerages.TradeStation.Tests;

[TestFixture]
public class TradeStationBrokerageHistoryProviderTests
{
    private TradeStationBrokerageTest _brokerage;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _brokerage = TestSetup.CreateBrokerage(null, null);
    }

    private static IEnumerable<TestCaseData> ValidHistoryParameters
    {
        get
        {
            var AAPL = CreateSymbol("AAPL", SecurityType.Equity);
            yield return new TestCaseData(AAPL, Resolution.Minute, new DateTime(2024, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(AAPL, Resolution.Hour, new DateTime(2024, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(AAPL, Resolution.Daily, new DateTime(2024, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(AAPL, Resolution.Minute, new DateTime(2023, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(AAPL, Resolution.Minute, new DateTime(2024, 02, 12), new DateTime(2024, 03, 23));

            var AAPLOption = CreateSymbol("AAPL", SecurityType.Option, OptionRight.Call, 200m, new DateTime(2024, 12, 13));
            yield return new TestCaseData(AAPLOption, Resolution.Minute, new DateTime(2024, 11, 06), new DateTime(2024, 12, 06));
            yield return new TestCaseData(AAPLOption, Resolution.Hour, new DateTime(2024, 11, 06), new DateTime(2024, 12, 06));
            yield return new TestCaseData(AAPLOption, Resolution.Daily, new DateTime(2024, 11, 06), new DateTime(2024, 12, 06));
            yield return new TestCaseData(AAPLOption, Resolution.Minute, new DateTime(2023, 11, 06), new DateTime(2024, 12, 06));

            var COTTON = Symbol.CreateFuture(Futures.Softs.Cotton2, Market.ICE, new DateTime(2024, 7, 1));
            yield return new TestCaseData(COTTON, Resolution.Minute, new DateTime(2024, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(COTTON, Resolution.Hour, new DateTime(2024, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(COTTON, Resolution.Daily, new DateTime(2024, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(COTTON, Resolution.Minute, new DateTime(2023, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(COTTON, Resolution.Minute, new DateTime(2024, 02, 12), new DateTime(2024, 03, 23));

            var VIX = Symbol.Create("VIX", SecurityType.Index, Market.USA);
            yield return new TestCaseData(VIX, Resolution.Minute, new DateTime(2024, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(VIX, Resolution.Hour, new DateTime(2024, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(VIX, Resolution.Daily, new DateTime(2024, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(VIX, Resolution.Minute, new DateTime(2023, 06, 18), new DateTime(2024, 07, 18));
            yield return new TestCaseData(VIX, Resolution.Minute, new DateTime(2024, 02, 12), new DateTime(2024, 03, 23));

            var VIXOption = Symbol.CreateOption(VIX, Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Call, 17m, new DateTime(2024, 12, 18));
            yield return new TestCaseData(VIXOption, Resolution.Minute, new DateTime(2024, 11, 06), new DateTime(2024, 12, 06));
            yield return new TestCaseData(VIXOption, Resolution.Hour, new DateTime(2024, 11, 06), new DateTime(2024, 12, 06));
            yield return new TestCaseData(VIXOption, Resolution.Daily, new DateTime(2024, 11, 06), new DateTime(2024, 12, 06));
            yield return new TestCaseData(VIXOption, Resolution.Minute, new DateTime(2023, 11, 06), new DateTime(2024, 12, 06));
        }
    }

    [TestCaseSource(nameof(ValidHistoryParameters))]
    public void GetHistoryData(Symbol symbol, Resolution resolution, DateTime startDate, DateTime endDate)
    {
        var historyRequest = CreateHistoryRequest(symbol, resolution, TickType.Trade, startDate, endDate);

        var history = _brokerage.GetHistory(historyRequest);

        Assert.IsNotNull(history);
        Assert.IsNotEmpty(history);

        AssertTradeBars(history.Select(x => x as TradeBar), symbol, resolution.ToTimeSpan());
    }

    [TestCase("AAPL", Resolution.Tick, TickType.Trade, Description = "Not supported Resolution.Tick")]
    [TestCase("AAPL", Resolution.Second, TickType.Trade, Description = "Not supported Resolution.Second")]
    [TestCase("AAPL", Resolution.Minute, TickType.OpenInterest, Description = "Not supported TickType.OpenInterest")]
    [TestCase("AAPL", Resolution.Minute, TickType.Quote, Description = "Not supported TickType.Quote")]
    public void GetEquityHistoryDataWithWrongParameters(string ticker, Resolution resolution, TickType tickType)
    {
        var symbol = CreateSymbol(ticker, SecurityType.Equity);

        var historyRequest = CreateHistoryRequest(symbol, resolution, tickType, new DateTime(2024, 06, 19), new DateTime(2024, 07, 19));

        var history = _brokerage.GetHistory(historyRequest);

        Assert.IsNull(history);
    }

    public static void AssertTradeBars(IEnumerable<TradeBar> tradeBars, Symbol symbol, TimeSpan period)
    {
        var counterTradeBar = default(int);
        foreach (var tradeBar in tradeBars)
        {
            Assert.That(tradeBar.Symbol, Is.EqualTo(symbol));
            Assert.That(tradeBar.Period, Is.EqualTo(period));
            Assert.That(tradeBar.Open, Is.GreaterThan(0));
            Assert.That(tradeBar.High, Is.GreaterThan(0));
            Assert.That(tradeBar.Low, Is.GreaterThan(0));
            Assert.That(tradeBar.Close, Is.GreaterThan(0));
            Assert.That(tradeBar.Price, Is.GreaterThan(0));
            Assert.That(tradeBar.Volume, Is.GreaterThanOrEqualTo(0));
            Assert.That(tradeBar.Time, Is.GreaterThan(default(DateTime)));
            Assert.That(tradeBar.EndTime, Is.GreaterThan(default(DateTime)));
            counterTradeBar++;
        }
        Log.Trace($"{nameof(AssertTradeBars)}: Successfully validated {counterTradeBar} trade bars for symbol '{symbol}' with period '{period}'.");
    }

    private static HistoryRequest CreateHistoryRequest(Symbol symbol, Resolution resolution, TickType tickType, DateTime startDateTime, DateTime endDateTime,
                SecurityExchangeHours exchangeHours = null, DateTimeZone dataTimeZone = null)
    {
        if (exchangeHours == null)
        {
            exchangeHours = SecurityExchangeHours.AlwaysOpen(TimeZones.NewYork);
        }

        if (dataTimeZone == null)
        {
            dataTimeZone = TimeZones.NewYork;
        }

        var dataType = LeanData.GetDataType(resolution, tickType);
        return new HistoryRequest(
            startDateTime,
            endDateTime,
            dataType,
            symbol,
            resolution,
            exchangeHours,
            dataTimeZone,
            null,
            true,
            false,
            DataNormalizationMode.Adjusted,
            tickType
            );
    }

    public static Symbol CreateSymbol(string ticker, SecurityType securityType, OptionRight? optionRight = null, decimal? strikePrice = null, DateTime? expirationDate = null, string market = Market.USA)
    {
        switch (securityType)
        {
            case SecurityType.Equity:
                return Symbol.Create(ticker, securityType, market);
            case SecurityType.Option:
                var underlyingEquitySymbol = Symbol.Create(ticker, SecurityType.Equity, market);
                return Symbol.CreateOption(underlyingEquitySymbol, market, OptionStyle.American, optionRight.Value, strikePrice.Value, expirationDate.Value);
            case SecurityType.Future:
                return Symbol.CreateFuture(ticker, market, expirationDate.Value);
            default:
                throw new NotSupportedException($"The security type '{securityType}' is not supported.");
        }
    }
}
