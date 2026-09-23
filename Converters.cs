using System;
using System.Globalization;
using System.Windows.Data;

namespace TinyFanControl
{
    // Splits "Nuvoton NCT6687D-R · CPU Fan" into its chip and fan parts.
    // ConverterParameter "fan" returns the part after the last "·",
    // "chip" returns the part before it (empty when there is no separator).
    public class FanNamePartConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var name = value as string ?? string.Empty;
            var wantChip = (parameter as string) == "chip";
            var idx = name.LastIndexOf('·');
            if (idx < 0)
                return wantChip ? string.Empty : name;
            return wantChip
                ? name.Substring(0, idx).Trim()
                : name.Substring(idx + 1).Trim();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
