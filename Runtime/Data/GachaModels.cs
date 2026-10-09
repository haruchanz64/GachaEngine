using System;
using System.Collections.Generic;
using UnityEngine;

namespace GachaEngine.Data
{
    public enum GachaRarity
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3
    }

    public enum GachaBannerType
    {
        Standard = 0,
        LimitedRateUp = 1,
        FatePath = 2,
        StepUp = 3
    }

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

        public string EffectiveLabel => string.IsNullOrWhiteSpace(customLabel) ? rarity.ToString() : customLabel;
    }

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

    [Serializable]
    public sealed class FatePathConfig
    {
        public bool enabled;
        [Min(1)] public int maxFatePoints = 2;
        public GachaRarity targetRarity = GachaRarity.Legendary;
    }

    [Serializable]
    public sealed class TierPityCounter
    {
        public GachaRarity rarity;
        public int pullsSinceHit;
        public bool guaranteedFeaturedNext;
    }

    [Serializable]
    public sealed class GachaPityState
    {
        public string pityGroupId;
        public int totalPulls;
        public int fatePoints;
        public string selectedFateItemId;
        public int currentStepIndex;
        public List<TierPityCounter> tierCounters = new List<TierPityCounter>();

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

    [Serializable]
    public struct DuplicateResolution
    {
        public bool isDuplicate;
        public bool isMaxRankOverflow;
        public int newOwnedCount;
        public string rewardCurrencyId;
        public int rewardAmount;
    }

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
