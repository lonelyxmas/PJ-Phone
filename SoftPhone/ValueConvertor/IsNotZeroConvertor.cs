using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SoftPhone.ValueConvertor
{
    internal class IsNotZeroConvertor : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not int iVal) return false;
            return iVal > 0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
