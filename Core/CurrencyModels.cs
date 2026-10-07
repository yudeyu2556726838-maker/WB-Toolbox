using System;
using System.Collections.Generic;

namespace WBToolbox.Native.Core
{
    internal sealed class CurrencyInfo
    {
        internal CurrencyInfo(string code, string name, string symbol)
        {
            Code = code;
            Name = name;
            Symbol = symbol;
        }

        internal string Code { get; private set; }
        internal string Name { get; private set; }
        internal string Symbol { get; private set; }

        public override string ToString()
        {
            return Name + "  " + Code;
        }
    }

    internal static class Currencies
    {
        internal static readonly CurrencyInfo Cny = new CurrencyInfo("CNY", "人民币", "¥");
        internal static readonly CurrencyInfo Rub = new CurrencyInfo("RUB", "卢布", "₽");
        internal static readonly CurrencyInfo Usd = new CurrencyInfo("USD", "美元", "$");
        internal static readonly CurrencyInfo Jpy = new CurrencyInfo("JPY", "日元", "¥");

        internal static readonly CurrencyInfo[] All = { Cny, Rub, Usd, Jpy };

        internal static CurrencyInfo Find(string code)
        {
            foreach (CurrencyInfo currency in All)
            {
                if (string.Equals(currency.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return currency;
                }
            }
            return null;
        }

        internal static bool IsSupportedPair(string fromCode, string toCode)
        {
            return Find(fromCode) != null &&
                Find(toCode) != null &&
                !string.Equals(fromCode, toCode, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class CurrencyMatrix
    {
        private readonly Dictionary<string, decimal> ratesToRub;

        internal CurrencyMatrix(Dictionary<string, decimal> ratesToRub, DateTimeOffset updatedAt, string source)
        {
            if (ratesToRub == null)
            {
                throw new ArgumentNullException("ratesToRub");
            }

            this.ratesToRub = new Dictionary<string, decimal>(ratesToRub, StringComparer.OrdinalIgnoreCase);
            UpdatedAt = updatedAt;
            Source = source ?? string.Empty;
        }

        internal DateTimeOffset UpdatedAt { get; private set; }
        internal string Source { get; private set; }

        internal decimal GetRate(string fromCode, string toCode)
        {
            decimal fromRate;
            decimal toRate;
            if (!ratesToRub.TryGetValue(fromCode, out fromRate) ||
                !ratesToRub.TryGetValue(toCode, out toRate) ||
                fromRate <= 0 || toRate <= 0)
            {
                throw new InvalidOperationException("缺少有效的币种汇率");
            }

            return fromRate / toRate;
        }

        internal decimal Convert(decimal amount, string fromCode, string toCode, decimal adjustmentRate)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException("amount", "金额不能是负数");
            }

            decimal adjustedAmount = amount;
            if (string.Equals(fromCode, "RUB", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(toCode, "RUB", StringComparison.OrdinalIgnoreCase))
            {
                adjustedAmount *= 1 + adjustmentRate;
            }

            return adjustedAmount * GetRate(fromCode, toCode);
        }

        internal Dictionary<string, decimal> CopyRates()
        {
            return new Dictionary<string, decimal>(ratesToRub, StringComparer.OrdinalIgnoreCase);
        }
    }
}
