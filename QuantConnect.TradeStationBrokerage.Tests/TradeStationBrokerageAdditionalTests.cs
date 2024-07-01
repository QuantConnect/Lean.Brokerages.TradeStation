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
using Newtonsoft.Json;
using NUnit.Framework;
using QuantConnect.Logging;
using System.Threading.Tasks;
using QuantConnect.Configuration;
using QuantConnect.Brokerages.TradeStation.Api;
using QuantConnect.Brokerages.TradeStation.Models;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation.Tests
{
    [TestFixture]
    public class TradeStationBrokerageAdditionalTests
    {
        [Test]
        public void DeserializeBalancesErrorResponse()
        {
            string jsonResponse = @"{
                ""Balances"": [ ],
                ""Errors"": [ 
                    { ""AccountID"": ""123456782C"",
                      ""Error"": ""Forbidden"",
                      ""Message"": ""Request not supported for account type.""
                    }
             ]}";

            var res = JsonConvert.DeserializeObject<TradeStationBalance>(jsonResponse);

            Assert.IsNotNull(res);
            Assert.Greater(res.Errors.Count(), 0);
            Assert.That(res.Errors.First().AccountID, Is.EqualTo("123456782C"));
            Assert.That(res.Errors.First().Error, Is.EqualTo("Forbidden"));
            Assert.That(res.Errors.First().Message, Is.EqualTo("Request not supported for account type."));
        }

        [TestCase(@"{ ""Orders"":[ { ""Legs"": [ { ""BuyOrSell"": ""BUY"" } ] } ],""Errors"":[] }", TradeStationTradeActionType.Buy)]
        [TestCase(@"{ ""Orders"":[ { ""Legs"": [ { ""BuyOrSell"": ""Buy"" } ] } ],""Errors"":[] }", TradeStationTradeActionType.Buy)]
        [TestCase(@"{ ""Orders"":[ { ""Legs"": [ { ""BuyOrSell"": ""SELL"" } ] } ],""Errors"":[] }", TradeStationTradeActionType.Sell)]
        [TestCase(@"{ ""Orders"":[ { ""Legs"": [ { ""BuyOrSell"": ""Sell"" } ] } ],""Errors"":[] }", TradeStationTradeActionType.Sell)]
        [TestCase(@"{ ""Orders"":[ { ""Legs"": [ { ""BuyOrSell"": ""BUYTOCOVER"" } ] } ],""Errors"":[] }", TradeStationTradeActionType.BuyToCover)]
        [TestCase(@"{ ""Orders"":[ { ""Legs"": [ { ""BuyOrSell"": ""BuyToCover"" } ] } ],""Errors"":[] }", TradeStationTradeActionType.BuyToCover)]
        public void DeserializeTradeStationTradeActionType(string json, TradeStationTradeActionType expectedActionType)
        {
            var result = JsonConvert.DeserializeObject<TradeStationOrderResponse>(json);
            var actualActionType = result.Orders.First().Legs.First().BuyOrSell;
            Assert.That(actualActionType, Is.EqualTo(expectedActionType));
        }

        [Test]
        public void GetTradeStationAccountsBalance()
        {
            var tradeStationApiClient = CreateTradeStationApiClient();

            var accountBalances = tradeStationApiClient.GetAccountBalance().SynchronouslyAwaitTaskResult();

            Assert.Greater(accountBalances.Balances.Count(), 0);
            Assert.That(accountBalances.Errors.Count(), Is.EqualTo(0));
        }

        [Test]
        public void GetTradeStationPositions()
        {
            var tradeStationApiClient = CreateTradeStationApiClient();
            var accountBalances = tradeStationApiClient.GetAccountPositions().SynchronouslyAwaitTaskResult();

            Assert.IsNotNull(accountBalances);
            Assert.Greater(accountBalances.Positions.Count(), 0);
        }

        [Test]
        public async Task GetOrders()
        {
            var tradeStationApiClient = CreateTradeStationApiClient();

            var orders = await tradeStationApiClient.GetOrders();

            Assert.IsNotNull(orders);

            var order = orders.Orders.First();

            Assert.IsInstanceOf<TradeStationOrderStatusType>(order.Status);
            Assert.IsInstanceOf<TradeStationOrderType>(order.OrderType);
            Assert.IsInstanceOf<TradeStationAssetType>(order.Legs.First().AssetType);
            Assert.IsInstanceOf<TradeStationOptionType>(order.Legs.First().OptionType);
            Assert.That(order.OpenedDateTime, Is.Not.EqualTo(default(DateTime)));
        }

        [Test]
        public async Task CancelOrder()
        {
            var tradeStationApiClient = CreateTradeStationApiClient();

            var result = await tradeStationApiClient.CancelOrder("833286672");

            Assert.IsNotNull(result);
        }

        [Test]
        public void GetSignInUrl()
        {
            var apiKey = Config.Get("trade-station-api-key");
            var apiSecret = Config.Get("trade-station-api-secret");
            var apiUrl = Config.Get("trade-station-api-url");
            var redirectUrl = Config.Get("trade-station-redirect-url");

            var tradeStationApiClient = new TradeStationApiClient(apiKey, apiSecret, apiUrl, redirectUrl, TradeStationAccountType.Margin);

            var signInUrl = tradeStationApiClient.GetSignInUrl();
            Assert.IsNotNull(signInUrl);
            Assert.IsNotEmpty(signInUrl);
            Log.Trace($"{nameof(TradeStationBrokerageAdditionalTests)}.{nameof(GetSignInUrl)}: SignInUrl: {signInUrl}");
        }

        [TestCase("AAPL")]
        public async Task GetQuoteSnapshot(string ticker)
        {
            var tradeStationApiClient = CreateTradeStationApiClient();

            var quoteSnapshot = await tradeStationApiClient.GetQuoteSnapshot(ticker);

            Assert.IsNotNull(quoteSnapshot);
            Assert.Greater(quoteSnapshot.Quotes.Count(), 0);
            Assert.Greater(quoteSnapshot.Quotes.First().Ask, 0);
            Assert.Greater(quoteSnapshot.Quotes.First().AskSize, 0);
            Assert.Greater(quoteSnapshot.Quotes.First().Bid, 0);
            Assert.Greater(quoteSnapshot.Quotes.First().BidSize, 0);
        }

        private TradeStationApiClient CreateTradeStationApiClient()
        {
            var apiKey = Config.Get("trade-station-api-key");
            var apiSecret = Config.Get("trade-station-api-secret");
            var apiUrl = Config.Get("trade-station-api-url");
            var authorizationCodeFromUrl = Config.Get("trade-station-code-from-url");
            var redirectUrl = Config.Get("trade-station-redirect-url");
            var accountType = Config.Get("trade-station-account-type");

            if (new string[] { apiKey, apiSecret, apiUrl }.Any(string.IsNullOrEmpty))
            {
                throw new ArgumentException("API key, secret, and URL cannot be empty or null. Please ensure these values are correctly set in the configuration file.");
            }

            return new TradeStationApiClient(apiKey, apiSecret, apiUrl, redirectUrl,
                TradeStationExtensions.ParseAccountType(accountType), authorizationCodeFromUrl);
        }
    }
}