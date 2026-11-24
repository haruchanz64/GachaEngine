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
        /// <inheritdoc/>
        public string ModifierId => "sample_event_rate_boost";

        /// <summary>Rarity tier whose effective probability is scaled by <see cref="Multiplier"/>.</summary>
        public GachaRarity TargetRarity { get; set; } = GachaRarity.Legendary;

        /// <summary>Non-negative probability multiplier applied to <see cref="TargetRarity"/> (clamped to at most 1.0).</summary>
        public double Multiplier { get; set; } = 2.0d;

        /// <summary>Maximum total pulls on the pity group eligible for the boost; set to <c>0</c> or less for no pull cap.</summary>
        public int FirstPullLimit { get; set; } = 10;

        /// <summary>Master toggle enabling or disabling this event rate boost.</summary>
        public bool EventActive { get; set; } = true;

        /// <inheritdoc/>
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
