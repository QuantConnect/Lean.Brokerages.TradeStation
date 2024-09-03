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

using QuantConnect.Brokerages.TradeStation.Models.Enums;
using System.Collections.Generic;

namespace QuantConnect.Brokerages.TradeStation.Models;

/// <summary>
/// Represents a TradeStation route that contains a collection of routes for placing orders.
/// </summary>
public readonly struct TradeStationRoute
{
    /// <summary>
    /// Gets the collection of routes associated with this TradeStation route.
    /// </summary>
    public IEnumerable<Route> Routes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeStationRoute"/> struct with the specified routes.
    /// </summary>
    /// <param name="routes">The collection of routes to be associated with this TradeStation route.</param>
    public TradeStationRoute(IEnumerable<Route> routes) => Routes = routes;
}

/// <summary>
/// Represents a route used in TradeStation for placing orders.
/// </summary>
public readonly struct Route
{
    /// <summary>
    /// The ID that must be sent in the optional Route property of a POST order request, when specifying a route for an order.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The asset type of the route. Valid Values are: STOCK, FUTURE, STOCKOPTION, and INDEXOPTION.
    /// </summary>
    public IEnumerable<TradeStationAssetType> AssetTypes { get; }

    /// <summary>
    /// The name of the route.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Route"/> struct with the specified route ID, asset types, and name.
    /// </summary>
    /// <param name="id">The ID of the route.</param>
    /// <param name="assetTypes">The asset types associated with this route.</param>
    /// <param name="name">The name of the route.</param>
    public Route(string id, IEnumerable<TradeStationAssetType> assetTypes, string name) => (Id, AssetTypes, Name) = (id, assetTypes, name);
}
