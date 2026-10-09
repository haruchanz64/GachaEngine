using System;
using System.Collections;
using System.Collections.Generic;
using GachaEngine.Data;
using GachaEngine.Presentation;
using UnityEngine;
using UnityEngine.Events;

namespace GachaEngine.Engine
{
    [Serializable] public sealed class GachaBannerUnityEvent : UnityEvent<GachaBannerAsset> { }
    [Serializable] public sealed class GachaRarityUnityEvent : UnityEvent<GachaRarity> { }
    [Serializable] public sealed class GachaPullResultUnityEvent : UnityEvent<GachaPullResult> { }
    [Serializable] public sealed class GachaPullBatchUnityEvent : UnityEvent<GachaPullBatchResult> { }
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

        public GachaRoller Roller
        {
            get
            {
                EnsureInitialized();
                return _roller;
            }
        }

        public InMemoryCurrencyWallet Wallet
        {
            get
            {
                EnsureInitialized();
                return _wallet;
            }
        }

        public IReadOnlyList<GachaBannerAsset> Banners => banners;
        public bool IsRevealing => _activeRevealCoroutine != null;

        public GachaBannerAsset ActiveBanner
        {
            get
            {
                if (banners == null || banners.Count == 0) return null;
                int clamped = Mathf.Clamp(activeBannerIndex, 0, banners.Count - 1);
                return banners[clamped];
            }
        }

        public event Action<GachaPullResult> OnPullCompleted;
        public event Action<GachaPullBatchResult> OnBatchCompleted;

        private void Awake()
        {
            EnsureInitialized();
        }

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

        public void SelectBanner(int index)
        {
            if (banners == null || banners.Count == 0) return;
            activeBannerIndex = Mathf.Clamp(index, 0, banners.Count - 1);
            onActiveBannerChanged?.Invoke(ActiveBanner);
        }

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
        /// Checks the 1-hour history refresh window and publishes pending pulls if elapsed.
        /// </summary>
        public bool RefreshHistoryIfReady()
        {
            return Roller.History.TryRefresh();
        }

        public GachaPullBatchResult PullSingle(GachaBannerAsset banner) =>
            ExecuteRollAndPresentation(banner, 1);

        public GachaPullBatchResult PullTen(GachaBannerAsset banner) =>
            ExecuteRollAndPresentation(banner, 10);

        public GachaPullBatchResult PullMulti(GachaBannerAsset banner, int count) =>
            ExecuteRollAndPresentation(banner, count);

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

        private IGachaRevealSequence ResolveRevealSequence()
        {
            if (_customRevealSequence != null) return _customRevealSequence;
            return revealSequenceBehaviour as IGachaRevealSequence;
        }

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

        private void SeedWalletForBanners()
        {
            if (_wallet == null || banners == null) return;
            for (int i = 0; i < banners.Count; i++)
            {
                SeedWalletForBanner(banners[i]);
            }
        }

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
