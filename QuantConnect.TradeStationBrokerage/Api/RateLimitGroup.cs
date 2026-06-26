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

namespace QuantConnect.Brokerages.TradeStation.Api;

/// <summary>
/// Identifies the TradeStation rate-limit resource group a REST request belongs to. Each request passes
/// its group explicitly so the right proactive throttle keeps it under the documented quota.
/// See https://api.tradestation.com/docs/fundamentals/rate-limiting/.
/// </summary>
public enum RateLimitGroup
{
    /// <summary>
    /// No throttling. Streaming endpoints do not consume request quota.
    /// </summary>
    None,

    /// <summary>
    /// Accounts, order details/execution, balances and positions (320 requests / 5 minutes).
    /// </summary>
    General,

    /// <summary>
    /// Quote snapshot (500 requests / 5 minutes).
    /// </summary>
    Quote,

    /// <summary>
    /// The option expirations endpoint (90 requests / 1 minute, per option endpoint).
    /// </summary>
    OptionExpirations,

    /// <summary>
    /// The option strikes endpoint (90 requests / 1 minute, per option endpoint).
    /// </summary>
    OptionStrikes,
}
