using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace PinayPalBackupManager.UI.Converters
{
    public class BoolToStatusBrushConverter : IValueConverter
    {
        public static readonly BoolToStatusBrushConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Brush.Parse("#FFB3C7") : Brush.Parse("#00D68C");
            return Brush.Parse("#00D68C");
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
