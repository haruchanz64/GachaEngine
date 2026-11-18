using System;
using System.Collections.Generic;
using System.IO;
using GachaEngine.Data;
using GachaEngine.Engine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GachaEngine.Editor.UI
{
    /// <summary>
    /// UI Toolkit Banner Simulator with four tabs:
    /// 1) Banner Pulls & Live Pity/Rate-Up Tuning
    /// 2) Banner Details (Rate-Up Character/Item Showcase & Drop Rates)
    /// 3) Pull History (1-Hour Refresh Window, Pagination & JSON/CSV Export)
    /// 4) Bulk Monte Carlo Audit
    /// </summary>
    public sealed class GachaSimulatorWindow : EditorWindow
    {
        private const int HistoryPageSize = 10;

        private GachaBannerAsset _assignedBanner;
        private GachaBannerAsset _sandboxBanner;
        private GachaRoller _roller;
        private GachaPullBatchResult _lastBatch;
        private GachaBulkSimulationReport _lastBulkReport;

        private int _activeTab;
        private int _historyPage;
        private bool _includePendingInExport;
        private string _historyRarityFilter = "All";

        private Button[] _tabButtons;
        private ScrollView _body;
        private Label _statusLabel;

        [MenuItem("Tools/HaruChanZ64/Gameplay/GachaEngine/Banner Simulator", false, 134)]
        public static void ShowWindow()
        {
            var window = GetWindow<GachaSimulatorWindow>("GachaEngine");
            window.minSize = new Vector2(380f, 360f);
            window.Show();
        }

        public static void OpenWithBanner(GachaBannerAsset banner)
        {
            var window = GetWindow<GachaSimulatorWindow>("GachaEngine");
            window.minSize = new Vector2(380f, 360f);
            window._assignedBanner = banner;
            window.Show();
            window.SelectTab(0);
        }

        private void OnEnable()
        {
            if (_roller == null)
            {
                _roller = new GachaRoller(history: new GachaHistoryLog(2000, 3600));
            }
            EnsureSandboxBanner();
        }

        private void OnDisable()
        {
            if (_sandboxBanner != null)
            {
                DestroyImmediate(_sandboxBanner);
                _sandboxBanner = null;
            }
        }

        private GachaBannerAsset ActiveBanner
        {
            get
            {
                if (_assignedBanner != null) return _assignedBanner;
                EnsureSandboxBanner();
                return _sandboxBanner;
            }
        }

        private void EnsureSandboxBanner()
        {
            if (_sandboxBanner == null)
            {
                _sandboxBanner = CreateInstance<GachaBannerAsset>();
                _sandboxBanner.ApplyLimitedCharacterPreset();
            }
        }

        public void CreateGUI()
        {
            if (_roller == null)
            {
                _roller = new GachaRoller(history: new GachaHistoryLog(2000, 3600));
            }
            EnsureSandboxBanner();

            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("ge-root");
            AttachStyles(rootVisualElement);

            var header = new VisualElement();
            header.AddToClassList("ge-header");
            var title = new Label("GachaEngine — Banner Simulator, Details & Pull History");
            title.AddToClassList("ge-title");
            header.Add(title);
            header.Add(new Button(ResetSimulatorState) { text = "Reset Pity & History" });
            rootVisualElement.Add(header);

            var tabs = new VisualElement();
            tabs.AddToClassList("ge-tabs");
            _tabButtons = new[]
            {
                MakeTabBtn("Banner & Pulls", 0),
                MakeTabBtn("Banner Details", 1),
                MakeTabBtn("Pull History (1h Refresh)", 2),
                MakeTabBtn("Monte Carlo Audit", 3)
            };
            for (int i = 0; i < _tabButtons.Length; i++) tabs.Add(_tabButtons[i]);
            rootVisualElement.Add(tabs);

            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.AddToClassList("ge-body");
            rootVisualElement.Add(_body);

            _statusLabel = new Label("Ready. Select a GachaBannerAsset or test any starter preset.");
            _statusLabel.AddToClassList("ge-status");
            rootVisualElement.Add(_statusLabel);

            SelectTab(_activeTab);
        }

        private Button MakeTabBtn(string label, int index)
        {
            var btn = new Button(() => SelectTab(index)) { text = label };
            btn.AddToClassList("ge-tab-btn");
            return btn;
        }

        private void SelectTab(int index)
        {
            _activeTab = index;
            if (_tabButtons != null)
            {
                for (int i = 0; i < _tabButtons.Length; i++)
                {
                    _tabButtons[i].EnableInClassList("ge-tab-btn--active", i == index);
                }
            }

            if (_body == null) return;
            _body.Clear();

            switch (index)
            {
                case 0: BuildBannerPullsTab(); break;
                case 1: BuildBannerDetailsTab(); break;
                case 2: BuildPullHistoryTab(); break;
                case 3: BuildMonteCarloTab(); break;
            }
        }

        // ==================== TAB 1: BANNER & PULLS ====================
        private void BuildBannerPullsTab()
        {
            var banner = ActiveBanner;
            var pity = _roller.PityStore.GetOrCreate(banner.pityGroupId);

            // Banner selector & quick sandbox presets
            var selectCard = new VisualElement();
            selectCard.AddToClassList("ge-card");
            selectCard.Add(MakeTitle("Banner Source & Live Pity Configuration"));

            var objField = new ObjectField("Banner Asset (Optional — Uses Sandbox When Empty)")
            {
                objectType = typeof(GachaBannerAsset),
                value = _assignedBanner
            };
            objField.RegisterValueChangedCallback(evt =>
            {
                _assignedBanner = evt.newValue as GachaBannerAsset;
                _lastBatch = null;
                SelectTab(0);
            });
            selectCard.Add(objField);

            var presetRow = new VisualElement();
            presetRow.AddToClassList("ge-row");
            presetRow.Add(new Button(() => LoadSandboxPreset(b => b.ApplyLimitedCharacterPreset(), "Limited Character (5★/SSR)")) { text = "Sandbox: Limited 50/50" });
            presetRow.Add(new Button(() => LoadSandboxPreset(b => b.ApplyFatePathWeaponPreset(), "Weapon Fate Path (S-Rank)")) { text = "Sandbox: Fate Path" });
            presetRow.Add(new Button(() => LoadSandboxPreset(b => b.ApplyStepUpPreset(), "5-Step Ladder")) { text = "Sandbox: Step-Up" });
            selectCard.Add(presetRow);

            // Live editable rarity labels, pity thresholds, and rate-up %
            var legTier = banner.GetTierConfig(GachaRarity.Legendary);
            var epicTier = banner.GetTierConfig(GachaRarity.Epic);

            if (legTier != null)
            {
                var row5 = new VisualElement();
                row5.AddToClassList("ge-row");
                var lblField = new TextField("Top Tier Label") { value = legTier.customLabel };
                lblField.style.flexGrow = 1f;
                lblField.style.minWidth = 165f;
                lblField.RegisterValueChangedCallback(e => legTier.customLabel = e.newValue);

                var softField = new IntegerField("Soft Pity") { value = legTier.softPityStartPull };
                softField.style.flexGrow = 0.6f;
                softField.style.minWidth = 115f;
                softField.RegisterValueChangedCallback(e => legTier.softPityStartPull = Mathf.Max(1, e.newValue));

                var hardField = new IntegerField("Hard Pity") { value = legTier.hardPityPull };
                hardField.style.flexGrow = 0.6f;
                hardField.style.minWidth = 115f;
                hardField.RegisterValueChangedCallback(e => legTier.hardPityPull = Mathf.Max(1, e.newValue));

                var rateUpField = new FloatField("Rate-Up %") { value = (float)(legTier.featuredRateUpChance * 100d) };
                rateUpField.style.flexGrow = 0.6f;
                rateUpField.style.minWidth = 125f;
                rateUpField.RegisterValueChangedCallback(e => legTier.featuredRateUpChance = Mathf.Clamp(e.newValue, 0f, 100f) / 100d);

                row5.Add(lblField);
                row5.Add(softField);
                row5.Add(hardField);
                row5.Add(rateUpField);
                selectCard.Add(row5);
            }

            if (epicTier != null)
            {
                var row4 = new VisualElement();
                row4.AddToClassList("ge-row");
                var lbl4 = new TextField("Mid Tier Label") { value = epicTier.customLabel };
                lbl4.style.flexGrow = 1f;
                lbl4.style.minWidth = 165f;
                lbl4.RegisterValueChangedCallback(e => epicTier.customLabel = e.newValue);

                var soft4 = new IntegerField("Soft Pity") { value = epicTier.softPityStartPull };
                soft4.style.flexGrow = 0.6f;
                soft4.style.minWidth = 115f;
                soft4.RegisterValueChangedCallback(e => epicTier.softPityStartPull = Mathf.Max(1, e.newValue));

                var hard4 = new IntegerField("Hard Pity") { value = epicTier.hardPityPull };
                hard4.style.flexGrow = 0.6f;
                hard4.style.minWidth = 115f;
                hard4.RegisterValueChangedCallback(e => epicTier.hardPityPull = Mathf.Max(1, e.newValue));

                var rateUp4 = new FloatField("Rate-Up %") { value = (float)(epicTier.featuredRateUpChance * 100d) };
                rateUp4.style.flexGrow = 0.6f;
                rateUp4.style.minWidth = 125f;
                rateUp4.RegisterValueChangedCallback(e => epicTier.featuredRateUpChance = Mathf.Clamp(e.newValue, 0f, 100f) / 100d);

                row4.Add(lbl4);
                row4.Add(soft4);
                row4.Add(hard4);
                row4.Add(rateUp4);
                selectCard.Add(row4);
            }

            // Fate Path target picker when enabled
            if (banner.fatePath != null && banner.fatePath.enabled)
            {
                var featuredLegs = banner.GetItemsByRarity(banner.fatePath.targetRarity, featuredOnly: true);
                if (featuredLegs.Count > 0)
                {
                    var choices = new List<string>();
                    int curIdx = 0;
                    for (int i = 0; i < featuredLegs.Count; i++)
                    {
                        choices.Add(featuredLegs[i].displayName);
                        if (featuredLegs[i].itemId == pity.selectedFateItemId) curIdx = i;
                    }
                    if (string.IsNullOrEmpty(pity.selectedFateItemId))
                    {
                        _roller.SetFateTarget(banner, featuredLegs[0].itemId);
                    }

                    var fateDropdown = new DropdownField("Fate Path Target", choices, curIdx);
                    fateDropdown.RegisterValueChangedCallback(evt =>
                    {
                        int idx = Mathf.Clamp(fateDropdown.index, 0, featuredLegs.Count - 1);
                        _roller.SetFateTarget(banner, featuredLegs[idx].itemId);
                        SelectTab(0);
                    });
                    selectCard.Add(fateDropdown);
                }
            }

            // Pull buttons
            var actionRow = new VisualElement();
            actionRow.AddToClassList("ge-row");
            actionRow.Add(new Button(() => DoPull(1)) { text = $"Pull x1 ({banner.singlePullCost} {banner.currencyId})" });
            string multiLabel = banner.bannerType == GachaBannerType.StepUp && banner.stepUpSteps.Count > 0
                ? $"Pull {banner.stepUpSteps[pity.currentStepIndex % banner.stepUpSteps.Count].stepLabel}"
                : $"Pull x10 ({banner.tenPullCost} {banner.currencyId})";
            actionRow.Add(new Button(() => DoPull(10)) { text = multiLabel });
            actionRow.Add(new Button(() => SelectTab(1)) { text = "View Banner Details" });
            actionRow.Add(new Button(() => SelectTab(2)) { text = $"History ({_roller.History.PublishedRecords.Count} synced / {_roller.History.PendingCount} pending)" });
            selectCard.Add(actionRow);
            _body.Add(selectCard);

            // Live Pity Dashboard
            var pityCard = new VisualElement();
            pityCard.AddToClassList("ge-card");
            pityCard.Add(MakeTitle($"Live Pity State (Group: {banner.pityGroupId})"));

            var legCounter = pity.GetOrCreateCounter(GachaRarity.Legendary);
            var epicCounter = pity.GetOrCreateCounter(GachaRarity.Epic);
            int legHard = legTier != null ? Mathf.Max(1, legTier.hardPityPull) : 90;
            int epicHard = epicTier != null ? Mathf.Max(1, epicTier.hardPityPull) : 10;

            var kpiRow = new VisualElement();
            kpiRow.AddToClassList("ge-wrap-row");
            AddKpi(kpiRow, "Total Pulls", pity.totalPulls.ToString());
            AddKpi(kpiRow, $"{banner.GetRarityLabel(GachaRarity.Legendary)} Pity", $"{legCounter.pullsSinceHit} / {legHard}");
            AddKpi(kpiRow, $"{banner.GetRarityLabel(GachaRarity.Epic)} Pity", $"{epicCounter.pullsSinceHit} / {epicHard}");
            AddKpi(kpiRow, "Featured Guarantee", legCounter.guaranteedFeaturedNext ? "GUARANTEED NEXT" : $"{(legTier?.featuredRateUpChance ?? 0.5d) * 100d:F0}% Rate-Up");
            if (banner.fatePath != null && banner.fatePath.enabled)
            {
                AddKpi(kpiRow, "Fate Points", $"{pity.fatePoints} / {banner.fatePath.maxFatePoints}");
            }
            if (banner.bannerType == GachaBannerType.StepUp && banner.stepUpSteps.Count > 0)
            {
                AddKpi(kpiRow, "Next Step", banner.stepUpSteps[pity.currentStepIndex % banner.stepUpSteps.Count].stepLabel);
            }
            pityCard.Add(kpiRow);

            pityCard.Add(new Label($"{banner.GetRarityLabel(GachaRarity.Legendary)} Pity Progress ({legCounter.pullsSinceHit}/{legHard})"));
            pityCard.Add(MakeProgressBar(legCounter.pullsSinceHit / (float)legHard, "ge-bar-fill-ssr"));

            pityCard.Add(new Label($"{banner.GetRarityLabel(GachaRarity.Epic)} Pity Progress ({epicCounter.pullsSinceHit}/{epicHard})"));
            pityCard.Add(MakeProgressBar(epicCounter.pullsSinceHit / (float)epicHard, "ge-bar-fill-sr"));
            _body.Add(pityCard);

            // Latest pull results grid
            var resCard = new VisualElement();
            resCard.AddToClassList("ge-card");
            resCard.Add(MakeTitle("Latest Pull Results"));

            if (_lastBatch == null || _lastBatch.results.Count == 0)
            {
                var empty = new Label("Click 'Pull x1' or 'Pull x10' to simulate pulls on this banner.");
                empty.AddToClassList("ge-muted");
                resCard.Add(empty);
            }
            else
            {
                var grid = new VisualElement();
                grid.AddToClassList("ge-wrap-row");
                for (int i = 0; i < _lastBatch.results.Count; i++)
                {
                    var r = _lastBatch.results[i];
                    var card = new VisualElement();
                    card.AddToClassList("ge-pull-card");
                    if (r.rarity == GachaRarity.Legendary) card.AddToClassList("ge-pull-card--ssr");
                    else if (r.rarity == GachaRarity.Epic) card.AddToClassList("ge-pull-card--sr");
                    else card.AddToClassList("ge-pull-card--r");

                    var nameLbl = new Label($"#{r.pullIndex} {r.item.displayName}");
                    nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                    var rarLbl = new Label($"{r.rarityLabel}{(r.isFeatured ? "  [FEATURED]" : string.Empty)}");
                    rarLbl.AddToClassList("ge-mono");

                    var tags = new List<string>();
                    if (r.triggeredHardPity) tags.Add("HARD PITY");
                    else if (r.triggeredSoftPity) tags.Add("SOFT PITY");
                    if (r.triggeredFatePath) tags.Add("FATE PATH");
                    if (r.triggeredStepGuarantee) tags.Add("STEP GUARANTEE");
                    if (r.triggeredRateUpGuarantee) tags.Add("GUARANTEED");
                    else if (r.wonRateUpFlip) tags.Add("RATE-UP WON");
                    else if (r.lostRateUpFlip) tags.Add("RATE-UP LOST");
                    if (r.duplicate.isDuplicate) tags.Add($"DUPE +{r.duplicate.rewardAmount} {r.duplicate.rewardCurrencyId}");

                    var tagLbl = new Label(tags.Count > 0 ? string.Join(" | ", tags) : "NEW");
                    tagLbl.AddToClassList("ge-muted");
                    tagLbl.style.whiteSpace = WhiteSpace.Normal;

                    card.Add(nameLbl);
                    card.Add(rarLbl);
                    card.Add(tagLbl);
                    grid.Add(card);
                }
                resCard.Add(grid);
            }

            _body.Add(resCard);
        }

        // ==================== TAB 2: BANNER DETAILS UI ====================
        private void BuildBannerDetailsTab()
        {
            var banner = ActiveBanner;

            var overview = new VisualElement();
            overview.AddToClassList("ge-card");
            overview.Add(MakeTitle($"{banner.bannerName} ({banner.bannerType})"));
            var desc = new Label(banner.bannerDescription);
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.AddToClassList("ge-mono");
            overview.Add(desc);
            overview.Add(new Label($"Pity Group: {banner.pityGroupId}  |  Single: {banner.singlePullCost} {banner.currencyId}  |  x10: {banner.tenPullCost} {banner.currencyId}"));
            var bannerMedia = new List<string>();
            if (banner.bannerArtwork != null) bannerMedia.Add($"Art: {banner.bannerArtwork.name}");
            if (banner.bannerLogo != null) bannerMedia.Add($"Logo: {banner.bannerLogo.name}");
            if (banner.bannerBgm != null) bannerMedia.Add($"BGM: {banner.bannerBgm.name}");
            if (banner.pullStartSfx != null) bannerMedia.Add($"Pull SFX: {banner.pullStartSfx.name}");
            if (banner.pullCutscenePrefab != null) bannerMedia.Add($"Cutscene: {banner.pullCutscenePrefab.name}");
            var mediaLbl = new Label(bannerMedia.Count > 0
                ? $"Presentation Assets: {string.Join("  |  ", bannerMedia)}"
                : "Presentation Assets: None assigned (configure Banner Art, BGM, Pull SFX, or Cutscene Prefab on the Banner Asset)");
            mediaLbl.AddToClassList("ge-muted");
            overview.Add(mediaLbl);
            _body.Add(overview);

            // Featured Rate-Up Character / Item Showcase
            var featuredCard = new VisualElement();
            featuredCard.AddToClassList("ge-card");
            featuredCard.Add(MakeTitle("Featured Rate-Up Characters & Items"));

            var featured = banner.GetFeaturedItems();
            if (featured.Count == 0)
            {
                featuredCard.Add(new Label("No featured rate-up items configured on this banner."));
            }
            else
            {
                for (int i = 0; i < featured.Count; i++)
                {
                    var item = featured[i];
                    var tier = banner.GetTierConfig(item.rarity);
                    var box = new VisualElement();
                    box.AddToClassList("ge-featured-card");
                    if (item.rarity == GachaRarity.Epic) box.AddToClassList("ge-featured-card--sr");

                    var title = new Label($"{item.displayName}  —  [{banner.GetRarityLabel(item.rarity)}]  {item.subtitle}");
                    title.style.unityFontStyleAndWeight = FontStyle.Bold;
                    var itemDesc = new Label(item.description);
                    itemDesc.style.whiteSpace = WhiteSpace.Normal;
                    itemDesc.AddToClassList("ge-mono");

                    string rateText = tier != null
                        ? $"Base Tier Rate: {tier.baseProbability * 100d:F2}%  |  Featured Rate-Up: {tier.featuredRateUpChance * 100d:F0}%  |  Pity: Soft {tier.softPityStartPull} / Hard {tier.hardPityPull}"
                        : string.Empty;
                    var meta = new Label($"{rateText}  |  Dupe Reward: +{item.duplicateRewardAmount} {item.duplicateCurrencyId}");
                    meta.AddToClassList("ge-muted");

                    box.Add(title);
                    box.Add(itemDesc);
                    box.Add(meta);
                    var itemAssets = new List<string>();
                    if (item.splashArt != null) itemAssets.Add($"Splash: {item.splashArt.name}");
                    if (item.previewPrefab != null) itemAssets.Add($"3D Prefab: {item.previewPrefab.name}");
                    if (item.voiceLineSfx != null) itemAssets.Add($"Voice: {item.voiceLineSfx.name}");
                    if (item.showcaseAnimation != null) itemAssets.Add($"Anim: {item.showcaseAnimation.name}");
                    if (tier != null && tier.revealVfxPrefab != null) itemAssets.Add($"Tier VFX: {tier.revealVfxPrefab.name}");
                    if (itemAssets.Count > 0)
                    {
                        var assetsLbl = new Label($"Media: {string.Join(" | ", itemAssets)}");
                        assetsLbl.AddToClassList("ge-muted");
                        box.Add(assetsLbl);
                    }
                    featuredCard.Add(box);
                }
            }
            _body.Add(featuredCard);

            // Drop Rate Rules & Full Pool Breakdown
            var ratesCard = new VisualElement();
            ratesCard.AddToClassList("ge-card");
            ratesCard.Add(MakeTitle("Drop Rate Rules & Complete Item Pool"));

            for (int t = 0; t < banner.tiers.Count; t++)
            {
                var tier = banner.tiers[t];
                if (tier == null) continue;

                string pityInfo = tier.enablePity
                    ? $"Soft Pity: Pull {tier.softPityStartPull} (+{tier.softPityBonusPerPull * 100d:F1}%/pull), Hard Guarantee: Pull {tier.hardPityPull}"
                    : "No Pity";
                string rateUpInfo = tier.hasFeaturedRateUp
                    ? $" | Featured Split: {tier.featuredRateUpChance * 100d:F0}%{(tier.guaranteeFeaturedOnNextMiss ? " (Guaranteed next on miss)" : string.Empty)}"
                    : string.Empty;

                var tierHeader = new Label($"{tier.EffectiveLabel} — Base {tier.baseProbability * 100d:F2}%  |  {pityInfo}{rateUpInfo}");
                tierHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                tierHeader.style.marginTop = 6f;
                ratesCard.Add(tierHeader);

                var pool = banner.GetItemsByRarity(tier.rarity);
                for (int i = 0; i < pool.Count; i++)
                {
                    var item = pool[i];
                    var row = new Label($"  • {item.displayName} ({item.subtitle}){(item.isFeatured ? " [RATE-UP]" : string.Empty)} — Weight: {item.weight:F1}");
                    row.AddToClassList("ge-mono");
                    ratesCard.Add(row);
                }
            }

            if (banner.bannerType == GachaBannerType.StepUp && banner.stepUpSteps.Count > 0)
            {
                var stepHeader = new Label("Step-Up Ladder Milestones:");
                stepHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                stepHeader.style.marginTop = 8f;
                ratesCard.Add(stepHeader);
                for (int i = 0; i < banner.stepUpSteps.Count; i++)
                {
                    var st = banner.stepUpSteps[i];
                    string g = st.guaranteeMinimumRarity
                        ? $" | Guaranteed {(st.guaranteeFeatured ? "Featured " : string.Empty)}{banner.GetRarityLabel(st.guaranteedRarity)}"
                        : string.Empty;
                    ratesCard.Add(new Label($"  • {st.stepLabel}: {st.pullCount} pulls for {st.costOverride} {banner.currencyId}{g}"));
                }
            }

            _body.Add(ratesCard);
        }

        // ==================== TAB 3: PULL HISTORY (1H REFRESH & JSON EXPORT) ====================
        private void BuildPullHistoryTab()
        {
            var hist = _roller.History;
            hist.TryRefresh();

            var ctrlCard = new VisualElement();
            ctrlCard.AddToClassList("ge-card");
            ctrlCard.Add(MakeTitle("Pull History (1-Hour Refresh Window & JSON Export)"));

            TimeSpan nextIn = hist.GetTimeUntilNextRefresh();
            var infoLbl = new Label(
                $"Refresh Interval: {hist.RefreshIntervalSeconds / 60} min ({hist.RefreshIntervalSeconds}s)  |  " +
                $"Last Refreshed (UTC): {hist.LastRefreshedUtc:yyyy-MM-dd HH:mm:ss}  |  " +
                $"Published: {hist.PublishedRecords.Count}  |  Pending: {hist.PendingCount}  |  " +
                $"Next Auto Refresh In: {(int)nextIn.TotalMinutes:D2}m {nextIn.Seconds:D2}s");
            infoLbl.AddToClassList("ge-mono");
            infoLbl.style.whiteSpace = WhiteSpace.Normal;
            ctrlCard.Add(infoLbl);

            var btnRow = new VisualElement();
            btnRow.AddToClassList("ge-row");
            btnRow.Add(new Button(() =>
            {
                bool refreshed = hist.TryRefresh();
                SetStatus(refreshed ? "History refreshed." : $"Refresh cooldown active ({(int)hist.GetTimeUntilNextRefresh().TotalMinutes}m remaining). Use Force Refresh for immediate dev preview.");
                SelectTab(2);
            }) { text = "Check 1h Refresh" });

            btnRow.Add(new Button(() =>
            {
                hist.ForceRefresh();
                SetStatus($"Forced history refresh: {hist.PublishedRecords.Count} record(s) synced.");
                SelectTab(2);
            }) { text = "Force Refresh Now (Dev)" });

            btnRow.Add(new Button(ExportHistoryJson) { text = "Export History JSON..." });
            btnRow.Add(new Button(ExportHistoryCsv) { text = "Export CSV..." });
            ctrlCard.Add(btnRow);

            var filterRow = new VisualElement();
            filterRow.AddToClassList("ge-row");
            var rarFilter = new DropdownField("Rarity Filter", new List<string> { "All", "Legendary", "Epic", "Rare" }, _historyRarityFilter);
            rarFilter.style.flexGrow = 1f;
            rarFilter.style.minWidth = 180f;
            rarFilter.style.maxWidth = 280f;
            rarFilter.RegisterValueChangedCallback(e =>
            {
                _historyRarityFilter = e.newValue;
                _historyPage = 0;
                SelectTab(2);
            });

            var includePendingToggle = new Toggle("Include Pending Pulls in Export") { value = _includePendingInExport };
            includePendingToggle.RegisterValueChangedCallback(e => _includePendingInExport = e.newValue);

            filterRow.Add(rarFilter);
            filterRow.Add(includePendingToggle);
            ctrlCard.Add(filterRow);
            _body.Add(ctrlCard);

            // Filtered paginated table (newest first)
            var listCard = new VisualElement();
            listCard.AddToClassList("ge-card");

            var filtered = new List<GachaHistoryRecord>();
            for (int i = hist.PublishedRecords.Count - 1; i >= 0; i--)
            {
                var r = hist.PublishedRecords[i];
                if (_historyRarityFilter == "All" || string.Equals(r.rarity.ToString(), _historyRarityFilter, StringComparison.Ordinal))
                {
                    filtered.Add(r);
                }
            }

            int totalPages = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)HistoryPageSize));
            _historyPage = Mathf.Clamp(_historyPage, 0, totalPages - 1);

            var navRow = new VisualElement();
            navRow.AddToClassList("ge-row");
            navRow.Add(MakeTitle($"Synced Pull Records ({filtered.Count}) — Page {_historyPage + 1} / {totalPages}"));
            var prevBtn = new Button(() => { _historyPage = Mathf.Max(0, _historyPage - 1); SelectTab(2); }) { text = "Prev" };
            var nextBtn = new Button(() => { _historyPage = Mathf.Min(totalPages - 1, _historyPage + 1); SelectTab(2); }) { text = "Next" };
            prevBtn.SetEnabled(_historyPage > 0);
            nextBtn.SetEnabled(_historyPage < totalPages - 1);
            navRow.Add(prevBtn);
            navRow.Add(nextBtn);
            listCard.Add(navRow);

            if (filtered.Count == 0)
            {
                var empty = new Label(hist.PendingCount > 0
                    ? $"{hist.PendingCount} pull(s) are pending the 1-hour history refresh. Click 'Force Refresh Now (Dev)' to view them immediately."
                    : "No pull history recorded yet.");
                empty.AddToClassList("ge-muted");
                empty.style.whiteSpace = WhiteSpace.Normal;
                listCard.Add(empty);
            }
            else
            {
                int start = _historyPage * HistoryPageSize;
                int end = Mathf.Min(filtered.Count, start + HistoryPageSize);
                for (int i = start; i < end; i++)
                {
                    var rec = filtered[i];
                    var row = new VisualElement();
                    row.AddToClassList("ge-history-row");
                    if (rec.rarity == GachaRarity.Legendary) row.AddToClassList("ge-history-row--ssr");
                    else if (rec.rarity == GachaRarity.Epic) row.AddToClassList("ge-history-row--sr");

                    var lbl = new Label(
                        $"#{rec.pullIndex,-4} | {rec.bannerName} | {rec.itemName} [{rec.rarityLabel}] | Pity: {rec.pityAtPull} ({rec.triggerTag}) | {rec.pulledAtLocal}");
                    lbl.AddToClassList("ge-mono");
                    row.Add(lbl);
                    listCard.Add(row);
                }
            }

            _body.Add(listCard);
        }

        // ==================== TAB 4: MONTE CARLO AUDIT ====================
        private void BuildMonteCarloTab()
        {
            var banner = ActiveBanner;

            var card = new VisualElement();
            card.AddToClassList("ge-card");
            card.Add(MakeTitle($"Bulk Monte Carlo Audit ({banner.bannerName})"));

            var slider = new SliderInt("Simulated Pulls", 1000, 100000) { value = 10000, showInputField = true };
            card.Add(slider);

            card.Add(new Button(() =>
            {
                var pity = _roller.PityStore.GetOrCreate(banner.pityGroupId);
                _lastBulkReport = GachaRoller.RunBulkSimulation(banner, slider.value, pity.selectedFateItemId);
                SetStatus($"Simulated {_lastBulkReport.totalPulls:N0} pulls on '{banner.bannerName}'.");
                SelectTab(3);
            }) { text = "Run Monte Carlo Simulation" });

            if (_lastBulkReport != null)
            {
                var kpis = new VisualElement();
                kpis.AddToClassList("ge-wrap-row");
                kpis.style.marginTop = 8f;

                double legPct = (_lastBulkReport.legendaryCount / (double)_lastBulkReport.totalPulls) * 100d;
                double epicPct = (_lastBulkReport.epicCount / (double)_lastBulkReport.totalPulls) * 100d;

                AddKpi(kpis, $"{banner.GetRarityLabel(GachaRarity.Legendary)} Total", $"{_lastBulkReport.legendaryCount:N0} ({legPct:F2}%)");
                AddKpi(kpis, "Featured Top Tier", _lastBulkReport.featuredLegendaryCount.ToString("N0"));
                AddKpi(kpis, "Avg Pulls / Top Tier", _lastBulkReport.avgPullsPerLegendary.ToString("F1"));
                AddKpi(kpis, "Avg Pulls / Featured", _lastBulkReport.avgPullsPerFeaturedLegendary.ToString("F1"));
                AddKpi(kpis, $"{banner.GetRarityLabel(GachaRarity.Epic)} Total", $"{_lastBulkReport.epicCount:N0} ({epicPct:F2}%)");
                AddKpi(kpis, "Rate-Up Won / Lost", $"{_lastBulkReport.rateUpWins} / {_lastBulkReport.rateUpLosses}");
                AddKpi(kpis, "Guarantees / Fate Path", $"{_lastBulkReport.rateUpGuarantees} / {_lastBulkReport.fatePathTriggers}");
                AddKpi(kpis, "Dupe Currency Earned", _lastBulkReport.totalDuplicateRewards.ToString("N0"));
                card.Add(kpis);
            }

            _body.Add(card);
        }

        private void DoPull(int count)
        {
            var banner = ActiveBanner;
            _lastBatch = count == 1 ? _roller.RollSingle(banner) : _roller.RollMulti(banner, count);
            SetStatus($"Executed {_lastBatch.results.Count} pull(s) on '{banner.bannerName}'. Pending history: {_roller.History.PendingCount}.");
            SelectTab(0);
        }

        private void LoadSandboxPreset(Action<GachaBannerAsset> configure, string label)
        {
            _assignedBanner = null;
            EnsureSandboxBanner();
            configure(_sandboxBanner);
            _lastBatch = null;
            SetStatus($"Loaded sandbox preset: {label}");
            SelectTab(0);
        }

        private void ResetSimulatorState()
        {
            _roller.ResetAllState();
            _lastBatch = null;
            _lastBulkReport = null;
            _historyPage = 0;
            SetStatus("Reset pity counters, owned counts, and pull history.");
            SelectTab(_activeTab);
        }

        private void ExportHistoryJson()
        {
            string path = EditorUtility.SaveFilePanel("Export Pull History JSON", Application.dataPath, "gacha_pull_history", "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, _roller.History.ExportToJson(includePending: _includePendingInExport, prettyPrint: true));
            SetStatus($"Exported pull history JSON to {path}");
        }

        private void ExportHistoryCsv()
        {
            string path = EditorUtility.SaveFilePanel("Export Pull History CSV", Application.dataPath, "gacha_pull_history", "csv");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, _roller.History.ExportToCsv(includePending: _includePendingInExport));
            SetStatus($"Exported pull history CSV to {path}");
        }

        private void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        private static Label MakeTitle(string text)
        {
            var l = new Label(text);
            l.AddToClassList("ge-section-title");
            return l;
        }

        private static void AddKpi(VisualElement parent, string label, string value)
        {
            var box = new VisualElement();
            box.AddToClassList("ge-kpi");
            var l = new Label(label);
            l.AddToClassList("ge-kpi-label");
            var v = new Label(value);
            v.AddToClassList("ge-kpi-value");
            box.Add(l);
            box.Add(v);
            parent.Add(box);
        }

        private static VisualElement MakeProgressBar(float normalized, string fillClass)
        {
            var track = new VisualElement();
            track.AddToClassList("ge-bar-track");
            var fill = new VisualElement();
            fill.AddToClassList(fillClass);
            fill.style.width = Length.Percent(Mathf.Clamp01(normalized) * 100f);
            track.Add(fill);
            return track;
        }

        internal static void AttachStyles(VisualElement target)
        {
            const string pkgPath = "Packages/com.haruchanz64.gacha-engine/Editor/UI/GachaSimulatorStyles.uss";
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(pkgPath);
            if (sheet == null)
            {
                string[] guids = AssetDatabase.FindAssets("GachaSimulatorStyles t:StyleSheet");
                if (guids.Length > 0)
                {
                    sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }
            if (sheet != null) target.styleSheets.Add(sheet);
        }
    }
}
