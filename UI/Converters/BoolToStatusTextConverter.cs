using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace PinayPalBackupManager.UI.Converters
{
    public class BoolToStatusTextConverter : IValueConverter
    {
        public static readonly BoolToStatusTextConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return b ? "Used" : "Available";
            return "Available";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
