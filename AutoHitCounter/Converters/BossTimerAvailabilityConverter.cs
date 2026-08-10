//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoHitCounter.Converters;

// Drives the boss-timer checkbox's Visibility directly (not just IsEnabled) --
// splits whose flag has no confirmed boss Entity ID don't get the checkbox at all,
// rather than showing it disabled.
public class BossTimerAvailabilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return Visibility.Collapsed;
        var eventId = values[0] switch
        {
            uint u => (uint?)u,
            _ => null
        };
        if (eventId == null) return Visibility.Collapsed;
        if (values[1] is not HashSet<uint> bossEntityFlagIds) return Visibility.Collapsed;

        return bossEntityFlagIds.Contains(eventId.Value) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
