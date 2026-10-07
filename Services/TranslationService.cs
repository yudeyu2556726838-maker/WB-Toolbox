using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WBToolbox.Native.Services
{
    internal sealed class TranslationResult
    {
        internal string Text { get; set; }
        internal string Provider { get; set; }
    }

    internal interface ITranslationService : IDisposable
    {
        Task<TranslationResult> TranslateAsync(
            string text,
            string sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken);
    }

    internal sealed class TranslationUnavailableException : InvalidOperationException
    {
        internal TranslationUnavailableException(string details, Exception cause)
            : base("翻译暂不可用，请稍后重试；悬停查看原因", cause)
        {
            Details = details;
        }

        internal string Details { get; private set; }
    }

    internal sealed class TranslationService : ITranslationService
    {
        private sealed class BingSession
        {
            internal string Ig { get; set; }
            internal string Key { get; set; }
            internal string Token { get; set; }
            internal DateTimeOffset ExpiresAt { get; set; }
        }

        private sealed class ProviderLimitException : InvalidOperationException
        {
            internal ProviderLimitException(string message) : base(message) { }
        }

        private const int MaximumCharacters = 2000;
        internal const int TranslationBudgetMilliseconds = 15000;
        private const int FallbackHedgeMilliseconds = 120;
        private const int CacheCapacity = 64;
        private readonly HttpClient httpClient;
        private readonly int budgetMilliseconds;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly bool enableBing;
        private readonly object cacheGate = new object();
        private readonly object bingGate = new object();
        private readonly Dictionary<string, TranslationResult> cache = new Dictionary<string, TranslationResult>();
        private readonly Queue<string> cacheOrder = new Queue<string>();
        private readonly Dictionary<string, DateTimeOffset> retryAfter = new Dictionary<string, DateTimeOffset>();
        private BingSession bingSession;
        private Task<BingSession> bingSessionTask;
        private DateTimeOffset bingRetryAfter;

        internal TranslationService()
            : this(new HttpClientHandler { UseCookies = true }, TranslationBudgetMilliseconds,
                () => DateTimeOffset.UtcNow, true)
        {
        }

        internal TranslationService(HttpMessageHandler handler)
            : this(handler, TranslationBudgetMilliseconds, () => DateTimeOffset.UtcNow, false)
        {
        }

        internal TranslationService(HttpMessageHandler handler, int budgetMilliseconds, Func<DateTimeOffset> utcNow)
            : this(handler, budgetMilliseconds, utcNow, false)
        {
        }

        internal TranslationService(
            HttpMessageHandler handler,
            int budgetMilliseconds,
            Func<DateTimeOffset> utcNow,
            bool enableBing)
        {
            if (budgetMilliseconds <= 0) throw new ArgumentOutOfRangeException("budgetMilliseconds");
            if (utcNow == null) throw new ArgumentNullException("utcNow");
            this.budgetMilliseconds = budgetMilliseconds;
            this.utcNow = utcNow;
            this.enableBing = enableBing;
            httpClient = new HttpClient(handler);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WBToolbox/4.0");
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
        }

        public async Task<TranslationResult> TranslateAsync(
            string text,
            string sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            string normalized = (text ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                throw new InvalidOperationException("请输入需要翻译的内容");
            }
            if (normalized.Length > MaximumCharacters)
            {
                throw new InvalidOperationException("翻译内容不能超过 2000 个字符");
            }

            string source = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage;
            string target = string.IsNullOrWhiteSpace(targetLanguage) ? "ru" : targetLanguage;
            cancellationToken.ThrowIfCancellationRequested();
            string resolvedSource = source == "auto" ? DetectLanguage(normalized) : source;
            if (resolvedSource == target)
            {
                return new TranslationResult { Text = normalized, Provider = "无需翻译" };
            }

            TranslationResult localResult;
            if (TryTranslateLocalQuantity(normalized, resolvedSource, target, out localResult))
            {
                return localResult;
            }

            string cacheKey = resolvedSource + "\0" + target + "\0" + normalized;
            TranslationResult cached;
            if (TryGetCached(cacheKey, out cached)) return cached;

            using (CancellationTokenSource raceCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task deadline = Task.Delay(budgetMilliseconds, raceCancellation.Token);
                Task<TranslationResult> google = TranslateWithGoogleAsync(
                    normalized, source, target, raceCancellation.Token);
                Task<TranslationResult> bing = enableBing
                    ? TranslateWithBingAsync(normalized, source, target, raceCancellation.Token)
                    : null;
                List<Task<TranslationResult>> primaryProviders = new List<Task<TranslationResult>> { google };
                if (bing != null) primaryProviders.Add(bing);
                Task<TranslationResult> myMemory = TranslateWithMyMemoryAfterDelayAsync(
                    normalized, resolvedSource, target, primaryProviders, raceCancellation.Token);
                List<Task<TranslationResult>> pending = new List<Task<TranslationResult>> { google, myMemory };
                if (bing != null) pending.Insert(1, bing);
                Exception googleError = null;
                Exception bingError = null;
                Exception myMemoryError = null;

                try
                {
                    while (pending.Count > 0)
                    {
                        List<Task> candidates = new List<Task>(pending);
                        candidates.Add(deadline);
                        Task finished = await Task.WhenAny(candidates).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (finished == deadline)
                        {
                            Exception timeout = new TimeoutException("网络响应超时，请检查连接后重试");
                            if (pending.Contains(google)) googleError = timeout;
                            if (bing != null && pending.Contains(bing)) bingError = timeout;
                            if (pending.Contains(myMemory)) myMemoryError = timeout;
                            break;
                        }
                        Task<TranslationResult> completed = (Task<TranslationResult>)finished;
                        pending.Remove(completed);
                        try
                        {
                            TranslationResult result = await completed.ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            SaveToCache(cacheKey, result);
                            return result;
                        }
                        catch (OperationCanceledException)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            Exception timeout = new TimeoutException("连接超时");
                            if (ReferenceEquals(completed, google)) googleError = timeout;
                            else if (ReferenceEquals(completed, bing)) bingError = timeout;
                            else myMemoryError = timeout;
                        }
                        catch (Exception error)
                        {
                            if (ReferenceEquals(completed, google)) googleError = error;
                            else if (ReferenceEquals(completed, bing)) bingError = error;
                            else myMemoryError = error;
                        }
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TranslationUnavailableException(
                        "Google：" + ErrorMessage(googleError) +
                        (enableBing ? "\nBing：" + ErrorMessage(bingError) : string.Empty) +
                        "\nMyMemory：" + ErrorMessage(myMemoryError),
                        myMemoryError ?? bingError ?? googleError);
                }
                finally
                {
                    raceCancellation.Cancel();
                    // Cancellation, failure and timeout must also observe both providers.
                    ObserveFailure(google);
                    if (bing != null) ObserveFailure(bing);
                    ObserveFailure(myMemory);
                }
            }
        }

        internal void WarmUp()
        {
            if (!enableBing) return;
            ObserveFailure(GetBingSessionAsync(CancellationToken.None));
        }

        private async Task<TranslationResult> TranslateWithBingAsync(
            string text, string source, string target, CancellationToken cancellationToken)
        {
            BingSession session = await GetBingSessionAsync(cancellationToken).ConfigureAwait(false);
            string host = "www.bing.com";
            ThrowIfProviderDeferred(host);
            string sourceCode = source == "auto" ? "auto-detect" : ToBingLanguage(source);
            string targetCode = ToBingLanguage(target);
            string url = "https://www.bing.com/ttranslatev3?isVertical=1&IG=" +
                Uri.EscapeDataString(session.Ig) + "&IID=translator.5028.1";
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Referrer = new Uri("https://www.bing.com/translator");
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "fromLang", sourceCode },
                    { "text", text },
                    { "to", targetCode },
                    { "token", session.Token },
                    { "key", session.Key }
                });
                using (HttpResponseMessage response = await httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == HttpStatusCode.Forbidden ||
                            response.StatusCode == HttpStatusCode.Unauthorized ||
                            (int)response.StatusCode == 429)
                        {
                            InvalidateBingSession();
                        }
                        ThrowForProviderStatus(host, response);
                    }
                    string json = Encoding.UTF8.GetString(
                        await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
                    string translated = ParseBingResponse(json);
                    if (!IsTargetScript(translated, target))
                        throw new InvalidOperationException("Bing 翻译结果与目标语言不符");
                    return new TranslationResult { Text = translated, Provider = "Bing 翻译" };
                }
            }
        }

        private Task<BingSession> GetBingSessionAsync(CancellationToken cancellationToken)
        {
            Task<BingSession> shared;
            lock (bingGate)
            {
                if (bingSession != null && bingSession.ExpiresAt > utcNow().AddMinutes(1))
                    return Task.FromResult(bingSession);
                bool failedRecently = bingSessionTask != null &&
                    (bingSessionTask.IsCanceled || bingSessionTask.IsFaulted) &&
                    bingRetryAfter > utcNow();
                if (!failedRecently && (bingSessionTask == null || bingSessionTask.IsCanceled ||
                    bingSessionTask.IsFaulted || bingSessionTask.IsCompleted))
                    bingSessionTask = FetchBingSessionAsync();
                shared = bingSessionTask;
            }
            return AwaitBingSessionAsync(shared, cancellationToken);
        }

        private static async Task<BingSession> AwaitBingSessionAsync(
            Task<BingSession> shared, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled) return await shared.ConfigureAwait(false);
            Task cancelled = Task.Delay(Timeout.Infinite, cancellationToken);
            Task finished = await Task.WhenAny(shared, cancelled).ConfigureAwait(false);
            if (!ReferenceEquals(finished, shared)) cancellationToken.ThrowIfCancellationRequested();
            return await shared.ConfigureAwait(false);
        }

        private async Task<BingSession> FetchBingSessionAsync()
        {
            try
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    string html = await DownloadStringAsync(
                        "https://www.bing.com/translator", timeout.Token).ConfigureAwait(false);
                    Match ig = Regex.Match(html, "IG:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    Match abuse = Regex.Match(html,
                        "params_AbusePreventionHelper\\s*=\\s*\\[\\s*([0-9]+)\\s*,\\s*\"([^\"]+)\"\\s*,\\s*([0-9]+)\\s*\\]",
                        RegexOptions.IgnoreCase);
                    if (!ig.Success || !abuse.Success)
                        throw new InvalidOperationException("Bing 翻译会话初始化失败");
                    int lifetimeMilliseconds;
                    if (!int.TryParse(abuse.Groups[3].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out lifetimeMilliseconds))
                    {
                        lifetimeMilliseconds = 3600000;
                    }
                    BingSession session = new BingSession
                    {
                        Ig = ig.Groups[1].Value,
                        Key = abuse.Groups[1].Value,
                        Token = abuse.Groups[2].Value,
                        ExpiresAt = utcNow().AddMilliseconds(Math.Max(120000, lifetimeMilliseconds))
                    };
                    lock (bingGate)
                    {
                        bingSession = session;
                        bingRetryAfter = DateTimeOffset.MinValue;
                    }
                    return session;
                }
            }
            catch
            {
                lock (bingGate) bingRetryAfter = utcNow().AddSeconds(10);
                throw;
            }
        }

        private void InvalidateBingSession()
        {
            lock (bingGate)
            {
                bingSession = null;
                bingSessionTask = null;
            }
        }

        private static string ToBingLanguage(string language)
        {
            return language == "zh-CN" ? "zh-Hans" : language;
        }

        private async Task<TranslationResult> TranslateWithGoogleAsync(
            string text, string source, string target, CancellationToken cancellationToken)
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=" +
                Uri.EscapeDataString(source) + "&tl=" + Uri.EscapeDataString(target) +
                "&dt=t&q=" + Uri.EscapeDataString(text);
            string json = await DownloadStringAsync(url, cancellationToken).ConfigureAwait(false);
            string translated = ParseGoogleResponse(json);
            if (!IsTargetScript(translated, target))
                throw new InvalidOperationException("翻译结果与目标语言不符");
            return new TranslationResult { Text = translated, Provider = "Google 公共翻译" };
        }

        private async Task<TranslationResult> TranslateWithMyMemoryAsync(
            string text, string source, string target, CancellationToken cancellationToken)
        {
            IList<string> chunks = SplitByUtf8Bytes(text, 450);
            Task<string>[] translations = new Task<string>[chunks.Count];
            using (SemaphoreSlim concurrency = new SemaphoreSlim(3, 3))
            {
                for (int index = 0; index < chunks.Count; index++)
                    translations[index] = TranslateMyMemoryChunkAsync(
                        chunks[index], source, target, concurrency, cancellationToken);
                string[] translatedChunks = await Task.WhenAll(translations).ConfigureAwait(false);
                return new TranslationResult { Text = string.Concat(translatedChunks), Provider = "MyMemory" };
            }
        }

        private async Task<TranslationResult> TranslateWithMyMemoryAfterDelayAsync(
            string text, string source, string target, IList<Task<TranslationResult>> primaryProviders,
            CancellationToken cancellationToken)
        {
            // A failed/rate-limited primary needs an immediate fallback. A healthy one
            // gets a brief head start to avoid spending public quota on duplicate work.
            Task allPrimary = Task.WhenAll(primaryProviders);
            ObserveFailure(allPrimary);
            await Task.WhenAny(allPrimary, Task.Delay(FallbackHedgeMilliseconds, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (Task<TranslationResult> primary in primaryProviders)
            {
                if (primary.Status == TaskStatus.RanToCompletion)
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            return await TranslateWithMyMemoryAsync(text, source, target, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> TranslateMyMemoryChunkAsync(
            string chunk,
            string source,
            string target,
            SemaphoreSlim concurrency,
            CancellationToken cancellationToken)
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string url = "https://api.mymemory.translated.net/get?q=" +
                    Uri.EscapeDataString(chunk) + "&langpair=" +
                    Uri.EscapeDataString(source + "|" + target) + "&mt=1";
                string json = await DownloadStringAsync(url, cancellationToken).ConfigureAwait(false);
                return ParseMyMemoryResponse(json, chunk, source, target);
            }
            catch (ProviderLimitException)
            {
                DeferProvider("api.mymemory.translated.net", utcNow().AddSeconds(60));
                throw;
            }
            finally
            {
                concurrency.Release();
            }
        }

        private bool TryGetCached(string key, out TranslationResult result)
        {
            lock (cacheGate)
            {
                TranslationResult stored;
                if (cache.TryGetValue(key, out stored))
                {
                    result = new TranslationResult { Text = stored.Text, Provider = stored.Provider + " · 缓存" };
                    return true;
                }
            }
            result = null;
            return false;
        }

        private void SaveToCache(string key, TranslationResult result)
        {
            lock (cacheGate)
            {
                if (cache.ContainsKey(key)) return;
                while (cache.Count >= CacheCapacity && cacheOrder.Count > 0)
                    cache.Remove(cacheOrder.Dequeue());
                cache[key] = new TranslationResult { Text = result.Text, Provider = result.Provider };
                cacheOrder.Enqueue(key);
            }
        }

        private static void ObserveFailure(Task<TranslationResult> task)
        {
            ObserveFailure((Task)task);
        }

        private static void ObserveFailure(Task task)
        {
            task.ContinueWith(
                completed => { Exception ignored = completed.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private static string ErrorMessage(Exception error)
        {
            return error == null ? "无有效结果" : error.Message;
        }

        private static IList<string> SplitByUtf8Bytes(string text, int maximumBytes)
        {
            List<string> chunks = new List<string>();
            StringBuilder current = new StringBuilder();
            int currentBytes = 0;
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                int elementBytes = Encoding.UTF8.GetByteCount(element);
                if (current.Length > 0 && currentBytes + elementBytes > maximumBytes)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                    currentBytes = 0;
                }
                current.Append(element);
                currentBytes += elementBytes;
            }
            if (current.Length > 0)
            {
                chunks.Add(current.ToString());
            }
            return chunks;
        }

        internal static string DetectLanguage(string text)
        {
            bool hasCyrillic = false;
            bool hasHan = false;
            foreach (char character in text ?? string.Empty)
            {
                if (character >= '\u3040' && character <= '\u30ff') return "ja";
                if (character >= '\u0400' && character <= '\u04ff') hasCyrillic = true;
                if (character >= '\u3400' && character <= '\u9fff') hasHan = true;
            }
            if (hasCyrillic) return "ru";
            if (hasHan) return "zh-CN";
            return "en";
        }

        internal static bool TryTranslateLocalQuantity(
            string text, string source, string target, out TranslationResult result)
        {
            result = null;
            if (source != "zh-CN") return false;
            Match match = Regex.Match(text ?? string.Empty, "^\\s*([0-9]{1,6})\\s*个\\s*$");
            int quantity;
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out quantity)) return false;

            string translated;
            if (target == "ru")
            {
                int lastTwo = quantity % 100;
                int last = quantity % 10;
                string noun = lastTwo >= 11 && lastTwo <= 14
                    ? "штук"
                    : last == 1 ? "штука" : last >= 2 && last <= 4 ? "штуки" : "штук";
                translated = quantity.ToString(CultureInfo.InvariantCulture) + " " + noun;
            }
            else if (target == "en")
            {
                translated = quantity.ToString(CultureInfo.InvariantCulture) +
                    (quantity == 1 ? " item" : " items");
            }
            else if (target == "ja")
            {
                translated = quantity.ToString(CultureInfo.InvariantCulture) + "個";
            }
            else
            {
                return false;
            }
            result = new TranslationResult { Text = translated, Provider = "本地电商短语 · 即时" };
            return true;
        }

        private async Task<string> DownloadStringAsync(string url, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string host = new Uri(url).Host;
            ThrowIfProviderDeferred(host);
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Accept.ParseAdd("application/json");
                using (HttpResponseMessage response = await httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) ThrowForProviderStatus(host, response);
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return Encoding.UTF8.GetString(bytes);
                }
            }
        }

        private void ThrowIfProviderDeferred(string host)
        {
            lock (cacheGate)
            {
                DateTimeOffset available;
                DateTimeOffset now = utcNow();
                if (retryAfter.TryGetValue(host, out available) && available > now)
                    throw new InvalidOperationException("接口暂时限流，约 " +
                        Math.Ceiling((available - now).TotalSeconds) + " 秒后恢复尝试");
            }
        }

        private void ThrowForProviderStatus(string host, HttpResponseMessage response)
        {
            if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.Forbidden)
            {
                DateTimeOffset now = utcNow();
                DateTimeOffset available = now.AddSeconds(60);
                if (response.Headers.RetryAfter != null)
                {
                    if (response.Headers.RetryAfter.Date.HasValue)
                        available = response.Headers.RetryAfter.Date.Value;
                    else if (response.Headers.RetryAfter.Delta.HasValue)
                        available = now.Add(response.Headers.RetryAfter.Delta.Value);
                }
                DeferProvider(host, available);
                throw new InvalidOperationException((int)response.StatusCode == 429
                    ? "请求被限流（429），已暂时停止重复请求"
                    : "服务拒绝访问（403），已暂时停止重复请求");
            }
            throw new HttpRequestException("服务返回 HTTP " + (int)response.StatusCode);
        }

        private void DeferProvider(string host, DateTimeOffset available)
        {
            lock (cacheGate)
            {
                DateTimeOffset existing;
                if (!retryAfter.TryGetValue(host, out existing) || available > existing)
                    retryAfter[host] = available;
            }
        }

        private string ParseGoogleResponse(string json)
        {
            object[] root = new JavaScriptSerializer().DeserializeObject(json) as object[];
            object[] segments = root == null || root.Length == 0 ? null : root[0] as object[];
            if (segments == null)
            {
                throw new InvalidOperationException("Google 翻译响应格式无效");
            }

            StringBuilder builder = new StringBuilder();
            foreach (object segmentValue in segments)
            {
                object[] segment = segmentValue as object[];
                if (segment != null && segment.Length > 0 && segment[0] != null)
                {
                    builder.Append(Convert.ToString(segment[0]));
                }
            }

            string result = builder.ToString().Trim();
            if (result.Length == 0)
            {
                throw new InvalidOperationException("Google 翻译未返回结果");
            }
            return result;
        }

        internal static string ParseBingResponse(string json)
        {
            object[] root = new JavaScriptSerializer().DeserializeObject(json) as object[];
            IDictionary<string, object> first = root != null && root.Length > 0
                ? root[0] as IDictionary<string, object> : null;
            object translationsValue;
            object[] translations = first != null && first.TryGetValue("translations", out translationsValue)
                ? translationsValue as object[] : null;
            IDictionary<string, object> translation = translations != null && translations.Length > 0
                ? translations[0] as IDictionary<string, object> : null;
            string translated = ReadValue(translation, "text").Trim();
            if (translated.Length == 0)
                throw new InvalidOperationException("Bing 翻译响应格式无效");
            return translated;
        }

        internal static string ParseMyMemoryResponse(string json, string original, string source, string target)
        {
            IDictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
            string status = ReadValue(root, "responseStatus");
            if (status == "429" || status == "403" || ReadValue(root, "quotaFinished") == "True")
                throw new ProviderLimitException("备用翻译额度已用尽或被限流，请稍后再试");
            if (status != "200")
            {
                throw new InvalidOperationException("备用翻译服务返回错误，请稍后再试");
            }

            // Public translation memories can contain incorrectly labelled entries.
            // Validate the original segment and actual script, not just the language metadata.
            object matchesValue;
            object[] matches = root.TryGetValue("matches", out matchesValue) ? matchesValue as object[] : null;
            string best = null;
            double bestScore = -1;
            foreach (object value in matches ?? new object[0])
            {
                IDictionary<string, object> match = value as IDictionary<string, object>;
                double score;
                bool machineTranslation = ReadValue(match, "id") == "0" && ReadValue(match, "created-by") == "MT!";
                string candidate = WebUtility.HtmlDecode(ReadValue(match, "translation")).Trim();
                if (!string.Equals(ReadValue(match, "segment").Trim(), original.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    !SameLanguage(ReadValue(match, "source"), source) ||
                    !SameLanguage(ReadValue(match, "target"), target) ||
                    !double.TryParse(ReadValue(match, "match"), NumberStyles.Float, CultureInfo.InvariantCulture, out score) ||
                    (!machineTranslation && score < 0.9) || !IsTargetScript(candidate, target))
                {
                    continue;
                }
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            if (best != null) return best;

            object responseDataValue;
            IDictionary<string, object> responseData = root != null && root.TryGetValue("responseData", out responseDataValue)
                ? responseDataValue as IDictionary<string, object>
                : null;
            object translatedValue;
            string translated = responseData != null && responseData.TryGetValue("translatedText", out translatedValue)
                ? WebUtility.HtmlDecode(Convert.ToString(translatedValue)).Trim()
                : string.Empty;
            // If candidates were provided but none passed validation, do not trust
            // the same unvalidated top entry repeated in responseData.
            if ((matches != null && matches.Length > 0) || !IsTargetScript(translated, target))
            {
                throw new InvalidOperationException("备用服务未返回可靠的目标语言译文，请重试");
            }
            return translated;
        }

        private static string ReadValue(IDictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value)
                ? Convert.ToString(value, CultureInfo.InvariantCulture) : string.Empty;
        }

        private static bool SameLanguage(string actual, string expected)
        {
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
            if (expected == "zh-CN") return actual == "zh" || actual == "zh-Hans";
            return actual.StartsWith(expected + "-", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsTargetScript(string text, string target)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            bool hasLetters = false;
            bool hasExpectedScript = false;
            foreach (char c in text)
            {
                if (!char.IsLetter(c)) continue;
                hasLetters = true;
                bool han = c >= '\u3400' && c <= '\u9fff';
                bool kana = c >= '\u3040' && c <= '\u30ff';
                bool cyrillic = c >= '\u0400' && c <= '\u04ff';
                bool latin = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                if ((target == "zh-CN" && han) || (target == "ja" && (han || kana)) ||
                    (target == "ru" && cyrillic) || (target == "en" && latin)) hasExpectedScript = true;
            }
            return !hasLetters || hasExpectedScript;
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }
    }
}
