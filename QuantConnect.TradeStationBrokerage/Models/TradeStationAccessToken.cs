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
using Newtonsoft.Json;

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents an access token response from TradeStation.
/// </summary>
public class TradeStationAccessToken
{
    /// <summary>
    /// Gets the access token.
    /// </summary>
    /// <remarks>
    /// Access Tokens have a lifetime of 20 minutes.
    /// </remarks>
    [JsonProperty("access_token")]
    public string AccessToken { get; }

    /// <summary>
    /// Gets the refresh token.
    /// </summary>
    [JsonProperty("refresh_token")]
    public string RefreshToken { get; }

    /// <summary>
    /// Gets the ID token.
    /// </summary>
    [JsonProperty("id_token")]
    public string IdToken { get; }

    /// <summary>
    /// Gets the scope.
    /// </summary>
    [JsonProperty("scope")]
    public string Scope { get; }

    /// <summary>
    /// Gets the expiration time of the token in seconds.
    /// </summary>
    [JsonProperty("expires_in")]
    public int ExpiresIn { get; }

    /// <summary>
    /// Gets the token type.
    /// </summary>
    [JsonProperty("token_type")]
    public string TokenType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationAccessToken"/> class.
    /// </summary>
    /// <param name="accessToken">The access token.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="idToken">The ID token.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="expiresIn">The expiration time of the token in seconds.</param>
    /// <param name="tokenType">The token type.</param>
    public TradeStationAccessToken(string accessToken, string refreshToken, string idToken, string scope, int expiresIn, string tokenType)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        IdToken = idToken;
        Scope = scope;
        ExpiresIn = expiresIn;
        TokenType = tokenType;
    }
}
