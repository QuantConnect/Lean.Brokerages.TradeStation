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
using System.Collections.Generic;
using Newtonsoft.Json.Converters;
using QuantConnect.Brokerages.TradeStation.Models.Enums;
using QuantConnect.Brokerages.TradeStation.Models.Interfaces;

namespace QuantConnect.Brokerages.TradeStation.Models;

public readonly struct TradeStationOrderResponse : ITradeStationError
{
    /// <summary>
    /// Gets the collection of orders associated with the positions.
    /// </summary>
    public List<TradeStationOrder> Orders { get; }

    /// <summary>
    /// Represents an error that occurred during the retrieval of trading account information.
    /// </summary>
    public List<TradeStationError> Errors { get; }

    /// <summary>
    /// A token returned with paginated orders which can be used in a subsequent request to retrieve the next page.
    /// </summary>
    public string NextToken { get; }

    [JsonConstructor]
    public TradeStationOrderResponse(List<TradeStationOrder> orders, List<TradeStationError> errors, string nextToken)
    {
        Orders = orders;
        Errors = errors;
        NextToken = nextToken;
    }
}

public readonly struct TradeStationOrder
{
    /// <summary>
    /// TradeStation Account ID.
    /// </summary>
    public string AccountID { get; }

    /// <summary>
    /// Will display a value when the order has advanced order rules associated with it or is part of a bracket order. Valid Values are: CND, AON, TRL, SHWQTY, DSCPR, NON, PEGVAL, BKO, PSO
    /// </summary>
    /// <remarks>
    /// AON - All or None,
    /// BKO - Book Only,
    /// CND - Activation Rule,
    /// DSCPR=&lt;Price&gt; - Discretionary price,
    /// NON - Non-Display,
    /// PEGVAL=&lt;Value&gt; -Peg Value,
    /// PSO - Add Liquidity,
    /// SHWQTY=&lt;quantity&gt; - Show Only,
    /// TRL - Trailing Stop
    /// </remarks>
    public string AdvancedOptions { get; }

    /// <summary>
    /// The Closed Date Time of this order.
    /// </summary>
    public DateTime ClosedDateTime { get; }

    /// <summary>
    /// The actual brokerage commission cost and routing fees (if applicable) for a trade based on the number of shares or contracts.
    /// </summary>
    public decimal CommissionFee { get; }

    /// <summary>
    /// Describes the relationship between linked orders in a group and this order.
    /// </summary>
    public List<ConditionalOrder> ConditionalOrders { get; }

    /// <summary>
    /// The currency conversion rate that is used in order to convert from the currency of the symbol to the currency of the account.
    /// </summary>
    public string ConversionRate { get; }

    /// <summary>
    /// Currency used to complete the Order.
    /// </summary>
    public string Currency { get; }

    /// <summary>
    /// The amount of time for which an order is valid.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public TradeStationDuration Duration { get; }

    /// <summary>
    /// At the top level, this is the average fill price. For expanded levels, this is the actual execution price.
    /// </summary>
    public string FilledPrice { get; }

    /// <summary>
    /// For GTC, GTC+, GTD and GTD+ order durations. The date the order will expire on in UTC format. The time portion, if "T00:00:00Z", should be ignored.
    /// </summary>
    public DateTime GoodTillDate { get; }

    /// <summary>
    /// It can be used to identify orders that are part of the same bracket.
    /// </summary>
    public string GroupName { get; }

    /// <summary>
    /// An array of legs associated with this order.
    /// </summary>
    public List<Leg> Legs { get; }

    /// <summary>
    /// Allows you to specify when an order will be placed based on the price action of one or more symbols.
    /// </summary>
    public List<MarketActivationRule> MarketActivationRules { get; }

    /// <summary>
    /// Allows you to specify a time that an order will be placed.
    /// </summary>
    public List<TimeActivationRule> TimeActivationRules { get; }

    /// <summary>
    /// The limit price for Limit and Stop Limit orders.
    /// </summary>
    public decimal LimitPrice { get; }

    /// <summary>
    /// The order ID of this order.
    /// </summary>
    public string OrderID { get; }

    /// <summary>
    /// Time the order was placed.
    /// </summary>
    public DateTime OpenedDateTime { get; }

    /// <summary>
    /// The order type of the order.
    /// </summary>
    /// <remarks>
    /// </remarks>
    [JsonConverter(typeof(StringEnumConverter))]
    public TradeStationOrderType OrderType { get; }

    /// <summary>
    /// Price used for the buying power calculation of the order.
    /// </summary>
    public string PriceUsedForBuyingPower { get; }

    /// <summary>
    /// If an order has been rejected, this will display the rejection. reason
    /// </summary>
    public string RejectReason { get; }

    /// <summary>
    /// Identifies the routing selection made by the customer when placing the order.
    /// </summary>
    public string Routing { get; }

    /// <summary>
    /// Hides the true number of shares intended to be bought or sold. Valid for Limit and StopLimit order types. Not valid for all exchanges.
    /// </summary>
    public string ShowOnlyQuantity { get; }

    /// <summary>
    /// The spread type for an option order.
    /// </summary>
    public string Spread { get; }

    /// <summary>
    /// The status of an order. Status filters can be used according to the order category:
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public TradeStationOrderStatusType Status { get; }

    /// <summary>
    /// Description of the status.
    /// </summary>
    public string StatusDescription { get; }

    /// <summary>
    /// The stop price for StopLimit and StopMarket orders.
    /// </summary>
    public decimal StopPrice { get; }

    /// <summary>
    /// TrailingStop offset; amount or percent.
    /// </summary>
    public TrailingStop TrailingStop { get; }

    /// <summary>
    /// Only applies to equities. Will contain a value if the order has received a routing fee.
    /// </summary>
    public string UnbundledRouteFee { get; }

    [JsonConstructor]
    public TradeStationOrder(string accountID, string advancedOptions, DateTime closedDateTime, decimal commissionFee, List<ConditionalOrder> conditionalOrders,
        string conversionRate, string currency, TradeStationDuration duration, string filledPrice, DateTime goodTillDate, string groupName, List<Leg> legs,
        List<MarketActivationRule> marketActivationRules, List<TimeActivationRule> timeActivationRules, decimal limitPrice, string orderID,
        DateTime openedDateTime, TradeStationOrderType orderType, string priceUsedForBuyingPower, string rejectReason, string routing, string showOnlyQuantity,
        string spread, TradeStationOrderStatusType status, string statusDescription, decimal stopPrice, TrailingStop trailingStop, string unbundledRouteFee)
    {
        AccountID = accountID;
        AdvancedOptions = advancedOptions;
        ClosedDateTime = closedDateTime;
        CommissionFee = commissionFee;
        ConditionalOrders = conditionalOrders;
        ConversionRate = conversionRate;
        Currency = currency;
        Duration = duration;
        FilledPrice = filledPrice;
        GoodTillDate = goodTillDate;
        GroupName = groupName;
        Legs = legs;
        MarketActivationRules = marketActivationRules;
        TimeActivationRules = timeActivationRules;
        LimitPrice = limitPrice;
        OrderID = orderID;
        OpenedDateTime = openedDateTime;
        OrderType = orderType;
        PriceUsedForBuyingPower = priceUsedForBuyingPower;
        RejectReason = rejectReason;
        Routing = routing;
        ShowOnlyQuantity = showOnlyQuantity;
        Spread = spread;
        Status = status;
        StatusDescription = statusDescription;
        StopPrice = stopPrice;
        TrailingStop = trailingStop;
        UnbundledRouteFee = unbundledRouteFee;
    }
}

public readonly struct Leg
{
    /// <summary>
    /// What kind of order leg - Opening or Closing.
    /// </summary>
    public string OpenOrClose { get; }

    /// <summary>
    /// Number of shares or contracts being purchased or sold.
    /// </summary>
    public decimal QuantityOrdered { get; }

    /// <summary>
    /// Number of shares that have been executed.
    /// </summary>
    public decimal ExecQuantity { get; }

    /// <summary>
    /// In a partially filled order, this is the number of shares or contracts that were unfilled.
    /// </summary>
    public decimal QuantityRemaining { get; }

    /// <summary>
    /// Identifies whether the order is a buy or sell. Valid values are Buy, Sell, SellShort, or BuyToCover.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public TradeStationTradeActionType BuyOrSell { get; }

    /// <summary>
    /// Symbol for the leg order.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Underlying Symbol associated. Only applies to Futures and Options.
    /// </summary>
    public string Underlying { get; }

    /// <summary>
    /// Indicates the asset type of the order.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public TradeStationAssetType AssetType { get; }

    /// <summary>
    /// The price at which order execution occurred.
    /// </summary>
    public decimal ExecutionPrice { get; }

    /// <summary>
    /// The expiration date of the future or option symbol.
    /// </summary>
    public DateTime ExpirationDate { get; }

    /// <summary>
    /// Present for options.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public TradeStationOptionType OptionType { get; }

    /// <summary>
    /// Present for options. The price at which the holder of an options contract can buy or sell the underlying asset.
    /// </summary>
    public decimal StrikePrice { get; }

    [JsonConstructor]
    public Leg(string openOrClose, decimal quantityOrdered, decimal execQuantity, decimal quantityRemaining, TradeStationTradeActionType buyOrSell, string symbol, string underlying,
        TradeStationAssetType assetType, decimal executionPrice, DateTime expirationDate, TradeStationOptionType optionType, decimal strikePrice)
    {
        OpenOrClose = openOrClose;
        QuantityOrdered = quantityOrdered;
        ExecQuantity = execQuantity;
        QuantityRemaining = quantityRemaining;
        BuyOrSell = buyOrSell;
        Symbol = symbol;
        Underlying = underlying;
        AssetType = assetType;
        ExecutionPrice = executionPrice;
        ExpirationDate = expirationDate;
        OptionType = optionType;
        StrikePrice = strikePrice;
    }
}

/// <summary>
/// Describes the relationship between linked orders in a group and this order.
/// </summary>
public readonly struct ConditionalOrder
{
    /// <summary>
    /// TradeStation Account ID.
    /// </summary>
    public string AccountID { get; }

    /// <summary>
    /// Describes the relationship of a linked order within a group order to the current returned order. 
    /// Valid Values are: BRK, OSP (linked parent), OSO (linked child), and OCO.
    /// </summary>
    public string Relationship { get; }

    [JsonConstructor]
    public ConditionalOrder(string accountID, string relationship)
    {
        AccountID = accountID;
        Relationship = relationship;
    }
}

/// <summary>
/// Allows you to specify when an order will be placed based on the price action of one or more symbols.
/// </summary>
public readonly struct MarketActivationRule
{
    /// <summary>
    /// Type of the activation rule. Currently only supports Price.
    /// </summary>
    public string RuleType { get; }

    /// <summary>
    /// Symbol that the rule is based on.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// The predicate comparison for the market rule type. E.g. Lt (less than).
    /// </summary>
    /// <remarks>
    /// Lt - Less Than
    /// Lte - Less Than or Equal
    /// Gt - Greater Than
    /// Gte - Greater Than or Equal</remarks>
    public string Predicate { get; }

    /// <summary>
    /// The ticks behavior for the activation rule.
    /// </summary>
    /// <remarks>Enum: "STT" "STTN" "SBA" "SAB" "DTT" "DTTN" "DBA" "DAB" "TTT" "TTTN" "TBA" "TAB"</remarks>
    public string TriggerKey { get; }

    /// <summary>
    /// Valid only for RuleType="Price", the price at which the rule will trigger when the price hits ticks as specified by TriggerKey.
    /// </summary>
    public string Price { get; }

    /// <summary>
    /// Relation with the previous activation rule when given a list of MarketActivationRules. Ignored for the first MarketActivationRule.
    /// </summary>
    /// <remarks>Enum: "And" "Or"</remarks>
    public string LogicOperator { get; }

    [JsonConstructor]
    public MarketActivationRule(string ruleType, string symbol, string predicate, string triggerKey, string price, string logicOperator)
    {
        RuleType = ruleType;
        Symbol = symbol;
        Predicate = predicate;
        TriggerKey = triggerKey;
        Price = price;
        LogicOperator = logicOperator;
    }
}

/// <summary>
/// Allows you to specify a time that an order will be placed.
/// </summary>
public readonly struct TimeActivationRule
{
    /// <summary>
    /// Timestamp represented as an RFC3339 formatted date, a profile of the ISO 8601 date standard. 
    /// For time activated orders, the date portion is required but not relevant. E.g. 2023-01-01T23:30:30Z.
    /// </summary>
    public string TimeUtc { get; }

    [JsonConstructor]
    public TimeActivationRule(string timeUtc)
    {
        TimeUtc = timeUtc;
    }
}

/// <summary>
/// TrailingStop offset; amount or percent.
/// </summary>
public readonly struct TrailingStop
{
    /// <summary>
    /// Currency Offset from current price. Note: Mutually exclusive with Percent.
    /// </summary>
    public string Amount { get; }

    /// <summary>
    /// Percentage offset from current price. Note: Mutually exclusive with Amount.
    /// </summary>
    public string Percent { get; }

    [JsonConstructor]
    public TrailingStop(string amount, string percent)
    {
        Amount = amount;
        Percent = percent;
    }
}
