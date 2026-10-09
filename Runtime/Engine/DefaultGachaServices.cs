using System;
using System.Collections.Generic;
using GachaEngine.Core;
using GachaEngine.Data;
using UnityEngine;

namespace GachaEngine.Engine
{
    public sealed class SeededGachaRng : IGachaRng
    {
        private readonly System.Random _rng;

        public SeededGachaRng(int? seed = null)
        {
            _rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        public double NextDouble() => _rng.NextDouble();
        public int NextInt(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);
    }

    [Serializable]
    internal sealed class PityStoreSnapshot
    {
        public List<GachaPityState> states = new List<GachaPityState>();
    }

    /// <summary>
    /// Default in-memory IPityStore with JSON export/import helpers (compatible with SaveEncryptionToolkit).
    /// </summary>
    public sealed class InMemoryPityStore : IPityStore
    {
        private readonly Dictionary<string, GachaPityState> _states = new Dictionary<string, GachaPityState>(StringComparer.Ordinal);

        public GachaPityState GetOrCreate(string pityGroupId)
        {
            string key = string.IsNullOrWhiteSpace(pityGroupId) ? "default" : pityGroupId;
            if (!_states.TryGetValue(key, out GachaPityState state))
            {
                state = new GachaPityState { pityGroupId = key };
                _states[key] = state;
            }
            return state;
        }

        public void Save(string pityGroupId, GachaPityState state)
        {
            string key = string.IsNullOrWhiteSpace(pityGroupId) ? "default" : pityGroupId;
            _states[key] = state;
        }

        public void Clear() => _states.Clear();

        public string ExportJson(bool prettyPrint = true)
        {
            var snap = new PityStoreSnapshot();
            foreach (var kvp in _states) snap.states.Add(kvp.Value);
            return JsonUtility.ToJson(snap, prettyPrint);
        }

        public void ImportJson(string json)
        {
            _states.Clear();
            if (string.IsNullOrWhiteSpace(json)) return;
            var snap = JsonUtility.FromJson<PityStoreSnapshot>(json);
            if (snap == null || snap.states == null) return;
            for (int i = 0; i < snap.states.Count; i++)
            {
                var st = snap.states[i];
                if (st != null && !string.IsNullOrEmpty(st.pityGroupId))
                {
                    _states[st.pityGroupId] = st;
                }
            }
        }
    }

    public sealed class DefaultDuplicateResolver : IDuplicateResolver
    {
        public DuplicateResolution Resolve(GachaItemEntry item, int ownedCountBeforePull)
        {
            int nextOwned = ownedCountBeforePull + 1;
            if (ownedCountBeforePull <= 0)
            {
                return new DuplicateResolution
                {
                    isDuplicate = false,
                    isMaxRankOverflow = false,
                    newOwnedCount = nextOwned
                };
            }

            bool overflow = nextOwned > Math.Max(1, item.maxOwnedRank);
            return new DuplicateResolution
            {
                isDuplicate = true,
                isMaxRankOverflow = overflow,
                newOwnedCount = nextOwned,
                rewardCurrencyId = overflow ? item.overflowCurrencyId : item.duplicateCurrencyId,
                rewardAmount = overflow ? item.overflowRewardAmount : item.duplicateRewardAmount
            };
        }
    }

    public sealed class InMemoryCurrencyWallet : IGachaCurrencyWallet
    {
        private readonly Dictionary<string, int> _balances = new Dictionary<string, int>(StringComparer.Ordinal);

        public int GetBalance(string currencyId) =>
            !string.IsNullOrEmpty(currencyId) && _balances.TryGetValue(currencyId, out int v) ? v : 0;

        public bool TrySpend(string currencyId, int amount)
        {
            if (amount <= 0) return true;
            int current = GetBalance(currencyId);
            if (current < amount) return false;
            _balances[currencyId] = current - amount;
            return true;
        }

        public void Add(string currencyId, int amount)
        {
            if (string.IsNullOrEmpty(currencyId) || amount <= 0) return;
            _balances[currencyId] = GetBalance(currencyId) + amount;
        }
    }
}
