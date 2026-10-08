using System;
using System.Collections.Generic;
using WBToolbox.Native.Core;
using WBToolbox.Native.Services;

namespace WBToolbox.Native.Tests
{
    internal static class CoreTests
    {
        private static int failures;

        private static void Main()
        {
            TestCurrencyConversion();
            TestCurrencyPairValidation();
            TestRubAdjustment();
            TestLightParcelBoundary();
            TestPricingFormula();
            TestCustomCommissionRate();
            TestInvalidProfitRate();
            TestNumericInput();
            TestCommissionCatalog();
            TestVideoOptimizationPolicy();

            if (failures > 0)
            {
                Console.Error.WriteLine("FAILED: " + failures + " core test(s)");
                Environment.Exit(1);
            }

            Console.WriteLine("PASS: 10 native core test groups");
        }

        private static void TestCurrencyConversion()
        {
            CurrencyMatrix matrix = Matrix();
            AssertClose(matrix.GetRate("USD", "CNY"), 6.8m, "USD/CNY cross-rate");
            AssertClose(matrix.Convert(100m, "CNY", "JPY", 0m), 2232.142857142857m, "CNY/JPY conversion");
        }

        private static void TestRubAdjustment()
        {
            CurrencyMatrix matrix = Matrix();
            AssertClose(matrix.Convert(1000m, "RUB", "USD", 0.05m), 12.3529411764706m, "RUB adjustment");
        }

        private static void TestCurrencyPairValidation()
        {
            if (!Currencies.IsSupportedPair("USD", "JPY") ||
                Currencies.IsSupportedPair("CNY", "CNY") ||
                Currencies.IsSupportedPair("EUR", "RUB") ||
                !ReferenceEquals(Currencies.Find("rub"), Currencies.Rub))
            {
                Fail("currency pair validation");
                return;
            }
            Console.WriteLine("PASS: currency pair validation");
        }

        private static void TestLightParcelBoundary()
        {
            PricingResult light = PricingEngine.Calculate(0.3m, 0m, 0.22m, 0.25m);
            PricingResult heavy = PricingEngine.Calculate(0.3001m, 0m, 0.22m, 0.25m);
            AssertClose(light.LogisticsFee, 22.4m, "light logistics");
            AssertClose(heavy.LogisticsFee, 23.9043m, "heavy logistics");
        }

        private static void TestPricingFormula()
        {
            PricingResult result = PricingEngine.Calculate(0.5m, 30m, 0.22m, 0.30m);
            AssertClose(result.LogisticsFee, 32.5m, "pricing logistics");
            AssertClose(result.SalePrice, 134.408602150537634m, "pricing sale price");
            AssertClose(result.ProfitAmount, 40.322580645161290m, "pricing profit");
        }

        private static void TestCustomCommissionRate()
        {
            PricingResult result = PricingEngine.Calculate(0.5m, 30m, 0.15m, 0.30m);
            AssertClose(result.SalePrice, 116.822429906542056m, "custom commission sale price");
            AssertClose(result.CommissionFee, 17.523364485981308m, "custom commission fee");
        }

        private static void TestInvalidProfitRate()
        {
            try
            {
                PricingEngine.Calculate(0.5m, 30m, 0.70m, 0.30m);
                Fail("invalid profit rate was accepted");
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("PASS: invalid profit rate rejected");
            }
        }

        private static void TestNumericInput()
        {
            decimal value;
            if (!NumericInput.TryReadNonNegative("12,5", out value) || value != 12.5m)
            {
                Fail("localized numeric input was not parsed");
                return;
            }
            if (NumericInput.TryReadNonNegative("-1", out value) || NumericInput.TryReadNonNegative("abc", out value))
            {
                Fail("invalid numeric input was accepted");
                return;
            }
            Console.WriteLine("PASS: numeric input boundaries");
        }

        private static void TestCommissionCatalog()
        {
            if (CommissionCatalog.Options.Count != 7435)
            {
                Fail("commission catalog row count: " + CommissionCatalog.Options.Count);
                return;
            }
            int total;
            IList<CommissionCategoryOption> doors = CommissionCatalog.Search("门", 120, out total);
            CommissionCategoryOption gate = null;
            foreach (CommissionCategoryOption option in doors)
                if (option.Product == "大门" && option.Category == "建筑材料") gate = option;
            if (total < 20 || gate == null || gate.CommissionPercent != 21m)
            {
                Fail("commission catalog searchable category mapping");
                return;
            }
            IList<CommissionCategoryOption> cleaner = CommissionCatalog.Search("玻璃清洁剂", 10, out total);
            if (cleaner.Count != 2 || cleaner[0].CommissionPercent == cleaner[1].CommissionPercent)
            {
                Fail("commission catalog duplicate product disambiguation");
                return;
            }
            Console.WriteLine("PASS: commission catalog search and rates");
        }

        private static void TestVideoOptimizationPolicy()
        {
            VideoTargetSize landscape = VideoBackgroundOptimizer.CalculateTargetSize(2560, 1440);
            VideoTargetSize portrait = VideoBackgroundOptimizer.CalculateTargetSize(1080, 1920);
            VideoTargetSize compact = VideoBackgroundOptimizer.CalculateTargetSize(640, 360);
            bool highLoad = VideoBackgroundOptimizer.NeedsOptimization(
                2560, 1440, 90, 64200000, "H264");
            bool efficient = VideoBackgroundOptimizer.NeedsOptimization(
                1280, 720, 30, 3500000, "H264");
            bool incompatible = VideoBackgroundOptimizer.NeedsOptimization(
                640, 360, 24, 1000000, "HEVC");
            if (landscape.Width != 1280 || landscape.Height != 720 ||
                portrait.Width != 720 || portrait.Height != 1280 ||
                compact.Width != 640 || compact.Height != 360 ||
                !highLoad || efficient || !incompatible)
            {
                Fail("video background optimization policy");
                return;
            }
            Console.WriteLine("PASS: video background optimization policy");
        }

        private static CurrencyMatrix Matrix()
        {
            return new CurrencyMatrix(
                new Dictionary<string, decimal>
                {
                    { "RUB", 1m },
                    { "CNY", 12.5m },
                    { "USD", 85m },
                    { "JPY", 0.56m }
                },
                DateTimeOffset.Now,
                "test");
        }

        private static void AssertClose(decimal actual, decimal expected, string name)
        {
            if (Math.Abs(actual - expected) > 0.000000001m)
            {
                Fail(name + ": expected " + expected + ", actual " + actual);
                return;
            }
            Console.WriteLine("PASS: " + name);
        }

        private static void Fail(string message)
        {
            failures++;
            Console.Error.WriteLine("FAIL: " + message);
        }
    }
}
