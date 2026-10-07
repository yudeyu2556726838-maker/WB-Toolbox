using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace WBToolbox.Native.Core
{
    internal sealed class CommissionCategoryOption
    {
        internal CommissionCategoryOption(string category, string product, decimal commissionPercent)
        {
            Category = category;
            Product = product;
            CommissionPercent = commissionPercent;
            Key = category + "\u001f" + product;
            SearchText = product + " " + category;
        }

        internal string Category { get; private set; }
        internal string Product { get; private set; }
        internal decimal CommissionPercent { get; private set; }
        internal string Key { get; private set; }
        internal string SearchText { get; private set; }
        internal string DisplayName { get { return Product + " · " + Category; } }

        public override string ToString()
        {
            return DisplayName + " · " +
                CommissionPercent.ToString("0.##", CultureInfo.CurrentCulture) + "%";
        }
    }

    internal static class CommissionCatalog
    {
        private sealed class SearchHit
        {
            internal CommissionCategoryOption Option;
            internal int Rank;
        }

        private const string ResourceName = "WBToolbox.Native.Assets.category-commissions.tsv";
        private static readonly Lazy<IReadOnlyList<CommissionCategoryOption>> catalog =
            new Lazy<IReadOnlyList<CommissionCategoryOption>>(LoadEmbedded, true);

        internal static IReadOnlyList<CommissionCategoryOption> Options
        {
            get { return catalog.Value; }
        }

        internal static IList<CommissionCategoryOption> Search(string query, int limit, out int totalMatches)
        {
            if (limit <= 0) throw new ArgumentOutOfRangeException("limit");
            string normalized = (query ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                totalMatches = 0;
                return new List<CommissionCategoryOption>();
            }

            string[] tokens = normalized.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            List<SearchHit> hits = new List<SearchHit>();
            foreach (CommissionCategoryOption option in Options)
            {
                bool matches = true;
                foreach (string token in tokens)
                {
                    if (option.SearchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        matches = false;
                        break;
                    }
                }
                if (!matches) continue;
                hits.Add(new SearchHit { Option = option, Rank = MatchRank(option, normalized) });
            }

            hits.Sort(delegate(SearchHit left, SearchHit right)
            {
                int byRank = left.Rank.CompareTo(right.Rank);
                if (byRank != 0) return byRank;
                int byProduct = StringComparer.CurrentCulture.Compare(left.Option.Product, right.Option.Product);
                return byProduct != 0
                    ? byProduct
                    : StringComparer.CurrentCulture.Compare(left.Option.Category, right.Option.Category);
            });

            totalMatches = hits.Count;
            List<CommissionCategoryOption> results = new List<CommissionCategoryOption>(
                Math.Min(limit, hits.Count));
            for (int index = 0; index < hits.Count && index < limit; index++)
                results.Add(hits[index].Option);
            return results;
        }

        internal static CommissionCategoryOption FindByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (CommissionCategoryOption option in Options)
                if (string.Equals(option.Key, key, StringComparison.Ordinal)) return option;
            return null;
        }

        internal static IReadOnlyList<CommissionCategoryOption> LoadFromText(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            List<CommissionCategoryOption> result = new List<CommissionCategoryOption>();
            using (StringReader reader = new StringReader(text))
            {
                string line;
                int lineNumber = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    if (lineNumber == 1) continue;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] fields = line.Split('\t');
                    decimal percent;
                    if (fields.Length != 3 || string.IsNullOrWhiteSpace(fields[0]) ||
                        string.IsNullOrWhiteSpace(fields[1]) ||
                        !decimal.TryParse(fields[2], NumberStyles.Number,
                            CultureInfo.InvariantCulture, out percent) || percent < 0 || percent >= 100)
                    {
                        throw new InvalidDataException("类目佣金数据第 " + lineNumber + " 行无效");
                    }
                    result.Add(new CommissionCategoryOption(
                        fields[0].Trim(), fields[1].Trim(), percent));
                }
            }
            if (result.Count == 0) throw new InvalidDataException("类目佣金数据为空");
            return result;
        }

        private static IReadOnlyList<CommissionCategoryOption> LoadEmbedded()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidDataException("未找到内置类目佣金数据");
                using (StreamReader reader = new StreamReader(
                    stream, new UTF8Encoding(false, true), true, 4096, false))
                {
                    return LoadFromText(reader.ReadToEnd());
                }
            }
        }

        private static int MatchRank(CommissionCategoryOption option, string query)
        {
            if (string.Equals(option.Product, query, StringComparison.OrdinalIgnoreCase)) return 0;
            if (option.Product.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
            if (option.Product.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (option.Category.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 3;
            return 4;
        }
    }
}
