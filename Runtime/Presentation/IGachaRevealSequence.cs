using System;
using System.Collections;
using GachaEngine.Data;

namespace GachaEngine.Presentation
{
    /// <summary>
    /// Optional presentation hook for custom pull cutscenes, 2D Canvas animations, 3D Scene Timelines,
    /// VFX spawning, and audio cues without coupling GachaRoller math to any specific UI framework.
    /// </summary>
    public interface IGachaRevealSequence
    {
        /// <summary>
        /// Runs once per roll batch (x1 or x10) after results are computed and before individual items reveal.
        /// Receives the highest rarity in the batch so meteor/portal/orb VFX can reflect the top tier immediately.
        /// </summary>
        /// <param name="banner">Active banner that produced the batch.</param>
        /// <param name="batch">Completed roll batch containing all pull outcomes.</param>
        /// <param name="highestRarity">Highest <see cref="GachaRarity"/> rolled in <paramref name="batch"/>.</param>
        /// <param name="isSkipRequested">Delegate polled each frame that returns <c>true</c> when the player requests skipping the sequence.</param>
        IEnumerator PlayBatchIntro(
            GachaBannerAsset banner,
            GachaPullBatchResult batch,
            GachaRarity highestRarity,
            Func<bool> isSkipRequested);

        /// <summary>
        /// Runs for each item reveal in the batch unless skipped.
        /// </summary>
        /// <param name="banner">Active banner supplying tier presentation assets.</param>
        /// <param name="result">Pull result currently being revealed.</param>
        /// <param name="isSkipRequested">Delegate polled each frame that returns <c>true</c> when the player requests skipping.</param>
        IEnumerator PlayItemReveal(
            GachaBannerAsset banner,
            GachaPullResult result,
            Func<bool> isSkipRequested);

        /// <summary>
        /// Called when a reveal sequence ends or is skipped so temporary VFX or 3D preview objects can clean up.
        /// </summary>
        void CleanupPresentation();
    }
}
