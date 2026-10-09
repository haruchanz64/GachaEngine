using System;
using GachaEngine.Core;
using GachaEngine.Data;

namespace GachaEngine.Samples
{
    /// <summary>
    /// Example IGachaPoolModifier that doubles the top-tier base rate during the first N pulls on a banner
    /// or while an active event flag is enabled.
    /// </summary>
    public sealed class SampleEventRateBoostModifier : IGachaPoolModifier
    {
        public string ModifierId => "sample_event_rate_boost";

        public GachaRarity TargetRarity { get; set; } = GachaRarity.Legendary;
        public double Multiplier { get; set; } = 2.0d;
        public int FirstPullLimit { get; set; } = 10;
        public bool EventActive { get; set; } = true;

        public void ModifyTierRate(
            GachaBannerAsset banner,
            RarityTierConfig tier,
            ref double effectiveProbability,
            GachaPityState pityState)
        {
            if (!EventActive || tier == null || tier.rarity != TargetRarity)
            {
                return;
            }

            if (FirstPullLimit > 0 && pityState != null && pityState.totalPulls > FirstPullLimit)
            {
                return;
            }

            effectiveProbability = Math.Min(1.0d, effectiveProbability * Math.Max(0.0d, Multiplier));
        }
    }
}
