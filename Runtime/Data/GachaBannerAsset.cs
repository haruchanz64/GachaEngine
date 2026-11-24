using System;
using System.Collections.Generic;
using UnityEngine;

namespace GachaEngine.Data
{
    /// <summary>
    /// Configurable gacha banner asset supporting custom rarity tier labels (5★ / S-Rank / SSR, 4★ / A-Rank / SR),
    /// editable soft/hard pity pull thresholds, 50/50 or custom featured rate-up guarantees, Fate Path, and Step-Up ladders.
    /// </summary>
    [CreateAssetMenu(fileName = "GachaBanner", menuName = "GachaEngine/Banner Asset", order = 100)]
    public class GachaBannerAsset : ScriptableObject
    {
        [Header("Identity & Pity Sharing")]
        public string bannerId = "banner_limited_01";
        public string bannerName = "Radiant Horizon";
        [TextArea(2, 4)] public string bannerDescription = "Limited character rate-up banner with shared pity and 50/50 featured guarantee.";
        public string pityGroupId = "limited_character_pool";
        public GachaBannerType bannerType = GachaBannerType.LimitedRateUp;

        [Header("Banner Visuals, Audio & Cutscene")]
        public Sprite bannerArtwork;
        public Sprite bannerLogo;
        public Color themeColor = new Color(0.95f, 0.72f, 0.22f, 1f);
        public AudioClip bannerBgm;
        public AudioClip pullStartSfx;
        public GameObject pullCutscenePrefab;

        [Header("Currency Costs")]
        public string currencyId = "AstralTickets";
        [Min(0)] public int singlePullCost = 160;
        [Min(0)] public int tenPullCost = 1600;

        [Header("Rarity Tiers (Editable Labels, Rates, Pity & Rate-Up)")]
        public List<RarityTierConfig> tiers = new List<RarityTierConfig>();

        [Header("Optional Fate Path & Step-Up Rules")]
        public FatePathConfig fatePath = new FatePathConfig();
        public List<StepUpStepConfig> stepUpSteps = new List<StepUpStepConfig>();

        [Header("Item / Character Pool")]
        public List<GachaItemEntry> items = new List<GachaItemEntry>();

        /// <summary>
        /// Returns the <see cref="RarityTierConfig"/> matching <paramref name="rarity"/>, or <c>null</c> if not configured.
        /// </summary>
        /// <param name="rarity">Rarity tier to look up.</param>
        public RarityTierConfig GetTierConfig(GachaRarity rarity)
        {
            for (int i = 0; i < tiers.Count; i++)
            {
                if (tiers[i] != null && tiers[i].rarity == rarity) return tiers[i];
            }
            return null;
        }

        /// <summary>
        /// Returns the display label configured for <paramref name="rarity"/>, falling back to the enum name when unset.
        /// </summary>
        /// <param name="rarity">Rarity tier to format.</param>
        public string GetRarityLabel(GachaRarity rarity)
        {
            var cfg = GetTierConfig(rarity);
            return cfg != null ? cfg.EffectiveLabel : rarity.ToString();
        }

        /// <summary>
        /// Returns a new list containing every non-null item marked as <see cref="GachaItemEntry.isFeatured"/>.
        /// </summary>
        public List<GachaItemEntry> GetFeaturedItems()
        {
            var list = new List<GachaItemEntry>();
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].isFeatured) list.Add(items[i]);
            }
            return list;
        }

        /// <summary>
        /// Returns all non-null items matching <paramref name="rarity"/>, optionally filtered by featured status.
        /// </summary>
        /// <param name="rarity">Target rarity tier.</param>
        /// <param name="featuredOnly">Pass <c>true</c> for featured items only, <c>false</c> for standard items only, or <c>null</c> for all items in the tier.</param>
        public List<GachaItemEntry> GetItemsByRarity(GachaRarity rarity, bool? featuredOnly = null)
        {
            var list = new List<GachaItemEntry>();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.rarity != rarity) continue;
                if (featuredOnly.HasValue && item.isFeatured != featuredOnly.Value) continue;
                list.Add(item);
            }
            return list;
        }

        /// <summary>
        /// Populates this asset with a Limited 50/50 character banner template (0.6% Legendary, soft pity at 74, hard pity at 90, Epic hard pity at 10).
        /// </summary>
        public void ApplyLimitedCharacterPreset()
        {
            bannerId = "limited_char_01";
            bannerName = "Radiant Horizon";
            bannerDescription = "Featured S-Rank / 5-Star character receives 50% rate-up on SSR pulls. Losing the 50/50 guarantees the featured character on the next SSR, carrying over across shared pity banners.";
            pityGroupId = "limited_character_group";
            bannerType = GachaBannerType.LimitedRateUp;
            currencyId = "AstralGems";
            singlePullCost = 160;
            tenPullCost = 1600;
            fatePath = new FatePathConfig { enabled = false, maxFatePoints = 2, targetRarity = GachaRarity.Legendary };
            stepUpSteps.Clear();

            tiers = new List<RarityTierConfig>
            {
                new RarityTierConfig
                {
                    rarity = GachaRarity.Legendary,
                    customLabel = "5★ / SSR",
                    baseProbability = 0.006d,
                    enablePity = true,
                    softPityStartPull = 74,
                    softPityBonusPerPull = 0.06d,
                    hardPityPull = 90,
                    hasFeaturedRateUp = true,
                    featuredRateUpChance = 0.5d,
                    guaranteeFeaturedOnNextMiss = true,
                    tierColor = new Color(0.95f, 0.72f, 0.22f, 1f)
                },
                new RarityTierConfig
                {
                    rarity = GachaRarity.Epic,
                    customLabel = "4★ / SR",
                    baseProbability = 0.051d,
                    enablePity = true,
                    softPityStartPull = 9,
                    softPityBonusPerPull = 0.45d,
                    hardPityPull = 10,
                    hasFeaturedRateUp = true,
                    featuredRateUpChance = 0.5d,
                    guaranteeFeaturedOnNextMiss = true,
                    tierColor = new Color(0.68f, 0.42f, 0.96f, 1f)
                },
                new RarityTierConfig
                {
                    rarity = GachaRarity.Rare,
                    customLabel = "3★ / R",
                    baseProbability = 0.943d,
                    enablePity = false,
                    hasFeaturedRateUp = false,
                    tierColor = new Color(0.28f, 0.62f, 0.96f, 1f)
                }
            };

            items = new List<GachaItemEntry>
            {
                new GachaItemEntry
                {
                    itemId = "ssr_kagura",
                    displayName = "Kagura, Dawnblade",
                    subtitle = "Featured Vanguard (Fire)",
                    description = "High-burst melee vanguard specializing in parries and flame shockwaves. Featured rate-up unit on Radiant Horizon.",
                    rarity = GachaRarity.Legendary,
                    isFeatured = true,
                    duplicateRewardAmount = 25
                },
                new GachaItemEntry { itemId = "ssr_lyra", displayName = "Lyra, Starweaver", subtitle = "Standard Support (Arcane)", description = "Standard pool support unit providing team energy regen and barrier shields.", rarity = GachaRarity.Legendary, isFeatured = false, duplicateRewardAmount = 25 },
                new GachaItemEntry { itemId = "ssr_darius", displayName = "Darius, Ironwall", subtitle = "Standard Tank (Earth)", description = "Standard pool frontline defender with taunt and damage mitigation aura.", rarity = GachaRarity.Legendary, isFeatured = false, duplicateRewardAmount = 25 },
                new GachaItemEntry { itemId = "sr_ren", displayName = "Ren, Windstep", subtitle = "Featured Scout (Wind)", description = "Fast multi-hit assassin with boosted rate on Radiant Horizon.", rarity = GachaRarity.Epic, isFeatured = true, duplicateRewardAmount = 10 },
                new GachaItemEntry { itemId = "sr_mira", displayName = "Mira, Alchemist", subtitle = "Featured Healer (Water)", description = "Potion specialist applying regen over time and cleanse.", rarity = GachaRarity.Epic, isFeatured = true, duplicateRewardAmount = 10 },
                new GachaItemEntry { itemId = "sr_torin", displayName = "Torin, Lancer", subtitle = "Standard Striker", description = "Reliable mid-range spearman from the standard SR pool.", rarity = GachaRarity.Epic, isFeatured = false, duplicateRewardAmount = 10 },
                new GachaItemEntry { itemId = "r_steel_blade", displayName = "Tempered Blade", subtitle = "Weapon", description = "Standard 3-Star blade used for early upgrades and enhancement fodder.", rarity = GachaRarity.Rare, isFeatured = false, duplicateRewardAmount = 2 },
                new GachaItemEntry { itemId = "r_oak_staff", displayName = "Runed Oak Staff", subtitle = "Weapon", description = "Standard 3-Star focus staff from the general pool.", rarity = GachaRarity.Rare, isFeatured = false, duplicateRewardAmount = 2 }
            };
        }

        /// <summary>
        /// Populates this asset with a Fate Path weapon banner template (75/25 featured odds, 80-pull hard pity, and 2-miss Fate Path guarantee).
        /// </summary>
        public void ApplyFatePathWeaponPreset()
        {
            ApplyLimitedCharacterPreset();
            bannerId = "fate_weapon_01";
            bannerName = "Forged Eclipse Arsenal";
            bannerDescription = "Weapon rate-up banner with 75/25 featured odds, 80-pull hard pity, and Fate Path guaranteeing your selected featured weapon after 2 misses.";
            pityGroupId = "weapon_fate_group";
            bannerType = GachaBannerType.FatePath;

            var leg = GetTierConfig(GachaRarity.Legendary);
            if (leg != null)
            {
                leg.customLabel = "S-Rank Weapon";
                leg.baseProbability = 0.007d;
                leg.softPityStartPull = 63;
                leg.softPityBonusPerPull = 0.07d;
                leg.hardPityPull = 80;
                leg.featuredRateUpChance = 0.75d;
            }

            var epic = GetTierConfig(GachaRarity.Epic);
            if (epic != null)
            {
                epic.customLabel = "A-Rank Weapon";
                epic.featuredRateUpChance = 0.75d;
            }

            var rare = GetTierConfig(GachaRarity.Rare);
            if (rare != null) rare.customLabel = "B-Rank Weapon";

            fatePath = new FatePathConfig { enabled = true, maxFatePoints = 2, targetRarity = GachaRarity.Legendary };
            items = new List<GachaItemEntry>
            {
                new GachaItemEntry { itemId = "wpn_eclipse", displayName = "Eclipse Edge", subtitle = "Featured Katana", description = "Signature katana granting crit rate and elemental skill amplification.", rarity = GachaRarity.Legendary, isFeatured = true, duplicateRewardAmount = 30 },
                new GachaItemEntry { itemId = "wpn_aegis", displayName = "Aegis Grimoire", subtitle = "Featured Catalyst", description = "Signature catalyst boosting shield strength and burst recharge.", rarity = GachaRarity.Legendary, isFeatured = true, duplicateRewardAmount = 30 },
                new GachaItemEntry { itemId = "wpn_skyward", displayName = "Skyward Halberd", subtitle = "Standard Polearm", description = "Standard S-Rank polearm with attack speed bonus.", rarity = GachaRarity.Legendary, isFeatured = false, duplicateRewardAmount = 30 },
                new GachaItemEntry { itemId = "wpn_fang", displayName = "Viper Fang", subtitle = "Featured Dagger", description = "A-Rank crit dagger on rate-up.", rarity = GachaRarity.Epic, isFeatured = true, duplicateRewardAmount = 10 },
                new GachaItemEntry { itemId = "wpn_iron", displayName = "Traveler Dirk", subtitle = "Weapon", description = "Standard B-Rank weapon.", rarity = GachaRarity.Rare, isFeatured = false, duplicateRewardAmount = 2 }
            };
        }

        /// <summary>
        /// Populates this asset with a 5-step recruitment ladder template featuring discounted early steps and guaranteed Epic/Featured Legendary milestones.
        /// </summary>
        public void ApplyStepUpPreset()
        {
            ApplyLimitedCharacterPreset();
            bannerId = "stepup_launch_01";
            bannerName = "Launch Step-Up Recruitment";
            bannerDescription = "Five-step ladder with 50% off Step 1, guaranteed SR on Step 3, and guaranteed featured SSR on Step 5.";
            pityGroupId = "stepup_launch_group";
            bannerType = GachaBannerType.StepUp;

            stepUpSteps = new List<StepUpStepConfig>
            {
                new StepUpStepConfig { stepLabel = "Step 1 (50% Off)", pullCount = 10, costOverride = 800 },
                new StepUpStepConfig { stepLabel = "Step 2 (25% Off)", pullCount = 10, costOverride = 1200 },
                new StepUpStepConfig { stepLabel = "Step 3 (Guaranteed SR)", pullCount = 10, costOverride = 1600, guaranteeMinimumRarity = true, guaranteedRarity = GachaRarity.Epic },
                new StepUpStepConfig { stepLabel = "Step 4 (Standard)", pullCount = 10, costOverride = 1600 },
                new StepUpStepConfig { stepLabel = "Step 5 (Featured SSR)", pullCount = 10, costOverride = 1600, guaranteeMinimumRarity = true, guaranteedRarity = GachaRarity.Legendary, guaranteeFeatured = true }
            };
        }
    }
}
