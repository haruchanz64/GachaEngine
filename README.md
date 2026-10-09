# GachaEngine

Configurable banner, pity, and drop-rate simulation package for Unity 6.

GachaEngine separates pure C# probability math from Unity UI and persistence layers so teams can define custom rarity tiers, tune soft and hard pity thresholds, test drop curves with Monte Carlo audits, and export pull histories directly from the Editor or at runtime.

## Features

- **Customizable rarity tiers and pity thresholds:** Define your own tier names (`5★ / SSR`, `S-Rank`, `4★ / SR`, `A-Rank`), base drop rates, soft pity start pull, soft pity bonus per pull, hard pity pull count, and featured rate-up odds (`50/50`, `75/25`, or any custom ratio) per banner.
- **Four banner archetypes:** Standard, Limited Rate-Up with cross-banner carry-over guarantees by `pityGroupId`, Fate Path (target guarantee after `N` misses), and Step-Up ladders with per-step cost overrides and guaranteed rarity milestones.
- **UI Toolkit Banner Simulator (`Tools > GachaEngine > Banner Simulator`):**
  - **Banner & Pulls:** Live controls for editing top-tier and mid-tier labels, soft/hard pity, and featured rate-up percentage, with x1 and x10 test pulls and real-time pity progress bars.
  - **Banner Details:** Rate-up item and character showcase cards paired with the complete itemized pool, drop rates, pity rules, and Step-Up milestones.
  - **Pull History (1h Refresh):** Paginated and rarity-filtered history log with a 1-hour (`3600s`) refresh cycle, pending counter, countdown timer, manual refresh check, force-refresh button for Editor inspection, and one-click JSON and CSV export.
  - **Monte Carlo Audit:** Seeded bulk simulation (`1,000` to `100,000` pulls) reporting observed SSR and SR rates, average pulls per featured hit, rate-up win/loss splits, and duplicate shard totals.
- **Pull history JSON export with 1-hour refresh:** Pull records stay pending until the configured refresh window elapses (`TryRefresh`) or `ForceRefresh` runs. Exports include Banner Name, Item Name, rarity label, pity count, trigger tag, and both UTC and local pull timestamps.
- **Canvas and 3D scene presentation hooks:** Assign banner artwork, BGM, pull start SFX, cutscene prefabs, per-tier VFX/SFX/animations, and per-item splash art, 3D preview prefabs, and voice lines directly on `GachaBannerAsset`. Wire uGUI buttons and custom cutscenes through `GachaController` UnityEvents, `GachaRevealPresenter`, or `IGachaRevealSequence` with built-in skip support.
- **Duplicate resolution and overflow currency:** Tracks owned copy counts per item ID and awards standard duplicate shards or max-rank overflow tokens automatically.
- **Clean extension contracts:** Swap RNG, pity persistence, wallet integration, duplicate conversion, reveal cutscenes, or event rate modifiers through `IGachaRng`, `IPityStore`, `IGachaCurrencyWallet`, `IDuplicateResolver`, `IGachaRevealSequence`, and `IGachaPoolModifier`.

## Requirements

- Unity `6000.0` or newer

## Installation

### Unity Package Manager (Git URL)

1. Open **Window > Package Manager**.
2. Click **+** and choose **Install package from git URL...**.
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

### 1. Create a banner asset

Right-click in the Project window and select **Create > GachaEngine > Banner Asset**. Use the custom Inspector buttons (**Limited 50/50**, **Fate Path Weapon**, or **Step-Up Ladder**) to seed a starting template, then adjust the tier labels, soft/hard pity pull counts, rate-up percentages, and item pool.

Click **Open in Banner Simulator** in the Inspector (or open **Tools > GachaEngine > Banner Simulator**) to test pulls, inspect the Banner Details sheet, view Pull History, and run Monte Carlo simulations.

### 2. Roll from C# at runtime

```csharp
using System;
using GachaEngine.Data;
using GachaEngine.Engine;
using UnityEngine;

public class BannerDemo : MonoBehaviour
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
        // Publishes pending pulls once 3600 seconds have passed since the last refresh
        _roller.History.TryRefresh(DateTime.UtcNow);
        return _roller.History.ExportToJson(includePending: false, prettyPrint: true);
    }
}
```

You can also attach `GachaController` to a GameObject for an Inspector-wired `MonoBehaviour` wrapper with `OnPullCompleted` and `OnBatchCompleted` events.

### 3. Wire to a Canvas or 3D scene

1. Add `GachaController` and `GachaRevealPresenter` to a GameObject in your scene, then assign `GachaRevealPresenter` to `GachaController.revealSequenceBehaviour`.
2. Assign your banner visuals, audio clips, tier VFX prefabs, and item splash art or 3D preview prefabs on your `GachaBannerAsset`.
3. Connect uGUI `Button.onClick()` directly to `GachaController.PullSingleActiveBanner`, `PullTenActiveBanner`, `SelectBanner(int)`, and `SkipReveal`.
4. Bind `onPullStarted`, `onHighestRarityDetermined`, `onPullRevealed`, `onBatchFinished`, and `onPullFailed` in the Inspector to drive Canvas popups, Timeline playables, or 3D camera rigs.

For custom reveal timing or bespoke animation graphs, implement `IGachaRevealSequence` (see `Samples~/CanvasAndSceneWiring/SampleCanvasSceneReveal.cs`).

## Pull history JSON format

Calling `GachaHistoryLog.ExportToJson()` produces a `JsonUtility`-compatible document containing banner name, item name, rarity label, and UTC plus local timestamps:

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

## Extension contracts

| Interface | Purpose | Default implementation |
| :--- | :--- | :--- |
| `IGachaRng` | Random number source for pulls and weighted picks | `SeededGachaRng` |
| `IPityStore` | Reads and saves `GachaPityState` per `pityGroupId` | `InMemoryPityStore` (with JSON snapshot import/export) |
| `IDuplicateResolver` | Maps repeat pulls to shards or max-rank overflow currency | `DefaultDuplicateResolver` |
| `IGachaCurrencyWallet` | Validates and deducts banner currency, credits duplicate rewards | `InMemoryCurrencyWallet` |
| `IGachaRevealSequence` | Controls skippable pull intro cutscenes, VFX, audio, and item reveals | `GachaRevealPresenter` (see `Samples~/CanvasAndSceneWiring/SampleCanvasSceneReveal.cs`) |
| `IGachaPoolModifier` | Adjusts effective tier probabilities for events or starter boosts | See `Samples~/CustomRules/SampleEventRateBoostModifier.cs` |

## Testing

The package includes EditMode NUnit tests in `Tests/Editor/GachaRollerTests.cs` covering:

- Custom rarity labels and modified soft/hard pity pull counts for top and mid tiers
- 50/50 rate-up loss and guaranteed carry-over across shared `pityGroupId` banners
- Fate Path target guarantee after max misses and point reset
- Step-Up ladder discounts and guaranteed step milestones
- Duplicate shard conversion and max-rank overflow currency
- 1-hour history refresh window and JSON export payload verification
- `GachaController` Inspector `UnityEvent` flow, active banner selection, and error reporting
- Pool modifiers, pity store JSON round-trip, and 10,000-pull bulk simulation distribution

## License

MIT. See [LICENSE.md](LICENSE.md).
