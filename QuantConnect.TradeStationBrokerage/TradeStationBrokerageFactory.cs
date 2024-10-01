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
using QuantConnect.Util;
using QuantConnect.Packets;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using System.Collections.Generic;
using QuantConnect.Configuration;

namespace QuantConnect.Brokerages.TradeStation;

/// <summary>
/// Provides a template implementation of BrokerageFactory
/// </summary>
public class TradeStationBrokerageFactory : BrokerageFactory
{
    /// <summary>
    /// Gets the brokerage data required to run the brokerage from configuration/disk
    /// </summary>
    /// <remarks>
    /// The implementation of this property will create the brokerage data dictionary required for
    /// running live jobs. See <see cref="IJobQueueHandler.NextJob"/>
    /// </remarks>
    public override Dictionary<string, string> BrokerageData => new()
    {
        { "trade-station-client-id", Config.Get("trade-station-client-id") },
        // Optional: The secret for the client application’s API Key. Required for standard Auth Code Flow. Not required for Auth Code Flow with PKCE.
        // https://api.tradestation.com/docs/fundamentals/authentication/refresh-tokens
        { "trade-station-client-secret", Config.Get("trade-station-client-secret", "") },
        // The URL to connect to brokerage environment:
        // Simulator(SIM): https://sim-api.tradestation.com/v3
        // LIVE: https://api.tradestation.com/v3
        { "trade-station-api-url", Config.Get("trade-station-api-url") },
        /// Optional: <see cref="Models.Enums.TradeStationAccountType"/>
        { "trade-station-account-type", Config.Get("trade-station-account-type") },
        // Optional: Users can have multiple different accounts
        { "trade-station-account-id", Config.Get("trade-station-account-id") },
        
        // USE CASE 1 (normal): lean CLI & live cloud wizard
        { "trade-station-refresh-token", Config.Get("trade-station-refresh-token") },

        // USE CASE 2 (developing): Only if refresh token not provided
        { "trade-station-redirect-url", Config.Get("trade-station-redirect-url") },
        { "trade-station-authorization-code", Config.Get("trade-station-authorization-code") }
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationBrokerageFactory"/> class
    /// </summary>
    public TradeStationBrokerageFactory() : base(typeof(TradeStationBrokerage))
    {
    }

    /// <summary>
    /// Gets a brokerage model that can be used to model this brokerage's unique behaviors
    /// </summary>
    /// <param name="orderProvider">The order provider</param>
    public override IBrokerageModel GetBrokerageModel(IOrderProvider orderProvider) => new TradeStationBrokerageModel();

    /// <summary>
    /// Creates a new IBrokerage instance
    /// </summary>
    /// <param name="job">The job packet to create the brokerage for</param>
    /// <param name="algorithm">The algorithm instance</param>
    /// <returns>A new brokerage instance</returns>
    public override IBrokerage CreateBrokerage(LiveNodePacket job, IAlgorithm algorithm)
    {
        var errors = new List<string>();

        var clientId = Read<string>(job.BrokerageData, "trade-station-client-id", errors);

        var clientSecret = default(string);
        if (job.BrokerageData.ContainsKey("trade-station-client-secret"))
        {
            clientSecret = Read<string>(job.BrokerageData, "trade-station-client-secret", errors);
        }

        var apiUrl = Read<string>(job.BrokerageData, "trade-station-api-url", errors);

        if (errors.Count != 0)
        {
            // if we had errors then we can't create the instance
            throw new ArgumentException(string.Join(Environment.NewLine, errors));
        }

        var refreshToken = Read<string>(job.BrokerageData, "trade-station-refresh-token", errors);
        var accountType = Read<string>(job.BrokerageData, "trade-station-account-type", errors);
        var accountId = Read<string>(job.BrokerageData, "trade-station-account-id", errors);

        var ts = default(TradeStationBrokerage);
        if (string.IsNullOrEmpty(refreshToken))
        {
            var authorizationCode = Read<string>(job.BrokerageData, "trade-station-authorization-code", errors);
            var redirectUrl = Read<string>(job.BrokerageData, "trade-station-redirect-url", errors);

            if (string.IsNullOrEmpty(authorizationCode) || string.IsNullOrEmpty(redirectUrl))
            {
                throw new ArgumentException("RedirectUrl or AuthorizationCode cannot be empty or null. Please ensure these values are correctly set in the configuration file.");
            }

            // Case 1: authentication with using redirectUrl, authorizationCode
            ts = new TradeStationBrokerage(clientId, clientSecret, apiUrl, redirectUrl, authorizationCode, accountType, algorithm, accountId);
        }
        else
        {
            // Case 2: authentication with using refreshToken
            ts = new TradeStationBrokerage(clientId, clientSecret, apiUrl, refreshToken, accountType, algorithm, accountId);
        }

        Composer.Instance.AddPart<IDataQueueHandler>(ts);

        return ts;
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public override void Dispose()
    {
        //Not needed
    }
}