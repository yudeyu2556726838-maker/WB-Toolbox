using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WBToolbox.Native.Core;
using WBToolbox.Native.Services;

namespace WBToolbox.Native.Tests
{
    internal static class IntegrationTests
    {
        private static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            try
            {
                RunAsync(Array.IndexOf(args, "--translation-only") >= 0).GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error.GetType().FullName + ": " + error.Message);
                TranslationUnavailableException unavailable = error as TranslationUnavailableException;
                if (unavailable != null) Console.Error.WriteLine(unavailable.Details);
                Console.Error.WriteLine(error.StackTrace);
                Exception inner = error.InnerException;
                while (inner != null)
                {
                    Console.Error.WriteLine("INNER: " + inner.GetType().FullName + ": " + inner.Message);
                    inner = inner.InnerException;
                }
                Environment.Exit(1);
            }
        }

        private static async Task RunAsync(bool translationOnly)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75)))
            using (ExchangeRateService rates = new ExchangeRateService())
            using (TranslationService translations = new TranslationService())
            {
                if (!translationOnly)
                {
                    Console.WriteLine("START: live exchange rate");
                    CurrencyMatrix matrix = await rates.FetchAsync(timeout.Token);
                    decimal cnyRub = matrix.GetRate("CNY", "RUB");
                    if (cnyRub <= 0)
                        throw new InvalidOperationException("CNY/RUB 行情无效");
                    Console.WriteLine("PASS: live CNY/RUB = " + cnyRub.ToString("0.000000"));
                }

                Console.WriteLine("START: live translation");
                Stopwatch translationElapsed = Stopwatch.StartNew();
                TranslationResult translated = await translations.TranslateAsync(
                    "你好，今天发货",
                    "zh-CN",
                    "ru",
                    timeout.Token);
                translationElapsed.Stop();
                if (string.IsNullOrWhiteSpace(translated.Text) || translated.Text == "你好，今天发货")
                {
                    throw new InvalidOperationException("翻译服务没有返回有效译文");
                }
                Console.WriteLine("PASS: translation in " + translationElapsed.ElapsedMilliseconds +
                    "ms via " + translated.Provider + " = " + translated.Text);
                TranslationResult greeting = await translations.TranslateAsync("привет", "auto", "zh-CN", timeout.Token);
                if (!greeting.Text.Contains("你好") && !greeting.Text.Contains("嗨") && !greeting.Text.Contains("您好"))
                {
                    throw new InvalidOperationException("俄语问候语没有正确译成中文：" + greeting.Text);
                }
                Console.WriteLine("PASS: привет -> " + greeting.Text + " via " + greeting.Provider);
                TranslationResult customerService = await translations.TranslateAsync(
                    "Hello! When will my order be shipped?", "en", "ru", timeout.Token);
                if (!TranslationService.IsTargetScript(customerService.Text, "ru"))
                    throw new InvalidOperationException("客服模板未返回俄语译文");
                Console.WriteLine("PASS: customer service -> " + customerService.Text + " via " + customerService.Provider);
            }
        }
    }
}
