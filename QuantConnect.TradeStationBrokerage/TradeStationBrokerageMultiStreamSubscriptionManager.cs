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
using NodaTime;
using System.Linq;
using System.Threading;
using QuantConnect.Data;
using QuantConnect.Util;
using QuantConnect.Logging;
using System.Threading.Tasks;
using QuantConnect.Data.Market;
using QuantConnect.Configuration;
using System.Collections.Generic;
using System.Collections.Concurrent;
using QuantConnect.Brokerages.TradeStation.Api;
using QuantConnect.Brokerages.TradeStation.Models;
using QuantConnect.Brokerages.TradeStation.Streaming;

namespace QuantConnect.Brokerages.TradeStation
{
    /// <summary>
    /// Manages multiple streaming subscriptions for the TradeStation brokerage, handling updates received from the TradeStation API.
    /// </summary>
    public class TradeStationBrokerageMultiStreamSubscriptionManager : EventBasedDataQueueHandlerSubscriptionManager, IDisposable
    {
        /// <summary>
        /// Manages the list of active quote stream managers.
        /// </summary>
        private List<StreamingTaskManager> _quoteStreamManagers = new();

        /// <summary>
        /// A thread-safe dictionary that stores the order books by brokerage symbols.
        /// </summary>
        private readonly ConcurrentDictionary<string, DefaultOrderBook> _orderBooks = new();

        /// <summary>
        /// TradeStation api client implementation
        /// </summary>
        private TradeStationApiClient _tradeStationApiClient;

        /// <summary>
        /// Use like synchronization context for threads
        /// </summary>
        private readonly object _synchronizationContext = new();

        /// <summary>
        /// Aggregates ticks and bars based on given subscriptions.
        /// </summary>
        protected IDataAggregator _aggregator;

        /// <summary>
        /// Provides the mapping between Lean symbols and brokerage specific symbols.
        /// </summary>
        private TradeStationSymbolMapper _symbolMapper;

        /// <summary>
        /// The event handler responsible for processing brokerage messages, such as status updates, errors, or information 
        /// received from the TradeStation API.
        /// </summary>
        private readonly EventHandler<BrokerageMessageEvent> _brokerageEventHandler;

        /// <summary>
        /// A thread-safe dictionary used to track whether delay checks have been performed for specific symbols.
        /// The key represents the symbol, and the value indicates whether the delay has been verified.
        /// </summary>
        private readonly ConcurrentDictionary<Symbol, bool> _symbolsDelayChecked = new();

        /// <summary>
        /// A thread-safe dictionary that maps a <see cref="Symbol"/> to a <see cref="DateTimeZone"/>.
        /// </summary>
        /// <remarks>
        /// This dictionary is used to store the time zone information for each symbol in a concurrent environment,
        /// ensuring thread safety when accessing or modifying the time zone data.
        /// </remarks>
        private readonly ConcurrentDictionary<Symbol, DateTimeZone> _exchangeTimeZoneByLeanSymbol = new();

        /// <summary>
        /// Indicates whether delayed streaming data is enabled for the application.
        /// </summary>
        private readonly bool _enableDelayedStreamingData = Config.GetBool("trade-station-enable-delayed-streaming-data");

        /// <summary>
        /// Manages multi-stream subscriptions for TradeStation Brokerage, allowing for real-time and delayed market data streaming.
        /// </summary>
        /// <param name="tradeStationApiClient"> The API client for interacting with TradeStation's brokerage services.</param>
        /// <param name="symbolMapper"> An object responsible for mapping between Lean and TradeStation symbol representations.</param>
        /// <param name="aggregator">The data aggregator for combining and processing market data streams.</param>
        /// <param name="enableDelayedStreamingData">
        /// A value indicating whether delayed market data streaming is enabled. 
        /// <see langword="true"/> enables delayed streaming; <see langword="false"/> enables real-time streaming.
        /// </param>
        /// <param name="brokerageEventHandler">
        /// The event handler for receiving and processing brokerage messages, such as connection status updates or error notifications.
        /// </param>
        public TradeStationBrokerageMultiStreamSubscriptionManager(TradeStationApiClient tradeStationApiClient, TradeStationSymbolMapper symbolMapper, IDataAggregator aggregator,
            EventHandler<BrokerageMessageEvent> brokerageEventHandler)
        {
            _aggregator = aggregator;
            _symbolMapper = symbolMapper;
            _tradeStationApiClient = tradeStationApiClient;
            _brokerageEventHandler = brokerageEventHandler;

            SubscribeImpl = (symbols, _) => Subscribe(symbols);
            UnsubscribeImpl = (symbols, _) => UnSubscribe(symbols);
        }

        /// <summary>
        /// Subscribes to updates for the specified collection of symbols.
        /// </summary>
        /// <param name="symbols">A collection of symbols to subscribe to.</param>
        /// <returns>Always, Returns <c>true</c> if the subscription was successful</returns>
        private bool Subscribe(IEnumerable<Symbol> symbols)
        {
            var subscribedBrokerageSymbolsQueue = new Queue<string>();
            foreach (var symbol in symbols)
            {
                subscribedBrokerageSymbolsQueue.Enqueue(AddOrderBook(symbol));
            }

            foreach (var quoteStream in _quoteStreamManagers)
            {
                // Skip this quote stream as its subscription is full
                if (!quoteStream.IsSubscriptionFilled)
                {
                    ProcessSubscriptions(quoteStream, subscribedBrokerageSymbolsQueue);
                }
            }

            while (subscribedBrokerageSymbolsQueue.Count > 0)
            {
                var streamQuoteTask = new StreamingTaskManager(StreamHandleQuoteEvents);
                _quoteStreamManagers.Add(streamQuoteTask);
                ProcessSubscriptions(streamQuoteTask, subscribedBrokerageSymbolsQueue);
            }

            return true;
        }

        /// <summary>
        /// Unsubscribes from updates for the specified collection of symbols.
        /// </summary>
        /// <param name="symbols">A collection of symbols to unsubscribe from.</param>
        /// <returns>Always, Returns <c>true</c> if the unSubscription was successful</returns>
        private bool UnSubscribe(IEnumerable<Symbol> symbols)
        {
            var streamsToRemove = new List<StreamingTaskManager>();

            foreach (var brokerageSymbol in symbols.Select(symbol => RemoveOrderBook(symbol)))
            {
                foreach (var streamQuoteTask in _quoteStreamManagers.Where(x => x.RemoveSubscriptionItem(brokerageSymbol)))
                {
                    if (streamQuoteTask.IsSubscriptionBrokerageTickerEmpty)
                    {
                        streamsToRemove.Add(streamQuoteTask);
                    }
                }
            }

            // Remove the streams that have no remaining subscriptions
            foreach (var streamToRemove in streamsToRemove)
            {
                streamToRemove.DisposeSafely();
                _quoteStreamManagers.Remove(streamToRemove);
                Log.Debug($"{nameof(TradeStationBrokerageMultiStreamSubscriptionManager)}.{nameof(UnSubscribe)}: Stream removed. Remaining active streams: {_quoteStreamManagers.Count}");
            }

            return true;
        }

        /// <summary>
        /// Handles streaming quote events for the specified brokerage tickers.
        /// </summary>
        /// <param name="brokerageTickers">A read-only collection of brokerage tickers to subscribe to for streaming quotes.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the streaming operation.</param>
        /// <returns>A task that represents the asynchronous operation, returning <c>false</c> upon completion.</returns>
        private async Task<bool> StreamHandleQuoteEvents(IReadOnlyCollection<string> brokerageTickers, CancellationToken cancellationToken)
        {
            await foreach (var quote in _tradeStationApiClient.StreamQuotes(brokerageTickers, cancellationToken))
            {
                HandleQuoteEvents(quote);
            }
            return false;
        }

        /// <summary>
        /// Handles incoming quote events and updates the order books accordingly.
        /// </summary>
        /// <param name="quote">The incoming quote containing bid, ask, and trade information.</param>
        private void HandleQuoteEvents(Quote quote)
        {
            if (_orderBooks.TryGetValue(quote.Symbol, out var orderBook))
            {
                if (!_enableDelayedStreamingData && quote.MarketFlags.IsDelayed != null && quote.MarketFlags.IsDelayed.Value && _symbolsDelayChecked.TryAdd(orderBook.Symbol, true))
                {
                    _brokerageEventHandler.Invoke(this,
                        new BrokerageMessageEvent(BrokerageMessageType.Error, -1, $"{nameof(TradeStationBrokerageMultiStreamSubscriptionManager)}: Detected delay streaming data for {orderBook.Symbol}. Expected delayed streaming data to be '{_enableDelayedStreamingData}', but received '{quote.MarketFlags.IsDelayed}'."));
                }

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
            if (!_exchangeTimeZoneByLeanSymbol.TryGetValue(symbol, out var exchangeTimeZone))
            {
                return;
            }

            var tradeTick = new Tick
            {
                Value = price,
                Time = DateTime.UtcNow.ConvertFromUtc(exchangeTimeZone),
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
            if (!_exchangeTimeZoneByLeanSymbol.TryGetValue(bestBidAskUpdatedEvent.Symbol, out var exchangeTimeZone))
            {
                return;
            }

            var tick = new Tick
            {
                AskPrice = bestBidAskUpdatedEvent.BestAskPrice,
                BidPrice = bestBidAskUpdatedEvent.BestBidPrice,
                Time = DateTime.UtcNow.ConvertFromUtc(exchangeTimeZone),
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
        private string AddOrderBook(Symbol symbol)
        {
            var exchangeTimeZone = symbol.GetSymbolExchangeTimeZone();
            _exchangeTimeZoneByLeanSymbol[symbol] = exchangeTimeZone;

            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            if (!_orderBooks.TryGetValue(brokerageSymbol, out var orderBook))
            {
                _orderBooks[brokerageSymbol] = new DefaultOrderBook(symbol);
                _orderBooks[brokerageSymbol].BestBidAskUpdated += OnBestBidAskUpdated;
            }

            return brokerageSymbol;
        }

        /// <summary>
        /// Removes the order book for the specified symbol if it exists.
        /// </summary>
        /// <param name="symbol">The symbol for which the order book is to be removed.</param>
        private string RemoveOrderBook(Symbol symbol)
        {
            _exchangeTimeZoneByLeanSymbol.Remove(symbol, out _);

            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            if (_orderBooks.TryRemove(brokerageSymbol, out var orderBook))
            {
                orderBook.BestBidAskUpdated -= OnBestBidAskUpdated;
            }

            return brokerageSymbol;
        }


        /// <summary>
        /// Processes subscription items from the queue and adds them to the quote stream manager.
        /// </summary>
        /// <param name="quoteStream">The quote stream manager responsible for handling the subscription items.</param>
        /// <param name="symbolsQueue">
        /// A queue of symbols representing the brokerage symbols to be subscribed. 
        /// Items that cannot be added to the subscription are re-enqueued, and the process stops when the subscription limit is reached.
        /// </param>
        private void ProcessSubscriptions(StreamingTaskManager quoteStream, Queue<string> symbolsQueue)
        {
            while (symbolsQueue.Count > 0)
            {
                var brokerageSymbol = symbolsQueue.Dequeue();

                if (!quoteStream.AddSubscriptionItem(brokerageSymbol))
                {
                    // Re-enqueue the symbol since adding it to the subscription failed
                    symbolsQueue.Enqueue(brokerageSymbol);
                    // The subscription limit is reached and no more items can be added.
                    break;
                }
            }
        }

        /// <summary>
        /// Releases the resources used by the current instance.
        /// </summary>
        public override void Dispose()
        {
            _aggregator.DisposeSafely();
            _tradeStationApiClient.DisposeSafely();
        }
    }
}