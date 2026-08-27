using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ScreenRecorder.Core.Recording;

namespace ScreenRecorder.UI.Converters;

/// <summary>RecordingMode ↔ RadioButton IsChecked（ConverterParameter 为模式名）。</summary>
public sealed class ModeToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is RecordingMode mode && Enum.TryParse<RecordingMode>(parameter as string, out var target) && mode == target;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && Enum.TryParse<RecordingMode>(parameter as string, out var target)
            ? target : Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (parameter as string == "invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}
