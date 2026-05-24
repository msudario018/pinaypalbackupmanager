using Avalonia.Controls;
using Avalonia.Media;
using PinayPalBackupManager.Services;
using System;
using System.Linq;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class BackupScheduleControl : UserControl
    {
        private string? _editingScheduleId;

        public BackupScheduleControl()
        {
            InitializeComponent();
            
            var btnCreate = this.FindControl<Button>("BtnCreate");
            if (btnCreate != null)
            {
                btnCreate.Click += (s, e) => ShowForm();
            }

            var btnCloseForm = this.FindControl<Button>("BtnCloseForm");
            if (btnCloseForm != null)
            {
                btnCloseForm.Click += (s, e) => HideForm();
            }

            var btnCancel = this.FindControl<Button>("BtnCancel");
            if (btnCancel != null)
            {
                btnCancel.Click += (s, e) => HideForm();
            }

            var btnSave = this.FindControl<Button>("BtnSave");
            if (btnSave != null)
            {
                btnSave.Click += (s, e) => SaveSchedule();
            }

            var btnClose = this.FindControl<Button>("BtnClose");
            if (btnClose != null)
            {
                btnClose.Click += (s, e) =>
                {
                    var parentWindow = TopLevel.GetTopLevel(this) as Window;
                    parentWindow?.Close();
                };
            }

            var cmbType = this.FindControl<ComboBox>("CmbType");
            if (cmbType != null)
            {
                cmbType.SelectionChanged += OnTypeChanged;
            }

            // Load initial data
            LoadSchedules();
        }

        private void LoadSchedules()
        {
            try
            {
                var schedules = BackupSchedulingService.GetAllSchedules();
                UpdateUI(schedules);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to load schedules: {ex.Message}", "Error", "BACKUPSCHEDULE");
            }
        }

        private void UpdateUI(System.Collections.Generic.List<BackupSchedulingService.BackupSchedule> schedules)
        {
            var txtSummary = this.FindControl<TextBlock>("TxtSummary");
            var scheduleList = this.FindControl<StackPanel>("ScheduleList");

            if (txtSummary != null)
            {
                var enabledCount = schedules.Count(s => s.IsEnabled);
                txtSummary.Text = $"Total: {schedules.Count} | Enabled: {enabledCount} | Disabled: {schedules.Count - enabledCount}";
            }

            if (scheduleList != null)
            {
                scheduleList.Children.Clear();
                
                if (schedules.Count == 0)
                {
                    scheduleList.Children.Add(new TextBlock 
                    { 
                        Text = "No schedules configured.", 
                        Foreground = Brush.Parse("#8B949E"), 
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 20)
                    });
                    return;
                }

                foreach (var schedule in schedules)
                {
                    var border = new Border
                    {
                        Background = schedule.IsEnabled ? Brush.Parse("#0A000000") : Brush.Parse("#05000000"),
                        BorderBrush = schedule.IsEnabled ? Brush.Parse("#30363D") : Brush.Parse("#21262D"),
                        BorderThickness = new Avalonia.Thickness(1),
                        CornerRadius = new Avalonia.CornerRadius(8),
                        Padding = new Avalonia.Thickness(12),
                        Opacity = schedule.IsEnabled ? 1.0 : 0.6
                    };

                    var panel = new StackPanel { Spacing = 10 };

                    // Header
                    var headerGrid = new Grid();
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Star));
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    headerGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    headerGrid.Margin = new Avalonia.Thickness(0, 0, 5, 0);
                    
                    var nameText = new TextBlock
                    {
                        Text = schedule.Name,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        FontSize = 12,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                    };

                    var statusBadge = new Border
                    {
                        Background = schedule.IsEnabled ? Brush.Parse("#3FB950") : Brush.Parse("#8B949E"),
                        CornerRadius = new Avalonia.CornerRadius(4),
                        Padding = new Avalonia.Thickness(6, 2),
                        Margin = new Avalonia.Thickness(10, 0, 5, 0),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };
                    statusBadge.Child = new TextBlock
                    {
                        Text = schedule.IsEnabled ? "Active" : "Disabled",
                        FontSize = 9,
                        Foreground = Brush.Parse("#FFFFFF")
                    };

                    var btnEdit = new Button
                    {
                        Content = "Edit",
                        FontSize = 10,
                        Margin = new Avalonia.Thickness(0, 0, 5, 0)
                    };
                    btnEdit.Classes.Add("Ghost");
                    btnEdit.Click += (s, e) => EditSchedule(schedule.Id);

                    var btnDelete = new Button
                    {
                        Content = "Delete",
                        FontSize = 10
                    };
                    btnDelete.Classes.Add("Ghost");
                    btnDelete.Click += (s, e) => DeleteSchedule(schedule.Id);

                    Grid.SetColumn(nameText, 0);
                    Grid.SetColumn(statusBadge, 1);
                    Grid.SetColumn(btnEdit, 2);
                    Grid.SetColumn(btnDelete, 3);
                    
                    headerGrid.Children.Add(nameText);
                    headerGrid.Children.Add(statusBadge);
                    headerGrid.Children.Add(btnEdit);
                    headerGrid.Children.Add(btnDelete);

                    // Details
                    var detailsGrid = new Grid();
                    detailsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    detailsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    detailsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    detailsGrid.Margin = new Avalonia.Thickness(0, 0, 5, 0);
                    
                    var serviceText = new TextBlock
                    {
                        Text = $"Service: {schedule.Service.ToUpper()}",
                        FontSize = 10,
                        Foreground = Brush.Parse("#8B949E")
                    };

                    var typeText = new TextBlock
                    {
                        Text = $"Type: {schedule.Type}",
                        FontSize = 10,
                        Foreground = Brush.Parse("#8B949E"),
                        Margin = new Avalonia.Thickness(15, 0, 0, 0)
                    };

                    var nextRunText = new TextBlock
                    {
                        Text = schedule.NextRun.HasValue ? $"Next: {schedule.NextRun.Value:MM/dd HH:mm}" : "Next: Not scheduled",
                        FontSize = 10,
                        Foreground = Brush.Parse("#8B949E"),
                        Margin = new Avalonia.Thickness(15, 0, 0, 0)
                    };

                    Grid.SetColumn(serviceText, 0);
                    Grid.SetColumn(typeText, 1);
                    Grid.SetColumn(nextRunText, 2);
                    
                    detailsGrid.Children.Add(serviceText);
                    detailsGrid.Children.Add(typeText);
                    detailsGrid.Children.Add(nextRunText);

                    // Stats
                    var statsGrid = new Grid();
                    statsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    statsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    statsGrid.ColumnDefinitions.Add(new Avalonia.Controls.ColumnDefinition(1, Avalonia.Controls.GridUnitType.Auto));
                    statsGrid.Margin = new Avalonia.Thickness(0, 5, 5, 0);
                    
                    var runCountText = new TextBlock
                    {
                        Text = $"Runs: {schedule.RunCount}",
                        FontSize = 10,
                        Foreground = Brush.Parse("#6E7681")
                    };

                    var lastRunText = new TextBlock
                    {
                        Text = schedule.LastRun.HasValue ? $"Last: {schedule.LastRun.Value:MM/dd HH:mm}" : "Last: Never",
                        FontSize = 10,
                        Foreground = Brush.Parse("#6E7681"),
                        Margin = new Avalonia.Thickness(15, 0, 0, 0)
                    };

                    var toggleBtn = new Button
                    {
                        Content = schedule.IsEnabled ? "Disable" : "Enable",
                        FontSize = 10,
                        Margin = new Avalonia.Thickness(15, 0, 0, 0)
                    };
                    toggleBtn.Classes.Add("Ghost");
                    toggleBtn.Click += (s, e) => ToggleSchedule(schedule.Id, schedule.IsEnabled);

                    Grid.SetColumn(runCountText, 0);
                    Grid.SetColumn(lastRunText, 1);
                    Grid.SetColumn(toggleBtn, 2);
                    
                    statsGrid.Children.Add(runCountText);
                    statsGrid.Children.Add(lastRunText);
                    statsGrid.Children.Add(toggleBtn);

                    panel.Children.Add(headerGrid);
                    panel.Children.Add(detailsGrid);
                    panel.Children.Add(statsGrid);

                    border.Child = panel;
                    scheduleList.Children.Add(border);
                }
            }
        }

        private void ShowForm()
        {
            _editingScheduleId = null;
            ClearForm();
            var borderForm = this.FindControl<Border>("BorderForm");
            if (borderForm != null) borderForm.IsVisible = true;
            
            // Scroll to the form
            var scrollViewer = this.FindControl<ScrollViewer>("MainScrollViewer");
            if (scrollViewer != null && borderForm != null)
            {
                scrollViewer.ScrollToEnd();
            }
        }

        private void HideForm()
        {
            _editingScheduleId = null;
            var borderForm = this.FindControl<Border>("BorderForm");
            if (borderForm != null) borderForm.IsVisible = false;
        }

        private void ClearForm()
        {
            var txtName = this.FindControl<TextBox>("TxtName");
            var cmbService = this.FindControl<ComboBox>("CmbService");
            var cmbType = this.FindControl<ComboBox>("CmbType");
            var numInterval = this.FindControl<NumericUpDown>("NumInterval");
            var dateOneTime = this.FindControl<CalendarDatePicker>("DateOneTime");
            var timeOneTime = this.FindControl<TimePicker>("TimeOneTime");
            var cmbBackupType = this.FindControl<ComboBox>("CmbBackupType");
            var chkEnabled = this.FindControl<CheckBox>("ChkEnabled");

            if (txtName != null) txtName.Text = "";
            if (cmbService != null) cmbService.SelectedIndex = 0;
            if (cmbType != null) cmbType.SelectedIndex = 0;
            if (numInterval != null) numInterval.Value = 6;
            if (dateOneTime != null) dateOneTime.SelectedDate = null;
            if (timeOneTime != null) timeOneTime.SelectedTime = null;
            if (cmbBackupType != null) cmbBackupType.SelectedIndex = 0;
            if (chkEnabled != null) chkEnabled.IsChecked = true;

            OnTypeChanged(null, null);
        }

        private void EditSchedule(string id)
        {
            var schedule = BackupSchedulingService.GetSchedule(id);
            if (schedule == null) return;

            _editingScheduleId = id;

            var txtName = this.FindControl<TextBox>("TxtName");
            var cmbService = this.FindControl<ComboBox>("CmbService");
            var cmbType = this.FindControl<ComboBox>("CmbType");
            var numInterval = this.FindControl<NumericUpDown>("NumInterval");
            var dateOneTime = this.FindControl<CalendarDatePicker>("DateOneTime");
            var timeOneTime = this.FindControl<TimePicker>("TimeOneTime");
            var cmbBackupType = this.FindControl<ComboBox>("CmbBackupType");
            var chkEnabled = this.FindControl<CheckBox>("ChkEnabled");

            if (txtName != null) txtName.Text = schedule.Name;
            if (cmbService != null) cmbService.SelectedIndex = GetServiceIndex(schedule.Service);
            if (cmbType != null) cmbType.SelectedIndex = GetTypeIndex(schedule.Type);
            if (numInterval != null && schedule.Interval.HasValue) numInterval.Value = (decimal)schedule.Interval.Value.TotalHours;
            if (dateOneTime != null) dateOneTime.SelectedDate = schedule.OneTimeDate;
            if (timeOneTime != null && schedule.OneTimeDate.HasValue) timeOneTime.SelectedTime = schedule.OneTimeDate.Value.TimeOfDay;
            if (cmbBackupType != null) cmbBackupType.SelectedIndex = schedule.BackupType == "Full" ? 0 : 1;
            if (chkEnabled != null) chkEnabled.IsChecked = schedule.IsEnabled;

            OnTypeChanged(null, null);

            var borderForm = this.FindControl<Border>("BorderForm");
            if (borderForm != null) borderForm.IsVisible = true;
        }

        private void SaveSchedule()
        {
            try
            {
                var txtName = this.FindControl<TextBox>("TxtName");
                var cmbService = this.FindControl<ComboBox>("CmbService");
                var cmbType = this.FindControl<ComboBox>("CmbType");
                var numInterval = this.FindControl<NumericUpDown>("NumInterval");
                var dateOneTime = this.FindControl<CalendarDatePicker>("DateOneTime");
                var timeOneTime = this.FindControl<TimePicker>("TimeOneTime");
                var cmbBackupType = this.FindControl<ComboBox>("CmbBackupType");
                var chkEnabled = this.FindControl<CheckBox>("ChkEnabled");

                if (txtName == null || cmbService == null || cmbType == null || cmbBackupType == null || chkEnabled == null)
                    return;

                var schedule = new BackupSchedulingService.BackupSchedule
                {
                    Name = txtName.Text ?? "Untitled",
                    Service = cmbService.SelectedItem?.ToString()?.ToLower() ?? "all",
                    Type = GetSelectedType(cmbType.SelectedIndex),
                    IsEnabled = chkEnabled.IsChecked ?? true,
                    BackupType = cmbBackupType.SelectedItem?.ToString() ?? "Full"
                };

                if (schedule.Type == BackupSchedulingService.ScheduleType.Interval && numInterval != null)
                {
                    schedule.Interval = System.TimeSpan.FromHours((double)(numInterval.Value ?? 6));
                }

                if (schedule.Type == BackupSchedulingService.ScheduleType.Once && dateOneTime != null && timeOneTime != null)
                {
                    var date = dateOneTime.SelectedDate ?? DateTime.Now;
                    var time = timeOneTime.SelectedTime ?? TimeSpan.FromHours(12);
                    schedule.OneTimeDate = date.Date + time;
                }

                if (_editingScheduleId != null)
                {
                    schedule.Id = _editingScheduleId;
                    BackupSchedulingService.UpdateSchedule(_editingScheduleId, schedule);
                }
                else
                {
                    BackupSchedulingService.CreateSchedule(schedule);
                }

                HideForm();
                LoadSchedules();
                NotificationService.ShowBackupToast("Schedule", "Schedule saved successfully", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to save schedule: {ex.Message}", "Error", "BACKUPSCHEDULE");
                NotificationService.ShowBackupToast("Schedule", "Failed to save schedule", "Error");
            }
        }

        private void DeleteSchedule(string id)
        {
            try
            {
                BackupSchedulingService.DeleteSchedule(id);
                LoadSchedules();
                NotificationService.ShowBackupToast("Schedule", "Schedule deleted", "Info");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to delete schedule: {ex.Message}", "Error", "BACKUPSCHEDULE");
            }
        }

        private void ToggleSchedule(string id, bool isEnabled)
        {
            try
            {
                if (isEnabled)
                {
                    BackupSchedulingService.DisableSchedule(id);
                }
                else
                {
                    BackupSchedulingService.EnableSchedule(id);
                }
                LoadSchedules();
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"Failed to toggle schedule: {ex.Message}", "Error", "BACKUPSCHEDULE");
            }
        }

        private void OnTypeChanged(object? sender, SelectionChangedEventArgs? e)
        {
            var cmbType = this.FindControl<ComboBox>("CmbType");
            var panelInterval = this.FindControl<StackPanel>("PanelInterval");
            var panelOneTime = this.FindControl<StackPanel>("PanelOneTime");

            if (cmbType == null || panelInterval == null || panelOneTime == null)
                return;

            panelInterval.IsVisible = cmbType.SelectedIndex == 4; // Interval
            panelOneTime.IsVisible = cmbType.SelectedIndex == 0; // Once
        }

        private int GetServiceIndex(string service)
        {
            return service.ToLower() switch
            {
                "all" => 0,
                "ftp" => 1,
                "sql" => 2,
                "mailchimp" => 3,
                _ => 0
            };
        }

        private int GetTypeIndex(BackupSchedulingService.ScheduleType type)
        {
            return type switch
            {
                BackupSchedulingService.ScheduleType.Once => 0,
                BackupSchedulingService.ScheduleType.Daily => 1,
                BackupSchedulingService.ScheduleType.Weekly => 2,
                BackupSchedulingService.ScheduleType.Monthly => 3,
                BackupSchedulingService.ScheduleType.Interval => 4,
                _ => 0
            };
        }

        private BackupSchedulingService.ScheduleType GetSelectedType(int index)
        {
            return index switch
            {
                0 => BackupSchedulingService.ScheduleType.Once,
                1 => BackupSchedulingService.ScheduleType.Daily,
                2 => BackupSchedulingService.ScheduleType.Weekly,
                3 => BackupSchedulingService.ScheduleType.Monthly,
                4 => BackupSchedulingService.ScheduleType.Interval,
                _ => BackupSchedulingService.ScheduleType.Daily
            };
        }
    }
}
