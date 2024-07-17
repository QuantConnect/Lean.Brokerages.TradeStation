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
using System.Threading;
using QuantConnect.Util;
using QuantConnect.Data;
using QuantConnect.Packets;
using QuantConnect.Logging;
using System.Threading.Tasks;
using QuantConnect.Interfaces;
using QuantConnect.Data.Market;
using QuantConnect.Configuration;
using System.Collections.Generic;
using System.Collections.Concurrent;
using QuantConnect.Brokerages.TradeStation.Models;

namespace QuantConnect.Brokerages.TradeStation;

/// <summary>
/// Represents the TradeStation Brokerage's IDataQueueHandler implementation.
/// </summary>
public partial class TradeStationBrokerage : IDataQueueHandler
{
    /// <inheritdoc cref="IDataAggregator"/>
    protected IDataAggregator _aggregator;

    /// <inheritdoc cref="DataQueueHandlerSubscriptionManager"/>
    protected DataQueueHandlerSubscriptionManager SubscriptionManager { get; set; }

    /// <summary>
    /// A thread-safe dictionary that stores the order books by brokerage symbols.
    /// </summary>
    private readonly ConcurrentDictionary<string, DefaultOrderBook> _orderBooks = new();

    /// <summary>
    /// Use like synchronization context for threads
    /// </summary>
    private readonly object _synchronizationContext = new();

    /// <summary>
    /// Synchronization object used to ensure thread safety when starting or restarting the streaming task.
    /// </summary>
    private readonly object _streamingTaskLock = new();

    /// <summary>
    /// Display that stream quote task was finished great
    /// </summary>
    private readonly AutoResetEvent _quoteStreamEndingAutoResetEvent = new(false);

    /// <summary>
    /// Indicates whether there are any pending subscription processes.
    /// </summary>
    private bool _subscriptionsPending;

    /// <summary>
    /// Specifies the delay interval between subscription attempts.
    /// </summary>
    private readonly TimeSpan _subscribeDelay = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// Represents the currently running task responsible for streaming quotes from the TradeStation API.
    /// </summary>
    private Task _quoteStreamingTask;

    /// <summary>
    /// Cancellation token source used to signal cancellation requests for the streaming quotes task.
    /// </summary>
    /// <remarks>
    /// This token source is used to cancel the streaming quotes task when it needs to be stopped or restarted.
    /// A new instance is created whenever the streaming task is restarted.
    /// </remarks>
    private CancellationTokenSource _streamQuoteCancellationTokenSource = new();

    /// <inheritdoc cref="IDataQueueHandler.SetJob(LiveNodePacket)"/>
    public void SetJob(LiveNodePacket job)
    {
        Initialize(
            apiKey: job.BrokerageData["trade-station-api-key"],
            apiKeySecret: job.BrokerageData.TryGetValue("trade-station-api-secret", out var apiKeySecret) ? apiKeySecret : null,
            restApiUrl: job.BrokerageData["trade-station-api-url"],
            redirectUrl: job.BrokerageData.TryGetValue("trade-station-redirect-url", out var redirectUrl) ? redirectUrl : string.Empty,
            authorizationCode: job.BrokerageData.TryGetValue("trade-station-authorization-code", out var authorizationCode) ? authorizationCode : string.Empty,
            refreshToken: job.BrokerageData.TryGetValue("trade-station-refresh-token", out var refreshToken) ? refreshToken : string.Empty,
            accountType: job.BrokerageData["trade-station-account-type"],
            orderProvider: null,
            securityProvider: null
        );

        if (!IsConnected)
        {
            Connect();
        }
    }

    /// <inheritdoc cref="IDataQueueHandler.Subscribe(SubscriptionDataConfig, EventHandler)"/>
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

    /// <inheritdoc cref="IDataQueueHandler.Unsubscribe(SubscriptionDataConfig)"/>
    public void Unsubscribe(SubscriptionDataConfig dataConfig)
    {
        SubscriptionManager.Unsubscribe(dataConfig);
        _aggregator.Remove(dataConfig);
    }

    /// <summary>
    /// Subscribes to updates for the specified collection of symbols.
    /// </summary>
    /// <param name="symbols">A collection of symbols to subscribe to.</param>
    /// <returns>Always, Returns <c>true</c> if the subscription was successful</returns>
    private bool Subscribe(IEnumerable<Symbol> symbols)
    {
        foreach (var symbol in symbols)
        {
            AddOrderBook(symbol);
        }
        SubscribeOnTickUpdateEvents();
        return true;
    }

    /// <summary>
    /// Unsubscribes from updates for the specified collection of symbols.
    /// </summary>
    /// <param name="symbols">A collection of symbols to unsubscribe from.</param>
    /// <returns>Always, Returns <c>true</c> if the unSubscription was successful</returns>
    private bool UnSubscribe(IEnumerable<Symbol> symbols)
    {
        foreach (var symbol in symbols)
        {
            RemoveOrderBook(symbol);
        }
        SubscribeOnTickUpdateEvents();
        return true;
    }

    /// <summary>
    /// Subscribes to tick update events and handles the streaming of quote updates.
    /// </summary>
    private void SubscribeOnTickUpdateEvents()
    {
        lock (_streamingTaskLock)
        {
            if (_subscriptionsPending)
            {
                // Avoid duplicate subscriptions by checking if a subscription is already in progress
                return;
            }
            _subscriptionsPending = true;
        }
        StopQuoteStreamingTask();

        _quoteStreamingTask = Task.Factory.StartNew(async () =>
        {
            // Wait for a specified delay to batch multiple symbol subscriptions into a single request
            await Task.Delay(_subscribeDelay).ConfigureAwait(false);

            List<string> brokerageTickers;
            lock (_streamingTaskLock)
            {
                _subscriptionsPending = false;
                brokerageTickers = _orderBooks.Keys.ToList();
                if (brokerageTickers.Count == 0)
                {
                    // If there are no symbols to subscribe to, exit the task
                    Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnTickUpdateEvents)}: No symbols to subscribe to at this time. Exiting subscription task.");
                    return;
                }
            }

            while (!_streamQuoteCancellationTokenSource.IsCancellationRequested)
            {
                Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnTickUpdateEvents)}: Starting to listen for tick updates...");
                try
                {
                    // Stream quotes from the TradeStation API and handle each quote event
                    await foreach (var quote in _tradeStationApiClient.StreamQuotes(brokerageTickers, _streamQuoteCancellationTokenSource.Token))
                    {
                        HandleQuoteEvents(quote);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnTickUpdateEvents)}.Exception: {ex}");
                }
                Log.Trace($"{nameof(TradeStationBrokerage)}.{nameof(SubscribeOnTickUpdateEvents)}: Connection lost. Reconnecting in 10 seconds...");
                _streamQuoteCancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            }
            // Signal that the quote streaming task is ending
            _quoteStreamEndingAutoResetEvent.Set();
        }, _streamQuoteCancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>
    /// Handles incoming quote events and updates the order books accordingly.
    /// </summary>
    /// <param name="quote">The incoming quote containing bid, ask, and trade information.</param>
    private void HandleQuoteEvents(Quote quote)
    {
        if (_orderBooks.TryGetValue(quote.Symbol, out var orderBook))
        {
            if (quote.Ask > 0 && quote.AskSize > 0)
            {
                orderBook.UpdateAskRow(quote.Ask, quote.AskSize);
            }
            else if (quote.AskSize == 0 && quote.Ask != 0)
            {
                orderBook.RemoveAskRow(quote.Ask);
            }

            if (quote.Bid > 0 && quote.BidSize > 0)
            {
                orderBook.UpdateBidRow(quote.Bid, quote.BidSize);
            }
            else if (quote.BidSize == 0 && quote.Bid != 0)
            {
                orderBook.RemoveBidRow(quote.Bid);
            }

            if (quote.Last > 0 && quote.LastSize > 0)
            {
                EmitTradeTick(orderBook.Symbol, quote.Last, quote.LastSize, quote.TradeTime);
            }
        }
        else
        {
            Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(HandleQuoteEvents)}: Symbol {quote.Symbol} not found in order books. This could indicate an unexpected symbol or a missing initialization step.");
        }
    }

    /// <summary>
    /// Emits a trade tick with the provided details and updates the aggregator.
    /// </summary>
    /// <param name="symbol">The symbol of the traded instrument.</param>
    /// <param name="price">The trade price.</param>
    /// <param name="size">The trade size.</param>
    /// <param name="tradeTime">The time of the trade.</param>
    private void EmitTradeTick(Symbol symbol, decimal price, decimal size, DateTime tradeTime)
    {
        var tradeTick = new Tick
        {
            Value = price,
            Time = tradeTime,
            Symbol = symbol,
            TickType = TickType.Trade,
            Quantity = size
        };

        lock (_synchronizationContext)
        {
            _aggregator.Update(tradeTick);
        }
    }

    /// <summary>
    /// Handles updates to the best bid and ask prices and updates the aggregator with a new quote tick.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="bestBidAskUpdatedEvent">The event arguments containing best bid and ask details.</param>
    private void OnBestBidAskUpdated(object sender, BestBidAskUpdatedEventArgs bestBidAskUpdatedEvent)
    {
        var tick = new Tick
        {
            AskPrice = bestBidAskUpdatedEvent.BestAskPrice,
            BidPrice = bestBidAskUpdatedEvent.BestBidPrice,
            Time = DateTime.UtcNow,
            Symbol = bestBidAskUpdatedEvent.Symbol,
            TickType = TickType.Quote,
            AskSize = bestBidAskUpdatedEvent.BestAskSize,
            BidSize = bestBidAskUpdatedEvent.BestBidSize
        };
        tick.SetValue();

        lock (_synchronizationContext)
        {
            _aggregator.Update(tick);
        }
    }

    /// <summary>
    /// Adds an order book for the specified symbol if it does not already exist.
    /// </summary>
    /// <param name="symbol">The symbol for which the order book is to be added.</param>
    private void AddOrderBook(Symbol symbol)
    {
        var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

        if (!_orderBooks.TryGetValue(brokerageSymbol, out var orderBook))
        {
            _orderBooks[brokerageSymbol] = new DefaultOrderBook(symbol);
            _orderBooks[brokerageSymbol].BestBidAskUpdated += OnBestBidAskUpdated;
        }
    }

    /// <summary>
    /// Removes the order book for the specified symbol if it exists.
    /// </summary>
    /// <param name="symbol">The symbol for which the order book is to be removed.</param>
    private void RemoveOrderBook(Symbol symbol)
    {
        var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

        if (_orderBooks.TryRemove(brokerageSymbol, out var orderBook))
        {
            orderBook.BestBidAskUpdated -= OnBestBidAskUpdated;
        }
    }

    /// <summary>
    /// Stops the currently running streaming task, if any. 
    /// This method cancels the task, waits for it to complete, 
    /// handles any exceptions that occur during cancellation, 
    /// and disposes of the current cancellation token source.
    /// </summary>
    /// <remarks>
    /// This method ensures that any running streaming task is stopped cleanly.
    /// A new cancellation token source is created for future tasks after the current one is disposed of.
    /// </remarks>
    private void StopQuoteStreamingTask(bool updateCancellationToken = true)
    {
        Log.Debug($"{nameof(TradeStationBrokerage)}.{nameof(StopQuoteStreamingTask)}._quoteStreamingTask = {_quoteStreamingTask?.Status}");
        if (_quoteStreamingTask != null)
        {
            _streamQuoteCancellationTokenSource.Cancel();
            try
            {
                _quoteStreamingTask.Wait();
                if (!_quoteStreamEndingAutoResetEvent.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(StopQuoteStreamingTask)}: TimeOut waiting for Quote Streaming Task to end.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{nameof(TradeStationBrokerage)}.{nameof(StopQuoteStreamingTask)}.Exception: {ex}");
            }
            finally
            {
                _streamQuoteCancellationTokenSource.Dispose();
                if (updateCancellationToken)
                {
                    _streamQuoteCancellationTokenSource = new CancellationTokenSource();
                }
            }
        }
    }
}
