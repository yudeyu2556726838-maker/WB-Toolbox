using System.Globalization;

namespace WBToolbox.Native.Core
{
    internal static class NumericInput
    {
        internal static bool TryReadNonNegative(string value, out decimal number)
        {
            string normalized = (value ?? string.Empty).Trim().Replace(',', '.');
            bool parsed = decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            return parsed && number >= 0;
        }
    }
}
