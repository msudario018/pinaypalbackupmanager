using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class DashboardCustomizationDialog : UserControl
    {
        public event Action<DashboardCustomization>? OnApply;
        
        private DashboardCustomization _settings = new DashboardCustomization();

        public DashboardCustomizationDialog()
        {
            InitializeComponent();
            
            this.FindControl<Button>("BtnApply")!.Click += (_, _) => ApplySettings();
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => CloseDialog();
        }
        
        private void ApplySettings()
        {
            _settings.ShowQuickStats = this.FindControl<CheckBox>("ChkQuickStats")?.IsChecked == true;
            _settings.ShowDailySchedule = this.FindControl<CheckBox>("ChkSchedule")?.IsChecked == true;
            _settings.ShowServiceCards = this.FindControl<CheckBox>("ChkServiceCards")?.IsChecked == true;
            _settings.ShowHealthDashboard = this.FindControl<CheckBox>("ChkHealth")?.IsChecked == true;
            _settings.ShowOperations = this.FindControl<CheckBox>("ChkOperations")?.IsChecked == true;
            _settings.ShowStorageUsage = this.FindControl<CheckBox>("ChkStorage")?.IsChecked == true;
            _settings.ShowRecentActivity = this.FindControl<CheckBox>("ChkRecentActivity")?.IsChecked == true;
            
            DashboardCustomization.Save(_settings);
            OnApply?.Invoke(_settings);
        }
        
        private void CloseDialog()
        {
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            parentWindow?.Close();
        }
    }
}
