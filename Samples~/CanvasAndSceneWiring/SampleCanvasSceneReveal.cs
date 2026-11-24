using System;
using System.Collections;
using GachaEngine.Data;
using GachaEngine.Engine;
using GachaEngine.Presentation;
using UnityEngine;

namespace GachaEngine.Samples
{
    /// <summary>
    /// Example showing how to pair GachaController with a custom IGachaRevealSequence for
    /// Canvas UI reveals, 3D Scene cutscenes, VFX spawning, and audio cues.
    ///
    /// Inspector wiring (no code needed for buttons):
    /// 1. Add GachaController + GachaRevealPresenter (or this script) to a scene GameObject.
    /// 2. Assign your GachaBannerAsset list and drag this component into GachaController.revealSequenceBehaviour.
    /// 3. Bind Canvas Button.onClick() to GachaController.PullSingleActiveBanner, PullTenActiveBanner, or SkipReveal.
    /// 4. Bind GachaController.onPullRevealed and onBatchFinished to your Canvas result panel.
    /// </summary>
    public sealed class SampleCanvasSceneReveal : MonoBehaviour, IGachaRevealSequence
    {
        [SerializeField] private GachaController controller;
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private Transform vfxAnchor;
        [SerializeField] private float introSeconds = 1.0f;
        [SerializeField] private float perItemSeconds = 0.5f;

        private GameObject _activeVfx;

        /// <summary>
        /// Registers this component as the controller's reveal sequence and subscribes to reveal events.
        /// </summary>
        private void Awake()
        {
            if (controller != null)
            {
                controller.SetRevealSequence(this);
                controller.onPullRevealed.AddListener(HandleItemRevealed);
                controller.onBatchFinished.AddListener(HandleBatchFinished);
            }
        }

        /// <summary>
        /// Unsubscribes from controller events and destroys any active temporary VFX object.
        /// </summary>
        private void OnDestroy()
        {
            if (controller != null)
            {
                controller.onPullRevealed.RemoveListener(HandleItemRevealed);
                controller.onBatchFinished.RemoveListener(HandleBatchFinished);
            }
            CleanupPresentation();
        }

        /// <inheritdoc/>
        public IEnumerator PlayBatchIntro(
            GachaBannerAsset banner,
            GachaPullBatchResult batch,
            GachaRarity highestRarity,
            Func<bool> isSkipRequested)
        {
            CleanupPresentation();

            if (sfxSource != null && banner != null && banner.pullStartSfx != null)
            {
                sfxSource.PlayOneShot(banner.pullStartSfx);
            }

            // Spawn highest-rarity meteor/portal VFX immediately so the player sees the tier color during intro
            RarityTierConfig topTier = banner != null ? banner.GetTierConfig(highestRarity) : null;
            if (topTier != null && topTier.revealVfxPrefab != null)
            {
                _activeVfx = Instantiate(topTier.revealVfxPrefab, vfxAnchor != null ? vfxAnchor : transform);
            }

            float elapsed = 0f;
            while (elapsed < introSeconds && !isSkipRequested())
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <inheritdoc/>
        public IEnumerator PlayItemReveal(
            GachaBannerAsset banner,
            GachaPullResult result,
            Func<bool> isSkipRequested)
        {
            if (sfxSource != null && result.item != null && result.item.voiceLineSfx != null)
            {
                sfxSource.PlayOneShot(result.item.voiceLineSfx);
            }

            float elapsed = 0f;
            while (elapsed < perItemSeconds && !isSkipRequested())
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <inheritdoc/>
        public void CleanupPresentation()
        {
            if (_activeVfx != null)
            {
                Destroy(_activeVfx);
                _activeVfx = null;
            }
        }

        /// <summary>
        /// Logs each revealed item's rarity label and display name to the Unity Console.
        /// </summary>
        private static void HandleItemRevealed(GachaPullResult result)
        {
            Debug.Log($"[SampleCanvasSceneReveal] Reveal: [{result.rarityLabel}] {result.item.displayName}");
        }

        /// <summary>
        /// Logs the completed batch summary once all item reveals finish.
        /// </summary>
        private static void HandleBatchFinished(GachaPullBatchResult batch)
        {
            Debug.Log($"[SampleCanvasSceneReveal] Summary ready for {batch.bannerName} ({batch.results.Count} pulls).");
        }
    }
}
