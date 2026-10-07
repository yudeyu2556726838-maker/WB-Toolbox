using System;

namespace WBToolbox.Native.Core
{
    internal sealed class PricingResult
    {
        internal decimal Weight { get; set; }
        internal decimal PurchasePrice { get; set; }
        internal decimal LogisticsFee { get; set; }
        internal decimal CommissionFee { get; set; }
        internal decimal ProfitAmount { get; set; }
        internal decimal SalePrice { get; set; }
        internal decimal CommissionRate { get; set; }
        internal decimal ProfitRate { get; set; }
        internal string Segment { get; set; }
        internal string FreightFormula { get; set; }
    }

    internal static class PricingEngine
    {
        internal const decimal DefaultCommissionRate = 0.22m;
        internal const decimal WithdrawalRate = 0.015m;
        internal const decimal DefaultProfitRate = 0.25m;

        internal static bool IsRatePlanValid(decimal commissionRate, decimal profitRate)
        {
            return commissionRate >= 0 && commissionRate < 1 &&
                profitRate >= 0 && profitRate < 1 &&
                commissionRate + profitRate + WithdrawalRate < 1;
        }

        internal static PricingResult Calculate(
            decimal weight,
            decimal purchasePrice,
            decimal commissionRate,
            decimal profitRate)
        {
            if (weight < 0)
            {
                throw new ArgumentOutOfRangeException("weight", "重量必须是非负数");
            }
            if (purchasePrice < 0)
            {
                throw new ArgumentOutOfRangeException("purchasePrice", "采购价必须是非负数");
            }
            if (commissionRate < 0 || commissionRate >= 1)
            {
                throw new ArgumentOutOfRangeException("commissionRate", "平台佣金率必须在 0% 到 100% 之间");
            }
            if (profitRate < 0 || profitRate >= 1)
            {
                throw new ArgumentOutOfRangeException("profitRate", "利润率必须在 0% 到 100% 之间");
            }
            if (!IsRatePlanValid(commissionRate, profitRate))
            {
                throw new ArgumentOutOfRangeException(
                    "profitRate",
                    "平台佣金率、利润率与 1.5% 提现费率之和必须小于 100%");
            }

            bool lightParcel = weight <= 0.3m;
            decimal logisticsFee = lightParcel
                ? 58m * weight + 2m + 3m
                : 43m * weight + 8m + 3m;
            decimal denominator = 1m - commissionRate - profitRate - WithdrawalRate;
            decimal salePrice = (purchasePrice + logisticsFee) / denominator;

            return new PricingResult
            {
                Weight = weight,
                PurchasePrice = purchasePrice,
                LogisticsFee = logisticsFee,
                CommissionFee = commissionRate * salePrice,
                ProfitAmount = profitRate * salePrice,
                SalePrice = salePrice,
                CommissionRate = commissionRate,
                ProfitRate = profitRate,
                Segment = lightParcel ? "≤ 0.3 kg" : "> 0.3 kg",
                FreightFormula = lightParcel ? "58 × 重量 + 2 + 3" : "43 × 重量 + 8 + 3"
            };
        }
    }
}
