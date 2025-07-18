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
using QuantConnect.Tests;
using QuantConnect.Securities;
using System.Collections.Generic;
using QuantConnect.Brokerages.TradeStation.Models;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    [TestFixture]
    public class TradeStationBrokerageSymbolMapperTests
    {
        /// <summary>
        /// Provides the mapping between Lean symbols and brokerage specific symbols.
        /// </summary>
        private TradeStationSymbolMapperTest _symbolMapper;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _symbolMapper = new TradeStationSymbolMapperTest();
        }


        private class TradeStationSymbolMapperTest : TradeStationSymbolMapper
        {
            public (string symbol, OptionRight optionRight, decimal strikePrice) PublicParsePositionOptionSymbol(string optionSymbol)
            {
                return ParsePositionOptionSymbol(optionSymbol);
            }
        }


        private static IEnumerable<LegSymbol> BrokerageSymbolTestCases
        {
            get
            {
                var APPLLeg = new Leg("", 0m, 0m, 0m, TradeStationTradeActionType.Buy, "AAPL", "AAPL", TradeStationAssetType.Stock, 0m, default, default, default);
                yield return new(APPLLeg, Symbols.AAPL);

                var ESLeg = new Leg("", 0m, 0m, 0m, TradeStationTradeActionType.Buy, "ESZ24", "ES", TradeStationAssetType.Future, 0m, new DateTime(2024, 12, 10), default, default);
                var SP500EMini = Symbol.CreateFuture(Futures.Indices.SP500EMini, Market.CME, new DateTime(2024, 12, 10));
                yield return new(ESLeg, SP500EMini);

                var NGLeg = new Leg("", 0m, 0m, 0m, TradeStationTradeActionType.Buy, "NGU25", "NG", TradeStationAssetType.Future, 0m, new DateTime(2025, 08, 29), default, default);
                var NaturalGas = Symbol.CreateFuture(Futures.Energy.NaturalGas, Market.NYMEX, new DateTime(2025, 08, 29));
                yield return new(NGLeg, NaturalGas);

                var CTLeg = new Leg("", 0m, 0m, 0m, TradeStationTradeActionType.Buy, "CTV24", "CT", TradeStationAssetType.Future, 0m, new DateTime(2024, 10, 1), default, default);
                var COTTON = Symbol.CreateFuture(Futures.Softs.Cotton2, Market.ICE, new DateTime(2024, 10, 1));
                yield return new(CTLeg, COTTON);

                var TSLAExpiryDate = new DateTime(2024, 5, 10);
                var TSLALeg = new Leg("", 0m, 0m, 0m, TradeStationTradeActionType.Buy, "TSLA 240510C167.5", "TSLA", TradeStationAssetType.StockOption, 0m, TSLAExpiryDate, TradeStationOptionType.Call, 167.5m);
                var TSLA = Symbol.Create("TSLA", SecurityType.Equity, Market.USA);
                var TSLAOption = Symbol.CreateOption(TSLA, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 167.5m, TSLAExpiryDate);
                yield return new(TSLALeg, TSLAOption);

                var RUTIndexLeg = new Leg("", 0m, 0m, 0m, TradeStationTradeActionType.Buy, "$RUT.X", "$RUT.X", TradeStationAssetType.Index, 0m, default, default, default);
                var RUT = Symbol.Create("RUT", SecurityType.Index, Market.USA);
                yield return new(RUTIndexLeg, RUT);

                var RUTWIndexOptionLeg = new Leg("", 0m, 0m, 0m, TradeStationTradeActionType.Buy, "RUTW 241206C2415", "$RUT.X", TradeStationAssetType.IndexOption, 0m, new DateTime(2024, 12, 6), TradeStationOptionType.Call, 2415m);
                var RUTWOption = Symbol.CreateOption(RUT, "RUTW", Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Call, 2415m, new DateTime(2024, 12, 6));
                yield return new(RUTWIndexOptionLeg, RUTWOption);

                var VIXIndexLeg = new Leg("", 0m, 0m, 0m, TradeStationTradeActionType.Buy, "$VIX.X", "$VIX.X", TradeStationAssetType.Index, 0m, default, default, default);
                var VIX = Symbol.Create("VIX", SecurityType.Index, Market.USA);
                yield return new(VIXIndexLeg, VIX);

                var VIXWeeklyLeg = new Leg("", 0m, 0m, 0m, TradeStationTradeActionType.Buy, "VIXW 241211C14.5", "$VIX.X", TradeStationAssetType.IndexOption, 0m, new DateTime(2024, 12, 11), TradeStationOptionType.Call, 14.5m);
                var VIXWeeklyOption = Symbol.CreateOption(VIX, "VIXW", Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Call, 14.5m, new DateTime(2024, 12, 11));
                yield return new(VIXWeeklyLeg, VIXWeeklyOption);
            }
        }

        [Test, TestCaseSource(nameof(BrokerageSymbolTestCases))]
        public void ReturnsCorrectLeanSymbol(LegSymbol metaData)
        {
            var (brokerageLeg, expectedLeanSymbol) = metaData;

            var ticker = brokerageLeg.Symbol;
            var optionRight = default(OptionRight);
            var strikePrice = default(decimal);
            var isIndexOrFuture = false;
            switch (brokerageLeg.AssetType)
            {
                case TradeStationAssetType.IndexOption:
                case TradeStationAssetType.StockOption:
                    (ticker, optionRight, strikePrice) = _symbolMapper.PublicParsePositionOptionSymbol(brokerageLeg.Symbol);
                    break;

                case TradeStationAssetType.Index:
                case TradeStationAssetType.Future:
                    isIndexOrFuture = true;
                    break;
            }

            var actualLeanSymbol = default(Symbol);
            if (!isIndexOrFuture)
            {
                actualLeanSymbol = _symbolMapper.GetLeanSymbol(ticker, brokerageLeg.AssetType.ConvertAssetTypeToSecurityType(),
                    expirationDate: brokerageLeg.ExpirationDate, strike: strikePrice, optionRight: optionRight);
            }
            else
            {
                Assert.IsTrue(_symbolMapper.TryGetLeanSymbol(ticker, brokerageLeg.AssetType, brokerageLeg.ExpirationDate,
                    out actualLeanSymbol));
            }

            Assert.That(actualLeanSymbol, Is.EqualTo(expectedLeanSymbol));
        }

        private static IEnumerable<LegSymbol> NotSupportedBrokerageSymbolTestCases
        {
            get
            {
                var EURUSDLeg = new Leg("", 0m, 0m, 0m, TradeStationTradeActionType.Buy, "EURUSD", "EURUSD", TradeStationAssetType.Forex, 0m, default, default, default);
                var EURUSD = Symbol.Create("EURUSD", SecurityType.Forex, Market.Oanda);
                yield return new(EURUSDLeg, EURUSD);
            }
        }

        [TestCaseSource(nameof(NotSupportedBrokerageSymbolTestCases))]
        public void TryConvertNotSupportedBrokerageSymbolToLeanSymbol(LegSymbol metaData)
        {
            var (brokerageLeg, expectedLeanSymbol) = metaData;

            Assert.Throws<NotImplementedException>(() => _symbolMapper.GetLeanSymbol(brokerageLeg.Symbol, brokerageLeg.AssetType.ConvertAssetTypeToSecurityType(), expirationDate: brokerageLeg.ExpirationDate));
        }


        private static IEnumerable<TestCaseData> LeanSymbolTestCases
        {
            get
            {
                TestGlobals.Initialize();
                var underlying = Symbol.Create("AAPL", SecurityType.Equity, Market.USA);
                yield return new TestCaseData(underlying, "AAPL");
                yield return new TestCaseData(Symbol.CreateOption(underlying, Market.USA, OptionStyle.American, OptionRight.Call, 167.5m, new DateTime(2024, 5, 10)), "AAPL 240510C167.5");
                yield return new TestCaseData(Symbol.CreateOption(underlying, Market.USA, OptionStyle.American, OptionRight.Call, 100m, new DateTime(2025, 11, 12)), "AAPL 251112C100");
                yield return new TestCaseData(Symbol.CreateOption(underlying, Market.USA, OptionStyle.American, OptionRight.Put, 100m, new DateTime(2025, 11, 12)), "AAPL 251112P100");
                yield return new TestCaseData(Symbol.CreateFuture("ES", Market.USA, new DateTime(2024, 12, 10)), "ESZ24");
                yield return new TestCaseData(Symbol.CreateFuture("ES", Market.USA, new DateTime(2024, 5, 10)), "ESK24");
                var indexUnderlying = Symbol.Create("RUTW", SecurityType.Index, Market.USA);
                yield return new TestCaseData(indexUnderlying, "$RUTW.X");
                yield return new TestCaseData(Symbol.CreateOption(indexUnderlying, Market.USA, OptionStyle.American, OptionRight.Call, 2415m, new DateTime(2024, 12, 6)), "RUTW 241206C2415");
                var indexUnderlying2 = Symbol.Create("VIX", SecurityType.Index, Market.USA);
                yield return new TestCaseData(indexUnderlying2, "$VIX.X");
                yield return new TestCaseData(Symbol.CreateOption(indexUnderlying2, Market.USA, OptionStyle.American, OptionRight.Call, 14.5m, new DateTime(2024, 12, 18)), "VIX 241218C14.5");

                var VIXWeeklyOption = Symbol.CreateOption(indexUnderlying2, "VIXW", Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Call, 14.5m, new DateTime(2024, 12, 11));
                yield return new TestCaseData(VIXWeeklyOption, "VIXW 241211C14.5");

                // GOOGL - Equity
                var GOOGL = Symbol.Create("GOOGL", SecurityType.Equity, Market.USA);
                yield return new TestCaseData(GOOGL, "GOOGL");
                yield return new TestCaseData(Symbol.CreateOption(GOOGL, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Put, 180m, new DateTime(2024, 12, 06)), "GOOGL 241206P180");
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

        [TestCase("AAPL 240517C185", "AAPL", OptionRight.Call, 185)]
        [TestCase("AAPL 111111C111.1", "AAPL", OptionRight.Call, 111.1)]
        [TestCase("AAPL 111111P111.1", "AAPL", OptionRight.Put, 111.1)]
        [TestCase("AAPL 240517C187.5", "AAPL", OptionRight.Call, 187.5)]
        [TestCase("T 240517C16", "T", OptionRight.Call, 16)]
        [TestCase("TT 240517C300", "TT", OptionRight.Call, 300)]
        [TestCase("TT 250618C300", "TT", OptionRight.Call, 300)]
        [TestCase("NANOS 241206C605", "NANOS", OptionRight.Call, 605)]
        [TestCase("RUTW 241206C2415", "RUTW", OptionRight.Call, 2415)]
        public void ParseTradeStationPositionOptionSymbol(string ticker, string expectedSymbol, OptionRight expectedRight, decimal expectedStrikePrice)
        {
            var optionParam = _symbolMapper.PublicParsePositionOptionSymbol(ticker);

            Assert.IsNotNull(optionParam);
            Assert.That(optionParam.symbol, Is.EqualTo(expectedSymbol));
            Assert.That(optionParam.optionRight, Is.EqualTo(expectedRight));
            Assert.That(optionParam.strikePrice, Is.EqualTo(expectedStrikePrice));
        }

        public static IEnumerable<TestCaseData> GetFutureSymbolsTestCases()
        {
            yield return new TestCaseData("ESZ24", Symbol.CreateFuture(Futures.Indices.SP500EMini, Market.CME, new DateTime(2024, 12, 10)));

            // Natural gas futures expire the month previous to the contract month:
            // Expiry: August -> Contract month: September (U)
            yield return new TestCaseData("NGU25", Symbol.CreateFuture(Futures.Energy.NaturalGas, Market.NYMEX, new DateTime(2025, 08, 29)));
            // Expiry: December 2025 -> Contract month: January (U) 2026 (26)
            yield return new TestCaseData("NGF26", Symbol.CreateFuture(Futures.Energy.NaturalGas, Market.NYMEX, new DateTime(2025, 12, 29)));

            // BrentLastDayFinancial futures expire two months previous to the contract month:
            // Expiry: August -> Contract month: October (V)
            yield return new TestCaseData("BZV25", Symbol.CreateFuture(Futures.Energy.BrentLastDayFinancial, Market.NYMEX, new DateTime(2025, 08, 29)));
            // Expiry: November 2025 -> Contract month: January (F) 2026 (26)
            yield return new TestCaseData("BZF26", Symbol.CreateFuture(Futures.Energy.BrentLastDayFinancial, Market.NYMEX, new DateTime(2025, 11, 29)));
            // Expiry: December 2025 -> Contract month: February (G) 2026 (26)
            yield return new TestCaseData("BZG26", Symbol.CreateFuture(Futures.Energy.BrentLastDayFinancial, Market.NYMEX, new DateTime(2025, 12, 29)));
        }

        [TestCaseSource(nameof(GetFutureSymbolsTestCases))]
        public void ConvertsFutureSymbolRoundTrip(string brokerageSymbol, Symbol leanSymbol)
        {
            var expiry = leanSymbol.ID.Date;
            Assert.IsTrue(_symbolMapper.TryGetLeanSymbol(brokerageSymbol, TradeStationAssetType.Future, expiry,
                out var convertedLeanSymbol));
            Assert.AreEqual(leanSymbol, convertedLeanSymbol);

            var convertedBrokerageSymbol = _symbolMapper.GetBrokerageSymbol(convertedLeanSymbol);
            Assert.AreEqual(brokerageSymbol, convertedBrokerageSymbol);
        }
    }
}