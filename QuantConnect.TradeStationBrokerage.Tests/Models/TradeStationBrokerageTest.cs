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

using System.Linq;
using QuantConnect.Orders;
using QuantConnect.Securities;
using System.Collections.Generic;
using QuantConnect.Logging;

namespace QuantConnect.Brokerages.TradeStation.Tests;

public class TradeStationBrokerageTest : TradeStationBrokerage
{
    /// <summary>
    /// Constructor for the TradeStation brokerage.
    /// </summary>
    /// <remarks>
    /// This constructor initializes a new instance of the TradeStationBrokerage class with the provided parameters.
    /// </remarks>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="apiKeySecret">The API key secret for authentication.</param>
    /// <param name="restApiUrl">The URL of the REST API.</param>
    /// <param name="redirectUrl">The redirect URL to generate great link to get right "authorizationCodeFromUrl"</param>
    /// <param name="authorizationCode">The authorization code obtained from the URL.</param>
    /// <param name="refreshToken">The refresh token used to obtain new access tokens for authentication.</param>
    /// <param name="accountType">The type of TradeStation account for the current session.
    /// For <see cref="TradeStationAccountType.Cash"/> or <seealso cref="TradeStationAccountType.Margin"/> accounts, it is used for trading <seealso cref="SecurityType.Equity"/> and <seealso cref="SecurityType.Option"/>.
    /// For <seealso cref="TradeStationAccountType.Futures"/> accounts, it is used for trading <seealso cref="SecurityType.Future"/> contracts.</param>
    /// <param name="orderProvider">The order provider.</param>
    /// <param name="accountId">The specific user account id.</param>
    public TradeStationBrokerageTest(string apiKey, string apiKeySecret, string restApiUrl, string redirectUrl,
        string authorizationCode, string refreshToken, string accountType, IOrderProvider orderProvider, ISecurityProvider securityProvider, string accountId = "")
        : base(apiKey, apiKeySecret, restApiUrl, redirectUrl, authorizationCode, refreshToken, accountType, orderProvider, securityProvider, accountId)
    { }

    /// <summary>
    /// Retrieves the last price of the specified symbol.
    /// </summary>
    /// <param name="symbol">The symbol for which to retrieve the last price.</param>
    /// <returns>The last price of the specified symbol as a decimal.</returns>
    public QuoteActualPrices GetPrice(Symbol symbol)
    {
        var quotes = GetQuote(symbol).Quotes.Single();
        Log.Trace($"{nameof(TradeStationBrokerageTest)}.{nameof(GetPrice)}: {symbol}: Ask = {quotes.Ask}, Bid = {quotes.Bid}, Last = {quotes.Last}");
        return new(quotes.Ask.Value, quotes.Bid.Value, quotes.Last.Value);
    }

    /// <summary>
    /// Holds ask, bid, and last prices.
    /// </summary>
    /// <param name="Ask">Ask price.</param>
    /// <param name="Bid">Bid price.</param>
    /// <param name="Last">Last traded price.</param>
    public record QuoteActualPrices(decimal Ask, decimal Bid, decimal Last);

    public bool GetTradeStationOrderRouteIdByOrder(TradeStationOrderProperties tradeStationOrderProperties, IReadOnlyCollection<SecurityType> securityTypes, out string routeId)
    {
        routeId = default;
        return GetTradeStationOrderRouteIdByOrderSecurityTypes(tradeStationOrderProperties, securityTypes, out routeId);
    }
}