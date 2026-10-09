using System;
using System.Collections.Generic;
using System.Globalization;
using GachaEngine.Core;
using GachaEngine.Data;
using UnityEngine;

namespace GachaEngine.Engine
{
    /// <summary>
    /// Deterministic pure C# gacha roller supporting customizable multi-tier soft/hard pity,
    /// 50/50 or custom featured guarantees, Fate Path, Step-Up ladders, duplicate conversion,
    /// and 1-hour refreshed pull history.
    /// </summary>
    public sealed class GachaRoller
    {
        private readonly IGachaRng _rng;
        private readonly IPityStore _pityStore;
        private readonly IDuplicateResolver _duplicateResolver;
        private readonly IGachaCurrencyWallet _wallet;
        private readonly Func<DateTime> _utcNowProvider;
        private readonly Dictionary<string, int> _ownedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<IGachaPoolModifier> _modifiers = new List<IGachaPoolModifier>();

        public IPityStore PityStore => _pityStore;
        public GachaHistoryLog History { get; }

        public event Action<GachaPullResult> OnPullCompleted;
        public event Action<GachaPullBatchResult> OnBatchCompleted;

        public GachaRoller(
            IGachaRng rng = null,
            IPityStore pityStore = null,
            IDuplicateResolver duplicateResolver = null,
            IGachaCurrencyWallet wallet = null,
            GachaHistoryLog history = null,
            Func<DateTime> utcNowProvider = null)
        {
            _rng = rng ?? new SeededGachaRng();
            _pityStore = pityStore ?? new InMemoryPityStore();
            _duplicateResolver = duplicateResolver ?? new DefaultDuplicateResolver();
            _wallet = wallet;
            _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
            History = history ?? new GachaHistoryLog(2000, 3600, _utcNowProvider());
        }

        public void AddModifier(IGachaPoolModifier modifier)
        {
            if (modifier != null && !_modifiers.Contains(modifier))
            {
                _modifiers.Add(modifier);
            }
        }

        public bool RemoveModifier(IGachaPoolModifier modifier) => _modifiers.Remove(modifier);

        public void SetFateTarget(GachaBannerAsset banner, string itemId)
        {
            if (banner == null) return;
            var state = _pityStore.GetOrCreate(banner.pityGroupId);
            if (!string.Equals(state.selectedFateItemId, itemId, StringComparison.Ordinal))
            {
                state.selectedFateItemId = itemId;
                state.fatePoints = 0; // Changing Fate Path target resets accumulated points
                _pityStore.Save(banner.pityGroupId, state);
            }
        }

        public int GetOwnedCount(string itemId) =>
            !string.IsNullOrEmpty(itemId) && _ownedCounts.TryGetValue(itemId, out int c) ? c : 0;

        public void ResetAllState()
        {
            _pityStore.Clear();
            _ownedCounts.Clear();
            History.Clear(_utcNowProvider());
        }

        public GachaPullBatchResult RollSingle(GachaBannerAsset banner, bool chargeWallet = false)
        {
            return RollMulti(banner, 1, chargeWallet);
        }

        public GachaPullBatchResult RollMulti(GachaBannerAsset banner, int count = 10, bool chargeWallet = false)
        {
            var batch = new GachaPullBatchResult
            {
                bannerId = banner != null ? banner.bannerId : string.Empty,
                bannerName = banner != null ? banner.bannerName : string.Empty,
                currencyId = banner != null ? banner.currencyId : string.Empty
            };

            if (banner == null || banner.items == null || banner.items.Count == 0)
            {
                batch.succeeded = false;
                batch.errorMessage = "Banner is null or contains no items.";
                return batch;
            }

            var pity = _pityStore.GetOrCreate(banner.pityGroupId);
            StepUpStepConfig activeStep = null;
            int actualCount = Math.Max(1, count);
            int cost = actualCount == 10 ? banner.tenPullCost : banner.singlePullCost * actualCount;

            if (banner.bannerType == GachaBannerType.StepUp && banner.stepUpSteps != null && banner.stepUpSteps.Count > 0)
            {
                int stepIdx = pity.currentStepIndex % banner.stepUpSteps.Count;
                activeStep = banner.stepUpSteps[stepIdx];
                actualCount = Math.Max(1, activeStep.pullCount);
                cost = activeStep.costOverride;
            }

            if (chargeWallet && _wallet != null && cost > 0)
            {
                if (!_wallet.TrySpend(banner.currencyId, cost))
                {
                    batch.succeeded = false;
                    batch.errorMessage = $"Insufficient {banner.currencyId} (needed {cost}, have {_wallet.GetBalance(banner.currencyId)}).";
                    return batch;
                }
            }

            batch.currencySpent = cost;

            for (int i = 0; i < actualCount; i++)
            {
                bool isFinalPullOfStep = activeStep != null && i == actualCount - 1;
                var single = ExecuteSinglePull(banner, pity, isFinalPullOfStep ? activeStep : null);
                batch.results.Add(single);
                History.Record(single);
                OnPullCompleted?.Invoke(single);
            }

            if (activeStep != null && banner.stepUpSteps.Count > 0)
            {
                pity.currentStepIndex = (pity.currentStepIndex + 1) % banner.stepUpSteps.Count;
            }

            _pityStore.Save(banner.pityGroupId, pity);
            OnBatchCompleted?.Invoke(batch);
            return batch;
        }

        private GachaPullResult ExecuteSinglePull(GachaBannerAsset banner, GachaPityState pity, StepUpStepConfig stepOverride)
        {
            pity.totalPulls++;

            // Sort configured tiers from highest rarity to lowest
            var sortedTiers = new List<RarityTierConfig>(banner.tiers);
            sortedTiers.Sort((a, b) => b.rarity.CompareTo(a.rarity));

            // Advance pity counters for all pity-enabled tiers
            for (int i = 0; i < sortedTiers.Count; i++)
            {
                if (sortedTiers[i] != null && sortedTiers[i].enablePity)
                {
                    pity.GetOrCreateCounter(sortedTiers[i].rarity).pullsSinceHit++;
                }
            }

            GachaRarity chosenRarity = sortedTiers.Count > 0 ? sortedTiers[sortedTiers.Count - 1].rarity : GachaRarity.Rare;
            RarityTierConfig chosenTier = sortedTiers.Count > 0 ? sortedTiers[sortedTiers.Count - 1] : null;
            bool softPity = false;
            bool hardPity = false;
            bool stepGuarantee = false;
            int pityAtPull = 0;

            // Check Step-Up guaranteed rarity first
            if (stepOverride != null && stepOverride.guaranteeMinimumRarity)
            {
                var stepTier = banner.GetTierConfig(stepOverride.guaranteedRarity);
                if (stepTier != null)
                {
                    chosenRarity = stepTier.rarity;
                    chosenTier = stepTier;
                    stepGuarantee = true;
                    pityAtPull = pity.GetOrCreateCounter(stepTier.rarity).pullsSinceHit;
                }
            }

            if (!stepGuarantee)
            {
                double roll = _rng.NextDouble();
                double cumulative = 0d;

                for (int i = 0; i < sortedTiers.Count; i++)
                {
                    var tier = sortedTiers[i];
                    if (tier == null) continue;

                    TierPityCounter counter = tier.enablePity ? pity.GetOrCreateCounter(tier.rarity) : null;
                    int currentPity = counter != null ? counter.pullsSinceHit : 0;

                    if (tier.enablePity && currentPity >= Math.Max(1, tier.hardPityPull))
                    {
                        chosenRarity = tier.rarity;
                        chosenTier = tier;
                        hardPity = true;
                        pityAtPull = currentPity;
                        break;
                    }

                    double effProb = tier.baseProbability;
                    bool inSoft = false;
                    if (tier.enablePity && currentPity >= Math.Max(1, tier.softPityStartPull))
                    {
                        int steps = (currentPity - tier.softPityStartPull) + 1;
                        effProb = Math.Min(1d, effProb + (steps * tier.softPityBonusPerPull));
                        inSoft = true;
                    }

                    for (int m = 0; m < _modifiers.Count; m++)
                    {
                        _modifiers[m].ModifyTierRate(banner, tier, ref effProb, pity);
                    }

                    effProb = Math.Clamp(effProb, 0d, 1d);
                    cumulative += effProb;

                    if (roll < cumulative || i == sortedTiers.Count - 1)
                    {
                        chosenRarity = tier.rarity;
                        chosenTier = tier;
                        softPity = inSoft;
                        pityAtPull = currentPity;
                        break;
                    }
                }
            }

            // Reset pity counter for the hit tier
            TierPityCounter hitCounter = chosenTier != null && chosenTier.enablePity
                ? pity.GetOrCreateCounter(chosenRarity)
                : null;
            if (hitCounter != null)
            {
                hitCounter.pullsSinceHit = 0;
            }

            // Pick concrete item within chosen rarity (Fate Path -> Step Guarantee -> 50/50 -> Weighted pool)
            GachaItemEntry picked = ResolveItemWithinRarity(
                banner,
                chosenTier,
                chosenRarity,
                pity,
                hitCounter,
                stepOverride,
                out bool usedGuarantee,
                out bool wonFlip,
                out bool lostFlip,
                out bool usedFatePath);

            // Resolve duplicate rewards
            int ownedBefore = picked != null ? GetOwnedCount(picked.itemId) : 0;
            DuplicateResolution dupe = picked != null ? _duplicateResolver.Resolve(picked, ownedBefore) : default;
            if (picked != null)
            {
                _ownedCounts[picked.itemId] = dupe.newOwnedCount;
                if (dupe.isDuplicate && dupe.rewardAmount > 0 && _wallet != null)
                {
                    _wallet.Add(dupe.rewardCurrencyId, dupe.rewardAmount);
                }
            }

            DateTime nowUtc = _utcNowProvider();
            return new GachaPullResult
            {
                pullIndex = pity.totalPulls,
                bannerId = banner.bannerId,
                bannerName = banner.bannerName,
                item = picked,
                rarity = chosenRarity,
                rarityLabel = banner.GetRarityLabel(chosenRarity),
                isFeatured = picked != null && picked.isFeatured,
                pityAtPull = pityAtPull,
                triggeredSoftPity = softPity,
                triggeredHardPity = hardPity,
                triggeredRateUpGuarantee = usedGuarantee,
                wonRateUpFlip = wonFlip,
                lostRateUpFlip = lostFlip,
                triggeredFatePath = usedFatePath,
                triggeredStepGuarantee = stepGuarantee,
                duplicate = dupe,
                pulledAtUtc = nowUtc.ToString("O", CultureInfo.InvariantCulture),
                pulledAtLocal = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
        }

        private GachaItemEntry ResolveItemWithinRarity(
            GachaBannerAsset banner,
            RarityTierConfig tier,
            GachaRarity rarity,
            GachaPityState pity,
            TierPityCounter counter,
            StepUpStepConfig stepOverride,
            out bool triggeredGuarantee,
            out bool wonFlip,
            out bool lostFlip,
            out bool triggeredFatePath)
        {
            triggeredGuarantee = false;
            wonFlip = false;
            lostFlip = false;
            triggeredFatePath = false;

            var featuredPool = banner.GetItemsByRarity(rarity, featuredOnly: true);
            var standardPool = banner.GetItemsByRarity(rarity, featuredOnly: false);
            var allInRarity = banner.GetItemsByRarity(rarity, featuredOnly: null);

            // 1. Fate Path check
            bool fateApplies = banner.fatePath != null &&
                               banner.fatePath.enabled &&
                               banner.fatePath.targetRarity == rarity &&
                               !string.IsNullOrWhiteSpace(pity.selectedFateItemId);

            if (fateApplies)
            {
                GachaItemEntry fateTarget = FindItemById(allInRarity, pity.selectedFateItemId);
                if (fateTarget != null && pity.fatePoints >= Math.Max(1, banner.fatePath.maxFatePoints))
                {
                    pity.fatePoints = 0;
                    if (counter != null) counter.guaranteedFeaturedNext = false;
                    triggeredFatePath = true;
                    return fateTarget;
                }
            }

            // 2. Step-Up forced featured
            if (stepOverride != null && stepOverride.guaranteeFeatured && featuredPool.Count > 0)
            {
                var chosen = PickWeighted(featuredPool);
                UpdateFatePointsAfterPick(banner, rarity, pity, chosen);
                return chosen;
            }

            // 3. Featured rate-up & 50/50 carry-over guarantee
            if (tier != null && tier.hasFeaturedRateUp && featuredPool.Count > 0)
            {
                bool guaranteed = counter != null && counter.guaranteedFeaturedNext;
                GachaItemEntry chosen;

                if (guaranteed)
                {
                    counter.guaranteedFeaturedNext = false;
                    triggeredGuarantee = true;
                    chosen = PickWeighted(featuredPool);
                }
                else if (_rng.NextDouble() < Math.Clamp(tier.featuredRateUpChance, 0d, 1d))
                {
                    wonFlip = true;
                    chosen = PickWeighted(featuredPool);
                }
                else
                {
                    lostFlip = true;
                    if (tier.guaranteeFeaturedOnNextMiss && counter != null)
                    {
                        counter.guaranteedFeaturedNext = true;
                    }
                    chosen = standardPool.Count > 0 ? PickWeighted(standardPool) : PickWeighted(allInRarity);
                }

                UpdateFatePointsAfterPick(banner, rarity, pity, chosen);
                return chosen;
            }

            var fallback = PickWeighted(allInRarity);
            UpdateFatePointsAfterPick(banner, rarity, pity, fallback);
            return fallback;
        }

        private static void UpdateFatePointsAfterPick(
            GachaBannerAsset banner,
            GachaRarity rarity,
            GachaPityState pity,
            GachaItemEntry chosen)
        {
            if (banner.fatePath == null || !banner.fatePath.enabled || banner.fatePath.targetRarity != rarity)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(pity.selectedFateItemId) || chosen == null) return;

            if (string.Equals(chosen.itemId, pity.selectedFateItemId, StringComparison.Ordinal))
            {
                pity.fatePoints = 0;
            }
            else
            {
                pity.fatePoints = Math.Min(banner.fatePath.maxFatePoints, pity.fatePoints + 1);
            }
        }

        private GachaItemEntry PickWeighted(List<GachaItemEntry> pool)
        {
            if (pool == null || pool.Count == 0) return null;
            if (pool.Count == 1) return pool[0];

            double total = 0d;
            for (int i = 0; i < pool.Count; i++) total += Math.Max(0.01f, pool[i].weight);

            double roll = _rng.NextDouble() * total;
            double acc = 0d;
            for (int i = 0; i < pool.Count; i++)
            {
                acc += Math.Max(0.01f, pool[i].weight);
                if (roll < acc) return pool[i];
            }

            return pool[pool.Count - 1];
        }

        private static GachaItemEntry FindItemById(List<GachaItemEntry> pool, string itemId)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (string.Equals(pool[i].itemId, itemId, StringComparison.Ordinal)) return pool[i];
            }
            return null;
        }

        public static GachaBulkSimulationReport RunBulkSimulation(
            GachaBannerAsset banner,
            int pullCount = 10000,
            string selectedFateItemId = null,
            int? seed = null)
        {
            int count = Math.Max(100, pullCount);
            var simRoller = new GachaRoller(new SeededGachaRng(seed));
            if (!string.IsNullOrEmpty(selectedFateItemId))
            {
                simRoller.SetFateTarget(banner, selectedFateItemId);
            }

            var report = new GachaBulkSimulationReport
            {
                bannerName = banner != null ? banner.bannerName : "Banner",
                totalPulls = count
            };

            if (banner == null) return report;

            int remaining = count;
            while (remaining > 0)
            {
                int batchSize = banner.bannerType == GachaBannerType.StepUp ? 10 : Math.Min(10, remaining);
                var batch = simRoller.RollMulti(banner, batchSize, chargeWallet: false);
                for (int i = 0; i < batch.results.Count && remaining > 0; i++)
                {
                    var r = batch.results[i];
                    remaining--;
                    if (r.rarity == GachaRarity.Legendary)
                    {
                        report.legendaryCount++;
                        if (r.isFeatured) report.featuredLegendaryCount++;
                        if (r.wonRateUpFlip) report.rateUpWins++;
                        if (r.lostRateUpFlip) report.rateUpLosses++;
                        if (r.triggeredRateUpGuarantee) report.rateUpGuarantees++;
                        if (r.triggeredFatePath) report.fatePathTriggers++;
                    }
                    else if (r.rarity == GachaRarity.Epic)
                    {
                        report.epicCount++;
                        if (r.isFeatured) report.featuredEpicCount++;
                    }
                    else
                    {
                        report.rareCount++;
                    }

                    if (r.duplicate.isDuplicate)
                    {
                        report.totalDuplicateRewards += r.duplicate.rewardAmount;
                    }
                }
            }

            report.avgPullsPerLegendary = report.legendaryCount > 0 ? count / (double)report.legendaryCount : 0d;
            report.avgPullsPerFeaturedLegendary = report.featuredLegendaryCount > 0 ? count / (double)report.featuredLegendaryCount : 0d;
            return report;
        }
    }
}
