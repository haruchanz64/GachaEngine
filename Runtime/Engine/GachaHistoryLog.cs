using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GachaEngine.Data;
using UnityEngine;

namespace GachaEngine.Engine
{
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

        public int RefreshIntervalSeconds { get; set; } = 3600;
        public DateTime LastRefreshedUtc => _lastRefreshedUtc;
        public int PendingCount => _pending.Count;
        public IReadOnlyList<GachaHistoryRecord> PublishedRecords => _published;

        public GachaHistoryLog(int maxRecords = 2000, int refreshIntervalSeconds = 3600, DateTime? initialUtcNow = null)
        {
            _maxRecords = Math.Max(10, maxRecords);
            RefreshIntervalSeconds = Math.Max(0, refreshIntervalSeconds);
            _lastRefreshedUtc = initialUtcNow ?? DateTime.UtcNow;
            _hasRefreshedOnce = true;
        }

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
        /// Returns true if a refresh occurred.
        /// </summary>
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
        public void ForceRefresh(DateTime? utcNow = null)
        {
            FlushPending(utcNow ?? DateTime.UtcNow);
        }

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

        public void Clear(DateTime? utcNow = null)
        {
            _published.Clear();
            _pending.Clear();
            _lastRefreshedUtc = utcNow ?? DateTime.UtcNow;
        }

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

        private void AppendPublished(GachaHistoryRecord rec)
        {
            _published.Add(rec);
            if (_published.Count > _maxRecords)
            {
                _published.RemoveAt(0);
            }
        }

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

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Contains(",") || s.Contains("\"") ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }

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
