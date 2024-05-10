﻿/*
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
            var leg = new Leg("", 0m, 0m, 0m, "", symbol, underlying, assetType, "", expirationDate, optionType, strikePrice);

            var leanSymbol = _symbolMapper.GetLeanSymbol(leg.Underlying, leg.AssetType.ConvertAssetTypeToSecurityType(), Market.USA,
                leg.ExpirationDate, leg.StrikePrice, leg.OptionType.ConvertOptionTypeToOptionRight());

            Assert.IsNotNull(leanSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol()
        {

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