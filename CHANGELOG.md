# Changelog

All notable changes to this package are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2026-11-24

### Changed

- Expanded `README.md` into a full developer guide covering the per-pull resolution pipeline, C# and custom extension code examples, core API reference table, included samples, test coverage, and troubleshooting notes.
- Added XML documentation comments across all public types, methods, constructors, properties, events, and non-trivial internal/private helpers in `Runtime/`, `Editor/`, and `Samples~/`.

## [1.0.0] - 2026-10-09

### Added

- `GachaBannerAsset` ScriptableObject with customizable rarity tier labels, base drop rates, editable soft and hard pity pull counts, featured rate-up odds, shared `pityGroupId` support, presentation media slots (banner artwork, logo, BGM, pull SFX, cutscene prefabs, per-tier VFX/SFX/animations, per-item splash art, 3D preview prefabs, and voice lines), and starter presets (`ApplyLimitedCharacterPreset`, `ApplyFatePathWeaponPreset`, `ApplyStepUpPreset`).
- `GachaRoller` pure C# rolling engine supporting Standard, Limited Rate-Up with 50/50 or custom carry-over, Fate Path guarantees, Step-Up ladders, duplicate conversion with max-rank overflow, `IGachaPoolModifier` hooks, and seeded Monte Carlo bulk simulation (`RunBulkSimulation`).
- `GachaHistoryLog` with a configurable 1-hour (`3600s`) refresh cycle (`TryRefresh`, `ForceRefresh`, `GetTimeUntilNextRefresh`) and JSON/CSV exporters capturing banner name, item name, rarity label, pity count, trigger tag, and UTC/local pull timestamps.
- `GachaController` MonoBehaviour wrapper with active banner helpers (`PullSingleActiveBanner`, `PullTenActiveBanner`, `SkipReveal`), Inspector-bindable `UnityEvent` hooks (`onPullStarted`, `onHighestRarityDetermined`, `onPullRevealed`, `onBatchFinished`, `onPullFailed`), and C# events.
- `IGachaRevealSequence` and drop-in `GachaRevealPresenter` for skippable 2D Canvas or 3D Scene reveal cutscenes, VFX spawning, AudioSource playback, and optional Animator triggers.
- UI Toolkit `GachaSimulatorWindow` (`Tools > GachaEngine > Banner Simulator`) with four tabs: Banner & Pulls (live editable pity/rate-up rules), Banner Details (rate-up character/item showcase, media summary, and full pool breakdown), Pull History (1-hour refresh controls, rarity filters, pagination, JSON/CSV export), and Monte Carlo Audit.
- UI Toolkit `GachaBannerInspector` with one-click preset templates and simulator launcher.
- Custom rule and Canvas/Scene wiring samples in `Samples~/CustomRules` and `Samples~/CanvasAndSceneWiring`.
- EditMode NUnit test suite in `Tests/Editor/GachaRollerTests.cs`.
