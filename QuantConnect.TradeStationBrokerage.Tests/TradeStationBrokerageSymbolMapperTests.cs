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
using NUnit.Framework;
using System.Collections.Generic;
using QuantConnect.Brokerages.TradeStation.Models;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    [TestFixture]
    public class TradeStationBrokerageSymbolMapperTests
    {
        /// <inheritdoc cref="TradeStationSymbolMapper"/>
        private TradeStationSymbolMapper _symbolMapper;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _symbolMapper = new TradeStationSymbolMapper();
        }

        [TestCase("AAPL", "AAPL", TradeStationAssetType.Stock, null, TradeStationOptionType.Call, 0)]
        [TestCase("ESZ24", "ES", TradeStationAssetType.Future, "2024/12/20", TradeStationOptionType.Call, 0)]
        [TestCase("TSLA 240510C167.5", "TSLA", TradeStationAssetType.StockOption, "2024/5/10", TradeStationOptionType.Call, 167.5)]
        public void ReturnsCorrectLeanSymbol(string symbol, string underlying, TradeStationAssetType assetType,
            DateTime expirationDate, TradeStationOptionType optionType, decimal strikePrice)
        {
            var leg = new Leg("", 0m, 0m, 0m, "", symbol, underlying, assetType, 0m, expirationDate, optionType, strikePrice);

            var leanSymbol = _symbolMapper.GetLeanSymbol(leg.Underlying, leg.AssetType.ConvertAssetTypeToSecurityType(), Market.USA,
                leg.ExpirationDate, leg.StrikePrice, leg.OptionType.ConvertOptionTypeToOptionRight());

            Assert.IsNotNull(leanSymbol);
        }

        private static IEnumerable<TestCaseData> LeanSymbolTestCases
        {
            get
            {
                var underlying = Symbol.Create("AAPL", SecurityType.Equity, Market.USA);
                yield return new TestCaseData(underlying, "AAPL");
                yield return new TestCaseData(Symbol.CreateOption(underlying, Market.USA, OptionStyle.American, OptionRight.Call, 167.5m, new DateTime(2024, 5, 10)), "AAPL 240510C167.5");
                yield return new TestCaseData(Symbol.CreateOption(underlying, Market.USA, OptionStyle.American, OptionRight.Call, 100m, new DateTime(2025, 11, 12)), "AAPL 251112C100");
                yield return new TestCaseData(Symbol.CreateOption(underlying, Market.USA, OptionStyle.American, OptionRight.Put, 100m, new DateTime(2025, 11, 12)), "AAPL 251112P100");
                yield return new TestCaseData(Symbol.CreateFuture("ES", Market.USA, new DateTime(2024, 12, 10)), "ESZ24");
                yield return new TestCaseData(Symbol.CreateFuture("ES", Market.USA, new DateTime(2024, 5, 10)), "ESK24");
            }
        }

        [Test, TestCaseSource(nameof(LeanSymbolTestCases))]
        public void ReturnsCorrectBrokerageSymbol(Symbol symbol, string expectedBrokerageSymbol)
        {
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.IsNotNull(brokerageSymbol);
            Assert.IsNotEmpty(brokerageSymbol);
            Assert.That(brokerageSymbol, Is.EqualTo(expectedBrokerageSymbol));
        }

        [TestCase("ESZ24", "ES")]
        public void ParseTradeStationPositionSymbol(string ticker, string expectedTicker)
        {
            var symbol = SymbolRepresentation.ParseFutureTicker(ticker);

            Assert.IsNotNull(symbol);
            Assert.That(symbol.Underlying, Is.EqualTo(expectedTicker));
        }
    }
}