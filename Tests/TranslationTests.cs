using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using WBToolbox.Native.Services;

namespace WBToolbox.Native.Tests
{
    internal static class TranslationTests
    {
        private const string BadTopEntry = "{\"responseStatus\":200,\"quotaFinished\":false,\"responseData\":{\"translatedText\":\"САША БИБЛИОТЕКА ПОЗОРНАЯ\"},\"matches\":[" +
            "{\"segment\":\"привет\",\"translation\":\"САША БИБЛИОТЕКА ПОЗОРНАЯ\",\"source\":\"ru-RU\",\"target\":\"zh-CN\",\"match\":1}," +
            "{\"segment\":\"привет\",\"translation\":\"你好\",\"source\":\"ru-RU\",\"target\":\"zh-CN\",\"match\":0.99}]}";

        private sealed class FakeHandler : HttpMessageHandler
        {
            internal int Calls;
            internal int GoogleCalls;
            internal int MyMemoryCalls;
            internal bool TimeoutPrimary;
            internal bool GoogleSucceeds;
            internal bool MyMemoryInvalid;
            internal int GoogleDelayMilliseconds;
            internal int MyMemoryDelayMilliseconds;
            internal int GoogleCancellations;
            internal HttpStatusCode GoogleStatus = HttpStatusCode.ServiceUnavailable;
            internal RetryConditionHeaderValue GoogleRetryAfter;
            internal HttpStatusCode MyMemoryStatus = HttpStatusCode.OK;
            internal bool MyMemoryQuotaExhausted;
            internal int MyMemoryResponseStatus = 200;
            internal bool IgnoreCancellation;
            internal bool FailAfterDelay;
            internal int LateFailures;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Interlocked.Increment(ref Calls);
                string original = ReadQuery(request.RequestUri, "q");
                if (request.RequestUri.Host == "translate.googleapis.com")
                {
                    Interlocked.Increment(ref GoogleCalls);
                    if (GoogleDelayMilliseconds > 0)
                    {
                        try { await Task.Delay(GoogleDelayMilliseconds, IgnoreCancellation ? CancellationToken.None : token); }
                        catch (OperationCanceledException)
                        {
                            Interlocked.Increment(ref GoogleCancellations);
                            throw;
                        }
                    }
                    ThrowLateFailureIfRequested();
                    if (TimeoutPrimary) throw new TaskCanceledException("provider timeout");
                    if (GoogleSucceeds)
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[[[\"你好\",\"привет\"]]]") };
                    HttpResponseMessage failure = new HttpResponseMessage(GoogleStatus);
                    failure.Headers.RetryAfter = GoogleRetryAfter;
                    return failure;
                }
                Interlocked.Increment(ref MyMemoryCalls);
                if (MyMemoryDelayMilliseconds > 0)
                    await Task.Delay(MyMemoryDelayMilliseconds, IgnoreCancellation ? CancellationToken.None : token);
                ThrowLateFailureIfRequested();
                if (!Uri.UnescapeDataString(request.RequestUri.Query).Contains("ru|zh-CN"))
                    throw new Exception("Wrong fallback language pair");
                string response = MyMemoryInvalid ? BadTopEntry.Replace("你好", "ошибка") : BadTopEntry;
                response = response.Replace("\"segment\":\"привет\"", "\"segment\":" + new JavaScriptSerializer().Serialize(original));
                response = response.Replace("\"responseStatus\":200", "\"responseStatus\":" + MyMemoryResponseStatus);
                if (MyMemoryQuotaExhausted) response = response.Replace("\"quotaFinished\":false", "\"quotaFinished\":true");
                return new HttpResponseMessage(MyMemoryStatus) { Content = new StringContent(response) };
            }

            private void ThrowLateFailureIfRequested()
            {
                if (!FailAfterDelay) return;
                Interlocked.Increment(ref LateFailures);
                throw new HttpRequestException("late provider failure regression");
            }

            private static string ReadQuery(Uri uri, string key)
            {
                foreach (string field in uri.Query.TrimStart('?').Split('&'))
                {
                    string[] pair = field.Split(new[] { '=' }, 2);
                    if (pair.Length == 2 && pair[0] == key) return Uri.UnescapeDataString(pair[1]);
                }
                return string.Empty;
            }
        }

        private sealed class BingHandler : HttpMessageHandler
        {
            internal int BingBootstrapCalls;
            internal int BingTranslationCalls;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request.RequestUri.Host == "www.bing.com" && request.Method == HttpMethod.Get)
                {
                    Interlocked.Increment(ref BingBootstrapCalls);
                    string page = "<script>var params_AbusePreventionHelper = [123456,\"test-token\",3600000];" +
                        "var config={IG:\"ABCDEF123456\"};</script>";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(page)
                    });
                }
                if (request.RequestUri.Host == "www.bing.com" && request.Method == HttpMethod.Post)
                {
                    Interlocked.Increment(ref BingTranslationCalls);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[{\"translations\":[{\"text\":\"Привет, мир\",\"to\":\"ru\"}]}]")
                    });
                }
                if (request.RequestUri.Host == "translate.googleapis.com")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
        }

        private static void Main()
        {
            try { Run().GetAwaiter().GetResult(); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
        }

        private static async Task Run()
        {
            Check(TranslationService.ParseMyMemoryResponse(BadTopEntry, "привет", "ru", "zh-CN") == "你好", "reject polluted top entry and select correct Chinese");
            Reject(BadTopEntry.Replace("你好", "ещё неверно"), "привет", "all wrong-language candidates");
            Reject(BadTopEntry.Replace("你好", "有效中文").Replace("\"segment\":\"привет\"", "\"segment\":\"другой текст\""), "привет", "unrelated source segments");
            Reject(BadTopEntry.Replace("\"responseStatus\":200", "\"responseStatus\":429"), "привет", "provider error status");
            Reject(BadTopEntry.Replace("\"quotaFinished\":false", "\"quotaFinished\":true"), "привет", "quota exhausted");
            Reject("{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"привет\"}}", "привет", "invalid response without candidates");
            Check(TranslationService.DetectLanguage("注文はいつ発送されますか？") == "ja", "Japanese starting with kanji");
            string machineResponse = "{\"responseStatus\":200,\"matches\":[{\"id\":0,\"created-by\":\"MT!\",\"segment\":\"你好，今天发货\",\"source\":\"zh-CN\",\"target\":\"ru-RU\",\"translation\":\"Здравствуйте! Отправка сегодня\",\"match\":0.85}]}";
            Check(TranslationService.ParseMyMemoryResponse(machineResponse, "你好，今天发货", "zh-CN", "ru").Contains("Отправка"), "accept full-source machine translation at provider score 0.85");
            Check(TranslationService.IsTargetScript("123 / SKU-1 您好", "zh-CN"), "mixed product codes and Chinese");
            Check(TranslationService.IsTargetScript("123", "zh-CN"), "numeric-only result");
            string bingJson = "[{\"translations\":[{\"text\":\"Привет, мир\",\"to\":\"ru\"}]}]";
            Check(TranslationService.ParseBingResponse(bingJson) == "Привет, мир", "parse Bing translation response");
            FakeHandler quantityHandler = new FakeHandler();
            using (TranslationService service = new TranslationService(quantityHandler))
            {
                string[] inputs = { "1个", "2个", "3个", "5个", "11个", "21个" };
                string[] expected = { "1 штука", "2 штуки", "3 штуки", "5 штук", "11 штук", "21 штука" };
                Stopwatch elapsed = Stopwatch.StartNew();
                for (int index = 0; index < inputs.Length; index++)
                {
                    TranslationResult result = await service.TranslateAsync(
                        inputs[index], "auto", "ru", CancellationToken.None);
                    Check(result.Text == expected[index] && result.Provider.Contains("即时"),
                        "local quantity shortcut " + inputs[index]);
                }
                elapsed.Stop();
                Check(quantityHandler.Calls == 0 && elapsed.ElapsedMilliseconds < 100,
                    "quantity shortcuts return without network");
                TranslationResult englishQuantity = await service.TranslateAsync(
                    "5 个", "auto", "en", CancellationToken.None);
                TranslationResult japaneseQuantity = await service.TranslateAsync(
                    "3个", "zh-CN", "ja", CancellationToken.None);
                Check(englishQuantity.Text == "5 items" && japaneseQuantity.Text == "3個" &&
                    quantityHandler.Calls == 0, "quantity shortcuts support English and Japanese");
            }
            BingHandler bingHandler = new BingHandler();
            using (TranslationService service = new TranslationService(
                bingHandler, 2000, () => DateTimeOffset.UtcNow, true))
            {
                service.WarmUp();
                TranslationResult result = await service.TranslateAsync(
                    "Hello world", "en", "ru", CancellationToken.None);
                Check(result.Text == "Привет, мир" && result.Provider == "Bing 翻译" &&
                    bingHandler.BingBootstrapCalls == 1 && bingHandler.BingTranslationCalls == 1,
                    "Bing races as an additional translation provider");
            }
            foreach (bool timeout in new[] { false, true })
            {
                FakeHandler handler = new FakeHandler { TimeoutPrimary = timeout };
                using (TranslationService service = new TranslationService(handler))
                {
                    TranslationResult result = await service.TranslateAsync("привет", "auto", "zh-CN", CancellationToken.None);
                    Check(result.Text == "你好" && handler.Calls == 2, timeout ? "timeout falls back" : "HTTP failure falls back");
                }
            }
            using (TranslationService service = new TranslationService(new FakeHandler(), 100, () => DateTimeOffset.UtcNow))
            {
                TranslationResult result = await service.TranslateAsync("привет", "ru", "zh-CN", CancellationToken.None);
                Check(result.Provider == "MyMemory", "failed primary starts fallback before hedge delay");
            }
            FakeHandler slowFallbackHandler = new FakeHandler
            {
                GoogleStatus = (HttpStatusCode)429,
                MyMemoryDelayMilliseconds = 2300
            };
            using (TranslationService service = new TranslationService(slowFallbackHandler))
            {
                TranslationResult result = await service.TranslateAsync("привет", "ru", "zh-CN", CancellationToken.None);
                Check(result.Text == "你好" && result.Provider == "MyMemory", "rate-limited primary permits fallback beyond old two-second cutoff");
            }
            await AssertProviderCooldown((HttpStatusCode)429, new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)), 30);
            await AssertProviderCooldown(HttpStatusCode.Forbidden, null, 60);
            await AssertProviderCooldown((HttpStatusCode)429, new RetryConditionHeaderValue(
                new DateTimeOffset(2026, 9, 8, 0, 0, 45, TimeSpan.Zero)), 45);
            await AssertFallbackCooldown((HttpStatusCode)429, 200, false);
            await AssertFallbackCooldown(HttpStatusCode.OK, 429, false);
            await AssertFallbackCooldown(HttpStatusCode.OK, 200, true);
            FakeHandler racingHandler = new FakeHandler { GoogleDelayMilliseconds = 1500, MyMemoryDelayMilliseconds = 35 };
            using (TranslationService service = new TranslationService(racingHandler))
            {
                Stopwatch elapsed = Stopwatch.StartNew();
                TranslationResult result = await service.TranslateAsync("привет", "auto", "zh-CN", CancellationToken.None);
                elapsed.Stop();
                Check(result.Text == "你好" && result.Provider == "MyMemory" && elapsed.ElapsedMilliseconds < 700,
                    "slow primary races fast fallback");
                await Task.Delay(30);
                Check(racingHandler.GoogleCancellations == 1, "winning fallback cancels primary");
            }
            FakeHandler cacheHandler = new FakeHandler { GoogleDelayMilliseconds = 1500, MyMemoryDelayMilliseconds = 20 };
            using (TranslationService service = new TranslationService(cacheHandler))
            {
                TranslationResult first = await service.TranslateAsync("привет", "auto", "zh-CN", CancellationToken.None);
                int callsAfterFirst = cacheHandler.Calls;
                Stopwatch elapsed = Stopwatch.StartNew();
                TranslationResult second = await service.TranslateAsync("привет", "auto", "zh-CN", CancellationToken.None);
                elapsed.Stop();
                Check(first.Text == second.Text && second.Provider.Contains("缓存") &&
                    cacheHandler.Calls == callsAfterFirst && elapsed.ElapsedMilliseconds < 30,
                    "repeat translation uses memory cache");
            }
            FakeHandler preferredHandler = new FakeHandler { GoogleSucceeds = true };
            using (TranslationService service = new TranslationService(preferredHandler))
            {
                TranslationResult result = await service.TranslateAsync("привет", "ru", "zh-CN", CancellationToken.None);
                Check(result.Provider == "Google 公共翻译" && preferredHandler.Calls == 1,
                    "fast valid primary avoids fallback");
            }
            FakeHandler invalidFallbackHandler = new FakeHandler
            {
                GoogleSucceeds = true,
                GoogleDelayMilliseconds = 250,
                MyMemoryInvalid = true
            };
            using (TranslationService service = new TranslationService(invalidFallbackHandler))
            {
                TranslationResult result = await service.TranslateAsync("привет", "ru", "zh-CN", CancellationToken.None);
                Check(result.Provider == "Google 公共翻译" && result.Text == "你好",
                    "invalid fast fallback cannot beat valid primary");
            }
            FakeHandler stalledHandler = new FakeHandler
            {
                GoogleDelayMilliseconds = 10000,
                MyMemoryDelayMilliseconds = 10000
            };
            const int shortBudget = 250;
            using (TranslationService service = new TranslationService(stalledHandler, shortBudget, () => DateTimeOffset.UtcNow))
            {
                Stopwatch elapsed = Stopwatch.StartNew();
                try
                {
                    await service.TranslateAsync("привет", "auto", "zh-CN", CancellationToken.None);
                    throw new Exception("Translation deadline ignored");
                }
                catch (InvalidOperationException)
                {
                    elapsed.Stop();
                    Check(elapsed.ElapsedMilliseconds >= shortBudget - 30 && elapsed.ElapsedMilliseconds <= shortBudget + 500,
                        "unavailable providers respect configured deadline");
                }
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                FakeHandler handler = new FakeHandler();
                using (TranslationService service = new TranslationService(handler))
                {
                    try { await service.TranslateAsync("привет", "auto", "zh-CN", cancellation.Token); throw new Exception("Cancellation ignored"); }
                    catch (OperationCanceledException) { Check(handler.Calls == 0, "user cancellation does not call fallback"); }
                }
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource(50))
            {
                FakeHandler handler = new FakeHandler
                {
                    GoogleDelayMilliseconds = 10000,
                    MyMemoryDelayMilliseconds = 10000
                };
                using (TranslationService service = new TranslationService(handler))
                {
                    Stopwatch elapsed = Stopwatch.StartNew();
                    try
                    {
                        await service.TranslateAsync("привет", "auto", "zh-CN", cancellation.Token);
                        throw new Exception("Active cancellation ignored");
                    }
                    catch (OperationCanceledException)
                    {
                        elapsed.Stop();
                        Check(handler.Calls == 1 && elapsed.ElapsedMilliseconds < 400,
                            "active cancellation stops before fallback");
                    }
                }
            }
            await AssertLateProviderFailures(false);
            await AssertLateProviderFailures(true);
        }

        private static async Task AssertProviderCooldown(HttpStatusCode status, RetryConditionHeaderValue retryAfter, int cooldownSeconds)
        {
            DateTimeOffset now = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
            FakeHandler handler = new FakeHandler { GoogleStatus = status, GoogleRetryAfter = retryAfter };
            using (TranslationService service = new TranslationService(handler, TranslationService.TranslationBudgetMilliseconds, () => now))
            {
                await service.TranslateAsync("привет", "ru", "zh-CN", CancellationToken.None);
                now = now.AddSeconds(cooldownSeconds - 1);
                TranslationResult fallback = await service.TranslateAsync("добрый день", "ru", "zh-CN", CancellationToken.None);
                Check(handler.GoogleCalls == 1 && handler.MyMemoryCalls == 2 && fallback.Provider == "MyMemory",
                    "cooldown skips primary without blocking fallback: " + status + "/" + cooldownSeconds);
                now = now.AddSeconds(2);
                handler.GoogleSucceeds = true;
                TranslationResult recovered = await service.TranslateAsync("спасибо", "ru", "zh-CN", CancellationToken.None);
                Check(handler.GoogleCalls == 2 && handler.MyMemoryCalls == 2 && recovered.Provider == "Google 公共翻译",
                    "expired cooldown retries primary: " + status + "/" + cooldownSeconds);
            }
        }

        private static async Task AssertFallbackCooldown(HttpStatusCode status, int responseStatus, bool quotaExhausted)
        {
            DateTimeOffset now = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
            FakeHandler handler = new FakeHandler
            {
                GoogleSucceeds = true,
                GoogleDelayMilliseconds = 220,
                MyMemoryStatus = status,
                MyMemoryResponseStatus = responseStatus,
                MyMemoryQuotaExhausted = quotaExhausted
            };
            string label = (int)status + "/" + responseStatus + "/quota=" + quotaExhausted;
            using (TranslationService service = new TranslationService(handler, 2000, () => now))
            {
                await service.TranslateAsync("привет", "ru", "zh-CN", CancellationToken.None);
                now = now.AddSeconds(59);
                TranslationResult healthy = await service.TranslateAsync("добрый день", "ru", "zh-CN", CancellationToken.None);
                Check(handler.MyMemoryCalls == 1 && healthy.Provider == "Google 公共翻译",
                    "fallback quota cooldown preserves healthy primary: " + label);
                now = now.AddSeconds(2);
                await service.TranslateAsync("спасибо", "ru", "zh-CN", CancellationToken.None);
                Check(handler.MyMemoryCalls == 2, "fallback quota cooldown expires: " + label);
            }
        }

        private static async Task AssertLateProviderFailures(bool userCancels)
        {
            int unobservedFailures = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> observer = (sender, args) =>
            {
                if (!args.Exception.ToString().Contains("late provider failure regression")) return;
                Interlocked.Increment(ref unobservedFailures);
                args.SetObserved();
            };
            TaskScheduler.UnobservedTaskException += observer;
            try
            {
                await RunAbandonedProviders(userCancels);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Check(unobservedFailures == 0, "late provider faults are observed after " + (userCancels ? "user cancellation" : "deadline"));
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= observer;
            }
        }

        private static async Task RunAbandonedProviders(bool userCancels)
        {
            FakeHandler handler = new FakeHandler
            {
                GoogleDelayMilliseconds = 900,
                MyMemoryDelayMilliseconds = 900,
                IgnoreCancellation = true,
                FailAfterDelay = true
            };
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            using (TranslationService service = new TranslationService(handler, userCancels ? 3000 : 250, () => DateTimeOffset.UtcNow))
            {
                if (userCancels) cancellation.CancelAfter(250);
                Stopwatch elapsed = Stopwatch.StartNew();
                bool stopped = false;
                try { await service.TranslateAsync("привет", "ru", "zh-CN", cancellation.Token); }
                catch (OperationCanceledException) { if (!userCancels) throw; stopped = true; }
                catch (InvalidOperationException) { if (userCancels) throw; stopped = true; }
                elapsed.Stop();
                Check(stopped && elapsed.ElapsedMilliseconds < 750,
                    "provider ignoring cancellation cannot delay " + (userCancels ? "user cancellation" : "deadline"));
                await Task.Delay(1100);
                Check(handler.LateFailures == 2, "both abandoned providers finish with late faults");
            }
        }

        private static void Reject(string json, string original, string label)
        {
            try { TranslationService.ParseMyMemoryResponse(json, original, "ru", "zh-CN"); }
            catch (InvalidOperationException) { Console.WriteLine("PASS: " + label); return; }
            throw new Exception("Invalid result accepted: " + label);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception(label);
            Console.WriteLine("PASS: " + label);
        }
    }
}
