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
using System.Threading;
using QuantConnect.Tests;
using QuantConnect.Orders;
using QuantConnect.Logging;
using System.Threading.Tasks;
using System.Collections.Generic;
using QuantConnect.Configuration;
using QuantConnect.Tests.Brokerages;
using QuantConnect.Algorithm.CSharp;
using QuantConnect.Orders.TimeInForces;
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
            Assert.GreaterOrEqual(accountBalances.Positions.Count(), 0);
        }

        [Test]
        public async Task GetOrders()
        {
            var ticker = "INTL";
            var orderDirection = TradeStationTradeActionType.Buy.ToString().ToUpper();
            var orderQuantity = 1m;
            var tradeStationApiClient = CreateTradeStationApiClient();

            var quoteLastPrice = (await tradeStationApiClient.GetQuoteSnapshot(ticker)).Quotes.Single().Last;

            var orderResponse = await tradeStationApiClient.PlaceOrder(
                Orders.OrderType.Limit,
                Orders.TimeInForce.GoodTilCanceled,
                orderQuantity,
                orderDirection,
                ticker,
                limitPrice: Math.Round(quoteLastPrice - 0.5m, 2));

            Assert.IsNotNull(orderResponse);
            Assert.IsNull(orderResponse.Errors);
            Assert.IsNotEmpty(orderResponse.Orders.First().OrderID);

            var orders = await tradeStationApiClient.GetOrders();

            Assert.IsNotNull(orders);
            var order = orders.Orders.First();
            Assert.IsInstanceOf<TradeStationOrderStatusType>(order.Status);
            Assert.IsInstanceOf<TradeStationOrderType>(order.OrderType);
            Assert.IsInstanceOf<TradeStationAssetType>(order.Legs.First().AssetType);
            Assert.IsInstanceOf<TradeStationOptionType>(order.Legs.First().OptionType);
            Assert.That(order.OpenedDateTime, Is.Not.EqualTo(default(DateTime)));

            var cancelResponse = await tradeStationApiClient.CancelOrder(orderResponse.Orders.First().OrderID);
            Assert.IsTrue(cancelResponse);
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
            var clientId = Config.Get("trade-station-client-id");
            var clientSecret = Config.Get("trade-station-client-secret");
            var apiUrl = Config.Get("trade-station-api-url");
            var redirectUrl = Config.Get("trade-station-redirect-url");

            var tradeStationApiClient = new TradeStationApiClient(clientId, clientSecret, apiUrl, TradeStationAccountType.Margin,
                string.Empty, redirectUrl, string.Empty);

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

        [TestCase("AAPL,INTL,TSLA,NVDA", Description = "Equtities")]
        [TestCase("AAPL,ESZ24", Description = "Equity|Future")]
        [TestCase("AAPL 240719C215,AAPL 240719C220,AAPL 240719C225,SPY 240719C250,SPY 240719C255,SPY 240719C260", Description = "Option")]
        public async Task GetStreamMarketData(string entranceSymbol)
        {
            Log.Debug($"{nameof(GetStreamMarketData)}: Starting...");

            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var locker = new object();
            var tradeStationApiClient = CreateTradeStationApiClient();

            var symbols = entranceSymbol.Split(',').ToDictionary(symbol => symbol, _ => 0);

            await foreach (var quote in tradeStationApiClient.StreamQuotes(symbols.Keys, cancellationTokenSource.Token))
            {
                Assert.IsNotNull(quote);

                Log.Debug($"{nameof(GetStreamMarketData)}.json: {quote}");

                lock (locker)
                {
                    symbols[quote.Symbol] += 1;
                }
            }
            Log.Debug($"{nameof(GetStreamMarketData)}.IsCancellationRequested: {cancellationTokenSource.IsCancellationRequested}");

            Assert.IsTrue(symbols.All((symbol) => symbol.Value > 0));
        }

        [TestCase(5, 1)]
        [TestCase(5, 100)]
        [TestCase(5, 200)]
        public async Task GetStreamQuotesRichRateLimit(int subscriptionTryCounter, int takeSymbolBeforeSubscriptionAmount)
        {
            Log.Debug($"{nameof(GetStreamQuotesRichRateLimit)}: Starting...");

            var locker = new object();
            var tradeStationApiClient = CreateTradeStationApiClient();

            var takeAmount = takeSymbolBeforeSubscriptionAmount;
            do
            {
                var symbols = StressSymbols.StockSymbols.Take(takeAmount).ToDictionary(symbol => symbol, _ => 0);
                takeAmount++;
                Log.Debug($"{nameof(GetStreamQuotesRichRateLimit)}: increase takeAmount = {takeAmount}");

                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                await foreach (var quote in tradeStationApiClient.StreamQuotes(symbols.Keys, cancellationTokenSource.Token))
                {
                    Assert.IsNotNull(quote);
                    Log.Debug($"{nameof(GetStreamQuotesRichRateLimit)}.json: {quote}");
                }
                Log.Debug($"{nameof(GetStreamQuotesRichRateLimit)}.IsCancellationRequested: {cancellationTokenSource.IsCancellationRequested}");
            } while (subscriptionTryCounter-- > 0);
        }

        [TestCase("AAPL")]
        public async Task GetOptionExpirations(string ticker)
        {
            var tradeStationApiClient = CreateTradeStationApiClient();

            await foreach (var optionContract in tradeStationApiClient.GetOptionExpirationsAndStrikes(ticker))
            {
                Assert.That(optionContract.expirationDate, Is.Not.EqualTo(default(DateTime)));
                Assert.Greater(optionContract.strikes.Count(), 0);
            }
        }

        [TestCase("AAPL", TradeStationUnitTimeIntervalType.Minute, "2024/06/18", "2024/07/18")]
        [TestCase("AAPL", TradeStationUnitTimeIntervalType.Hour, "2024/06/18", "2024/07/18")]
        [TestCase("AAPL", TradeStationUnitTimeIntervalType.Daily, "2024/06/18", "2024/07/18")]
        [TestCase("AAPL", TradeStationUnitTimeIntervalType.Minute, "2023/06/18", "2024/07/18")]
        [TestCase("AAPL", TradeStationUnitTimeIntervalType.Minute, "2024/02/12", "2024/03/23")]
        public async Task GetBars(string ticker, TradeStationUnitTimeIntervalType unitTime, DateTime startDate, DateTime endDate)
        {
            var tradeStationApiClient = CreateTradeStationApiClient();

            var bars = new List<TradeStationBar>();
            await foreach (var bar in tradeStationApiClient.GetBars(ticker, unitTime, startDate, endDate))
            {
                bars.Add(bar);
            }

            Assert.IsNotNull(bars);
            Assert.Greater(bars.Count, 0);

            AssertTimeIntervalBetweenDates(unitTime, bars[1].TimeStamp - bars[0].TimeStamp);
        }

        private static void AssertTimeIntervalBetweenDates(TradeStationUnitTimeIntervalType unitTime, TimeSpan differenceBetweenDates)
        {
            switch (unitTime)
            {
                case TradeStationUnitTimeIntervalType.Minute:
                    Assert.That(differenceBetweenDates, Is.EqualTo(Time.OneMinute));
                    break;
                case TradeStationUnitTimeIntervalType.Hour:
                    Assert.That(differenceBetweenDates, Is.EqualTo(Time.OneHour));
                    break;
                case TradeStationUnitTimeIntervalType.Daily:
                    Assert.That(differenceBetweenDates, Is.EqualTo(Time.OneDay));
                    break;
            }
        }

        [Test, Explicit("Pass the trade station account ID in the configuration explicitly.")]
        public void GetBrokerageCashBalanceByAccountId()
        {
            var clientId = Config.Get("trade-station-client-id");
            var clientSecret = Config.Get("trade-station-client-secret");
            var restApiUrl = Config.Get("trade-station-api-url");
            var accountType = Config.Get("trade-station-account-type");
            var refreshToken = Config.Get("trade-station-refresh-token");

            var accountId = Config.Get("trade-station-account-id");

            var brokerage = new TradeStationBrokerage(clientId, clientSecret, restApiUrl, string.Empty, string.Empty, refreshToken, accountType, null, null, accountId);

            var cashBalance = brokerage.GetCashBalance();

            Assert.IsNotEmpty(cashBalance);
            Assert.Greater(cashBalance[0].Amount, 0);
        }

        [TestCase(OrderType.Limit, "GoodTilCanceled", null, PlaceOrderDuration.GoodTillCanceled)]
        [TestCase(OrderType.Limit, "GoodTilCanceled", true, PlaceOrderDuration.GoodTillCanceledPlus)]
        [TestCase(OrderType.Limit, "GoodTilCanceled", false, PlaceOrderDuration.GoodTillCanceled)]
        [TestCase(OrderType.Limit, "Day", null, PlaceOrderDuration.Day)]
        [TestCase(OrderType.Limit, "Day", true, PlaceOrderDuration.DayPlus)]
        [TestCase(OrderType.Limit, "Day", false, PlaceOrderDuration.Day)]
        [TestCase(OrderType.Limit, "GoodTilDate", null, PlaceOrderDuration.GoodThroughDate)]
        [TestCase(OrderType.Limit, "GoodTilDate", true, PlaceOrderDuration.GoodThroughDatePlus)]
        [TestCase(OrderType.Limit, "GoodTilDate", false, PlaceOrderDuration.GoodThroughDate)]
        public void GetBrokerageTimeInForceByLeanTimeInForce(OrderType orderType, string timeInForceName, bool? outsideRegularTradingHours, PlaceOrderDuration expectedDuration)
        {
            var leanTimeInForce = default(Orders.TimeInForce);
            var orderExpiryTime = default(DateTime);
            switch (timeInForceName)
            {
                case "GoodTilCanceled":
                    leanTimeInForce = Orders.TimeInForce.GoodTilCanceled;
                    break;
                case "Day":
                    leanTimeInForce = Orders.TimeInForce.Day;
                    break;
                case "GoodTilDate":
                    orderExpiryTime = new DateTime(2025, 04, 29);
                    leanTimeInForce = Orders.TimeInForce.GoodTilDate(orderExpiryTime);
                    break;
                default:
                    throw new NotSupportedException($"{nameof(TradeStationBrokerageAdditionalTests)}.{nameof(GetBrokerageTimeInForceByLeanTimeInForce)}: The specified TimeInForce '{timeInForceName}' is not supported.");
            }

            var orderProperties = new OrderProperties() { TimeInForce = leanTimeInForce };
            if (outsideRegularTradingHours != null)
            {
                orderProperties = new TradeStationOrderProperties() { TimeInForce = orderProperties.TimeInForce, OutsideRegularTradingHours = outsideRegularTradingHours.Value };
            }

            var (actualDuration, actualExpiryDateTime) = TradeStationExtensions.GetBrokerageTimeInForce(leanTimeInForce, orderType, (orderProperties as TradeStationOrderProperties)?.OutsideRegularTradingHours ?? false);

            Assert.AreEqual(expectedDuration, actualDuration);

            if (timeInForceName.Equals("GoodTilDate", StringComparison.InvariantCultureIgnoreCase))
            {
                Assert.AreEqual(orderExpiryTime.ToIso8601Invariant(), actualExpiryDateTime);
            }
        }

        [TestCase(TradeStationDuration.DayPlus, true)]
        [TestCase(TradeStationDuration.Day, false)]
        [TestCase(TradeStationDuration.GoodTillCanceledPlus, true)]
        [TestCase(TradeStationDuration.GoodTillCanceled, false)]
        [TestCase(TradeStationDuration.GoodThroughDatePlus, true)]
        [TestCase(TradeStationDuration.GoodThroughDate, false)]
        public void GetLeanTimeInForceByBrokerageTimeInForce(TradeStationDuration brokerageDuration, bool expectedOutsideRegularTradingHours)
        {
            var goodTillDate = new DateTime(2025, 04, 29);
            var orderProperties = new TradeStationOrderProperties();
            Assert.True(orderProperties.GetLeanTimeInForce(brokerageDuration, goodTillDate));

            Assert.AreEqual(expectedOutsideRegularTradingHours, orderProperties.OutsideRegularTradingHours);

            switch (brokerageDuration)
            {
                case TradeStationDuration.Day:
                case TradeStationDuration.DayPlus:
                    Assert.That(orderProperties.TimeInForce, Is.TypeOf<DayTimeInForce>());
                    break;
                case TradeStationDuration.GoodTillCanceled:
                case TradeStationDuration.GoodTillCanceledPlus:
                    Assert.That(orderProperties.TimeInForce, Is.TypeOf<GoodTilCanceledTimeInForce>());
                    break;
                case TradeStationDuration.GoodThroughDate:
                case TradeStationDuration.GoodThroughDatePlus:
                    Assert.That(orderProperties.TimeInForce, Is.TypeOf<GoodTilDateTimeInForce>());
                    Assert.AreEqual(goodTillDate, (orderProperties.TimeInForce as GoodTilDateTimeInForce).Expiry);
                    break;
            }
        }

        [Test]
        public void CancelsOrderWhenStatusIsExpiredFromTradeStation()
        {
            var resetEvent = new AutoResetEvent(false);
            var orderProvider = new OrderProvider();
            var ts = TestSetup.CreateBrokerage(orderProvider, default);

            ts.OrdersStatusChanged += (_, orderEvents) =>
            {
                if (orderEvents[0].Status == OrderStatus.Canceled)
                {
                    resetEvent.Set();
                }
            };

            ts.Connect();

            var moo = new MarketOnCloseOrder(Symbols.AAPL, -75, DateTime.UtcNow)
            {
                Status = OrderStatus.Submitted
            };
            moo.BrokerId.Add("906146665");

            orderProvider.Add(moo);

            var json = @"{
                ""AccountID"": ""SIM2784990M"",
                ""ClosedDateTime"": ""2025-08-05T20:00:05Z"",
                ""CommissionFee"": ""0"",
                ""Currency"": ""USD"",
                ""Duration"": ""CLO"",
                ""FilledPrice"": ""0"",
                ""Legs"": [
                    {
                        ""AssetType"": ""STOCK"",
                        ""BuyOrSell"": ""SellShort"",
                        ""ExecQuantity"": ""0"",
                        ""OpenOrClose"": ""Open"",
                        ""QuantityOrdered"": ""75"",
                        ""QuantityRemaining"": ""0"",
                        ""Symbol"": ""AAPL""
                    }
                ],
                ""OpenedDateTime"": ""2025-08-05T14:34:00Z"",
                ""OrderID"": ""906146665"",
                ""OrderType"": ""Market"",
                ""PriceUsedForBuyingPower"": ""204.51"",
                ""Routing"": ""Intelligent"",
                ""Status"": ""EXP"",
                ""StatusDescription"": ""Expired"",
                ""UnbundledRouteFee"": ""0""
            }";

            ts.HandleTradeStationMessage(json);

            Assert.IsTrue(resetEvent.WaitOne(TimeSpan.FromSeconds(1)));
        }

        private TradeStationApiClient CreateTradeStationApiClient()
        {
            var clientId = Config.Get("trade-station-client-id");
            var clientSecret = Config.Get("trade-station-client-secret");
            var apiUrl = Config.Get("trade-station-api-url");
            var accountType = Config.Get("trade-station-account-type");

            if (new string[] { clientId, clientSecret, apiUrl, accountType }.Any(string.IsNullOrEmpty))
            {
                throw new ArgumentException("API key, secret, and URL cannot be empty or null. Please ensure these values are correctly set in the configuration file.");
            }

            var refreshToken = Config.Get("trade-station-refresh-token");

            if (string.IsNullOrEmpty(refreshToken))
            {
                var authorizationCode = Config.Get("trade-station-authorization-code");
                var redirectUrl = Config.Get("trade-station-redirect-url");

                if (new string[] { authorizationCode, redirectUrl }.Any(string.IsNullOrEmpty))
                {
                    throw new ArgumentException("API key, secret, and URL cannot be empty or null. Please ensure these values are correctly set in the configuration file.");
                }

                return new TradeStationApiClient(clientId, clientSecret, apiUrl, TradeStationExtensions.ParseAccountType(accountType), string.Empty,
                    redirectUrl, authorizationCode);
            }

            return new TradeStationApiClient(clientId, clientSecret, apiUrl, TradeStationExtensions.ParseAccountType(accountType), refreshToken, string.Empty, string.Empty);
        }
    }
}