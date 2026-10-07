using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using WBToolbox.Native.Core;

namespace WBToolbox.Native.Services
{
    internal sealed class ExchangeRateService : IDisposable
    {
        private const string CbrEndpoint = "https://www.cbr.ru/scripts/XML_daily_eng.asp";
        private const string MoexEndpoint = "https://iss.moex.com/iss/engines/currency/markets/selt/boards/CETS/securities/CNYRUB_TOM.xml?iss.meta=off&iss.only=marketdata%2Csecurities&marketdata.columns=SECID%2CBOARDID%2CBID%2COFFER%2CLAST%2CTIME%2CWAPRICE%2CMARKETPRICETODAY%2CTRADINGSTATUS%2CUPDATETIME%2CSYSTIME&securities.columns=SECID%2CBOARDID%2CPREVPRICE%2CPREVWAPRICE%2CPREVDATE";

        private readonly HttpClient httpClient;
        private CurrencyMatrix cachedCbrMatrix;
        private DateTimeOffset cachedCbrAt;

        internal ExchangeRateService()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.UseCookies = false;
            httpClient = new HttpClient(handler);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WBToolbox/4.0");
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
        }

        internal async Task<CurrencyMatrix> FetchAsync(CancellationToken cancellationToken)
        {
            CurrencyMatrix cbr = await FetchCbrAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<string, decimal> rates = cbr.CopyRates();
            string source = "俄罗斯央行";

            try
            {
                decimal moexCnyRate = await FetchMoexCnyRateAsync(cancellationToken).ConfigureAwait(false);
                if (moexCnyRate > 0)
                {
                    rates["CNY"] = moexCnyRate;
                    source = "MOEX CNY/RUB + 俄罗斯央行交叉汇率";
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                source = "俄罗斯央行（MOEX 暂不可用）";
            }

            return new CurrencyMatrix(rates, DateTimeOffset.Now, source);
        }

        private async Task<CurrencyMatrix> FetchCbrAsync(CancellationToken cancellationToken)
        {
            if (cachedCbrMatrix != null && DateTimeOffset.Now - cachedCbrAt < TimeSpan.FromMinutes(1))
            {
                return cachedCbrMatrix;
            }

            byte[] bytes = await DownloadBytesAsync(CbrEndpoint, cancellationToken).ConfigureAwait(false);
            string xmlText = Encoding.GetEncoding(1251).GetString(bytes);
            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            document.LoadXml(xmlText);

            Dictionary<string, decimal> rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            rates["RUB"] = 1m;
            XmlNodeList nodes = document.SelectNodes("/ValCurs/Valute");
            foreach (XmlNode node in nodes)
            {
                string code = ReadNode(node, "CharCode").ToUpperInvariant();
                if (code != "CNY" && code != "USD" && code != "JPY")
                {
                    continue;
                }

                decimal unitRate;
                if (!TryParseProviderDecimal(ReadNode(node, "VunitRate"), out unitRate) || unitRate <= 0)
                {
                    decimal value;
                    decimal nominal;
                    if (!TryParseProviderDecimal(ReadNode(node, "Value"), out value) ||
                        !TryParseProviderDecimal(ReadNode(node, "Nominal"), out nominal) || nominal <= 0)
                    {
                        continue;
                    }
                    unitRate = value / nominal;
                }
                rates[code] = unitRate;
            }

            if (!rates.ContainsKey("CNY") || !rates.ContainsKey("USD") || !rates.ContainsKey("JPY"))
            {
                throw new InvalidOperationException("俄罗斯央行响应缺少必要币种");
            }

            cachedCbrMatrix = new CurrencyMatrix(rates, DateTimeOffset.Now, "俄罗斯央行");
            cachedCbrAt = DateTimeOffset.Now;
            return cachedCbrMatrix;
        }

        private async Task<decimal> FetchMoexCnyRateAsync(CancellationToken cancellationToken)
        {
            byte[] bytes = await DownloadBytesAsync(MoexEndpoint, cancellationToken).ConfigureAwait(false);
            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            document.LoadXml(Encoding.UTF8.GetString(bytes));

            XmlNode marketRow = document.SelectSingleNode("//data[@id='marketdata']/rows/row");
            XmlNode securityRow = document.SelectSingleNode("//data[@id='securities']/rows/row");
            if (marketRow == null)
            {
                throw new InvalidOperationException("MOEX 响应缺少行情数据");
            }

            decimal bid;
            decimal offer;
            string tradingStatus = ReadAttribute(marketRow, "TRADINGSTATUS");
            bool hasBid = TryParseProviderDecimal(ReadAttribute(marketRow, "BID"), out bid);
            bool hasOffer = TryParseProviderDecimal(ReadAttribute(marketRow, "OFFER"), out offer);
            if (tradingStatus == "T" && hasBid && hasOffer && bid > 0 && offer >= bid)
            {
                decimal middle = (bid + offer) / 2m;
                if ((offer - bid) / middle <= 0.01m)
                {
                    return middle;
                }
            }

            string[] marketFields = { "LAST", "WAPRICE", "MARKETPRICETODAY" };
            foreach (string field in marketFields)
            {
                decimal rate;
                if (TryParseProviderDecimal(ReadAttribute(marketRow, field), out rate) && rate > 0)
                {
                    return rate;
                }
            }

            if (securityRow != null)
            {
                string[] previousFields = { "PREVWAPRICE", "PREVPRICE" };
                foreach (string field in previousFields)
                {
                    decimal rate;
                    if (TryParseProviderDecimal(ReadAttribute(securityRow, field), out rate) && rate > 0)
                    {
                        return rate;
                    }
                }
            }

            throw new InvalidOperationException("MOEX 暂无有效 CNY/RUB 行情");
        }

        private async Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Accept.ParseAdd("application/xml,text/xml;q=0.9,*/*;q=0.1");
                    using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        private static string ReadNode(XmlNode node, string childName)
        {
            XmlNode child = node.SelectSingleNode(childName);
            return child == null ? string.Empty : child.InnerText.Trim();
        }

        private static string ReadAttribute(XmlNode node, string name)
        {
            XmlAttribute attribute = node.Attributes == null ? null : node.Attributes[name];
            return attribute == null ? string.Empty : attribute.Value;
        }

        private static bool TryParseProviderDecimal(string value, out decimal result)
        {
            return decimal.TryParse(
                (value ?? string.Empty).Trim().Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }
    }
}
