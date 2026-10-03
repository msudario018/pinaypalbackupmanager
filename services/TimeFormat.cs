using System;

namespace PinayPalBackupManager.Services;

/// <summary>
/// Central time-formatting helpers.
///
/// Every timestamp a human reads goes through this class so the 12-hour AM/PM
/// convention stays consistent across the desktop app, the web dashboard, the
/// AI assistant and the API. Previously each call site interpolated its own
/// format string, which is how <c>MM/dd hh:mm:ss</c> (12-hour, but with no
/// AM/PM marker, so 3:30 PM rendered as "03:30") ended up sitting next to
/// 24-hour values in the same field.
///
/// The two groups are deliberately separated:
///   * User-facing  -> 12-hour with an explicit AM/PM marker.
///   * Machine/diagnostic -> 24-hour, and only when the UTC offset is stated
///     alongside it (log files, CSV exports, email templates).
/// </summary>
public static class TimeFormat
{
    // ── User-facing (12-hour, always carries AM/PM) ──────────────────────────
    public const string Stamp12h = "MMM d, yyyy h:mm tt";
    public const string StampSeconds12h = "MMM d, yyyy h:mm:ss tt";
    public const string DateTime12h = "MM/dd h:mm tt";
    public const string DateTimeShortSeconds12h = "MM/dd h:mm:ss tt";
    public const string DateTimeShort12h = "MMM d h:mm tt";
    public const string Clock12h = "h:mm tt";
    public const string Clock12hSeconds = "h:mm:ss tt";

    // ── Machine-readable (24-hour; only use with an explicit UTC marker) ─────
    public const string UtcStamp24h = "yyyy-MM-dd HH:mm:ss";

    /// <summary>Full user-facing timestamp, e.g. "Oct 3, 2026 3:45 PM".</summary>
    public static string Stamp(DateTime value) => value.ToString(Stamp12h);

    /// <summary>Full user-facing timestamp with seconds, e.g. "Oct 3, 2026 3:45:22 PM".</summary>
    public static string StampSeconds(DateTime value) => value.ToString(StampSeconds12h);

    /// <summary>Numeric user-facing timestamp, e.g. "10/03 3:45 PM".</summary>
    public static string DateTimeShort(DateTime value) => value.ToString(DateTime12h);

    /// <summary>Compact user-facing timestamp, e.g. "Oct 3 3:45 PM".</summary>
    public static string Compact(DateTime value) => value.ToString(DateTimeShort12h);

    /// <summary>User-facing clock, e.g. "3:45 PM".</summary>
    public static string Clock(DateTime value) => value.ToString(Clock12h);

    /// <summary>User-facing clock with seconds, e.g. "3:45:22 PM".</summary>
    public static string ClockSeconds(DateTime value) => value.ToString(Clock12hSeconds);

    /// <summary>Numeric timestamp with seconds, e.g. "10/03 3:45:22 PM".</summary>
    public static string DateTimeShortSeconds(DateTime value) => value.ToString(DateTimeShortSeconds12h);

    /// <summary>Nullable-friendly variants; return "Not scheduled"/fallback when null.</summary>
    public static string StampOr(DateTime? value, string fallback) =>
        value.HasValue ? Stamp(value.Value) : fallback;

    public static string ClockOr(DateTime? value, string fallback) =>
        value.HasValue ? Clock(value.Value) : fallback;

    /// <summary>
    /// Machine-readable UTC stamp for log files and exports. Always render the
    /// result with a "UTC" suffix at the call site so the 24-hour format is unambiguous.
    /// </summary>
    public static string UtcStamp(DateTime value) => value.ToString(UtcStamp24h);
}