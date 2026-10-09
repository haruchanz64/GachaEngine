using System;
using System.Collections.Generic;
using GachaEngine.Core;
using GachaEngine.Data;
using GachaEngine.Engine;
using NUnit.Framework;
using UnityEngine;

namespace GachaEngine.Tests
{
    public sealed class GachaRollerTests
    {
        private sealed class ScriptedRng : IGachaRng
        {
            private readonly Queue<double> _values;
            private readonly double _fallback;

            public ScriptedRng(double fallback = 0.9999d, params double[] sequence)
            {
                _fallback = fallback;
                _values = new Queue<double>(sequence ?? Array.Empty<double>());
            }

            public double NextDouble() => _values.Count > 0 ? _values.Dequeue() : _fallback;

            public int NextInt(int minInclusive, int maxExclusive)
            {
                if (maxExclusive <= minInclusive) return minInclusive;
                double v = NextDouble();
                return Math.Min(maxExclusive - 1, minInclusive + (int)(v * (maxExclusive - minInclusive)));
            }
        }

        private sealed class BoostLegendaryModifier : IGachaPoolModifier
        {
            public string ModifierId => "test_boost_legendary";

            public void ModifyTierRate(
                GachaBannerAsset banner,
                RarityTierConfig tier,
                ref double effectiveProbability,
                GachaPityState pityState)
            {
                if (tier.rarity == GachaRarity.Legendary)
                {
                    effectiveProbability = 1.0d;
                }
            }
        }

        private static GachaBannerAsset CreateLimitedBanner()
        {
            var banner = ScriptableObject.CreateInstance<GachaBannerAsset>();
            banner.ApplyLimitedCharacterPreset();
            return banner;
        }

        [Test]
        public void CustomRarityLabelsAndEditedPityThresholds_TriggerAtExactCounts()
        {
            var banner = CreateLimitedBanner();
            try
            {
                var topTier = banner.GetTierConfig(GachaRarity.Legendary);
                topTier.customLabel = "S-Rank";
                topTier.softPityStartPull = 40;
                topTier.hardPityPull = 50;

                var midTier = banner.GetTierConfig(GachaRarity.Epic);
                midTier.customLabel = "A-Rank";
                midTier.softPityStartPull = 7;
                midTier.hardPityPull = 8;

                // Always roll high so only hard pity triggers top and mid tiers
                var roller = new GachaRoller(new ScriptedRng(fallback: 0.9999d));

                GachaPullResult firstMidHit = null;
                GachaPullResult firstTopHit = null;

                for (int i = 1; i <= 50; i++)
                {
                    var res = roller.RollSingle(banner).results[0];
                    if (res.rarity == GachaRarity.Epic && firstMidHit == null)
                    {
                        firstMidHit = res;
                    }
                    if (res.rarity == GachaRarity.Legendary && firstTopHit == null)
                    {
                        firstTopHit = res;
                    }
                }

                Assert.IsNotNull(firstMidHit);
                Assert.AreEqual(8, firstMidHit.pullIndex);
                Assert.AreEqual("A-Rank", firstMidHit.rarityLabel);
                Assert.IsTrue(firstMidHit.triggeredHardPity);

                Assert.IsNotNull(firstTopHit);
                Assert.AreEqual(50, firstTopHit.pullIndex);
                Assert.AreEqual("S-Rank", firstTopHit.rarityLabel);
                Assert.IsTrue(firstTopHit.triggeredHardPity);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(banner);
            }
        }

        [Test]
        public void RateUpLoss_GuaranteesFeaturedAcrossSharedPityGroupBanners()
        {
            var bannerA = CreateLimitedBanner();
            var bannerB = CreateLimitedBanner();
            try
            {
                bannerA.bannerId = "limited_a";
                bannerA.bannerName = "Banner Alpha";
                bannerA.pityGroupId = "shared_limited_group";
                bannerA.GetTierConfig(GachaRarity.Legendary).hardPityPull = 2;
                bannerA.GetTierConfig(GachaRarity.Legendary).softPityStartPull = 2;

                bannerB.bannerId = "limited_b";
                bannerB.bannerName = "Banner Beta";
                bannerB.pityGroupId = "shared_limited_group";
                bannerB.GetTierConfig(GachaRarity.Legendary).hardPityPull = 2;
                bannerB.GetTierConfig(GachaRarity.Legendary).softPityStartPull = 2;
                bannerB.items[0].itemId = "ssr_beta_featured";
                bannerB.items[0].displayName = "Vespera, Moonblade";

                // Pull 1: miss (0.99).
                // Pull 2: hard pity triggers Legendary; rate-up flip consumes 0.90 (> 0.50 -> lose), standard pick consumes 0.10.
                // Pull 3 (on Banner B): miss (0.99).
                // Pull 4 (on Banner B): hard pity triggers Legendary -> guaranteed featured carried over from Banner A.
                var rng = new ScriptedRng(0.99d, 0.99d, 0.90d, 0.10d, 0.99d);
                var roller = new GachaRoller(rng);

                roller.RollSingle(bannerA);
                var lossPull = roller.RollSingle(bannerA).results[0];

                Assert.AreEqual(GachaRarity.Legendary, lossPull.rarity);
                Assert.IsFalse(lossPull.isFeatured);
                Assert.IsTrue(lossPull.lostRateUpFlip);
                Assert.IsTrue(roller.PityStore.GetOrCreate("shared_limited_group").GetOrCreateCounter(GachaRarity.Legendary).guaranteedFeaturedNext);

                roller.RollSingle(bannerB);
                var guaranteedPull = roller.RollSingle(bannerB).results[0];

                Assert.AreEqual(GachaRarity.Legendary, guaranteedPull.rarity);
                Assert.IsTrue(guaranteedPull.isFeatured);
                Assert.IsTrue(guaranteedPull.triggeredRateUpGuarantee);
                Assert.AreEqual("ssr_beta_featured", guaranteedPull.item.itemId);
                Assert.IsFalse(roller.PityStore.GetOrCreate("shared_limited_group").GetOrCreateCounter(GachaRarity.Legendary).guaranteedFeaturedNext);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bannerA);
                UnityEngine.Object.DestroyImmediate(bannerB);
            }
        }

        [Test]
        public void FatePath_GuaranteesSelectedTargetAfterMaxFatePoints()
        {
            var banner = ScriptableObject.CreateInstance<GachaBannerAsset>();
            try
            {
                banner.ApplyFatePathWeaponPreset();
                var leg = banner.GetTierConfig(GachaRarity.Legendary);
                leg.hardPityPull = 1;
                leg.softPityStartPull = 1;
                banner.fatePath.maxFatePoints = 2;

                // Always fail the 75/25 featured flip (0.95 >= 0.75) so first two S-Rank pulls are standard misses
                var roller = new GachaRoller(new ScriptedRng(fallback: 0.95d));
                roller.SetFateTarget(banner, "wpn_eclipse");

                var pull1 = roller.RollSingle(banner).results[0];
                var pull2 = roller.RollSingle(banner).results[0];
                var pull3 = roller.RollSingle(banner).results[0];

                Assert.AreNotEqual("wpn_eclipse", pull1.item.itemId);
                Assert.AreNotEqual("wpn_eclipse", pull2.item.itemId);
                Assert.AreEqual("wpn_eclipse", pull3.item.itemId);
                Assert.IsTrue(pull3.triggeredFatePath);
                Assert.AreEqual(0, roller.PityStore.GetOrCreate(banner.pityGroupId).fatePoints);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(banner);
            }
        }

        [Test]
        public void StepUpBanner_AppliesStepDiscountsAndStep5FeaturedGuarantee()
        {
            var banner = ScriptableObject.CreateInstance<GachaBannerAsset>();
            try
            {
                banner.ApplyStepUpPreset();
                var wallet = new InMemoryCurrencyWallet();
                wallet.Add(banner.currencyId, 10000);

                var roller = new GachaRoller(new ScriptedRng(fallback: 0.9999d), wallet: wallet);

                var step1 = roller.RollMulti(banner, 10, chargeWallet: true);
                var step2 = roller.RollMulti(banner, 10, chargeWallet: true);
                var step3 = roller.RollMulti(banner, 10, chargeWallet: true);
                var step4 = roller.RollMulti(banner, 10, chargeWallet: true);
                var step5 = roller.RollMulti(banner, 10, chargeWallet: true);

                Assert.AreEqual(800, step1.currencySpent);
                Assert.AreEqual(1200, step2.currencySpent);
                Assert.AreEqual(1600, step3.currencySpent);
                Assert.AreEqual(1600, step4.currencySpent);
                Assert.AreEqual(1600, step5.currencySpent);

                // Step 3 final pull is guaranteed Epic (SR)
                Assert.AreEqual(GachaRarity.Epic, step3.results[9].rarity);
                Assert.IsTrue(step3.results[9].triggeredStepGuarantee);

                // Step 5 final pull is guaranteed Featured Legendary (SSR)
                var step5Final = step5.results[9];
                Assert.AreEqual(GachaRarity.Legendary, step5Final.rarity);
                Assert.IsTrue(step5Final.isFeatured);
                Assert.IsTrue(step5Final.triggeredStepGuarantee);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(banner);
            }
        }

        [Test]
        public void DuplicateResolver_GrantsShardsAndMaxRankOverflowCurrency()
        {
            var banner = CreateLimitedBanner();
            try
            {
                var leg = banner.GetTierConfig(GachaRarity.Legendary);
                leg.hardPityPull = 1;
                leg.softPityStartPull = 1;
                leg.featuredRateUpChance = 1.0d;

                var featured = banner.items[0];
                featured.maxOwnedRank = 2;
                featured.duplicateCurrencyId = "StarShards";
                featured.duplicateRewardAmount = 25;
                featured.overflowCurrencyId = "MasterTokens";
                featured.overflowRewardAmount = 60;

                var wallet = new InMemoryCurrencyWallet();
                var roller = new GachaRoller(new ScriptedRng(fallback: 0.1d), wallet: wallet);

                var first = roller.RollSingle(banner).results[0];
                var second = roller.RollSingle(banner).results[0];
                var third = roller.RollSingle(banner).results[0];

                Assert.IsFalse(first.duplicate.isDuplicate);
                Assert.AreEqual(1, first.duplicate.newOwnedCount);

                Assert.IsTrue(second.duplicate.isDuplicate);
                Assert.IsFalse(second.duplicate.isMaxRankOverflow);
                Assert.AreEqual(25, wallet.GetBalance("StarShards"));

                Assert.IsTrue(third.duplicate.isDuplicate);
                Assert.IsTrue(third.duplicate.isMaxRankOverflow);
                Assert.AreEqual(60, wallet.GetBalance("MasterTokens"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(banner);
            }
        }

        [Test]
        public void HistoryLog_EnforcesOneHourRefreshAndExportsJsonWithRequiredFields()
        {
            var banner = CreateLimitedBanner();
            try
            {
                DateTime clock = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
                var history = new GachaHistoryLog(maxRecords: 200, refreshIntervalSeconds: 3600, initialUtcNow: clock);
                var roller = new GachaRoller(
                    rng: new SeededGachaRng(42),
                    history: history,
                    utcNowProvider: () => clock);

                // Pull 3 times at 12:05 UTC
                clock = clock.AddMinutes(5);
                roller.RollMulti(banner, 3);

                Assert.AreEqual(0, history.PublishedRecords.Count);
                Assert.AreEqual(3, history.PendingCount);

                // At 12:59 UTC (59 min since initial refresh), TryRefresh must not publish yet
                clock = new DateTime(2026, 11, 24, 12, 59, 0, DateTimeKind.Utc);
                Assert.IsFalse(history.TryRefresh(clock));
                Assert.AreEqual(0, history.PublishedRecords.Count);
                Assert.AreEqual(3, history.PendingCount);

                // At 13:00 UTC (full 1 hour = 3600s), TryRefresh publishes records
                clock = new DateTime(2026, 11, 24, 13, 0, 0, DateTimeKind.Utc);
                Assert.IsTrue(history.TryRefresh(clock));
                Assert.AreEqual(3, history.PublishedRecords.Count);
                Assert.AreEqual(0, history.PendingCount);

                string json = history.ExportToJson(includePending: false, prettyPrint: true, utcNow: clock);
                var parsed = JsonUtility.FromJson<GachaHistoryExportPayload>(json);

                Assert.IsNotNull(parsed);
                Assert.AreEqual(3, parsed.totalRecords);
                Assert.AreEqual(3600, parsed.refreshIntervalSeconds);
                Assert.AreEqual("Radiant Horizon", parsed.records[0].bannerName);
                Assert.IsFalse(string.IsNullOrEmpty(parsed.records[0].itemName));
                Assert.IsTrue(parsed.records[0].pulledAtUtc.StartsWith("2026-11-24T12:05:00", StringComparison.Ordinal));
                Assert.IsFalse(string.IsNullOrEmpty(parsed.records[0].pulledAtLocal));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(banner);
            }
        }

        [Test]
        public void PoolModifierAndPityStoreSnapshot_WorkDeterministically()
        {
            var banner = CreateLimitedBanner();
            try
            {
                var store = new InMemoryPityStore();
                var roller = new GachaRoller(new ScriptedRng(fallback: 0.5d), pityStore: store);
                roller.AddModifier(new BoostLegendaryModifier());

                var res = roller.RollSingle(banner).results[0];
                Assert.AreEqual(GachaRarity.Legendary, res.rarity);

                string snapshot = store.ExportJson();
                var restoredStore = new InMemoryPityStore();
                restoredStore.ImportJson(snapshot);

                Assert.AreEqual(1, restoredStore.GetOrCreate(banner.pityGroupId).totalPulls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(banner);
            }
        }

        [Test]
        public void BulkSimulation_MatchesExpectedSoftAndHardPityDistribution()
        {
            var banner = CreateLimitedBanner();
            try
            {
                var report = GachaRoller.RunBulkSimulation(banner, pullCount: 10000, seed: 1337);

                Assert.AreEqual(10000, report.totalPulls);
                Assert.Greater(report.legendaryCount, 120);
                Assert.Less(report.legendaryCount, 220);
                Assert.Greater(report.featuredLegendaryCount, 60);
                Assert.Greater(report.epicCount, 1000);
                Assert.Greater(report.avgPullsPerLegendary, 45d);
                Assert.Less(report.avgPullsPerLegendary, 80d);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(banner);
            }
        }

        [Test]
        public void GachaController_UnityEventsAndActiveBannerHelpers_FireInExpectedOrder()
        {
            var banner = CreateLimitedBanner();
            var go = new GameObject("GachaControllerTestHost");
            try
            {
                var controller = go.AddComponent<GachaController>();
                GachaBannerAsset activeChangedBanner = null;
                GachaBannerAsset startedBanner = null;
                GachaRarity? highestRarity = null;
                int revealedCount = 0;
                GachaPullBatchResult finishedBatch = null;

                controller.onActiveBannerChanged.AddListener(b => activeChangedBanner = b);
                controller.onPullStarted.AddListener(b => startedBanner = b);
                controller.onHighestRarityDetermined.AddListener(r => highestRarity = r);
                controller.onPullRevealed.AddListener(_ => revealedCount++);
                controller.onBatchFinished.AddListener(b => finishedBatch = b);

                controller.SetBanners(new[] { banner }, initialIndex: 0);
                Assert.AreSame(banner, activeChangedBanner);
                Assert.AreSame(banner, controller.ActiveBanner);

                controller.PullTenActiveBanner();

                Assert.AreSame(banner, startedBanner);
                Assert.IsTrue(highestRarity.HasValue);
                Assert.AreEqual(10, revealedCount);
                Assert.IsNotNull(finishedBatch);
                Assert.AreEqual(finishedBatch.GetHighestRarity(), highestRarity.Value);
                Assert.GreaterOrEqual((int)highestRarity.Value, (int)GachaRarity.Epic);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(banner);
            }
        }

        [Test]
        public void BannerPreset_PopulatesDefaultTierColorsAndSupportsNullBannerErrorEvent()
        {
            var banner = CreateLimitedBanner();
            var go = new GameObject("GachaErrorEventTestHost");
            try
            {
                var leg = banner.GetTierConfig(GachaRarity.Legendary);
                var epic = banner.GetTierConfig(GachaRarity.Epic);
                var rare = banner.GetTierConfig(GachaRarity.Rare);

                Assert.AreNotEqual(leg.tierColor, epic.tierColor);
                Assert.AreNotEqual(epic.tierColor, rare.tierColor);

                var controller = go.AddComponent<GachaController>();
                string errorMessage = null;
                controller.onPullFailed.AddListener(msg => errorMessage = msg);

                controller.PullSingleActiveBanner();
                Assert.IsFalse(string.IsNullOrEmpty(errorMessage));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(banner);
            }
        }
    }
}
