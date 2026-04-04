using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PinayPalBackupManager.Services
{
    public static class HolidayModeService
    {
        private static readonly string HolidayFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinayPalBackupManager", "holidays.json");

        public class HolidayPeriod
        {
            public string Name { get; set; } = "";
            public DateTime StartDate { get; set; } = DateTime.Now;
            public DateTime EndDate { get; set; } = DateTime.Now;
            public bool IsActive { get; set; } = true;
            public string Reason { get; set; } = "";
            public bool RecurringYearly { get; set; } = false;
        }

        public class HolidayStatus
        {
            public bool IsHolidayModeActive { get; set; }
            public HolidayPeriod CurrentHoliday { get; set; } = null!;
            public List<HolidayPeriod> UpcomingHolidays { get; set; } = new();
            public List<HolidayPeriod> AllHolidays { get; set; } = new();
            public string NextHolidayName { get; set; } = "";
            public DateTime? NextHolidayStart { get; set; }
            public TimeSpan? TimeUntilNextHoliday { get; set; }
        }

        public static HolidayStatus GetHolidayStatus()
        {
            var holidays = LoadHolidays();
            var now = DateTime.Now;
            var status = new HolidayStatus
            {
                AllHolidays = holidays,
                UpcomingHolidays = holidays.Where(h => h.IsActive && h.StartDate > now).OrderBy(h => h.StartDate).ToList()
            };

            // Check if currently in holiday mode
            var currentHoliday = holidays.FirstOrDefault(h => h.IsActive && IsDateInRange(now, h));
            if (currentHoliday != null)
            {
                status.IsHolidayModeActive = true;
                status.CurrentHoliday = currentHoliday;
            }

            // Get next holiday
            var nextHoliday = status.UpcomingHolidays.FirstOrDefault();
            if (nextHoliday != null)
            {
                status.NextHolidayName = nextHoliday.Name;
                status.NextHolidayStart = nextHoliday.StartDate;
                status.TimeUntilNextHoliday = nextHoliday.StartDate - now;
            }

            return status;
        }

        public static bool ShouldPauseBackups()
        {
            var status = GetHolidayStatus();
            return status.IsHolidayModeActive;
        }

        public static async Task AddHolidayAsync(HolidayPeriod holiday)
        {
            var holidays = LoadHolidays();
            
            // Check for overlaps
            var overlapping = holidays.FirstOrDefault(h => h.IsActive && 
                (IsDateInRange(holiday.StartDate, h) || IsDateInRange(holiday.EndDate, h) ||
                IsDateInRange(h.StartDate, holiday) || IsDateInRange(h.EndDate, holiday)));
            
            if (overlapping != null)
            {
                throw new InvalidOperationException($"Holiday period overlaps with existing holiday: {overlapping.Name}");
            }

            holidays.Add(holiday);
            await SaveHolidaysAsync(holidays);
            
            LogService.WriteLiveLog($"[HOLIDAY] Added holiday period: {holiday.Name} ({holiday.StartDate:yyyy-MM-dd} to {holiday.EndDate:yyyy-MM-dd})", "", "Information", "SYSTEM");
        }

        public static async Task UpdateHolidayAsync(HolidayPeriod holiday)
        {
            var holidays = LoadHolidays();
            var index = holidays.FindIndex(h => h.Name.Equals(holiday.Name, StringComparison.OrdinalIgnoreCase));
            
            if (index >= 0)
            {
                holidays[index] = holiday;
                await SaveHolidaysAsync(holidays);
                LogService.WriteLiveLog($"[HOLIDAY] Updated holiday period: {holiday.Name}", "", "Information", "SYSTEM");
            }
            else
            {
                throw new ArgumentException($"Holiday not found: {holiday.Name}");
            }
        }

        public static async Task DeleteHolidayAsync(string holidayName)
        {
            var holidays = LoadHolidays();
            var removed = holidays.RemoveAll(h => h.Name.Equals(holidayName, StringComparison.OrdinalIgnoreCase));
            
            if (removed > 0)
            {
                await SaveHolidaysAsync(holidays);
                LogService.WriteLiveLog($"[HOLIDAY] Deleted holiday period: {holidayName}", "", "Information", "SYSTEM");
            }
            else
            {
                throw new ArgumentException($"Holiday not found: {holidayName}");
            }
        }

        public static async Task ToggleHolidayAsync(string holidayName, bool isActive)
        {
            var holidays = LoadHolidays();
            var holiday = holidays.FirstOrDefault(h => h.Name.Equals(holidayName, StringComparison.OrdinalIgnoreCase));
            
            if (holiday != null)
            {
                holiday.IsActive = isActive;
                await SaveHolidaysAsync(holidays);
                LogService.WriteLiveLog($"[HOLIDAY] {(isActive ? "Activated" : "Deactivated")} holiday period: {holidayName}", "", "Information", "SYSTEM");
            }
            else
            {
                throw new ArgumentException($"Holiday not found: {holidayName}");
            }
        }

        public static List<HolidayPeriod> LoadHolidays()
        {
            try
            {
                if (!File.Exists(HolidayFile))
                {
                    // Create default holidays (Philippine holidays)
                    var defaultHolidays = GetDefaultPhilippineHolidays();
                    _ = Task.Run(async () => await SaveHolidaysAsync(defaultHolidays));
                    return defaultHolidays;
                }

                var json = File.ReadAllText(HolidayFile);
                return JsonSerializer.Deserialize<List<HolidayPeriod>>(json) ?? new List<HolidayPeriod>();
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[HOLIDAY] Error loading holidays: {ex.Message}", "", "Error", "SYSTEM");
                return new List<HolidayPeriod>();
            }
        }

        public static async Task SaveHolidaysAsync(List<HolidayPeriod> holidays)
        {
            try
            {
                var directory = Path.GetDirectoryName(HolidayFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(holidays, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(HolidayFile, json);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[HOLIDAY] Error saving holidays: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        public static async Task ProcessRecurringHolidaysAsync()
        {
            var holidays = LoadHolidays();
            var currentYear = DateTime.Now.Year;
            var updated = false;

            foreach (var holiday in holidays.Where(h => h.RecurringYearly && h.IsActive))
            {
                // Check if the holiday needs to be updated for the new year
                if (holiday.EndDate.Year < currentYear)
                {
                    var newStart = new DateTime(currentYear, holiday.StartDate.Month, holiday.StartDate.Day);
                    var newEnd = new DateTime(currentYear, holiday.EndDate.Month, holiday.EndDate.Day);
                    
                    // If the holiday has already passed this year, schedule for next year
                    if (newEnd < DateTime.Now)
                    {
                        newStart = newStart.AddYears(1);
                        newEnd = newEnd.AddYears(1);
                    }

                    holiday.StartDate = newStart;
                    holiday.EndDate = newEnd;
                    updated = true;
                    
                    LogService.WriteLiveLog($"[HOLIDAY] Updated recurring holiday: {holiday.Name} ({holiday.StartDate:yyyy-MM-dd} to {holiday.EndDate:yyyy-MM-dd})", "", "Information", "SYSTEM");
                }
            }

            if (updated)
            {
                await SaveHolidaysAsync(holidays);
            }
        }

        private static bool IsDateInRange(DateTime date, HolidayPeriod holiday)
        {
            return date >= holiday.StartDate && date <= holiday.EndDate;
        }

        private static List<HolidayPeriod> GetDefaultPhilippineHolidays()
        {
            var currentYear = DateTime.Now.Year;
            var holidays = new List<HolidayPeriod>
            {
                new HolidayPeriod
                {
                    Name = "New Year",
                    StartDate = new DateTime(currentYear, 1, 1),
                    EndDate = new DateTime(currentYear, 1, 1),
                    RecurringYearly = true,
                    Reason = "New Year's Day"
                },
                new HolidayPeriod
                {
                    Name = "Holy Week",
                    StartDate = new DateTime(currentYear, 4, 17), // Maundy Thursday (approximate)
                    EndDate = new DateTime(currentYear, 4, 21), // Easter Monday (approximate)
                    RecurringYearly = false, // Dates change yearly
                    Reason = "Holy Week Break"
                },
                new HolidayPeriod
                {
                    Name = "Labor Day",
                    StartDate = new DateTime(currentYear, 5, 1),
                    EndDate = new DateTime(currentYear, 5, 1),
                    RecurringYearly = true,
                    Reason = "Labor Day"
                },
                new HolidayPeriod
                {
                    Name = "Independence Day",
                    StartDate = new DateTime(currentYear, 6, 12),
                    EndDate = new DateTime(currentYear, 6, 12),
                    RecurringYearly = true,
                    Reason = "Philippine Independence Day"
                },
                new HolidayPeriod
                {
                    Name = "Christmas Season",
                    StartDate = new DateTime(currentYear, 12, 24),
                    EndDate = new DateTime(currentYear, 12, 26),
                    RecurringYearly = true,
                    Reason = "Christmas Break"
                },
                new HolidayPeriod
                {
                    Name = "Year End",
                    StartDate = new DateTime(currentYear, 12, 31),
                    EndDate = new DateTime(currentYear, 12, 31),
                    RecurringYearly = true,
                    Reason = "Year End Holiday"
                }
            };

            return holidays;
        }

        public static string GetHolidayModeMessage()
        {
            var status = GetHolidayStatus();
            
            if (status.IsHolidayModeActive)
            {
                return $"🏖️ Holiday Mode Active: {status.CurrentHoliday.Name} - {status.CurrentHoliday.Reason}";
            }
            else if (status.NextHolidayStart.HasValue)
            {
                var daysUntil = status.TimeUntilNextHoliday?.Days ?? 0;
                if (daysUntil <= 7)
                {
                    return $"📅 Upcoming Holiday: {status.NextHolidayName} in {daysUntil} day(s)";
                }
            }
            
            return null!;
        }

        public static async Task CreateQuickHolidayAsync(string name, int days, string reason = "Maintenance")
        {
            var holiday = new HolidayPeriod
            {
                Name = name,
                StartDate = DateTime.Now,
                EndDate = DateTime.Now.AddDays(days),
                Reason = reason,
                RecurringYearly = false
            };

            await AddHolidayAsync(holiday);
        }

        public static async Task ExtendHolidayAsync(string holidayName, int additionalDays)
        {
            var holidays = LoadHolidays();
            var holiday = holidays.FirstOrDefault(h => h.Name.Equals(holidayName, StringComparison.OrdinalIgnoreCase));
            
            if (holiday != null)
            {
                holiday.EndDate = holiday.EndDate.AddDays(additionalDays);
                await SaveHolidaysAsync(holidays);
                LogService.WriteLiveLog($"[HOLIDAY] Extended {holidayName} by {additionalDays} days", "", "Information", "SYSTEM");
            }
            else
            {
                throw new ArgumentException($"Holiday not found: {holidayName}");
            }
        }
    }
}
