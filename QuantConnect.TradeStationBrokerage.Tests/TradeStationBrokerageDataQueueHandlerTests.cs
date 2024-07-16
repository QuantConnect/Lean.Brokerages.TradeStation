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
using NUnit.Framework;
using System.Threading;
using QuantConnect.Data;
using QuantConnect.Tests;
using QuantConnect.Logging;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using QuantConnect.Data.Market;
using System.Collections.Generic;
using QuantConnect.Algorithm.CSharp;
using System.Collections.Concurrent;

namespace QuantConnect.Brokerages.TradeStation.Tests;

[TestFixture]
public partial class TradeStationBrokerageTests
{
    private Lazy<IEnumerable<Symbol>> _equitySymbols;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _equitySymbols = new(() =>
        {
            return _stockSymbols.Select(s => Symbol.Create(s, SecurityType.Equity, Market.USA));
        });
    }

    private static IEnumerable<TestCaseData> SubscribeTestParameters
    {
        get
        {
            yield return new TestCaseData(Symbols.AAPL, Resolution.Tick);
            yield return new TestCaseData(Symbols.AAPL, Resolution.Second);
        }
    }

    [TestCaseSource(nameof(SubscribeTestParameters))]
    public void StreamsData(Symbol symbol, Resolution resolution)
    {
        var cancelationToken = new CancellationTokenSource();
        var configs = GetSubscriptionDataConfigsBySymbolResolution(symbol, resolution);

        var trade = new ManualResetEvent(false);
        var quote = new ManualResetEvent(false);
        foreach (var config in configs)
        {
            ProcessFeed(_brokerage.Subscribe(config, (s, e) => { }),
                cancelationToken,
                (baseData) =>
                {
                    if (baseData != null)
                    {
                        if ((baseData as Tick)?.TickType == TickType.Quote || baseData is QuoteBar)
                        {
                            quote.Set();
                        }
                        else if ((baseData as Tick)?.TickType == TickType.Trade || baseData is TradeBar)
                        {
                            trade.Set();
                        }
                        Log.Trace($"Data received: {baseData}");
                    }
                });
        }

        Assert.IsTrue(trade.WaitOne(resolution.ToTimeSpan() + TimeSpan.FromSeconds(30)));
        Assert.IsTrue(quote.WaitOne(resolution.ToTimeSpan() + TimeSpan.FromSeconds(30)));

        foreach (var config in configs)
        {
            _brokerage.Unsubscribe(config);
        }

        Thread.Sleep(2000);

        cancelationToken.Cancel();
    }

    [TestCase(100)]
    public void MultipleSubscription(int subscribeAmount)
    {
        var lockObject = new object();
        var resetEvent = new ManualResetEvent(false);
        var cancelationToken = new CancellationTokenSource();
        var amountDataBySymbol = new ConcurrentDictionary<Symbol, int>();
        var configBySymbol = new Dictionary<Symbol, List<SubscriptionDataConfig>>();

        foreach (var symbol in _equitySymbols.Value.Take(subscribeAmount))
        {
            foreach (var config in GetSubscriptionDataConfigsBySymbolResolution(symbol, Resolution.Tick))
            {
                // Try to add a new entry with a single-element array containing the current config
                if (!configBySymbol.TryAdd(symbol, new List<SubscriptionDataConfig>{ config }))
                {
                    // If the key already exists, append the new config to the existing array
                    var existingConfigs = new List<SubscriptionDataConfig>(configBySymbol[symbol])
                    {
                        config
                    };
                    configBySymbol[symbol] = existingConfigs;
                }

                ProcessFeed(_brokerage.Subscribe(config, (s, e) => { }), cancelationToken.Token, callback:
                    (baseData) =>
                    {
                        if (baseData != null)
                        {
                            Log.Trace($"Data received: {baseData}");
                            lock (lockObject)
                            {
                                amountDataBySymbol.AddOrUpdate(baseData.Symbol, 1, (k, o) => o + 1);

                                if (amountDataBySymbol.Count == 100)
                                {
                                    resetEvent.Set();
                                }
                            }
                        }
                    });
            }
        }

        resetEvent.WaitOne(TimeSpan.FromSeconds(30), cancelationToken.Token);

        foreach (var configs in configBySymbol.Values.Take(subscribeAmount - 2))
        {
            foreach(var config in configs)
            {
                _brokerage.Unsubscribe(config);
            }
        }

        amountDataBySymbol.Clear();

        Log.Debug($"{nameof(TradeStationBrokerageTests)}.{nameof(MultipleSubscription)}.1.amountDataBySymbol.Count = {amountDataBySymbol.Count}");

        resetEvent.WaitOne(TimeSpan.FromSeconds(60), cancelationToken.Token);

        foreach (var configs in configBySymbol.Values.Skip(subscribeAmount - 2))
        {
            foreach (var config in configs)
            {
                _brokerage.Unsubscribe(config);
            }
        }

        Log.Debug($"{nameof(TradeStationBrokerageTests)}.{nameof(MultipleSubscription)}.2.amountDataBySymbol.Count = {amountDataBySymbol.Count}");

        cancelationToken.Cancel();
    }

    private Task ProcessFeed(
    IEnumerator<BaseData> enumerator,
    CancellationToken cancellationToken,
    int cancellationTokenDelayMilliseconds = 100,
    Action<BaseData> callback = null,
    Action throwExceptionCallback = null)
    {
        return Task.Factory.StartNew(() =>
        {
            try
            {
                while (enumerator.MoveNext() && !cancellationToken.IsCancellationRequested)
                {
                    BaseData tick = enumerator.Current;

                    if (tick != null)
                    {
                        callback?.Invoke(tick);
                    }

                    cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(cancellationTokenDelayMilliseconds));
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"{nameof(TradeStationBrokerageTest)}.{nameof(ProcessFeed)}.Exception: {ex.Message}");
                throw;
            }
        }, cancellationToken).ContinueWith(task =>
        {
            if (throwExceptionCallback != null)
            {
                throwExceptionCallback();
            }
            Log.Debug("The throwExceptionCallback is null.");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private IEnumerable<SubscriptionDataConfig> GetSubscriptionDataConfigsBySymbolResolution(Symbol symbol, Resolution resolution) => resolution switch
    {
        Resolution.Tick => GetTickSubscriptionDataConfigs(symbol),
        _ => GetSubscriptionDataConfigs(symbol, resolution)
    };

    private IEnumerable<SubscriptionDataConfig> GetTickSubscriptionDataConfigs(Symbol symbol)
    {
        yield return new SubscriptionDataConfig(GetSubscriptionDataConfig<Tick>(symbol, Resolution.Tick), tickType: TickType.Trade);
        yield return new SubscriptionDataConfig(GetSubscriptionDataConfig<Tick>(symbol, Resolution.Tick), tickType: TickType.Quote);
    }

    private static IEnumerable<SubscriptionDataConfig> GetSubscriptionDataConfigs(Symbol symbol, Resolution resolution)
    {
        yield return GetSubscriptionDataConfig<TradeBar>(symbol, resolution);
        yield return GetSubscriptionDataConfig<QuoteBar>(symbol, resolution);
    }

    private readonly string[] _stockSymbols = new string[]
    {
        "UBQU", "AREB", "AMIX", "NVDA", "RDAR", "FFIE", "TONR", "TSLA", "MAXN", "DJT",
        "DATS", "LCID", "SOUN", "DRNK", "NIO", "MARA", "OPTT", "SCNI", "MIRA", "RIVN",
        "AEON", "AAPL", "TKMO", "PLUG", "F", "SOFI", "PLTR", "PDPG", "FCEL", "CLSK",
        "WHEN", "ASTA", "DNA", "AGRI", "AMD", "INND", "INTC", "SIRI", "GRST", "BOTY",
        "ZRFY", "NNAX", "GNS", "M", "QS", "RIOT", "OPEN", "TWOH", "IGEX", "IFXY", "BITF",
        "AAL", "RUN", "CCL", "IREN", "QLGN", "BRGO", "CORZ", "HOOD", "BAC", "PHUN",
        "AMZN", "VNUE", "AUR", "SPYR", "TELL", "PAOG", "MNGG", "ERIC", "CEI", "LYG",
        "GRAB", "WULF", "VPLM", "CBIA", "ABR", "AVGO", "BDPT", "GPFT", "TSOI", "WFC",
        "CHPT", "COSG", "KZIA", "COIN", "CLF", "T", "MDCE", "BB", "BTBT", "JBLU", "PFE",
        "SMR", "DNN", "ACHR", "CFGX", "GME", "EOSE", "HOLO", "BB", "C", "GOOGL", "COSG",
        "TSM", "MDCE", "NKE", "UPST", "EOSE", "TLRY", "CELH", "NEE", "ASNS", "MU",
        "CFGX", "WBA", "CIFR", "CGBS", "NU", "EARI", "XPEV", "PPJE", "AGNC", "KGC",
        "PRST", "WBD", "IJJP", "AES", "VALE", "HUMA", "RONN", "BABA", "WLDS", "AMC",
        "UBER", "NSAV", "DAL", "BIDU", "IVP", "CSCO", "CPOP", "AUNXF", "CMG", "VHAI",
        "IGPK", "ITUB", "CNP", "JD", "IAG", "SWN", "BBBT", "META", "JOBY", "BTG", "HUT",
        "SEDG", "KMI", "SMCI", "ADHC", "INFY", "MWG", "AFRM", "ILUS", "PTON", "MSFT",
        "HCP", "PDD", "JPM", "NOK", "CMCSA", "JMIA", "ALTM", "XOM", "IQ", "KVUE",
        "NOVA", "SING", "LYFT", "PSNY", "PYPL", "RVMD", "SISI", "RUM", "LAZR", "GM",
        "BTE", "DKNG", "HIMS", "GOOG", "BBD", "BMY", "VZ", "PACB", "SIRC",
        "ARTL", "PATH", "HBAN", "CSX", "MLGO"
    };
}