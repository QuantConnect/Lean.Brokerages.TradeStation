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
using QuantConnect.Securities;
using QuantConnect.Data.Market;
using System.Collections.Generic;
using System.Collections.Concurrent;
using QuantConnect.Brokerages.TradeStation.Models;

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

    private static IEnumerable<TestCaseData> SubscribeDifferentSecurityTypeTestParameters
    {
        get
        {
            yield return new TestCaseData(new[]
            {
                Symbols.AAPL,
                Symbol.CreateFuture(Futures.Softs.Cotton2, Market.ICE, new DateTime(2025, 12, 1)),
                Symbol.CreateOption(Symbols.AAPL, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 245m, new DateTime(2025, 10, 17)),
                Symbol.CreateOption(Symbols.AAPL, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Put, 245m, new DateTime(2025, 10, 17)),
                Symbol.CreateOption(Symbols.AAPL, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Call, 247.5m, new DateTime(2025, 10, 17)),
                Symbol.CreateOption(Symbols.AAPL, Market.USA, SecurityType.Option.DefaultOptionStyle(), OptionRight.Put, 247.5m, new DateTime(2025, 10, 17))
            }, Resolution.Tick);

            var index = Symbol.Create("VIX", SecurityType.Index, Market.USA);
            yield return new TestCaseData(new[]
            {
                index,
                Symbol.CreateOption(index, Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Call, 22m, new DateTime(2025, 10, 22)),
                Symbol.CreateOption(index, Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Put, 22m, new DateTime(2025, 10, 22)),
                Symbol.CreateOption(index, Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Call, 21.5m, new DateTime(2025, 10, 22)),
                Symbol.CreateOption(index, Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Put, 21.5m, new DateTime(2025, 10, 22)),
                Symbol.CreateOption(index, "VIXW", Market.USA, SecurityType.IndexOption.DefaultOptionStyle(), OptionRight.Call, 22m, new DateTime(2025, 10, 29))
            }, Resolution.Tick);
        }
    }

    [TestCaseSource(nameof(SubscribeTestParameters))]
    public void StreamsData(Symbol symbol, Resolution resolution)
    {
        var cancelationToken = new CancellationTokenSource();
        var configs = GetSubscriptionDataConfigsBySymbolResolution(symbol, resolution);

        var quotes = new List<BaseData>();

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
                        Log.Trace($"Data received: {baseData}");

                        switch (baseData)
                        {
                            case Tick t when t.TickType == TickType.Quote:
                            case QuoteBar qb:
                                quotes.Add(baseData);

                                if (quotes.Count >= 10)
                                {
                                    quote.Set();
                                }
                                break;
                            case Tick t when t.TickType == TickType.Trade:
                            case TradeBar qb:
                                trade.Set();
                                break;
                        }
                    }
                });
        }

        Assert.IsTrue(trade.WaitOne(resolution.ToTimeSpan() + TimeSpan.FromSeconds(30)));
        Assert.IsTrue(quote.WaitOne(resolution.ToTimeSpan() + TimeSpan.FromSeconds(240)));

        foreach (var config in configs)
        {
            _brokerage.Unsubscribe(config);
        }

        Thread.Sleep(2000);

        cancelationToken.Cancel();

        Assert.GreaterOrEqual(quotes.Count, 2);

        AssertBaseData(quotes);
    }

    [TestCase(105, 2)]
    [TestCase(300, 100)]
    public void MultipleSubscription(int initSubscribeAmount, int unSubscribeAmount)
    {
        var lockObject = new object();
        var resetEvent = new ManualResetEvent(false);
        var cancelationToken = new CancellationTokenSource();
        var amountDataBySymbol = new ConcurrentDictionary<Symbol, int>();
        var configBySymbol = new Dictionary<Symbol, (List<SubscriptionDataConfig> Configs, CancellationTokenSource CancellationTokenSource)>();

        var equitySymbols = _equitySymbols.Value.Take(initSubscribeAmount);

        Log.Trace("");
        Log.Trace($"SUBSCRIBE 105 UNSUBSCRIBE 2 THEN 100 TEST");
        Log.Trace("");

        foreach (var symbol in equitySymbols)
        {
            foreach (var config in GetSubscriptionDataConfigsBySymbolResolution(symbol, Resolution.Tick))
            {
                // Try to add a new entry with a single-element array containing the current config
                if (!configBySymbol.TryAdd(symbol, (new List<SubscriptionDataConfig> { config }, new CancellationTokenSource())))
                {
                    // If the key already exists, append the new config to the existing array
                    var existingConfigs = new List<SubscriptionDataConfig>(configBySymbol[symbol].Configs)
                    {
                        config
                    };
                    configBySymbol[symbol] = (existingConfigs, configBySymbol[symbol].CancellationTokenSource);
                }

                ProcessFeed(_brokerage.Subscribe(config, (s, e) => { }), configBySymbol[symbol].CancellationTokenSource.Token, callback:
                    (baseData) =>
                    {
                        if (baseData != null)
                        {
                            Log.Trace($"Data received: {baseData}");
                            lock (lockObject)
                            {
                                amountDataBySymbol.AddOrUpdate(baseData.Symbol, 1, (k, o) => o + 1);

                                if (amountDataBySymbol.Count == 49)
                                {
                                    resetEvent.Set();
                                }
                            }
                        }
                    });
            }
        }

        resetEvent.WaitOne(TimeSpan.FromSeconds(30), cancelationToken.Token);

        foreach (var configs in configBySymbol.Values.TakeLast(unSubscribeAmount))
        {
            foreach (var config in configs.Configs)
            {
                _brokerage.Unsubscribe(config);
            }
            configs.CancellationTokenSource.Cancel();
            configBySymbol.Remove(configs.Configs[0].Symbol);
        }

        Assert.Greater(amountDataBySymbol.Count, 0);
        Assert.True(amountDataBySymbol.Values.All(x => x > 0));

        amountDataBySymbol.Clear();
        resetEvent.Reset();

        resetEvent.WaitOne(TimeSpan.FromSeconds(30), cancelationToken.Token);

        foreach (var configs in configBySymbol.Values)
        {
            foreach (var config in configs.Configs)
            {
                _brokerage.Unsubscribe(config);
            }
            configs.CancellationTokenSource.Cancel();
        }

        Assert.Greater(amountDataBySymbol.Count, 0);
        Assert.True(amountDataBySymbol.Values.All(x => x > 0));

        cancelationToken.Cancel();
    }

    [Test, TestCaseSource(nameof(SubscribeDifferentSecurityTypeTestParameters))]
    public void SubscriptionOnDifferentSymbolSecurityTypes(Symbol[] subscribeSymbols, Resolution resolution)
    {
        var lockObject = new object();
        var cancelationToken = new CancellationTokenSource();
        var amountDataBySymbol = new ConcurrentDictionary<Symbol, int>();
        var resetEvent = new AutoResetEvent(false);
        var configBySymbol = new Dictionary<Symbol, List<SubscriptionDataConfig>>();
        var quotes = new List<BaseData>();

        foreach (var symbol in subscribeSymbols)
        {
            foreach (var config in GetSubscriptionDataConfigs(symbol, resolution))
            {
                // Try to add a new entry with a single-element array containing the current config
                if (!configBySymbol.TryAdd(config.Symbol, new List<SubscriptionDataConfig> { config }))
                {
                    // If the key already exists, append the new config to the existing array
                    var existingConfigs = new List<SubscriptionDataConfig>(configBySymbol[config.Symbol])
                    {
                        config
                    };
                    configBySymbol[config.Symbol] = existingConfigs;
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

                                if (amountDataBySymbol.Values.Count == subscribeSymbols.Length && amountDataBySymbol.Values.All(x => x > 2))
                                {
                                    resetEvent.Set();
                                }
                            }

                            switch (baseData)
                            {
                                case Tick t when t.TickType == TickType.Quote:
                                case QuoteBar qb:
                                    quotes.Add(baseData);
                                    break;
                            }
                        }
                    });
            }
        }

        resetEvent.WaitOne(TimeSpan.FromSeconds(60), cancelationToken.Token);

        foreach (var configs in configBySymbol.Values)
        {
            foreach (var config in configs)
            {
                _brokerage.Unsubscribe(config);
            }
        }

        resetEvent.WaitOne(TimeSpan.FromSeconds(5), cancelationToken.Token);

        Assert.Greater(amountDataBySymbol.Count, 0);
        Assert.True(amountDataBySymbol.Values.All(x => x > 1));

        cancelationToken.Cancel();
        AssertBaseData(quotes);
    }

    public static IEnumerable<TestCaseData> CacheSymbolTestCases()
    {
        var underlying = Symbol.Create("AAPL", SecurityType.Equity, Market.USA);
        yield return new TestCaseData(underlying, "AAPL");
        var oc = Symbol.CreateOption(underlying, Market.USA, OptionStyle.American, OptionRight.Call, 167.5m, new DateTime(2024, 5, 10));
        yield return new TestCaseData(oc, "AAPL 240510C167.5");

        var indexUnderlying2 = Symbol.Create("VIX", SecurityType.Index, Market.USA);
        yield return new TestCaseData(indexUnderlying2, "$VIX.X");

        yield return new TestCaseData(Symbol.CreateFuture("ES", Market.CME, new DateTime(2024, 12, 10)), "ESZ24");
    }

    [TestCaseSource(nameof(CacheSymbolTestCases))]
    public void ConvertsBrokerageSymbolToLeanSymbolWhenSymbolIsInCache(Symbol leanSymbol, string actualBrokerageSymbol)
    {
        // load the lean symbol into the mapper cache (simulate subscription)
        _ = _brokerage._symbolMapper.GetBrokerageSymbol(leanSymbol);

        _brokerage.HandleQuoteEvents(new Quote() { Symbol = actualBrokerageSymbol });

        // we can map the brokerage symbol back to the original lean symbol
        Assert.IsTrue(_brokerage._symbolMapper.TryGetLeanSymbol(actualBrokerageSymbol, default, default, out var convertedLeanSymbol));
        Assert.AreEqual(leanSymbol, convertedLeanSymbol);
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

    private static void AssertBaseData(IEnumerable<BaseData> baseData)
    {
        foreach (var data in baseData)
        {
            switch (data)
            {
                case Tick t:
                    Assert.Greater(t.AskPrice, t.BidPrice);
                    break;
                case QuoteBar qb when qb.Ask != null && qb.Bid != null:
                    Assert.Greater(qb.Ask.Open, qb.Bid.Open);
                    Assert.Greater(qb.Ask.High, qb.Bid.High);
                    Assert.Greater(qb.Ask.Low, qb.Bid.Low);
                    Assert.Greater(qb.Ask.Close, qb.Bid.Close);
                    break;
            }

        }
    }
}