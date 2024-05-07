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
using QuantConnect.Util;
using QuantConnect.Logging;
using QuantConnect.Interfaces;
using QuantConnect.Configuration;
using QuantConnect.Brokerages.TradeStation.Api;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    [TestFixture]
    public class TradeStationBrokerageAdditionalTests
    {
        [Test]
        public void ParameterlessConstructorComposerUsage()
        {
            var brokerage = Composer.Instance.GetExportedValueByTypeName<IDataQueueHandler>("TradeStationBrokerage");
            Assert.IsNotNull(brokerage);
        }

        [Test]
        public void GetTradeStationAccounts()
        {
            var tradeStationApiClient = CreateTradeStationApiClient(true);

            var result = tradeStationApiClient.GetAccounts();

            Assert.IsNotNull(result);
            Assert.Greater(result.Count(), 0);
        }

        [Test]
        public void GetSignInUrl()
        {
            var tradeStationApiClient = CreateTradeStationApiClient(false);

            var signInUrl = tradeStationApiClient.GetSignInUrl();
            Assert.IsNotNull(signInUrl);
            Assert.IsNotEmpty(signInUrl);
            Log.Trace($"{nameof(TradeStationBrokerageAdditionalTests)}.{nameof(GetSignInUrl)}: SignInUrl: {signInUrl}");
        }

        private TradeStationApiClient CreateTradeStationApiClient(bool withAuthorizationCodeFromUr = true)
        {
            var apiKey = Config.Get("trade-station-api-key");
            var apiSecret = Config.Get("trade-station-api-secret");
            var apiUrl = Config.Get("trade-station-api-url");

            if (new string[] { apiKey, apiSecret, apiUrl }.Any(string.IsNullOrEmpty))
            {
                throw new ArgumentException("API key, secret, and URL cannot be empty or null. Please ensure these values are correctly set in the configuration file.");
            }

            if (withAuthorizationCodeFromUr)
            {
                var authorizationCodeFromUrl = Config.Get("trade-station-code-from-url");

                if (string.IsNullOrEmpty(authorizationCodeFromUrl))
                {
                    throw new ArgumentException("The authorization code from url cannot be empty or null. Please ensure these values are correctly set in the configuration file.");
                }

                return new TradeStationApiClient(apiKey, apiSecret, apiUrl, authorizationCodeFromUrl);
            }

            return new TradeStationApiClient(apiKey, apiSecret, apiUrl);
        }
    }
}