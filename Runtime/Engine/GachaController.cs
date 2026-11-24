using System;
using System.Collections;
using System.Collections.Generic;
using GachaEngine.Data;
using GachaEngine.Presentation;
using UnityEngine;
using UnityEngine.Events;

namespace GachaEngine.Engine
{
    /// <summary>Inspector-bindable <see cref="UnityEvent{T0}"/> carrying a <see cref="GachaBannerAsset"/>.</summary>
    [Serializable] public sealed class GachaBannerUnityEvent : UnityEvent<GachaBannerAsset> { }

    /// <summary>Inspector-bindable <see cref="UnityEvent{T0}"/> carrying a <see cref="GachaRarity"/>.</summary>
    [Serializable] public sealed class GachaRarityUnityEvent : UnityEvent<GachaRarity> { }

    /// <summary>Inspector-bindable <see cref="UnityEvent{T0}"/> carrying a single <see cref="GachaPullResult"/>.</summary>
    [Serializable] public sealed class GachaPullResultUnityEvent : UnityEvent<GachaPullResult> { }

    /// <summary>Inspector-bindable <see cref="UnityEvent{T0}"/> carrying a completed <see cref="GachaPullBatchResult"/>.</summary>
    [Serializable] public sealed class GachaPullBatchUnityEvent : UnityEvent<GachaPullBatchResult> { }

    /// <summary>Inspector-bindable <see cref="UnityEvent{T0}"/> carrying an error message when a pull fails.</summary>
    [Serializable] public sealed class GachaErrorUnityEvent : UnityEvent<string> { }

    /// <summary>
    /// Drop-in MonoBehaviour hosting a GachaRoller with Inspector-assigned banners, optional
    /// IGachaRevealSequence presentation hooks, and Inspector-bindable UnityEvents for Canvas
    /// buttons, VFX, audio, animations, and 3D scene wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GachaController : MonoBehaviour
    {
        [Header("Banners & Wallet")]
        [SerializeField] private List<GachaBannerAsset> banners = new List<GachaBannerAsset>();
        [SerializeField, Min(0)] private int activeBannerIndex;
        [SerializeField, Min(0)] private int historyRefreshIntervalSeconds = 3600;
        [SerializeField] private bool enforceWalletBalance = false;
        [SerializeField] private int startingCurrencyAmount = 16000;

        [Header("Optional Presentation Sequencer (Canvas / 3D Scene / VFX / Audio)")]
        [Tooltip("Assign a GachaRevealPresenter or any MonoBehaviour implementing IGachaRevealSequence.")]
        [SerializeField] private MonoBehaviour revealSequenceBehaviour;

        [Header("Inspector Events (Wire to Canvas UI, Audio, VFX, or Timeline)")]
        public GachaBannerUnityEvent onActiveBannerChanged = new GachaBannerUnityEvent();
        public GachaBannerUnityEvent onPullStarted = new GachaBannerUnityEvent();
        public GachaRarityUnityEvent onHighestRarityDetermined = new GachaRarityUnityEvent();
        public GachaPullResultUnityEvent onPullRevealed = new GachaPullResultUnityEvent();
        public GachaPullBatchUnityEvent onBatchFinished = new GachaPullBatchUnityEvent();
        public GachaErrorUnityEvent onPullFailed = new GachaErrorUnityEvent();

        private GachaRoller _roller;
        private InMemoryCurrencyWallet _wallet;
        private IGachaRevealSequence _customRevealSequence;
        private Coroutine _activeRevealCoroutine;
        private bool _skipRequested;

        /// <summary>
        /// Underlying <see cref="GachaRoller"/> instance, lazily initialized on first access.
        /// </summary>
        public GachaRoller Roller
        {
            get
            {
                EnsureInitialized();
                return _roller;
            }
        }

        /// <summary>
        /// Built-in <see cref="InMemoryCurrencyWallet"/> used when <c>enforceWalletBalance</c> is enabled, lazily initialized on first access.
        /// </summary>
        public InMemoryCurrencyWallet Wallet
        {
            get
            {
                EnsureInitialized();
                return _wallet;
            }
        }

        /// <summary>
        /// Read-only list of banners registered on this controller.
        /// </summary>
        public IReadOnlyList<GachaBannerAsset> Banners => banners;

        /// <summary>
        /// <c>true</c> while an <see cref="IGachaRevealSequence"/> coroutine is currently playing.
        /// </summary>
        public bool IsRevealing => _activeRevealCoroutine != null;

        /// <summary>
        /// Currently selected banner clamped to the bounds of <see cref="Banners"/>, or <c>null</c> when no banners are configured.
        /// </summary>
        public GachaBannerAsset ActiveBanner
        {
            get
            {
                if (banners == null || banners.Count == 0) return null;
                int clamped = Mathf.Clamp(activeBannerIndex, 0, banners.Count - 1);
                return banners[clamped];
            }
        }

        /// <summary>
        /// Raised for each individual pull immediately when rolled by <see cref="Roller"/> (before reveal playback).
        /// </summary>
        public event Action<GachaPullResult> OnPullCompleted;

        /// <summary>
        /// Raised after all pulls in a batch finish rolling (before reveal playback).
        /// </summary>
        public event Action<GachaPullBatchResult> OnBatchCompleted;

        private void Awake()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// Replaces the configured banner list, seeds starting wallet currency for each banner, and selects <paramref name="initialIndex"/>.
        /// </summary>
        /// <param name="bannerAssets">Sequence of banners to assign; <c>null</c> clears the list.</param>
        /// <param name="initialIndex">Zero-based banner index to activate, clamped to valid bounds.</param>
        public void SetBanners(IEnumerable<GachaBannerAsset> bannerAssets, int initialIndex = 0)
        {
            banners.Clear();
            if (bannerAssets != null)
            {
                banners.AddRange(bannerAssets);
            }

            activeBannerIndex = banners.Count > 0 ? Mathf.Clamp(initialIndex, 0, banners.Count - 1) : 0;
            SeedWalletForBanners();
            if (ActiveBanner != null)
            {
                onActiveBannerChanged?.Invoke(ActiveBanner);
            }
        }

        /// <summary>
        /// Selects the active banner by zero-based index and invokes <see cref="onActiveBannerChanged"/>.
        /// </summary>
        /// <param name="index">Banner index, clamped to <see cref="Banners"/> bounds.</param>
        public void SelectBanner(int index)
        {
            if (banners == null || banners.Count == 0) return;
            activeBannerIndex = Mathf.Clamp(index, 0, banners.Count - 1);
            onActiveBannerChanged?.Invoke(ActiveBanner);
        }

        /// <summary>
        /// Selects <paramref name="banner"/> as the active banner, appending it to <see cref="Banners"/> and seeding currency if not already present.
        /// </summary>
        /// <param name="banner">Banner asset to activate; ignored when <c>null</c>.</param>
        public void SelectBanner(GachaBannerAsset banner)
        {
            if (banner == null) return;
            int idx = banners.IndexOf(banner);
            if (idx < 0)
            {
                banners.Add(banner);
                idx = banners.Count - 1;
                SeedWalletForBanner(banner);
            }
            activeBannerIndex = idx;
            onActiveBannerChanged?.Invoke(banner);
        }

        /// <summary>
        /// Assigns a runtime <see cref="IGachaRevealSequence"/> that overrides the Inspector-assigned <c>revealSequenceBehaviour</c>.
        /// </summary>
        /// <param name="sequence">Custom reveal sequence, or <c>null</c> to revert to the Inspector reference.</param>
        public void SetRevealSequence(IGachaRevealSequence sequence)
        {
            _customRevealSequence = sequence;
        }

        /// <summary>
        /// Zero-argument helper directly bindable to a uGUI Canvas Button.onClick().
        /// </summary>
        public void PullSingleActiveBanner()
        {
            if (ActiveBanner == null)
            {
                onPullFailed?.Invoke("No active banner configured on GachaController.");
                return;
            }
            PullSingle(ActiveBanner);
        }

        /// <summary>
        /// Zero-argument helper directly bindable to a uGUI Canvas Button.onClick().
        /// </summary>
        public void PullTenActiveBanner()
        {
            if (ActiveBanner == null)
            {
                onPullFailed?.Invoke("No active banner configured on GachaController.");
                return;
            }
            PullTen(ActiveBanner);
        }

        /// <summary>
        /// Requests skipping any active pull intro or item reveal animation immediately.
        /// Bindable directly to a Canvas Skip Button.onClick().
        /// </summary>
        public void SkipReveal()
        {
            _skipRequested = true;
        }

        /// <summary>
        /// Checks the configured history refresh window and publishes pending pulls if elapsed.
        /// </summary>
        /// <returns><c>true</c> if pending records were flushed into published history; otherwise <c>false</c>.</returns>
        public bool RefreshHistoryIfReady()
        {
            return Roller.History.TryRefresh();
        }

        /// <summary>
        /// Executes a single pull on <paramref name="banner"/> and starts reveal presentation when available.
        /// </summary>
        /// <param name="banner">Target banner asset to roll.</param>
        /// <returns>Batch result containing one pull outcome, or a failed result with <see cref="GachaPullBatchResult.errorMessage"/>.</returns>
        public GachaPullBatchResult PullSingle(GachaBannerAsset banner) =>
            ExecuteRollAndPresentation(banner, 1);

        /// <summary>
        /// Executes a 10-pull batch (or the current Step-Up step) on <paramref name="banner"/> and starts reveal presentation when available.
        /// </summary>
        /// <param name="banner">Target banner asset to roll.</param>
        /// <returns>Batch result containing the rolled outcomes, or a failed result with <see cref="GachaPullBatchResult.errorMessage"/>.</returns>
        public GachaPullBatchResult PullTen(GachaBannerAsset banner) =>
            ExecuteRollAndPresentation(banner, 10);

        /// <summary>
        /// Executes <paramref name="count"/> pulls on <paramref name="banner"/> and starts reveal presentation when available.
        /// </summary>
        /// <param name="banner">Target banner asset to roll.</param>
        /// <param name="count">Number of pulls requested (overridden by step configuration on Step-Up banners).</param>
        /// <returns>Batch result containing the rolled outcomes, or a failed result with <see cref="GachaPullBatchResult.errorMessage"/>.</returns>
        public GachaPullBatchResult PullMulti(GachaBannerAsset banner, int count) =>
            ExecuteRollAndPresentation(banner, count);

        /// <summary>
        /// Runs the underlying roll, fires Inspector events, and either launches the active <see cref="IGachaRevealSequence"/> coroutine or dispatches events immediately.
        /// </summary>
        private GachaPullBatchResult ExecuteRollAndPresentation(GachaBannerAsset banner, int count)
        {
            EnsureInitialized();
            if (banner == null)
            {
                var err = new GachaPullBatchResult
                {
                    succeeded = false,
                    errorMessage = "Banner is null."
                };
                onPullFailed?.Invoke(err.errorMessage);
                return err;
            }

            if (_activeRevealCoroutine != null)
            {
                StopCoroutine(_activeRevealCoroutine);
                _activeRevealCoroutine = null;
                ResolveRevealSequence()?.CleanupPresentation();
            }

            onPullStarted?.Invoke(banner);

            GachaPullBatchResult batch = count == 1
                ? _roller.RollSingle(banner, chargeWallet: enforceWalletBalance)
                : _roller.RollMulti(banner, count, chargeWallet: enforceWalletBalance);

            if (!batch.succeeded)
            {
                onPullFailed?.Invoke(batch.errorMessage);
                return batch;
            }

            GachaRarity highest = batch.GetHighestRarity();
            onHighestRarityDetermined?.Invoke(highest);

            IGachaRevealSequence sequence = ResolveRevealSequence();
            if (sequence != null && Application.isPlaying && isActiveAndEnabled)
            {
                _skipRequested = false;
                _activeRevealCoroutine = StartCoroutine(RunRevealRoutine(sequence, banner, batch, highest));
            }
            else
            {
                for (int i = 0; i < batch.results.Count; i++)
                {
                    onPullRevealed?.Invoke(batch.results[i]);
                }
                onBatchFinished?.Invoke(batch);
            }

            return batch;
        }

        /// <summary>
        /// Coroutine driving <see cref="IGachaRevealSequence.PlayBatchIntro"/>, per-item <see cref="IGachaRevealSequence.PlayItemReveal"/>, cleanup, and <see cref="onBatchFinished"/>.
        /// </summary>
        private IEnumerator RunRevealRoutine(
            IGachaRevealSequence sequence,
            GachaBannerAsset banner,
            GachaPullBatchResult batch,
            GachaRarity highest)
        {
            Func<bool> isSkipped = () => _skipRequested;

            yield return sequence.PlayBatchIntro(banner, batch, highest, isSkipped);

            for (int i = 0; i < batch.results.Count; i++)
            {
                GachaPullResult result = batch.results[i];
                onPullRevealed?.Invoke(result);

                if (!_skipRequested)
                {
                    yield return sequence.PlayItemReveal(banner, result, isSkipped);
                }
            }

            sequence.CleanupPresentation();
            _activeRevealCoroutine = null;
            onBatchFinished?.Invoke(batch);
        }

        /// <summary>
        /// Returns the runtime-assigned reveal sequence if set, otherwise casts <c>revealSequenceBehaviour</c> to <see cref="IGachaRevealSequence"/>.
        /// </summary>
        private IGachaRevealSequence ResolveRevealSequence()
        {
            if (_customRevealSequence != null) return _customRevealSequence;
            return revealSequenceBehaviour as IGachaRevealSequence;
        }

        /// <summary>
        /// Lazily initializes the internal wallet, history log, and <see cref="GachaRoller"/> and forwards roller events.
        /// </summary>
        private void EnsureInitialized()
        {
            if (_roller != null) return;

            _wallet = new InMemoryCurrencyWallet();
            SeedWalletForBanners();

            var history = new GachaHistoryLog(2000, historyRefreshIntervalSeconds);
            _roller = new GachaRoller(wallet: _wallet, history: history);
            _roller.OnPullCompleted += r => OnPullCompleted?.Invoke(r);
            _roller.OnBatchCompleted += b => OnBatchCompleted?.Invoke(b);
        }

        /// <summary>
        /// Seeds starting currency balances for every banner currently in <see cref="Banners"/>.
        /// </summary>
        private void SeedWalletForBanners()
        {
            if (_wallet == null || banners == null) return;
            for (int i = 0; i < banners.Count; i++)
            {
                SeedWalletForBanner(banners[i]);
            }
        }

        /// <summary>
        /// Credits <c>startingCurrencyAmount</c> for <paramref name="banner"/>'s currency when its current balance is zero.
        /// </summary>
        private void SeedWalletForBanner(GachaBannerAsset banner)
        {
            if (_wallet == null || banner == null || string.IsNullOrEmpty(banner.currencyId)) return;
            if (_wallet.GetBalance(banner.currencyId) == 0 && startingCurrencyAmount > 0)
            {
                _wallet.Add(banner.currencyId, startingCurrencyAmount);
            }
        }
    }
}
