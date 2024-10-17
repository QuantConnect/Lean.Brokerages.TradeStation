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
using QuantConnect.Logging;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace QuantConnect.Brokerages.TradeStation.Streaming;

/// <summary>
/// Manages streaming tasks for a collection of items, allowing for subscription, unSubscription and restarting of streaming processes.
/// </summary>
public class StreamingTaskManager : IDisposable
{
    /// <summary>
    /// The maximum number of symbols allowed per quote stream request.
    /// </summary>
    /// <see href="https://api.tradestation.com/docs/specification#tag/MarketData/operation/GetQuoteChangeStream"/>
    private const int MaxSymbolsPerQuoteStreamRequest = 100;

    /// <summary>
    /// Indicates whether there are any pending subscription processes.
    /// </summary>
    private bool _hasPendingSubscriptions;

    /// <summary>
    /// Signals to a <see cref="CancellationToken"/> that it should be canceled.
    /// </summary>
    private CancellationTokenSource _cancellationTokenSource = new();

    /// <summary>
    /// The task representing the ongoing streaming operation.
    /// </summary>
    private Task _streamingTask;

    /// <summary>
    /// Synchronization object used to ensure thread safety when starting or restarting the streaming task.
    /// </summary>
    private readonly object _streamingTaskLock = new();

    /// <summary>
    /// Synchronization object used to ensure thread safety when Add or Remove item in <see cref="_subscriptionBrokerageTickers"/>.
    /// </summary>
    private readonly object _brokerageTickerLock = new();

    /// <summary>
    /// Specifies the delay interval between subscription attempts.
    /// </summary>
    private readonly TimeSpan _subscribeDelay = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// The action to execute for streaming the subscribed items.
    /// </summary>
    private readonly Func<IReadOnlyCollection<string>, CancellationToken, Task<bool>> _streamAction;

    /// <summary>
    /// Gets the collection of subscribed items.
    /// </summary>
    private readonly HashSet<string> _subscriptionBrokerageTickers = new();

    /// <summary>
    /// Indicates whether there are no subscribed brokerage tickers.
    /// </summary>
    public bool IsSubscriptionBrokerageTickerEmpty { get => _subscriptionBrokerageTickers.Count == 0; }

    /// <summary>
    /// Indicates whether the maximum number of subscribed brokerage tickers has been reached.
    /// </summary>
    public bool IsSubscriptionFilled { get => _subscriptionBrokerageTickers.Count == MaxSymbolsPerQuoteStreamRequest; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingTaskManager"/> class.
    /// </summary>
    /// <param name="streamingAction">The action to execute for streaming the items.</param>
    public StreamingTaskManager(Func<IReadOnlyCollection<string>, CancellationToken, Task<bool>> streamingAction)
    {
        _streamAction = streamingAction ?? throw new ArgumentNullException(nameof(streamingAction), "Streaming action cannot be null.");
    }

    /// <summary>
    /// Adds an item to the subscription list if the maximum limit is not reached. 
    /// If the item is already present, it will not be added, and the method will return false.
    /// </summary>
    /// <param name="item">The item to add to the subscription list. This should be a unique identifier 
    /// for the item being subscribed to.</param>
    /// <returns><c>true</c> if the item was added successfully; otherwise, <c>false</c>.</returns>
    public bool AddSubscriptionItem(string item)
    {
        lock (_brokerageTickerLock)
        {
            if (_subscriptionBrokerageTickers.Count >= MaxSymbolsPerQuoteStreamRequest)
            {
                Log.Debug($"{nameof(StreamingTaskManager)}.{nameof(AddSubscriptionItem)}: Cannot add more items. Maximum limit reached.");
                return false;
            }

            if (!_subscriptionBrokerageTickers.Add(item))
            {
                Log.Debug($"{nameof(StreamingTaskManager)}.{nameof(AddSubscriptionItem)}: Item already exists in the list.");
                return false;
            }
        }

        RestartStreaming();

        return true;
    }

    /// <summary>
    /// Removes an item from the subscription list.
    /// </summary>
    /// <param name="item">The item to remove from the subscription list.</param>
    /// <returns><c>true</c> if the item was removed successfully; otherwise, <c>false</c>.</returns>
    public bool RemoveSubscriptionItem(string item)
    {
        lock (_brokerageTickerLock)
        {
            if (_subscriptionBrokerageTickers.Remove(item))
            {
                RestartStreaming();
                return true;
            }
        }
        Log.Debug($"{nameof(StreamingTaskManager)}.{nameof(RemoveSubscriptionItem)}: Cannot remove item: [{item}]. Item not found.");
        return false;
    }

    /// <summary>
    /// Restarts the streaming task by stopping the current one and starting a new one. 
    /// This is useful for updating subscriptions without needing to manually stop and start.
    /// </summary>
    private void RestartStreaming()
    {
        StopStreaming();
        StartStreaming();
    }

    /// <summary>
    /// Starts the streaming task and executes the provided streaming action.
    /// </summary>
    private void StartStreaming()
    {
        lock (_streamingTaskLock)
        {
            if (_hasPendingSubscriptions)
            {
                // Avoid duplicate subscriptions by checking if a subscription is already in progress
                return;
            }
            _hasPendingSubscriptions = true;
        }

        _streamingTask = Task.Factory.StartNew(async () =>
        {
            // Wait for a specified delay to batch multiple symbol subscriptions into a single request
            await Task.Delay(_subscribeDelay).ConfigureAwait(false);

            List<string> brokerageTickers;
            lock (_streamingTaskLock)
            {
                _hasPendingSubscriptions = false;
                brokerageTickers = _subscriptionBrokerageTickers.ToList();
                if (brokerageTickers.Count == 0)
                {
                    // If there are no symbols to subscribe to, exit the task
                    Log.Trace($"{nameof(StreamingTaskManager)}.{nameof(StartStreaming)}: No symbols to subscribe to at this time. Exiting subscription task.");
                    return;
                }
            }

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    var result = await _streamAction(brokerageTickers, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException oex)
                {
                    // Ski
                }
                catch (Exception ex)
                {
                    Log.Error($"{nameof(StreamingTaskManager)}.Exception: {ex}");
                }
            }
        });
    }

    /// <summary>
    /// Stops the currently running streaming task and cancels the current task.
    /// </summary>
    private void StopStreaming()
    {
        lock (_streamingTaskLock)
        {
            if (_hasPendingSubscriptions)
            {
                // Avoid duplicate subscriptions by checking if a subscription is already in progress
                return;
            }
        }

        Log.Debug($"{nameof(StreamingTaskManager)}.{nameof(StopStreaming)}: Stopping the current streaming task.");

        if (_streamingTask != null)
        {
            _cancellationTokenSource.Cancel();

            try
            {
                if (!_streamingTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    Log.Error($"{nameof(StreamingTaskManager)}.{nameof(StopStreaming)}: Timeout while waiting for the streaming task to complete.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{nameof(StreamingTaskManager)}.{nameof(StopStreaming)}: Error during task cancellation: {ex}");
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
            }
        }
    }

    /// <summary>
    /// Releases the resources used by the current instance.
    /// </summary>
    public void Dispose()
    {
        _streamingTask?.DisposeSafely();
        _cancellationTokenSource?.DisposeSafely();
    }
}
