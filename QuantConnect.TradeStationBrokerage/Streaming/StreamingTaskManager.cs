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
using QuantConnect.Logging;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace QuantConnect.Brokerages.TradeStation.Streaming;

/// <summary>
/// Manages streaming tasks for a collection of items, allowing for subscription, unSubscription and restarting of streaming processes.
/// </summary>
public class StreamingTaskManager
{
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
    /// The maximum number of items that can be subscribed to.
    /// </summary>
    private readonly int _maxSubscriptionLimit;

    /// <summary>
    /// Synchronization object used to ensure thread safety when starting or restarting the streaming task.
    /// </summary>
    private readonly object _streamingTaskLock = new();

    /// <summary>
    /// Specifies the delay interval between subscription attempts.
    /// </summary>
    private readonly TimeSpan _subscribeDelay = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// The action to execute for streaming the subscribed items.
    /// </summary>
    private readonly Func<IReadOnlyCollection<string>, CancellationToken, Task<bool>> _streamAction;

    /// <summary>
    /// Event used to signal the completion of the streaming task.
    /// </summary>
    private readonly AutoResetEvent autoResetEvent = new(false);

    /// <summary>
    /// Gets the collection of subscribed items.
    /// </summary>
    public readonly HashSet<string> subscriptionBrokerageTickers;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingTaskManager"/> class.
    /// </summary>
    /// <param name="streamingAction">The action to execute for streaming the items.</param>
    /// <param name="initialSubscribedItems">Initial collection of items to subscribe to.</param>
    /// <param name="maxSubscriptionLimit">The maximum number of items that can be subscribed to. (Defaults to 100)</param>
    public StreamingTaskManager(
        Func<IReadOnlyCollection<string>, CancellationToken, Task<bool>> streamingAction,
        IEnumerable<string> initialSubscribedItems,
        int maxSubscriptionLimit = 100)
    {
        _streamAction = streamingAction ?? throw new ArgumentNullException(nameof(streamingAction), "Streaming action cannot be null.");
        subscriptionBrokerageTickers = new(initialSubscribedItems ?? throw new ArgumentNullException(nameof(initialSubscribedItems), "Initial subscribed items cannot be null."));
        _maxSubscriptionLimit = maxSubscriptionLimit;
    }

    /// <summary>
    /// Starts the streaming task and executes the provided streaming action.
    /// </summary>
    public void StartStreaming()
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
                brokerageTickers = subscriptionBrokerageTickers.ToList();
                if (brokerageTickers.Count == 0)
                {
                    // If there are no symbols to subscribe to, exit the task
                    Log.Trace($"{nameof(StreamingTaskManager)}.{nameof(StartStreaming)}: No symbols to subscribe to at this time. Exiting subscription task.");
                    return;
                }
            }

            try
            {
                var result = await _streamAction(brokerageTickers, _cancellationTokenSource.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error($"{nameof(StreamingTaskManager)}.Exception: {ex}");
            }
            finally
            {
                // Signal: task is completed
                autoResetEvent.Set();
            }
        });
    }

    /// <summary>
    /// Stops the currently running streaming task and cancels the current task.
    /// </summary>
    public void StopStreaming()
    {
        Log.Debug($"{nameof(StreamingTaskManager)}.{nameof(StopStreaming)}: Stopping the current streaming task.");

        if (_streamingTask != null)
        {
            _cancellationTokenSource.Cancel();

            if (!autoResetEvent.WaitOne(TimeSpan.FromSeconds(5)))
            {
                Log.Error($"{nameof(StreamingTaskManager)}.{nameof(StopStreaming)}: Timeout while waiting for the streaming task to complete.");
            }

            try
            {
                _streamingTask.Wait();
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
    /// Restarts the streaming task by stopping the current one and starting a new one. 
    /// This is useful for updating subscriptions without needing to manually stop and start.
    /// </summary>
    public void RestartStreaming()
    {
        StopStreaming();
        StartStreaming();
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
        if (subscriptionBrokerageTickers.Count >= _maxSubscriptionLimit)
        {
            Log.Debug($"{nameof(StreamingTaskManager)}.{nameof(AddSubscriptionItem)}: Cannot add more items. Maximum limit reached.");
            return false;
        }

        if (!subscriptionBrokerageTickers.Add(item))
        {
            Log.Debug($"{nameof(StreamingTaskManager)}.{nameof(AddSubscriptionItem)}: Item already exists in the list.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Removes an item from the subscription list.
    /// </summary>
    /// <param name="item">The item to remove from the subscription list.</param>
    /// <returns><c>true</c> if the item was removed successfully; otherwise, <c>false</c>.</returns>
    public bool RemoveSubscriptionItem(string item)
    {
        if (subscriptionBrokerageTickers.Remove(item))
        {
            return true;
        }

        Log.Debug($"{nameof(StreamingTaskManager)}.{nameof(RemoveSubscriptionItem)}: Cannot remove item: [{item}]. Item not found.");
        return false;
    }
}
