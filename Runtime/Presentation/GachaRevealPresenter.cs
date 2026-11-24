using System;
using System.Collections;
using System.Collections.Generic;
using GachaEngine.Data;
using UnityEngine;
using UnityEngine.Events;

namespace GachaEngine.Presentation
{
    /// <summary>
    /// Inspector-bindable <see cref="UnityEvent{T0, T1}"/> fired when a pull batch intro begins with the banner and highest rolled rarity.
    /// </summary>
    [Serializable] public sealed class GachaIntroUnityEvent : UnityEvent<GachaBannerAsset, GachaRarity> { }

    /// <summary>
    /// Inspector-bindable <see cref="UnityEvent{T0, T1}"/> fired for each revealed item along with its tier accent color.
    /// </summary>
    [Serializable] public sealed class GachaItemRevealUnityEvent : UnityEvent<GachaPullResult, Color> { }

    /// <summary>
    /// Drop-in MonoBehaviour implementing IGachaRevealSequence for both uGUI Canvas and 3D Scene workflows.
    /// Spawns banner cutscenes, tier VFX prefabs, 3D item preview prefabs, plays SFX/voice clips,
    /// triggers optional Animator states, and supports Skip at any point.
    /// </summary>
    public sealed class GachaRevealPresenter : MonoBehaviour, IGachaRevealSequence
    {
        [Header("Scene / Canvas Spawn Anchors")]
        [SerializeField] private Transform cutsceneSpawnPoint;
        [SerializeField] private Transform vfxSpawnPoint;
        [SerializeField] private Transform previewSpawnPoint;

        [Header("Audio Sources")]
        [SerializeField] private AudioSource sfxAudioSource;
        [SerializeField] private AudioSource voiceAudioSource;

        [Header("Optional Animator Integration")]
        [SerializeField] private Animator revealAnimator;
        [SerializeField] private string introTriggerName = "PullIntro";
        [SerializeField] private string revealTriggerName = "ItemReveal";
        [SerializeField] private string rarityIntParamName = "RarityTier";

        [Header("Timing & Cleanup")]
        [SerializeField, Min(0f)] private float introDurationSeconds = 1.25f;
        [SerializeField, Min(0f)] private float itemRevealDurationSeconds = 0.85f;
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField] private bool destroySpawnedObjectsOnCleanup = true;

        [Header("Inspector Events (Canvas / Timeline / VFX Hooks)")]
        public GachaIntroUnityEvent onIntroStarted = new GachaIntroUnityEvent();
        public GachaItemRevealUnityEvent onItemPresentationStarted = new GachaItemRevealUnityEvent();
        public UnityEvent onPresentationCleared = new UnityEvent();

        private readonly List<GameObject> _spawnedObjects = new List<GameObject>();
        private GameObject _activePreviewObject;

        /// <inheritdoc/>
        public IEnumerator PlayBatchIntro(
            GachaBannerAsset banner,
            GachaPullBatchResult batch,
            GachaRarity highestRarity,
            Func<bool> isSkipRequested)
        {
            CleanupPresentation();

            if (banner != null)
            {
                if (banner.pullCutscenePrefab != null)
                {
                    SpawnTemporary(banner.pullCutscenePrefab, cutsceneSpawnPoint != null ? cutsceneSpawnPoint : transform);
                }

                PlayClip(sfxAudioSource, banner.pullStartSfx);
            }

            if (revealAnimator != null)
            {
                if (!string.IsNullOrEmpty(rarityIntParamName))
                {
                    revealAnimator.SetInteger(rarityIntParamName, (int)highestRarity);
                }
                if (!string.IsNullOrEmpty(introTriggerName))
                {
                    revealAnimator.SetTrigger(introTriggerName);
                }
            }

            onIntroStarted?.Invoke(banner, highestRarity);

            yield return WaitSkippable(introDurationSeconds, isSkipRequested);
        }

        /// <inheritdoc/>
        public IEnumerator PlayItemReveal(
            GachaBannerAsset banner,
            GachaPullResult result,
            Func<bool> isSkipRequested)
        {
            if (result == null) yield break;

            ClearActivePreview();

            RarityTierConfig tier = banner != null ? banner.GetTierConfig(result.rarity) : null;
            Color tierColor = tier != null ? tier.tierColor : Color.white;

            if (tier != null)
            {
                if (tier.revealVfxPrefab != null)
                {
                    SpawnTemporary(tier.revealVfxPrefab, vfxSpawnPoint != null ? vfxSpawnPoint : transform);
                }
                PlayClip(sfxAudioSource, tier.revealSfx);
            }

            if (result.item != null)
            {
                if (result.item.previewPrefab != null)
                {
                    Transform parent = previewSpawnPoint != null ? previewSpawnPoint : transform;
                    _activePreviewObject = Instantiate(result.item.previewPrefab, parent);
                }

                PlayClip(voiceAudioSource != null ? voiceAudioSource : sfxAudioSource, result.item.voiceLineSfx);
            }

            if (revealAnimator != null)
            {
                if (!string.IsNullOrEmpty(rarityIntParamName))
                {
                    revealAnimator.SetInteger(rarityIntParamName, (int)result.rarity);
                }
                if (!string.IsNullOrEmpty(revealTriggerName))
                {
                    revealAnimator.SetTrigger(revealTriggerName);
                }
            }

            onItemPresentationStarted?.Invoke(result, tierColor);

            yield return WaitSkippable(itemRevealDurationSeconds, isSkipRequested);
        }

        /// <inheritdoc/>
        public void CleanupPresentation()
        {
            ClearActivePreview();

            if (destroySpawnedObjectsOnCleanup)
            {
                for (int i = 0; i < _spawnedObjects.Count; i++)
                {
                    if (_spawnedObjects[i] != null)
                    {
                        DestroyObjectSafe(_spawnedObjects[i]);
                    }
                }
            }

            _spawnedObjects.Clear();
            onPresentationCleared?.Invoke();
        }

        /// <summary>
        /// Destroys the currently instantiated 3D item preview object if one exists.
        /// </summary>
        private void ClearActivePreview()
        {
            if (_activePreviewObject != null)
            {
                DestroyObjectSafe(_activePreviewObject);
                _activePreviewObject = null;
            }
        }

        /// <summary>
        /// Instantiates a temporary cutscene or VFX prefab and tracks it for cleanup at the end of the reveal.
        /// </summary>
        private void SpawnTemporary(GameObject prefab, Transform parent)
        {
            var instance = Instantiate(prefab, parent);
            _spawnedObjects.Add(instance);
        }

        /// <summary>
        /// Plays <paramref name="clip"/> as a one-shot on <paramref name="source"/> when both references are assigned.
        /// </summary>
        private static void PlayClip(AudioSource source, AudioClip clip)
        {
            if (source != null && clip != null)
            {
                source.PlayOneShot(clip);
            }
        }

        /// <summary>
        /// Waits for <paramref name="seconds"/> using scaled or unscaled time, exiting early if <paramref name="isSkipRequested"/> returns <c>true</c>.
        /// </summary>
        private IEnumerator WaitSkippable(float seconds, Func<bool> isSkipRequested)
        {
            if (seconds <= 0f) yield break;

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                if (isSkipRequested != null && isSkipRequested())
                {
                    yield break;
                }

                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// Destroys a spawned GameObject safely in both Play Mode and Edit Mode.
        /// </summary>
        private static void DestroyObjectSafe(GameObject go)
        {
            if (Application.isPlaying)
            {
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }

        private void OnDestroy()
        {
            CleanupPresentation();
        }
    }
}
