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
using System.Threading;
using QuantConnect.Orders;
using System.Threading.Tasks;
using QuantConnect.Securities;
using System.Collections.Generic;
using QuantConnect.Orders.TimeInForces;
using QuantConnect.Brokerages.TradeStation.Models;
using QuantConnect.Brokerages.TradeStation.Models.Enums;

namespace QuantConnect.Brokerages.TradeStation;

/// <summary>
/// Provides extension methods.
/// </summary>
public static class TradeStationExtensions
{
    /// <summary>
    /// Converts a <see cref="TradeStationOptionType"/> to an <see cref="OptionRight"/>.
    /// </summary>
    /// <param name="optionType">The <see cref="TradeStationOptionType"/> to convert.</param>
    /// <returns>The corresponding <see cref="OptionRight"/> value.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the <paramref name="optionType"/> is not supported.
    /// </exception>
    public static OptionRight ConvertOptionTypeToOptionRight(this TradeStationOptionType optionType) => optionType switch
    {
        TradeStationOptionType.Put => OptionRight.Put,
        TradeStationOptionType.Call => OptionRight.Call,
        _ => throw new NotSupportedException($"{nameof(TradeStationBrokerage)}.{nameof(ConvertOptionTypeToOptionRight)}: " +
            $"The optionType '{optionType}' is not supported.")
    };

    /// <summary>
    /// Converts a <see cref="TradeStationAssetType"/> to a <see cref="SecurityType"/>.
    /// </summary>
    /// <param name="assetType">The <see cref="TradeStationAssetType"/> to convert.</param>
    /// <returns>The corresponding <see cref="SecurityType"/> value.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the <paramref name="assetType"/> is not supported.
    /// </exception>
    public static SecurityType ConvertAssetTypeToSecurityType(this TradeStationAssetType assetType) => assetType switch
    {
        TradeStationAssetType.Stock => SecurityType.Equity,
        TradeStationAssetType.StockOption => SecurityType.Option,
        TradeStationAssetType.Future => SecurityType.Future,
        TradeStationAssetType.FutureOption => SecurityType.FutureOption,
        TradeStationAssetType.Forex => SecurityType.Forex,
        TradeStationAssetType.Index => SecurityType.Index,
        TradeStationAssetType.IndexOption => SecurityType.IndexOption,
        _ => throw new NotSupportedException($"{nameof(TradeStationBrokerage)}.{nameof(ConvertAssetTypeToSecurityType)}: " +
            $"The AssetType '{assetType}' is not supported.")
    };

    /// <summary>
    /// Converts a Lean order type to its equivalent TradeStation order type.
    /// </summary>
    /// <param name="orderType">The Lean order type to convert.</param>
    /// <returns>The equivalent TradeStation order type.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the specified order type is not supported by the conversion.
    /// </exception>
    public static TradeStationOrderType ConvertLeanOrderTypeToTradeStation(this OrderType orderType)
    {
        switch (orderType)
        {
            case OrderType.Market:
            case OrderType.MarketOnOpen:
            case OrderType.MarketOnClose:
            case OrderType.ComboMarket:
                return TradeStationOrderType.Market;
            case OrderType.Limit:
            case OrderType.ComboLimit:
                return TradeStationOrderType.Limit;
            case OrderType.StopMarket:
                return TradeStationOrderType.StopMarket;
            case OrderType.StopLimit:
                return TradeStationOrderType.StopLimit;
            default:
                throw new NotSupportedException($"{nameof(TradeStationBrokerage)}.{nameof(ConvertLeanOrderTypeToTradeStation)}:" +
                    $" The order type '{orderType}' is not supported for conversion to TradeStation order type.");
        }
    }

    /// <summary>
    /// The util, transform Lean Order TimeInForce to brokerage format for orders
    /// </summary>
    /// <param name="leanOrderTimeInForce">Lean Order TimeInForce</param>
    /// <returns>brokerage:(expirationType and expirationTimestamp)</returns>
    public static (TradeStationDuration Duration, string expiryDateTime) GetBrokerageTimeInForce(
        this Orders.TimeInForce leanOrderTimeInForce,
        OrderType leanOrderType,
        bool? outsideRegularTradingHours)
    {
        var duration = default(TradeStationDuration);
        var expiryDateTime = default(string);

        switch (leanOrderType)
        {
            case OrderType.MarketOnOpen:
                duration = TradeStationDuration.Opening;
                break;
            case OrderType.MarketOnClose:
                duration = TradeStationDuration.Close;
                break;
            default:
                switch (leanOrderTimeInForce)
                {
                    case DayTimeInForce _:
                        duration = outsideRegularTradingHours == true
                            ? TradeStationDuration.DayPlus
                            : TradeStationDuration.Day;
                        break;
                    case GoodTilDateTimeInForce gt:
                        duration = outsideRegularTradingHours == true
                            ? TradeStationDuration.GoodThroughDatePlus
                            : TradeStationDuration.GoodThroughDate;
                        expiryDateTime = gt.Expiry.ToIso8601Invariant();
                        break;
                    case GoodTilCanceledTimeInForce _:
                        duration = outsideRegularTradingHours == true
                            ? TradeStationDuration.GoodTillCanceledPlus
                            : TradeStationDuration.GoodTillCanceled;
                        break;
                }
                break;
        }

        return (duration, expiryDateTime);
    }

    /// <summary>
    /// Converts the specified brokerage order duration string into the corresponding Lean <see cref="Orders.TimeInForce"/> type.
    /// This method supports three brokerage order duration values: 'DAY', 'GTD' (Good 'Til Date), and 'GTC' (Good 'Til Canceled).
    /// </summary>
    /// <param name="brokerageOrderDuration">
    /// The duration of the order provided by the brokerage as a string. Valid values include:
    /// <list type="bullet">
    /// <item>
    /// <term>DAY</term>
    /// <description>The order is active only for the current trading day and expires at the end of the day.</description>
    /// </item>
    /// <item>
    /// <term>GTD</term>
    /// <description>The order remains active until the specified expiration date and time (Good 'Til Date).</description>
    /// </item>
    /// <item>
    /// <term>GTC</term>
    /// <description>The order remains active indefinitely until explicitly canceled (Good 'Til Canceled).</description>
    /// </item>
    /// </list>
    /// </param>
    /// <param name="goodTilDateTime">
    /// The expiration date and time for a Good 'Til Date (GTD) order. This parameter is used only when <paramref name="brokerageOrderDuration"/> is 'GTD'.
    /// </param>
    /// <returns>
    /// Returns <c>true</c> if the conversion was successful and a valid <see cref="Orders.TimeInForce"/> value was assigned to 
    /// the <see cref="TradeStationOrderProperties.TimeInForce"/> property. Returns <c>false</c> if an unsupported brokerage order duration was provided.
    /// </returns>
    public static bool GetLeanTimeInForce(this TradeStationOrderProperties orderProperties, TradeStationDuration brokerageOrderDuration, DateTime goodTilDateTime)
    {
        switch (brokerageOrderDuration)
        {
            case TradeStationDuration.Day:
                orderProperties.TimeInForce = Orders.TimeInForce.Day;
                return true;
            case TradeStationDuration.DayPlus:
                orderProperties.TimeInForce = Orders.TimeInForce.Day;
                return true;
            case TradeStationDuration.GoodThroughDate:
                orderProperties.TimeInForce = Orders.TimeInForce.GoodTilDate(goodTilDateTime);
                return true;
            case TradeStationDuration.GoodThroughDatePlus:
                orderProperties.TimeInForce = Orders.TimeInForce.GoodTilDate(goodTilDateTime);
                return true;
            case TradeStationDuration.GoodTillCanceled:
                orderProperties.TimeInForce = Orders.TimeInForce.GoodTilCanceled;
                return true;
            case TradeStationDuration.GoodTillCanceledPlus:
                orderProperties.TimeInForce = Orders.TimeInForce.GoodTilCanceled;
                return true;
            case TradeStationDuration.Close:
            case TradeStationDuration.Opening:
                orderProperties.TimeInForce = Orders.TimeInForce.GoodTilCanceled;
                return true;
            default:
                return false;
        }
        ;
    }

    /// <summary>
    /// Determines whether the specified trade action type is a short sell action.
    /// </summary>
    /// <param name="buyOrSell">The trade action type to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the trade action type is one of the short sell actions; otherwise, <c>false</c>.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the trade action type is not recognized or supported.
    /// </exception>
    public static bool IsShort(this TradeStationTradeActionType buyOrSell)
    {
        switch (buyOrSell)
        {
            case TradeStationTradeActionType.Sell:
            case TradeStationTradeActionType.SellShort:
            case TradeStationTradeActionType.SellToOpen:
            case TradeStationTradeActionType.SellToClose:
                return true;

            case TradeStationTradeActionType.Buy:
            case TradeStationTradeActionType.BuyToCover:
            case TradeStationTradeActionType.BuyToClose:
            case TradeStationTradeActionType.BuyToOpen:
                return false;

            default:
                throw new NotSupportedException($"The TradeStationTradeActionType '{buyOrSell}' is not supported. Please provide a valid trade action type.");
        }
    }

    /// <summary>
    /// Gets the stop price of the specified order.
    /// </summary>
    /// <param name="order">The order to retrieve the stop price from.</param>
    /// <returns>The stop price if the order is a StopMarketOrder or StopLimitOrder; otherwise, null.</returns>
    public static decimal? GetStopPrice(this Order order) => order switch
    {
        StopMarketOrder smo => smo.StopPrice,
        StopLimitOrder slo => slo.StopPrice,
        _ => null
    };

    /// <summary>
    /// Gets the limit price of the specified order.
    /// </summary>
    /// <param name="order">The order to retrieve the limit price from.</param>
    /// <returns>The limit price if the order is a LimitOrder or StopLimitOrder; otherwise, null.</returns>
    public static decimal? GetLimitPrice(this Order order) => order switch
    {
        LimitOrder lo => lo.LimitPrice,
        StopLimitOrder slo => slo.LimitPrice,
        ComboLimitOrder clo => clo.GroupOrderManager.LimitPrice,
        _ => null
    };

    /// <summary>
    /// Parses the provided account type string into a <see cref="TradeStationAccountType"/> enum value.
    /// </summary>
    /// <param name="accountType">The account type string to parse.</param>
    /// <returns>The parsed <see cref="TradeStationAccountType"/> enum value.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the provided account type is null, empty, or not a valid <see cref="TradeStationAccountType"/>.
    /// </exception>
    public static TradeStationAccountType ParseAccountType(string accountType)
    {
        if (string.IsNullOrWhiteSpace(accountType))
        {
            throw new ArgumentException("Account type cannot be null or empty.", nameof(accountType));
        }

        if (!Enum.TryParse<TradeStationAccountType>(accountType, true, out var parsedAccountType) ||
            !Enum.IsDefined(typeof(TradeStationAccountType), parsedAccountType))
        {
            throw new ArgumentException($"An error occurred while parsing the account type '{accountType}'. Please ensure that the provided account type is valid and supported by the system.");
        }

        return parsedAccountType;
    }

    /// <summary>
    /// Converts an <see cref="IAsyncEnumerable{T}"/> to an <see cref="IEnumerable{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">The source <see cref="IAsyncEnumerable{T}"/> to convert.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
    /// <returns>An <see cref="IEnumerable{T}"/> that iterates over the source <see cref="IAsyncEnumerable{T}"/>.</returns>
    /// <remarks>
    /// This method allows you to synchronously iterate over an asynchronous sequence. Use it cautiously
    /// as it may block the calling thread if the asynchronous sequence takes time to produce elements.
    /// </remarks>
    public static IEnumerable<T> ToEnumerable<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<T> e = source.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                ValueTask<bool> moveNext = e.MoveNextAsync();
                if (moveNext.IsCompletedSuccessfully ? moveNext.Result : moveNext.AsTask().GetAwaiter().GetResult())
                {
                    yield return e.Current;
                }
                else break;
            }
        }
        finally
        {
            e.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Retrieves the time zone of the exchange for the given symbol.
    /// </summary>
    /// <param name="symbol">The symbol for which to get the exchange time zone.</param>
    /// <returns>
    /// The <see cref="NodaTime.DateTimeZone"/> representing the time zone of the exchange
    /// where the given symbol is traded.
    /// </returns>
    /// <remarks>
    /// This method uses the <see cref="MarketHoursDatabase"/> to fetch the exchange hours
    /// and extract the time zone information for the provided symbol.
    /// </remarks>
    public static NodaTime.DateTimeZone GetSymbolExchangeTimeZone(this Symbol symbol)
        => MarketHoursDatabase.FromDataFolder().GetExchangeHours(symbol.ID.Market, symbol, symbol.SecurityType).TimeZone;

    /// <summary>
    /// Sets the status of the Lean order and associates it with the corresponding TradeStation broker ID.
    /// </summary>
    /// <param name="leanOrder">The Lean <see cref="Order"/> object whose status and broker ID are to be set.</param>
    /// <param name="order">The TradeStation order providing the status and broker ID information.</param>
    /// <param name="leg">The specific leg of the order, used to determine the execution quantity and final status.</param>
    public static Order SetOrderStatusAndBrokerId(this Order leanOrder, TradeStationOrder order, Models.Leg leg)
    {
        leanOrder.Status = leg.ExecQuantity > 0m && leg.ExecQuantity != leg.QuantityOrdered
            ? OrderStatus.PartiallyFilled
            : OrderStatus.Submitted;

        leanOrder.BrokerId.Add(order.OrderID);

        return leanOrder;
    }

    /// <summary>
    /// Calculates the greatest common divisor (GCD) of two decimal numbers using the Euclidean algorithm.
    /// </summary>
    /// <param name="a">The first decimal number.</param>
    /// <param name="b">The second decimal number. If this value is 0, the method returns the first number.</param>
    /// <returns>The greatest common divisor of <paramref name="a"/> and <paramref name="b"/>.</returns>
    public static decimal GreatestCommonDivisor(decimal a, decimal b) => b == 0 ? a : GreatestCommonDivisor(b, a % b);
}
