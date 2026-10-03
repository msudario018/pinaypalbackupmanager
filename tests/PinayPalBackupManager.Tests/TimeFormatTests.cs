using PinayPalBackupManager.Services;
using Xunit;

namespace PinayPalBackupManager.Tests;

/// <summary>
/// Guards the 12-hour AM/PM convention. This failed in practice before: a call site
/// used "MM/dd hh:mm:ss" (12-hour, no AM/PM marker) so 3:30 PM rendered as "03:30",
/// and another used "yyyy-MM-dd hh:mm:sstt" with no space, rendering "03:45:22PM".
/// These assertions lock both of those failure modes out.
/// </summary>
public class TimeFormatTests
{
    private static readonly DateTime Afternoon = new(2026, 10, 3, 15, 45, 22);
    private static readonly DateTime Midnight = new(2026, 10, 3, 0, 0, 0);
    private static readonly DateTime Noon = new(2026, 10, 3, 12, 0, 0);
    private static readonly DateTime JustBeforeMidnight = new(2026, 10, 3, 23, 59, 59);

    [Fact]
    public void Clock_Uses12HourWithAmPmMarker()
    {
        Assert.Equal("3:45 PM", TimeFormat.Clock(Afternoon));
    }

    [Fact]
    public void Clock_MidnightIs12Am_Not0Am()
    {
        Assert.Equal("12:00 AM", TimeFormat.Clock(Midnight));
    }

    [Fact]
    public void Clock_NoonIs12Pm_Not0Pm()
    {
        Assert.Equal("12:00 PM", TimeFormat.Clock(Noon));
    }

    [Fact]
    public void Clock_ZeroPadsMinutes()
    {
        Assert.Equal("3:05 AM", TimeFormat.Clock(new DateTime(2026, 10, 3, 3, 5, 0)));
    }

    [Fact]
    public void Clock_LateEveningStaysPm()
    {
        Assert.Equal("11:59 PM", TimeFormat.Clock(JustBeforeMidnight));
    }

    [Fact]
    public void EveryUserFacingFormatCarriesASpacedAmPmMarker()
    {
        // The real regression: "hh:mm:sstt" (no space) renders "03:45:22PM", and
        // "MM/dd hh:mm:ss" (no tt at all) renders "03:45:22" with no indication of
        // AM vs PM. Both must be rejected, so require a digit, then whitespace,
        // then the marker, at the very end of the string.
        foreach (var s in new[]
                 {
                     TimeFormat.Stamp(Afternoon),
                     TimeFormat.StampSeconds(Afternoon),
                     TimeFormat.DateTimeShort(Afternoon),
                     TimeFormat.DateTimeShortSeconds(Afternoon),
                     TimeFormat.Compact(Afternoon),
                     TimeFormat.Clock(Afternoon),
                     TimeFormat.ClockSeconds(Afternoon),
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(s));
            Assert.Matches(@"\d\s(AM|PM)$", s);
        }
    }

    [Fact]
    public void ClockSeconds_KeepsSpaceBeforeMarker()
    {
        // Guards the specific "hh:mm:sstt" typo that shipped in MainWindow.
        var s = TimeFormat.ClockSeconds(Afternoon);
        Assert.Equal("3:45:22 PM", s);
        Assert.EndsWith(" PM", s);
    }

    [Fact]
    public void UserFacingOutputNeverContains24HourValues()
    {
        var morning = new DateTime(2026, 10, 3, 9, 5, 0);
        Assert.DoesNotContain("09", TimeFormat.Clock(morning));
        Assert.DoesNotContain("09", TimeFormat.Stamp(morning));
    }

    [Fact]
    public void Stamp_UsesFullMonthName()
    {
        Assert.Equal("Oct 3, 2026 3:45:22 PM", TimeFormat.StampSeconds(Afternoon));
    }

    [Fact]
    public void UtcStamp_Stays24HourForMachineReadableOutput()
    {
        Assert.Equal("2026-10-03 15:45:22", TimeFormat.UtcStamp(Afternoon));
    }

    [Fact]
    public void Nullables_ReturnFallback()
    {
        Assert.Equal("Not scheduled", TimeFormat.StampOr(null, "Not scheduled"));
        Assert.Equal("Never", TimeFormat.ClockOr(null, "Never"));
    }

    [Fact]
    public void Nullables_FormatWhenPresent()
    {
        Assert.Equal("Oct 3, 2026 3:45 PM", TimeFormat.StampOr(Afternoon, "Not scheduled"));
        Assert.Equal("3:45 PM", TimeFormat.ClockOr(Afternoon, "Never"));
    }

    [Fact]
    public void Formats_AreCultureIndependent()
    {
        // PinayPal targets PH users; a machine set to a locale that prefers 24-hour
        // must not silently change the dashboard output.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("3:45 PM", TimeFormat.Clock(Afternoon));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}