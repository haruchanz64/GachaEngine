using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GachaEngine.Data;
using UnityEngine;

namespace GachaEngine.Engine
{
    /// <summary>
    /// Serializable pull history row capturing banner identity, item identity, rarity label, pity count, trigger tag, and UTC/local timestamps.
    /// </summary>
    [Serializable]
    public sealed class GachaHistoryRecord
    {
        public int pullIndex;
        public string bannerId;
        public string bannerName;
        public string itemId;
        public string itemName;
        public string rarityLabel;
        public GachaRarity rarity;
        public bool isFeatured;
        public int pityAtPull;
        public string triggerTag;
        public string pulledAtUtc;
        public string pulledAtLocal;
    }

    /// <summary>
    /// Root JSON serialization envelope produced by <see cref="GachaHistoryLog.ExportToJson"/>.
    /// </summary>
    [Serializable]
    public sealed class GachaHistoryExportPayload
    {
        public string exportedUtc;
        public string lastRefreshedUtc;
        public int refreshIntervalSeconds;
        public int totalRecords;
        public List<GachaHistoryRecord> records = new List<GachaHistoryRecord>();
    }

    /// <summary>
    /// Stores pull history with a configurable refresh window (default 1 hour = 3600s) matching live gacha
    /// history feeds, plus JSON and CSV export containing Banner Name, Item Name, and pull Date/Time.
    /// </summary>
    public sealed class GachaHistoryLog
    {
        private readonly List<GachaHistoryRecord> _published = new List<GachaHistoryRecord>(256);
        private readonly List<GachaHistoryRecord> _pending = new List<GachaHistoryRecord>(64);
        private readonly int _maxRecords;
        private DateTime _lastRefreshedUtc;
        private bool _hasRefreshedOnce;

        /// <summary>
        /// Minimum interval in seconds before pending pull records flush into <see cref="PublishedRecords"/>. Set to <c>0</c> for immediate publishing.
        /// </summary>
        public int RefreshIntervalSeconds { get; set; } = 3600;

        /// <summary>
        /// UTC timestamp of the most recent history refresh or log initialization.
        /// </summary>
        public DateTime LastRefreshedUtc => _lastRefreshedUtc;

        /// <summary>
        /// Number of recorded pulls waiting for the next refresh cycle to be published.
        /// </summary>
        public int PendingCount => _pending.Count;

        /// <summary>
        /// Chronological list of published pull history records, capped at the configured maximum retention count.
        /// </summary>
        public IReadOnlyList<GachaHistoryRecord> PublishedRecords => _published;

        /// <summary>
        /// Initializes a pull history log with retention cap, refresh interval, and optional reference clock.
        /// </summary>
        /// <param name="maxRecords">Maximum published records retained in FIFO order (clamped to at least <c>10</c>).</param>
        /// <param name="refreshIntervalSeconds">Seconds between automatic pending flushes (<c>0</c> publishes immediately).</param>
        /// <param name="initialUtcNow">Optional starting UTC timestamp for deterministic testing; defaults to <see cref="DateTime.UtcNow"/>.</param>
        public GachaHistoryLog(int maxRecords = 2000, int refreshIntervalSeconds = 3600, DateTime? initialUtcNow = null)
        {
            _maxRecords = Math.Max(10, maxRecords);
            RefreshIntervalSeconds = Math.Max(0, refreshIntervalSeconds);
            _lastRefreshedUtc = initialUtcNow ?? DateTime.UtcNow;
            _hasRefreshedOnce = true;
        }

        /// <summary>
        /// Appends a <see cref="GachaPullResult"/> to pending history, or directly to <see cref="PublishedRecords"/> when <see cref="RefreshIntervalSeconds"/> is <c>0</c>.
        /// </summary>
        /// <param name="result">Completed pull result; ignored if <c>null</c> or missing an item.</param>
        public void Record(GachaPullResult result)
        {
            if (result == null || result.item == null) return;

            var entry = new GachaHistoryRecord
            {
                pullIndex = result.pullIndex,
                bannerId = result.bannerId,
                bannerName = result.bannerName,
                itemId = result.item.itemId,
                itemName = result.item.displayName,
                rarityLabel = result.rarityLabel,
                rarity = result.rarity,
                isFeatured = result.isFeatured,
                pityAtPull = result.pityAtPull,
                triggerTag = BuildTriggerTag(result),
                pulledAtUtc = result.pulledAtUtc,
                pulledAtLocal = result.pulledAtLocal
            };

            if (RefreshIntervalSeconds <= 0)
            {
                AppendPublished(entry);
            }
            else
            {
                _pending.Add(entry);
            }
        }

        /// <summary>
        /// Returns the remaining duration until <see cref="TryRefresh"/> will flush pending records, or <see cref="TimeSpan.Zero"/> if ready.
        /// </summary>
        /// <param name="utcNow">Optional UTC reference time; defaults to <see cref="DateTime.UtcNow"/>.</param>
        public TimeSpan GetTimeUntilNextRefresh(DateTime? utcNow = null)
        {
            if (RefreshIntervalSeconds <= 0) return TimeSpan.Zero;
            DateTime now = utcNow ?? DateTime.UtcNow;
            DateTime next = _lastRefreshedUtc.AddSeconds(RefreshIntervalSeconds);
            TimeSpan remaining = next - now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>
        /// Flushes pending pull records into the published history view once the refresh interval (default 1 hour) has elapsed.
        /// </summary>
        /// <param name="utcNow">Optional UTC reference time; defaults to <see cref="DateTime.UtcNow"/>.</param>
        /// <returns><c>true</c> if a refresh occurred; otherwise <c>false</c>.</returns>
        public bool TryRefresh(DateTime? utcNow = null)
        {
            DateTime now = utcNow ?? DateTime.UtcNow;
            if (_hasRefreshedOnce && RefreshIntervalSeconds > 0)
            {
                double elapsed = (now - _lastRefreshedUtc).TotalSeconds;
                if (elapsed < RefreshIntervalSeconds)
                {
                    return false;
                }
            }

            FlushPending(now);
            return true;
        }

        /// <summary>
        /// Immediately flushes all pending pulls into published history (useful for dev/Editor inspection).
        /// </summary>
        /// <param name="utcNow">Optional UTC timestamp recorded as <see cref="LastRefreshedUtc"/>; defaults to <see cref="DateTime.UtcNow"/>.</param>
        public void ForceRefresh(DateTime? utcNow = null)
        {
            FlushPending(utcNow ?? DateTime.UtcNow);
        }

        /// <summary>
        /// Runs <see cref="TryRefresh"/> and exports history records as a <see cref="JsonUtility"/>-compatible <see cref="GachaHistoryExportPayload"/> JSON string.
        /// </summary>
        /// <param name="includePending">When <c>true</c>, appends unrefreshed pending records after published records.</param>
        /// <param name="prettyPrint">When <c>true</c>, formats the JSON output with indentation.</param>
        /// <param name="utcNow">Optional UTC reference time for the refresh check and export timestamp.</param>
        public string ExportToJson(bool includePending = false, bool prettyPrint = true, DateTime? utcNow = null)
        {
            DateTime now = utcNow ?? DateTime.UtcNow;
            TryRefresh(now);

            var payload = new GachaHistoryExportPayload
            {
                exportedUtc = now.ToString("O", CultureInfo.InvariantCulture),
                lastRefreshedUtc = _lastRefreshedUtc.ToString("O", CultureInfo.InvariantCulture),
                refreshIntervalSeconds = RefreshIntervalSeconds
            };

            payload.records.AddRange(_published);
            if (includePending && _pending.Count > 0)
            {
                payload.records.AddRange(_pending);
            }
            payload.totalRecords = payload.records.Count;

            return JsonUtility.ToJson(payload, prettyPrint);
        }

        /// <summary>
        /// Runs <see cref="TryRefresh"/> and exports history records as an RFC 4180 CSV document with a header row.
        /// </summary>
        /// <param name="includePending">When <c>true</c>, appends unrefreshed pending records after published records.</param>
        /// <param name="utcNow">Optional UTC reference time for the refresh check.</param>
        public string ExportToCsv(bool includePending = false, DateTime? utcNow = null)
        {
            TryRefresh(utcNow);
            var sb = new StringBuilder(1024);
            sb.AppendLine("PullIndex,BannerName,ItemName,Rarity,Featured,PityAtPull,Trigger,PulledAtUtc,PulledAtLocal");

            AppendCsvRows(sb, _published);
            if (includePending)
            {
                AppendCsvRows(sb, _pending);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Removes all published and pending history records and resets <see cref="LastRefreshedUtc"/>.
        /// </summary>
        /// <param name="utcNow">Optional UTC timestamp recorded as the new <see cref="LastRefreshedUtc"/>.</param>
        public void Clear(DateTime? utcNow = null)
        {
            _published.Clear();
            _pending.Clear();
            _lastRefreshedUtc = utcNow ?? DateTime.UtcNow;
        }

        /// <summary>
        /// Moves all queued pending records into <see cref="PublishedRecords"/> and updates the refresh timestamp.
        /// </summary>
        private void FlushPending(DateTime now)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                AppendPublished(_pending[i]);
            }
            _pending.Clear();
            _lastRefreshedUtc = now;
            _hasRefreshedOnce = true;
        }

        /// <summary>
        /// Appends a record to <c>_published</c> and evicts the oldest entry when exceeding <c>_maxRecords</c>.
        /// </summary>
        private void AppendPublished(GachaHistoryRecord rec)
        {
            _published.Add(rec);
            if (_published.Count > _maxRecords)
            {
                _published.RemoveAt(0);
            }
        }

        /// <summary>
        /// Formats each record in <paramref name="list"/> as an invariant-culture CSV row.
        /// </summary>
        private static void AppendCsvRows(StringBuilder sb, List<GachaHistoryRecord> list)
        {
            var ic = CultureInfo.InvariantCulture;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                sb.Append(r.pullIndex.ToString(ic)).Append(',')
                  .Append(EscapeCsv(r.bannerName)).Append(',')
                  .Append(EscapeCsv(r.itemName)).Append(',')
                  .Append(EscapeCsv(r.rarityLabel)).Append(',')
                  .Append(r.isFeatured ? "true" : "false").Append(',')
                  .Append(r.pityAtPull.ToString(ic)).Append(',')
                  .Append(EscapeCsv(r.triggerTag)).Append(',')
                  .Append(EscapeCsv(r.pulledAtUtc)).Append(',')
                  .AppendLine(EscapeCsv(r.pulledAtLocal));
            }
        }

        /// <summary>
        /// Quotes and escapes CSV fields containing commas or double quotes.
        /// </summary>
        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Contains(",") || s.Contains("\"") ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }

        /// <summary>
        /// Produces a compact diagnostic tag summarizing which pity, guarantee, or rate-up rule fired on <paramref name="r"/>.
        /// </summary>
        private static string BuildTriggerTag(GachaPullResult r)
        {
            if (r.triggeredFatePath) return "FATE_PATH";
            if (r.triggeredStepGuarantee) return "STEP_GUARANTEE";
            if (r.triggeredHardPity) return r.triggeredRateUpGuarantee ? "HARD_PITY+GUARANTEED" : "HARD_PITY";
            if (r.triggeredSoftPity) return r.triggeredRateUpGuarantee ? "SOFT_PITY+GUARANTEED" : "SOFT_PITY";
            if (r.triggeredRateUpGuarantee) return "GUARANTEED";
            if (r.wonRateUpFlip) return "RATE_UP_WON";
            if (r.lostRateUpFlip) return "RATE_UP_LOST";
            return "STANDARD";
        }
    }
}
