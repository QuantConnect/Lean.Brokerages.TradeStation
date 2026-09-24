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

using NUnit.Framework;
using QuantConnect.Tests;
using QuantConnect.Orders;
using QuantConnect.Tests.Brokerages;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    public partial class TradeStationBrokerageTests
    {
        private static readonly OrderTestParameters ContingentLimit = GetOrderTestParameters(OrderType.Limit, Symbols.AAPL, 1000m, 100m);
        private static readonly OrderTestParameters ContingentOtherLimit = GetOrderTestParameters(OrderType.Limit, Symbols.AAPL, 1010m, 90m);
        private static readonly OrderTestParameters ContingentStop = GetOrderTestParameters(OrderType.StopMarket, Symbols.AAPL, 1000m, 50m);
        private static readonly OrderTestParameters ContingentMarket = GetOrderTestParameters(OrderType.Market, Symbols.AAPL);

        /// <summary>
        /// Order groups (OCO, BRK) and order sends order (OSO), resting: the prices are far from the market
        /// </summary>
        private static TestCaseData[] RestingContingentOrders => new[]
        {
            new TestCaseData(ContingentOrderTestParameters.OneCancelsOther(ContingentLimit, ContingentOtherLimit)),
            new TestCaseData(ContingentOrderTestParameters.OneUpdatesOther(ContingentLimit, ContingentOtherLimit)),
            new TestCaseData(ContingentOrderTestParameters.OneTriggersOther(ContingentLimit, ContingentOtherLimit)),
            new TestCaseData(ContingentOrderTestParameters.Bracket(ContingentLimit, ContingentOtherLimit, ContingentStop))
        };

        /// <summary>
        /// Order sends order where the first order fills right away
        /// </summary>
        private static TestCaseData[] TriggeredContingentOrders => new[]
        {
            new TestCaseData(ContingentOrderTestParameters.OneTriggersOther(ContingentMarket, ContingentLimit)),
            new TestCaseData(ContingentOrderTestParameters.Bracket(ContingentMarket, ContingentLimit, ContingentStop))
        };

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
