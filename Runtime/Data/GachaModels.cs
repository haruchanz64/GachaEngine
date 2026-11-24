using System;
using System.Collections.Generic;
using UnityEngine;

namespace GachaEngine.Data
{
    /// <summary>
    /// Built-in rarity ordering evaluated from highest (<see cref="Legendary"/>) to lowest (<see cref="Common"/>).
    /// Display labels are customizable per banner via <see cref="RarityTierConfig.customLabel"/>.
    /// </summary>
    public enum GachaRarity
    {
        /// <summary>Lowest rarity tier.</summary>
        Common = 0,
        /// <summary>Standard filler tier (commonly labeled 3-star or R).</summary>
        Rare = 1,
        /// <summary>Mid-high tier (commonly labeled 4-star, SR, or A-Rank).</summary>
        Epic = 2,
        /// <summary>Top chase tier (commonly labeled 5-star, SSR, or S-Rank).</summary>
        Legendary = 3
    }

    /// <summary>
    /// High-level rule archetype for a <see cref="GachaBannerAsset"/>.
    /// </summary>
    public enum GachaBannerType
    {
        /// <summary>Permanent pool banner without Step-Up or Fate Path overrides.</summary>
        Standard = 0,
        /// <summary>Rate-up banner with featured odds and cross-banner carry-over guarantees by <c>pityGroupId</c>.</summary>
        LimitedRateUp = 1,
        /// <summary>Banner that guarantees a player-selected target item after a configured number of misses.</summary>
        FatePath = 2,
        /// <summary>Multi-step ladder with per-step pull counts, cost overrides, and milestone rarity guarantees.</summary>
        StepUp = 3
    }

    /// <summary>
    /// Single pullable character, weapon, or reward entry inside a <see cref="GachaBannerAsset"/> pool.
    /// </summary>
    [Serializable]
    public sealed class GachaItemEntry
    {
        public string itemId = "item_id";
        public string displayName = "New Item";
        public string subtitle = "Character";
        [TextArea(2, 4)] public string description = "Item or character summary shown in the Banner Details UI.";
        public GachaRarity rarity = GachaRarity.Rare;
        [Min(0.01f)] public float weight = 1f;
        public bool isFeatured;
        public Sprite icon;
        public UnityEngine.Object assetReference;

        [Header("Presentation & Scene Assets")]
        public Sprite splashArt;
        public GameObject previewPrefab;
        public AudioClip voiceLineSfx;
        public AnimationClip showcaseAnimation;

        [Header("Duplicate Conversion")]
        public string duplicateCurrencyId = "StarShards";
        [Min(0)] public int duplicateRewardAmount = 10;
        [Min(1)] public int maxOwnedRank = 7;
        public string overflowCurrencyId = "MasterTokens";
        [Min(0)] public int overflowRewardAmount = 25;
    }

    /// <summary>
    /// Designer-configurable rarity tier allowing custom labels (5★ / S-Rank / SSR, 4★ / A-Rank / SR),
    /// base drop rates, soft/hard pity pull counts, and featured rate-up guarantee rules.
    /// </summary>
    [Serializable]
    public sealed class RarityTierConfig
    {
        public GachaRarity rarity = GachaRarity.Legendary;
        public string customLabel = "5★ / SSR";
        [Range(0f, 1f)] public double baseProbability = 0.006d;

        [Header("Pity Configuration")]
        public bool enablePity = true;
        [Min(1)] public int softPityStartPull = 74;
        [Range(0f, 1f)] public double softPityBonusPerPull = 0.06d;
        [Min(1)] public int hardPityPull = 90;

        [Header("Featured Rate-Up Rules")]
        public bool hasFeaturedRateUp = true;
        [Range(0f, 1f)] public double featuredRateUpChance = 0.5d;
        public bool guaranteeFeaturedOnNextMiss = true;

        [Header("Tier Presentation (VFX / Audio / Animation)")]
        public Color tierColor = new Color(0.95f, 0.72f, 0.22f, 1f);
        public GameObject revealVfxPrefab;
        public AudioClip revealSfx;
        public AnimationClip revealAnimation;

        /// <summary>
        /// Display label for this tier, falling back to the <see cref="GachaRarity"/> enum name when <see cref="customLabel"/> is blank.
        /// </summary>
        public string EffectiveLabel => string.IsNullOrWhiteSpace(customLabel) ? rarity.ToString() : customLabel;
    }

    /// <summary>
    /// Configuration for one step on a <see cref="GachaBannerType.StepUp"/> banner ladder.
    /// </summary>
    [Serializable]
    public sealed class StepUpStepConfig
    {
        public string stepLabel = "Step 1";
        [Min(1)] public int pullCount = 10;
        [Min(0)] public int costOverride = 800;
        public bool guaranteeMinimumRarity;
        public GachaRarity guaranteedRarity = GachaRarity.Epic;
        public bool guaranteeFeatured;
    }

    /// <summary>
    /// Target guarantee configuration that awards the player's chosen item after <see cref="maxFatePoints"/> misses on <see cref="targetRarity"/>.
    /// </summary>
    [Serializable]
    public sealed class FatePathConfig
    {
        public bool enabled;
        [Min(1)] public int maxFatePoints = 2;
        public GachaRarity targetRarity = GachaRarity.Legendary;
    }

    /// <summary>
    /// Mutable per-tier pity tracking record stored inside <see cref="GachaPityState"/>.
    /// </summary>
    [Serializable]
    public sealed class TierPityCounter
    {
        public GachaRarity rarity;
        public int pullsSinceHit;
        public bool guaranteedFeaturedNext;
    }

    /// <summary>
    /// Serializable pity, Fate Path, and Step-Up progression state shared by all banners with the same <see cref="pityGroupId"/>.
    /// </summary>
    [Serializable]
    public sealed class GachaPityState
    {
        public string pityGroupId;
        public int totalPulls;
        public int fatePoints;
        public string selectedFateItemId;
        public int currentStepIndex;
        public List<TierPityCounter> tierCounters = new List<TierPityCounter>();

        /// <summary>
        /// Returns the existing <see cref="TierPityCounter"/> for <paramref name="rarity"/>, adding a zeroed counter if absent.
        /// </summary>
        /// <param name="rarity">Rarity tier whose counter should be retrieved.</param>
        public TierPityCounter GetOrCreateCounter(GachaRarity rarity)
        {
            for (int i = 0; i < tierCounters.Count; i++)
            {
                if (tierCounters[i].rarity == rarity) return tierCounters[i];
            }

            var counter = new TierPityCounter { rarity = rarity };
            tierCounters.Add(counter);
            return counter;
        }
    }

    /// <summary>
    /// Outcome of resolving ownership and duplicate or max-rank overflow currency rewards for a pulled item.
    /// </summary>
    [Serializable]
    public struct DuplicateResolution
    {
        /// <summary><c>true</c> when the player already owned at least one copy before this pull.</summary>
        public bool isDuplicate;
        /// <summary><c>true</c> when <see cref="newOwnedCount"/> exceeds the item's <see cref="GachaItemEntry.maxOwnedRank"/>.</summary>
        public bool isMaxRankOverflow;
        /// <summary>Total owned copy count after applying this pull.</summary>
        public int newOwnedCount;
        /// <summary>Currency identifier awarded for this duplicate, or <c>null</c> for first-time acquisitions.</summary>
        public string rewardCurrencyId;
        /// <summary>Currency amount awarded for this duplicate, or <c>0</c> for first-time acquisitions.</summary>
        public int rewardAmount;
    }

    /// <summary>
    /// Detailed outcome of a single gacha pull including item, rarity, pity triggers, duplicate resolution, and UTC/local timestamps.
    /// </summary>
    [Serializable]
    public sealed class GachaPullResult
    {
        public int pullIndex;
        public string bannerId;
        public string bannerName;
        public GachaItemEntry item;
        public GachaRarity rarity;
        public string rarityLabel;
        public bool isFeatured;
        public int pityAtPull;
        public bool triggeredSoftPity;
        public bool triggeredHardPity;
        public bool triggeredRateUpGuarantee;
        public bool wonRateUpFlip;
        public bool lostRateUpFlip;
        public bool triggeredFatePath;
        public bool triggeredStepGuarantee;
        public DuplicateResolution duplicate;
        public string pulledAtUtc;
        public string pulledAtLocal;
    }

    /// <summary>
    /// Aggregate outcome of a single or multi-pull transaction, including currency spent, error details, and individual pull results.
    /// </summary>
    [Serializable]
    public sealed class GachaPullBatchResult
    {
        public string bannerId;
        public string bannerName;
        public int currencySpent;
        public string currencyId;
        public bool succeeded = true;
        public string errorMessage;
        public List<GachaPullResult> results = new List<GachaPullResult>();

        /// <summary>
        /// Returns the highest <see cref="GachaRarity"/> present in <see cref="results"/>, or <see cref="GachaRarity.Common"/> if empty.
        /// </summary>
        public GachaRarity GetHighestRarity()
        {
            GachaRarity highest = GachaRarity.Common;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] != null && results[i].rarity > highest)
                {
                    highest = results[i].rarity;
                }
            }
            return highest;
        }
    }

    /// <summary>
    /// Summary metrics produced by <see cref="Engine.GachaRoller.RunBulkSimulation"/> for drop-rate and pity auditing.
    /// </summary>
    [Serializable]
    public sealed class GachaBulkSimulationReport
    {
        public string bannerName;
        public int totalPulls;
        public int legendaryCount;
        public int featuredLegendaryCount;
        public int epicCount;
        public int featuredEpicCount;
        public int rareCount;
        public int rateUpWins;
        public int rateUpLosses;
        public int rateUpGuarantees;
        public int fatePathTriggers;
        public int totalDuplicateRewards;
        public double avgPullsPerLegendary;
        public double avgPullsPerFeaturedLegendary;
    }
}
