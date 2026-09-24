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
using System.Linq;
using NUnit.Framework;
using QuantConnect.Tests;
using QuantConnect.Orders;
using System.Collections.Generic;
using QuantConnect.Tests.Brokerages;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    public partial class TradeStationBrokerageTests
    {
        /// <summary>
        /// The symbols of each supported security type with prices far from the market, a high one and a low one.
        /// The futures require a futures account
        /// </summary>
        private static readonly (Symbol Symbol, decimal HighPrice, decimal LowPrice)[] ContingentOrderSymbols =
        [
            (Symbols.AAPL, 1000m, 100m),
            (Symbol.CreateOption(Symbols.AAPL, Market.USA, OptionStyle.American, OptionRight.Call, 340m, new DateTime(2026, 10, 2)), 100m, 1m),
            (Symbol.CreateOption(Symbols.SPX, "SPXW", Market.USA, OptionStyle.European, OptionRight.Call, 7675m, new DateTime(2026, 10, 2)), 1000m, 2m),
            (Symbol.CreateFuture("MES", Market.CME, new DateTime(2026, 12, 18)), 10000m, 5000m)
        ];

        /// <summary>
        /// Order groups (OCO, BRK) and order sends order (OSO), resting. A bracket group (BRK) requires a stop order
        /// </summary>
        private static IEnumerable<TestCaseData> RestingContingentOrders => ContingentOrderSymbols.SelectMany(x =>
        {
            var (limit, otherLimit, stop, _) = GetContingentOrderTestParameters(x.Symbol, x.HighPrice, x.LowPrice);
            return new[]
            {
                new TestCaseData(ContingentOrderTestParameters.OneCancelsOther(limit, otherLimit)),
                new TestCaseData(ContingentOrderTestParameters.OneUpdatesOther(limit, stop)),
                new TestCaseData(ContingentOrderTestParameters.OneTriggersOther(limit, otherLimit)),
                new TestCaseData(ContingentOrderTestParameters.Bracket(limit, otherLimit, stop))
            };
        });

        /// <summary>
        /// Order sends order where the first order fills right away
        /// </summary>
        private static IEnumerable<TestCaseData> TriggeredContingentOrders => ContingentOrderSymbols.SelectMany(x =>
        {
            var (limit, _, stop, market) = GetContingentOrderTestParameters(x.Symbol, x.HighPrice, x.LowPrice);
            return new[]
            {
                new TestCaseData(ContingentOrderTestParameters.OneTriggersOther(market, limit)),
                new TestCaseData(ContingentOrderTestParameters.Bracket(market, limit, stop))
            };
        });

        private static (OrderTestParameters Limit, OrderTestParameters OtherLimit, OrderTestParameters Stop, OrderTestParameters Market)
            GetContingentOrderTestParameters(Symbol symbol, decimal highPrice, decimal lowPrice)
        {
            return (GetOrderTestParameters(OrderType.Limit, symbol, highPrice, lowPrice),
                GetOrderTestParameters(OrderType.Limit, symbol, highPrice * 1.01m, lowPrice * 0.9m),
                GetOrderTestParameters(OrderType.StopMarket, symbol, highPrice, lowPrice / 2),
                GetOrderTestParameters(OrderType.Market, symbol));
        }

        [Test, Explicit("Requires a TradeStation account"), TestCaseSource(nameof(RestingContingentOrders))]
        public override void ContingentOrdersCancel(ContingentOrderTestParameters parameters)
        {
            base.ContingentOrdersCancel(parameters);
        }

        [Test, Explicit("Requires a TradeStation account"), TestCaseSource(nameof(RestingContingentOrders))]
        public override void ContingentOrdersUpdate(ContingentOrderTestParameters parameters)
        {
            base.ContingentOrdersUpdate(parameters);
        }

        [Test, Explicit("Requires a TradeStation account"), TestCaseSource(nameof(TriggeredContingentOrders))]
        public override void ContingentOrdersTrigger(ContingentOrderTestParameters parameters)
        {
            base.ContingentOrdersTrigger(parameters);
        }
    }
}
