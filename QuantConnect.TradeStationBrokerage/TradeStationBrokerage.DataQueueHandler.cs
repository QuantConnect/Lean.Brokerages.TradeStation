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
using QuantConnect.Data;
using QuantConnect.Packets;
using QuantConnect.Interfaces;
using System.Collections.Generic;

namespace QuantConnect.Brokerages.TradeStation;

/// <summary>
/// Represents the TradeStation Brokerage's IDataQueueHandler implementation.
/// </summary>
public partial class TradeStationBrokerage : IDataQueueHandler
{
    /// <summary>
    /// Count number of subscribers for each channel (Symbol, Socket) pair
    /// </summary>
    protected TradeStationBrokerageMultiStreamSubscriptionManager SubscriptionManager { get; set; }

    /// <summary>
    /// Aggregates ticks and bars based on given subscriptions.
    /// </summary>
    protected IDataAggregator _aggregator;

    /// <summary>
    /// Sets the job we're subscribing for
    /// </summary>
    /// <param name="job">Job we're subscribing for</param>
    public void SetJob(LiveNodePacket job)
    {
        Initialize(
            clientId: job.BrokerageData["trade-station-client-id"],
            clientSecret: job.BrokerageData.TryGetValue("trade-station-client-secret", out var clientSecret) ? clientSecret : null,
            restApiUrl: job.BrokerageData["trade-station-api-url"],
            redirectUrl: job.BrokerageData.TryGetValue("trade-station-redirect-url", out var redirectUrl) ? redirectUrl : string.Empty,
            authorizationCode: job.BrokerageData.TryGetValue("trade-station-authorization-code", out var authorizationCode) ? authorizationCode : string.Empty,
            refreshToken: job.BrokerageData.TryGetValue("trade-station-refresh-token", out var refreshToken) ? refreshToken : string.Empty,
            accountType: job.BrokerageData.TryGetValue("trade-station-account-type", out var accountType) ? accountType : string.Empty,
            orderProvider: null,
            securityProvider: null,
            accountId: job.BrokerageData.TryGetValue("trade-station-account-id", out var accountId) ? accountId : string.Empty
        );

        if (!IsConnected)
        {
            Connect();
        }
    }

    /// <summary>
    /// Subscribe to the specified configuration
    /// </summary>
    /// <param name="dataConfig">defines the parameters to subscribe to a data feed</param>
    /// <param name="newDataAvailableHandler">handler to be fired on new data available</param>
    /// <returns>The new enumerator for this subscription request</returns>
    public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
    {
        if (!CanSubscribe(dataConfig.Symbol))
        {
            return null;
        }

        var enumerator = _aggregator.Add(dataConfig, newDataAvailableHandler);
        SubscriptionManager.Subscribe(dataConfig);

        return enumerator;
    }

    /// <summary>
    /// Removes the specified configuration
    /// </summary>
    /// <param name="dataConfig">Subscription config to be removed</param>
    public void Unsubscribe(SubscriptionDataConfig dataConfig)
    {
        SubscriptionManager.Unsubscribe(dataConfig);
        _aggregator.Remove(dataConfig);
    }
}
