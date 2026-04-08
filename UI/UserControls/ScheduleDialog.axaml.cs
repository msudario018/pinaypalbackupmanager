using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class ScheduleDialog : Window
    {
        private bool _ftpIsPm;
        private bool _mcIsPm;
        private bool _sqlIsPm;

        public ScheduleDialog()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            LoadValues();
            WireButtons();
        }

        private void LoadValues()
        {
            var s = ConfigService.Current.Schedule;

            var (ftpH, ftpPm) = To12Hr(s.FtpDailySyncHourMnl);
            _ftpIsPm = ftpPm;
            SetTb("FtpDailyHour", ftpH.ToString());
            SetTb("FtpDailyMinute", s.FtpDailySyncMinuteMnl.ToString("D2"));
            UpdateAmPm("Ftp", _ftpIsPm);

            var (mcH, mcPm) = To12Hr(s.MailchimpDailySyncHourMnl);
            _mcIsPm = mcPm;
            SetTb("McDailyHour", mcH.ToString());
            SetTb("McDailyMinute", s.MailchimpDailySyncMinuteMnl.ToString("D2"));
            UpdateAmPm("Mc", _mcIsPm);

            var (sqlH, sqlPm) = To12Hr(s.SqlDailySyncHourMnl);
            _sqlIsPm = sqlPm;
            SetTb("SqlDailyHour", sqlH.ToString());
            SetTb("SqlDailyMinute", s.SqlDailySyncMinuteMnl.ToString("D2"));
            UpdateAmPm("Sql", _sqlIsPm);

            SetTb("FtpScanHours",   s.FtpAutoScanHours.ToString());
            SetTb("FtpScanMinutes", s.FtpAutoScanMinutes.ToString("D2"));
            SetTb("McScanHours",    s.MailchimpAutoScanHours.ToString());
            SetTb("McScanMinutes",  s.MailchimpAutoScanMinutes.ToString("D2"));
            SetTb("SqlScanHours",   s.SqlAutoScanHours.ToString());
            SetTb("SqlScanMinutes", s.SqlAutoScanMinutes.ToString("D2"));
        }

        private void WireButtons()
        {
            this.FindControl<Border>("FtpAmBtn")!.PointerPressed += (_, _) => { _ftpIsPm = false; UpdateAmPm("Ftp", false); };
            this.FindControl<Border>("FtpPmBtn")!.PointerPressed += (_, _) => { _ftpIsPm = true;  UpdateAmPm("Ftp", true);  };
            this.FindControl<Border>("McAmBtn")!.PointerPressed  += (_, _) => { _mcIsPm  = false; UpdateAmPm("Mc",  false); };
            this.FindControl<Border>("McPmBtn")!.PointerPressed  += (_, _) => { _mcIsPm  = true;  UpdateAmPm("Mc",  true);  };
            this.FindControl<Border>("SqlAmBtn")!.PointerPressed += (_, _) => { _sqlIsPm = false; UpdateAmPm("Sql", false); };
            this.FindControl<Border>("SqlPmBtn")!.PointerPressed += (_, _) => { _sqlIsPm = true;  UpdateAmPm("Sql", true);  };

            this.FindControl<Button>("BtnReset")!.Click  += (_, _) => ResetToDefault();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnSave")!.Click   += (_, _) => SaveAndClose();
        }

        private void UpdateAmPm(string prefix, bool isPm)
        {
            var amBtn  = this.FindControl<Border>($"{prefix}AmBtn");
            var pmBtn  = this.FindControl<Border>($"{prefix}PmBtn");
            var amText = this.FindControl<TextBlock>($"{prefix}AmText");
            var pmText = this.FindControl<TextBlock>($"{prefix}PmText");

            string activeColor = prefix == "Ftp" ? "#52B788" : prefix == "Mc" ? "#48CAE4" : "#C77DFF";
            string activeBg    = prefix == "Ftp" ? "#10002B" : prefix == "Mc" ? "#0F2028" : "#2A2510";

            if (amBtn  != null) amBtn.Background  = Brush.Parse(isPm ? "#5A189A" : activeBg);
            if (pmBtn  != null) pmBtn.Background  = Brush.Parse(isPm ? activeBg  : "#5A189A");
            if (amText != null) { amText.Foreground = Brush.Parse(isPm ? "#9D4EDD" : activeColor); }
            if (pmText != null) { pmText.Foreground = Brush.Parse(isPm ? activeColor : "#9D4EDD"); }
        }

        private void ResetToDefault()
        {
            var d = new Services.ScheduleSettings();

            var (ftpH, ftpPm) = To12Hr(d.FtpDailySyncHourMnl);
            _ftpIsPm = ftpPm;
            SetTb("FtpDailyHour",   ftpH.ToString());
            SetTb("FtpDailyMinute", d.FtpDailySyncMinuteMnl.ToString("D2"));
            UpdateAmPm("Ftp", _ftpIsPm);

            var (mcH, mcPm) = To12Hr(d.MailchimpDailySyncHourMnl);
            _mcIsPm = mcPm;
            SetTb("McDailyHour",   mcH.ToString());
            SetTb("McDailyMinute", d.MailchimpDailySyncMinuteMnl.ToString("D2"));
            UpdateAmPm("Mc", _mcIsPm);

            var (sqlH, sqlPm) = To12Hr(d.SqlDailySyncHourMnl);
            _sqlIsPm = sqlPm;
            SetTb("SqlDailyHour",   sqlH.ToString());
            SetTb("SqlDailyMinute", d.SqlDailySyncMinuteMnl.ToString("D2"));
            UpdateAmPm("Sql", _sqlIsPm);

            SetTb("FtpScanHours",   d.FtpAutoScanHours.ToString());
            SetTb("FtpScanMinutes", d.FtpAutoScanMinutes.ToString("D2"));
            SetTb("McScanHours",    d.MailchimpAutoScanHours.ToString());
            SetTb("McScanMinutes",  d.MailchimpAutoScanMinutes.ToString("D2"));
            SetTb("SqlScanHours",   d.SqlAutoScanHours.ToString());
            SetTb("SqlScanMinutes", d.SqlAutoScanMinutes.ToString("D2"));

            var status = this.FindControl<TextBlock>("DialogStatus");
            if (status != null) { status.Text = "Reset to defaults — click Save to apply"; status.Foreground = Brush.Parse("#FAB387"); }
        }

        private void SaveAndClose()
        {
            try
            {
                var s = ConfigService.Current.Schedule;

                s.FtpDailySyncHourMnl        = To24Hr(Parse(GetTb("FtpDailyHour"),   12, 1, 12), _ftpIsPm);
                s.FtpDailySyncMinuteMnl       = Parse(GetTb("FtpDailyMinute"),  0, 0, 59);
                s.MailchimpDailySyncHourMnl   = To24Hr(Parse(GetTb("McDailyHour"),    6,  1, 12), _mcIsPm);
                s.MailchimpDailySyncMinuteMnl = Parse(GetTb("McDailyMinute"),   0, 0, 59);
                s.SqlDailySyncHourMnl         = To24Hr(Parse(GetTb("SqlDailyHour"),   5,  1, 12), _sqlIsPm);
                s.SqlDailySyncMinuteMnl       = Parse(GetTb("SqlDailyMinute"),  0, 0, 59);

                s.FtpAutoScanHours        = Parse(GetTb("FtpScanHours"),   3, 0, 23);
                s.FtpAutoScanMinutes      = Parse(GetTb("FtpScanMinutes"), 0, 0, 59);
                s.MailchimpAutoScanHours   = Parse(GetTb("McScanHours"),    2, 0, 23);
                s.MailchimpAutoScanMinutes = Parse(GetTb("McScanMinutes"), 0, 0, 59);
                s.SqlAutoScanHours        = Parse(GetTb("SqlScanHours"),   2, 0, 23);
                s.SqlAutoScanMinutes      = Parse(GetTb("SqlScanMinutes"), 15, 0, 59);

                ConfigService.SaveSchedule();
                ConfigService.Load();
                LogService.WriteSystemLog("Schedule configuration updated", "Information", "SETTINGS");
                Close(true);
            }
            catch (Exception ex)
            {
                var status = this.FindControl<TextBlock>("DialogStatus");
                if (status != null)
                {
                    status.Text = $"Error: {ex.Message}";
                    status.Foreground = Brush.Parse("#F38BA8");
                }
            }
        }

        private void SetTb(string name, string value)
        {
            var tb = this.FindControl<TextBox>(name);
            if (tb != null) tb.Text = value;
        }

        private string GetTb(string name) => this.FindControl<TextBox>(name)?.Text ?? "";

        private static int Parse(string text, int fallback, int min, int max)
        {
            if (int.TryParse(text.Trim(), out int v) && v >= min && v <= max) return v;
            return fallback;
        }

        private static (int hour12, bool isPm) To12Hr(int hour24)
        {
            bool isPm = hour24 >= 12;
            int h = hour24 % 12;
            if (h == 0) h = 12;
            return (h, isPm);
        }

        private static int To24Hr(int hour12, bool isPm)
        {
            if (!isPm) return hour12 == 12 ? 0 : hour12;
            return hour12 == 12 ? 12 : hour12 + 12;
        }
    }
}
