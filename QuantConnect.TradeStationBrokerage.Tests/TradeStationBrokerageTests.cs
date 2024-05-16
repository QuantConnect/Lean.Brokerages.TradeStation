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

using Moq;
using System;
using System.Linq;
using NUnit.Framework;
using QuantConnect.Tests;
using QuantConnect.Orders;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Configuration;
using System.Collections.Generic;
using QuantConnect.Tests.Brokerages;
using QuantConnect.Lean.Engine.DataFeeds;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    [TestFixture]
    public partial class TradeStationBrokerageTests : BrokerageTests
    {
        protected override Symbol Symbol { get; }

        protected override SecurityType SecurityType { get; }

        protected override IBrokerage CreateBrokerage(IOrderProvider orderProvider, ISecurityProvider securityProvider)
        {
            var algorithm = new Mock<IAlgorithm>();

            var apiKey = Config.Get("trade-station-api-key");
            var apiSecret = Config.Get("trade-station-api-secret");
            var apiUrl = Config.Get("trade-station-api-url");
            var authorizationCodeFromUrl = Config.Get("trade-station-code-from-url");
            var accountType = Config.Get("trade-station-account-type");

            if (new string[] { apiKey, apiSecret, apiUrl, authorizationCodeFromUrl }.Any(string.IsNullOrEmpty))
            {
                throw new ArgumentException("API key, secret, and URL cannot be empty or null. Please ensure these values are correctly set in the configuration file.");
        }

            return new TradeStationBrokerage(apiKey, apiSecret, apiUrl, authorizationCodeFromUrl, accountType, orderProvider, useProxy: true);
        }
        protected override bool IsAsync()
        {
            return false;
        }

        protected override decimal GetAskPrice(Symbol symbol)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Provides the data required to test each order type in various cases
        /// </summary>
        private static IEnumerable<TestCaseData> OrderParameters
        {
            get
            {
                yield return new TestCaseData(new LimitOrderTestParameters(Symbols.AAPL, 200m, 170m));
                yield return new TestCaseData(new StopMarketOrderTestParameters(Symbols.AAPL, 200m, 170m));
                yield return new TestCaseData(new StopLimitOrderTestParameters(Symbols.AAPL, 200m, 170m));
            }
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CancelOrders(OrderTestParameters parameters)
        {
            base.CancelOrders(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void LongFromZero(OrderTestParameters parameters)
        {
            base.LongFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CloseFromLong(OrderTestParameters parameters)
        {
            base.CloseFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void ShortFromZero(OrderTestParameters parameters)
        {
            base.ShortFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CloseFromShort(OrderTestParameters parameters)
        {
            base.CloseFromShort(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void ShortFromLong(OrderTestParameters parameters)
        {
            base.ShortFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void LongFromShort(OrderTestParameters parameters)
        {
            base.LongFromShort(parameters);
        }
    }
}