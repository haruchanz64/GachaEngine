using System;
using System.Collections.Generic;
using GachaEngine.Core;
using GachaEngine.Data;
using UnityEngine;

namespace GachaEngine.Engine
{
    /// <summary>
    /// Default <see cref="IGachaRng"/> backed by <see cref="System.Random"/> with optional deterministic seeding.
    /// </summary>
    public sealed class SeededGachaRng : IGachaRng
    {
        private readonly System.Random _rng;

        /// <summary>
        /// Creates an RNG instance with an optional fixed <paramref name="seed"/> for reproducible pulls and simulations.
        /// </summary>
        /// <param name="seed">Optional integer seed; uses time-dependent default seeding when <c>null</c>.</param>
        public SeededGachaRng(int? seed = null)
        {
            _rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        /// <inheritdoc/>
        public double NextDouble() => _rng.NextDouble();

        /// <inheritdoc/>
        public int NextInt(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);
    }

    /// <summary>
    /// Serialization wrapper used by <see cref="InMemoryPityStore"/> to round-trip pity states through <see cref="JsonUtility"/>.
    /// </summary>
    [Serializable]
    internal sealed class PityStoreSnapshot
    {
        public List<GachaPityState> states = new List<GachaPityState>();
    }

    /// <summary>
    /// Default in-memory <see cref="IPityStore"/> with JSON snapshot export and import helpers.
    /// </summary>
    public sealed class InMemoryPityStore : IPityStore
    {
        private readonly Dictionary<string, GachaPityState> _states = new Dictionary<string, GachaPityState>(StringComparer.Ordinal);

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public void Save(string pityGroupId, GachaPityState state)
        {
            string key = string.IsNullOrWhiteSpace(pityGroupId) ? "default" : pityGroupId;
            _states[key] = state;
        }

        /// <inheritdoc/>
        public void Clear() => _states.Clear();

        /// <summary>
        /// Serializes all stored pity states to a <see cref="JsonUtility"/>-compatible JSON string.
        /// </summary>
        /// <param name="prettyPrint">When <c>true</c>, formats the output JSON with indentation.</param>
        public string ExportJson(bool prettyPrint = true)
        {
            var snap = new PityStoreSnapshot();
            foreach (var kvp in _states) snap.states.Add(kvp.Value);
            return JsonUtility.ToJson(snap, prettyPrint);
        }

        /// <summary>
        /// Replaces all in-memory pity states with entries deserialized from <paramref name="json"/>.
        /// </summary>
        /// <param name="json">JSON snapshot previously produced by <see cref="ExportJson"/>; blank input leaves the store empty.</param>
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

    /// <summary>
    /// Default <see cref="IDuplicateResolver"/> that grants standard duplicate shards until <see cref="GachaItemEntry.maxOwnedRank"/> is exceeded, then awards overflow currency.
    /// </summary>
    public sealed class DefaultDuplicateResolver : IDuplicateResolver
    {
        /// <inheritdoc/>
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

    /// <summary>
    /// Simple dictionary-backed <see cref="IGachaCurrencyWallet"/> for prototyping, offline play, and EditMode tests.
    /// </summary>
    public sealed class InMemoryCurrencyWallet : IGachaCurrencyWallet
    {
        private readonly Dictionary<string, int> _balances = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <inheritdoc/>
        public int GetBalance(string currencyId) =>
            !string.IsNullOrEmpty(currencyId) && _balances.TryGetValue(currencyId, out int v) ? v : 0;

        /// <inheritdoc/>
        public bool TrySpend(string currencyId, int amount)
        {
            if (amount <= 0) return true;
            int current = GetBalance(currencyId);
            if (current < amount) return false;
            _balances[currencyId] = current - amount;
            return true;
        }

        /// <inheritdoc/>
        public void Add(string currencyId, int amount)
        {
            if (string.IsNullOrEmpty(currencyId) || amount <= 0) return;
            _balances[currencyId] = GetBalance(currencyId) + amount;
        }
    }
}
