# GachaEngine

Configurable banner, pity, and drop-rate framework for Unity 6. It separates pure C# probability math from Unity UI and persistence layers so teams can define custom rarity tiers, tune soft and hard pity curves, validate drop distributions with seeded Monte Carlo audits, and wire reveal cutscenes to uGUI Canvas or 3D scenes.

## Features

- **Customizable rarity tiers and pity curves (`GachaBannerAsset`, `RarityTierConfig`):** Configure tier display labels (`5-star / SSR`, `S-Rank`, `4-star / SR`, `A-Rank`), base drop rates, soft pity start pull, linear soft pity bonus per pull, hard pity pull count, and featured rate-up odds (`50/50`, `75/25`, or any custom ratio) per banner.
- **Four banner archetypes (`GachaBannerType`):**
  - `Standard` for permanent pools without Step-Up or Fate Path overrides.
  - `LimitedRateUp` with featured odds and cross-banner carry-over guarantees shared by `pityGroupId`.
  - `FatePath` guaranteeing a player-selected target item after `maxFatePoints` misses on `targetRarity`.
  - `StepUp` ladders with per-step pull counts, cost overrides, and guaranteed rarity or featured milestones on the final pull of a step.
- **Deterministic pure C# roller (`GachaRoller`):** Runs headless in runtime code or EditMode tests with swappable RNG (`IGachaRng`), pity storage (`IPityStore`), duplicate conversion (`IDuplicateResolver`), currency wallet (`IGachaCurrencyWallet`), custom probability modifiers (`IGachaPoolModifier`), and seeded Monte Carlo auditing (`GachaRoller.RunBulkSimulation`).
- **Pull history with 1-hour refresh window (`GachaHistoryLog`):** Queues newly rolled records as pending until the configured refresh interval (`3600s` by default) elapses (`TryRefresh`) or `ForceRefresh` runs. Exports `JsonUtility`-compatible JSON and CSV containing banner name, item name, rarity label, pity count, trigger tag, and UTC plus local timestamps.
- **Canvas and 3D scene presentation hooks (`GachaController`, `GachaRevealPresenter`):** Assign banner artwork, logo, BGM, pull SFX, cutscene prefabs, per-tier VFX/SFX/animations, and per-item splash art, 3D preview prefabs, and voice lines on `GachaBannerAsset`. Wire uGUI buttons and custom cutscenes through `GachaController` UnityEvents, `GachaRevealPresenter`, or custom `IGachaRevealSequence` implementations with built-in skip support.
- **Duplicate resolution and overflow currency (`DefaultDuplicateResolver`):** Tracks owned copy counts per item ID and credits standard duplicate shards or max-rank overflow tokens to the configured wallet automatically.
- **UI Toolkit Banner Simulator and custom Inspector:** Includes `GachaBannerInspector` (with one-click **Limited 50/50**, **Fate Path**, and **Step-Up** presets) and `GachaSimulatorWindow` (`Tools > HaruChanZ64 > Gameplay > GachaEngine > Banner Simulator`) with four tabs: **Banner & Pulls**, **Banner Details**, **Pull History (1h Refresh)**, and **Monte Carlo Audit**.

## Requirements

- Unity `6000.0` or newer
- Zero required external dependencies

## Installation

### Unity Package Manager (Git URL)

1. Open **Window > Package Manager** in Unity 6 (`6000.0+`).
2. Click **+** (top-left) and choose **Install package from git URL...**.
3. Enter:

```text
https://github.com/haruchanz64/GachaEngine.git
```

### Local package

Add the package path to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.haruchanz64.gacha-engine": "file:../Plugins/GachaEngine"
  }
}
```

## Quick start

1. Create a banner asset via **Assets > Create > GachaEngine > Banner Asset**.
2. In the Inspector, click **Limited 50/50**, **Fate Path**, or **Step-Up** to populate a starting template, then adjust tier labels, soft/hard pity thresholds, featured odds, currency costs, and the item pool.
3. Click **Open in Banner Simulator** in the Inspector (or open **Tools > HaruChanZ64 > Gameplay > GachaEngine > Banner Simulator**) to run x1/x10 test pulls, inspect the Banner Details sheet, test the 1-hour Pull History refresh and JSON/CSV export, and run Monte Carlo audits.
4. For scene integration, add `GachaController` and `GachaRevealPresenter` to a GameObject, assign your banner list to `GachaController`, and assign `GachaRevealPresenter` to `revealSequenceBehaviour`.
5. Bind uGUI `Button.onClick()` directly to `GachaController.PullSingleActiveBanner`, `PullTenActiveBanner`, `SelectBanner(int)`, and `SkipReveal`, and connect `onPullStarted`, `onHighestRarityDetermined`, `onPullRevealed`, `onBatchFinished`, and `onPullFailed` to your UI or Timeline hooks.

## Core concepts

### Per-pull resolution pipeline

Each call to `GachaRoller.RollSingle` or `GachaRoller.RollMulti` loads the `GachaPityState` for `banner.pityGroupId`, applies Step-Up step count and cost overrides if applicable, optionally charges `IGachaCurrencyWallet`, and executes each pull in four stages:

1. **Counter increment:** Increments `pity.totalPulls` and increments `pullsSinceHit` on every tier with `enablePity = true`.
2. **Rarity selection (highest to lowest `GachaRarity`):**
   - If the pull is the final pull of a Step-Up step with `guaranteeMinimumRarity = true`, that tier is selected immediately.
   - Otherwise, tiers are checked from highest rarity to lowest: if `pullsSinceHit >= hardPityPull`, hard pity triggers that tier; if `pullsSinceHit >= softPityStartPull`, linear soft pity adds `((pullsSinceHit - softPityStartPull) + 1) * softPityBonusPerPull` to `baseProbability`; registered `IGachaPoolModifier.ModifyTierRate(...)` hooks run and the result is clamped to `[0, 1]`. The cumulative probability is tested against a single `IGachaRng.NextDouble()` roll, falling back to the lowest tier.
   - `pullsSinceHit` resets to `0` for the selected tier.
3. **Item selection within the chosen rarity:**
   - **Fate Path:** If `banner.fatePath.enabled`, `targetRarity == rarity`, a target is selected via `SetFateTarget`, and `fatePoints >= maxFatePoints`, awards the selected item and resets `fatePoints` and `guaranteedFeaturedNext`.
   - **Step-Up featured guarantee:** If the active step sets `guaranteeFeatured = true` and featured items exist, picks weighted from the featured pool.
   - **Featured rate-up and carry-over:** If `tier.hasFeaturedRateUp` and featured items exist, consumes `guaranteedFeaturedNext` when set; otherwise rolls against `featuredRateUpChance`. Winning picks from the featured pool; losing picks from the standard pool and sets `guaranteedFeaturedNext = true` when `guaranteeFeaturedOnNextMiss` is enabled.
   - **Weighted fallback:** Picks from all items in the chosen rarity weighted by `GachaItemEntry.weight` (minimum `0.01`). Updates `fatePoints` afterward when Fate Path applies.
4. **Duplicate resolution and logging:** Calls `IDuplicateResolver.Resolve(item, ownedBefore)`, updates owned count, credits duplicate or overflow currency to `IGachaCurrencyWallet` when assigned, appends a `GachaHistoryRecord` to `GachaHistoryLog`, and raises `OnPullCompleted` and `OnBatchCompleted`.

### Pull history refresh window and JSON format

`GachaHistoryLog` holds new records in `PendingCount` until `RefreshIntervalSeconds` (default `3600` seconds) has elapsed since `LastRefreshedUtc`, mirroring live-service wish/warp history feeds. Calling `TryRefresh()` flushes pending records once the window expires; `ForceRefresh()` flushes immediately; setting `refreshIntervalSeconds: 0` publishes every pull immediately.

Calling `GachaHistoryLog.ExportToJson()` produces a `JsonUtility`-compatible `GachaHistoryExportPayload`:

```json
{
  "exportedUtc": "2026-11-24T13:00:00.0000000Z",
  "lastRefreshedUtc": "2026-11-24T13:00:00.0000000Z",
  "refreshIntervalSeconds": 3600,
  "totalRecords": 1,
  "records": [
    {
      "pullIndex": 76,
      "bannerId": "limited_char_01",
      "bannerName": "Radiant Horizon",
      "itemId": "ssr_kagura",
      "itemName": "Kagura, Dawnblade",
      "rarityLabel": "5★ / SSR",
      "rarity": 3,
      "isFeatured": true,
      "pityAtPull": 76,
      "triggerTag": "SOFT_PITY",
      "pulledAtUtc": "2026-11-24T12:05:00.0000000Z",
      "pulledAtLocal": "2026-11-24 20:05:00"
    }
  ]
}
```

## Code examples

### 1. Roll from C# with wallet charging and 1-hour history export

```csharp
using System;
using GachaEngine.Data;
using GachaEngine.Engine;
using UnityEngine;

public sealed class BannerDemo : MonoBehaviour
{
    [SerializeField] private GachaBannerAsset limitedBanner;

    private GachaRoller _roller;

    private void Awake()
    {
        var wallet = new InMemoryCurrencyWallet();
        wallet.Add(limitedBanner.currencyId, 16000);

        // 1-hour (3600s) refresh cycle for published pull history
        var history = new GachaHistoryLog(maxRecords: 2000, refreshIntervalSeconds: 3600);
        _roller = new GachaRoller(wallet: wallet, history: history);
    }

    public void PullTen()
    {
        GachaPullBatchResult batch = _roller.RollMulti(limitedBanner, count: 10, chargeWallet: true);
        if (!batch.succeeded)
        {
            Debug.LogWarning(batch.errorMessage);
            return;
        }

        foreach (GachaPullResult pull in batch.results)
        {
            Debug.Log($"[{pull.rarityLabel}] {pull.item.displayName} (Pity {pull.pityAtPull})");
        }
    }

    public string ExportPublishedHistoryJson()
    {
        _roller.History.TryRefresh(DateTime.UtcNow);
        return _roller.History.ExportToJson(includePending: false, prettyPrint: true);
    }
}
```

### 2. Write a custom `IGachaPoolModifier`

Implement `IGachaPoolModifier` and attach it with `roller.AddModifier(...)` to adjust tier probabilities dynamically during events or beginner promotions:

```csharp
using System;
using GachaEngine.Core;
using GachaEngine.Data;

public sealed class WeekendLegendaryBoost : IGachaPoolModifier
{
    public string ModifierId => "weekend_legendary_boost";
    public bool IsActive { get; set; } = true;

    public void ModifyTierRate(
        GachaBannerAsset banner,
        RarityTierConfig tier,
        ref double effectiveProbability,
        GachaPityState pityState)
    {
        if (!IsActive || tier == null || tier.rarity != GachaRarity.Legendary) return;
        effectiveProbability = Math.Min(1.0d, effectiveProbability * 1.5d);
    }
}
```

### 3. Persist pity state and run a seeded Monte Carlo audit

Use `InMemoryPityStore.ExportJson` / `ImportJson` to persist pity across sessions, and call `GachaRoller.RunBulkSimulation` to validate tuning changes:

```csharp
using GachaEngine.Data;
using GachaEngine.Engine;
using UnityEngine;

public static class BannerAuditUtility
{
    public static void SaveAndAudit(GachaBannerAsset banner, InMemoryPityStore pityStore)
    {
        string pityJson = pityStore.ExportJson(prettyPrint: false);
        PlayerPrefs.SetString("gacha_pity_snapshot", pityJson);

        GachaBulkSimulationReport report = GachaRoller.RunBulkSimulation(
            banner,
            pullCount: 10000,
            selectedFateItemId: null,
            seed: 1337);

        Debug.Log(
            $"{report.bannerName}: {report.legendaryCount}/{report.totalPulls} top-tier " +
            $"(avg {report.avgPullsPerLegendary:F1} pulls/hit, " +
            $"rate-up W/L {report.rateUpWins}/{report.rateUpLosses})");
    }
}
```

### 4. Implement a custom `IGachaRevealSequence`

Implement `IGachaRevealSequence` on a `MonoBehaviour` and assign it to `GachaController.revealSequenceBehaviour` (or call `controller.SetRevealSequence(...)`) to drive custom Timelines, Canvas transitions, or 3D camera choreography:

```csharp
using System;
using System.Collections;
using GachaEngine.Data;
using GachaEngine.Presentation;
using UnityEngine;

public sealed class MinimalRevealSequence : MonoBehaviour, IGachaRevealSequence
{
    public IEnumerator PlayBatchIntro(
        GachaBannerAsset banner,
        GachaPullBatchResult batch,
        GachaRarity highestRarity,
        Func<bool> isSkipRequested)
    {
        float t = 0f;
        while (t < 0.75f && !isSkipRequested())
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    public IEnumerator PlayItemReveal(
        GachaBannerAsset banner,
        GachaPullResult result,
        Func<bool> isSkipRequested)
    {
        Debug.Log($"Revealing {result.item.displayName} ({result.rarityLabel})");
        float t = 0f;
        while (t < 0.4f && !isSkipRequested())
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    public void CleanupPresentation()
    {
        // Destroy temporary VFX or reset camera rig here.
    }
}
```

## Extension contracts and core API

| Type | Namespace | Purpose | Built-in implementation |
| :--- | :--- | :--- | :--- |
| `IGachaRng` | `GachaEngine.Core` | Random number source for tier rolls and weighted item picks | `SeededGachaRng` |
| `IPityStore` | `GachaEngine.Core` | Reads and saves `GachaPityState` keyed by `pityGroupId` | `InMemoryPityStore` (with JSON export/import) |
| `IDuplicateResolver` | `GachaEngine.Core` | Maps repeat item pulls to duplicate shards or max-rank overflow currency | `DefaultDuplicateResolver` |
| `IGachaCurrencyWallet` | `GachaEngine.Core` | Validates and deducts banner currency and credits duplicate payouts | `InMemoryCurrencyWallet` |
| `IGachaPoolModifier` | `GachaEngine.Core` | Adjusts effective tier drop probabilities during rarity resolution | See `Samples~/CustomRules` |
| `IGachaRevealSequence` | `GachaEngine.Presentation` | Coroutine hook for skippable batch intro cutscenes, VFX, audio, and item reveals | `GachaRevealPresenter` |
| `GachaBannerAsset` | `GachaEngine.Data` | ScriptableObject defining banner metadata, media slots, tiers, Fate Path, Step-Up, and item pool | `ApplyLimitedCharacterPreset`, `ApplyFatePathWeaponPreset`, `ApplyStepUpPreset` |
| `GachaRoller` | `GachaEngine.Engine` | Pure C# rolling engine and static Monte Carlo simulator (`RunBulkSimulation`) | `GachaRoller` |
| `GachaHistoryLog` | `GachaEngine.Engine` | Pull history store with configurable refresh window and JSON/CSV export | `GachaHistoryLog` |
| `GachaController` | `GachaEngine.Engine` | Drop-in `MonoBehaviour` hosting `GachaRoller`, banner switching, `UnityEvent` hooks, and reveal playback | `GachaController` |

## Included samples

Import samples from the Package Manager **Samples** tab:

- **Custom Pool Modifier Sample** (`Samples~/CustomRules`):
  - `SampleEventRateBoostModifier`: implements `IGachaPoolModifier` to multiply a target rarity's effective drop rate during an active event or for the first `N` pulls on a banner.
- **Canvas & Scene Wiring Sample** (`Samples~/CanvasAndSceneWiring`):
  - `SampleCanvasSceneReveal`: pairs `GachaController` with a custom `IGachaRevealSequence` that plays pull start SFX, spawns highest-rarity intro VFX, plays per-item voice lines, supports skip, and logs reveal callbacks.

## Testing

The package includes EditMode NUnit tests under `Tests/Editor/` runnable from **Window > General > Test Runner** (EditMode tab):

- `Tests/Editor/GachaRollerTests.cs`: verifies custom rarity labels and edited soft/hard pity thresholds, 50/50 rate-up loss and guaranteed carry-over across shared `pityGroupId` banners, Fate Path target guarantee and point reset, Step-Up discounts and guaranteed step milestones, duplicate shards and max-rank overflow currency, 1-hour history refresh window and JSON export schema, `IGachaPoolModifier` hooks and `InMemoryPityStore` JSON round-trip, 10,000-pull seeded Monte Carlo distribution, `GachaController` `UnityEvent` ordering and active banner helpers, and default preset tier colors with null-banner error handling.

## Troubleshooting and limitations

- **Pull history looks empty right after rolling:** `GachaHistoryLog` defaults to `refreshIntervalSeconds = 3600` (1 hour), so new pulls remain in `PendingCount` until `TryRefresh()` runs after the window elapses. Pass `refreshIntervalSeconds: 0` for instant publishing, call `ForceRefresh()` during development, or pass `includePending: true` to `ExportToJson`/`ExportToCsv`.
- **`GachaController` allows pulls even with zero currency:** `enforceWalletBalance` defaults to `false` on `GachaController`, and `GachaRoller.RollSingle`/`RollMulti` default to `chargeWallet: false`. Enable `enforceWalletBalance` in the Inspector (or pass `chargeWallet: true`) to enforce wallet balances.
- **Step-Up banners override requested pull count and cost:** When `bannerType` is `GachaBannerType.StepUp` and `stepUpSteps` is non-empty, both `RollSingle` and `RollMulti` execute the current step's `pullCount` and charge `costOverride`, advancing `currentStepIndex` cyclically across steps.
- **Changing Fate Path target resets Fate Points:** Calling `GachaRoller.SetFateTarget(banner, newItemId)` with a different item ID immediately resets `fatePoints` to `0` for that banner's `pityGroupId`.
- **Reveal sequences do not animate in EditMode or on disabled components:** `GachaController` starts the `IGachaRevealSequence` coroutine only when `Application.isPlaying && isActiveAndEnabled`; otherwise `onPullRevealed` and `onBatchFinished` fire synchronously immediately after the roll completes.
- **Owned item counts are not included in `InMemoryPityStore` snapshots:** `InMemoryPityStore.ExportJson()` persists pity counters, Fate Path progress, and Step-Up index, while `GachaRoller` tracks owned copy counts in memory. To persist duplicate counts across sessions, provide a custom `IDuplicateResolver` backed by your inventory save data.
- **Keep tier base probabilities summing to `1.0`:** `GachaRoller` evaluates tiers from highest `GachaRarity` to lowest against a cumulative probability sum and falls back to the lowest tier when the roll exceeds the sum. Leaving higher-tier probabilities above `1.0` can starve lower tiers.

## License

[MIT](LICENSE.md)
