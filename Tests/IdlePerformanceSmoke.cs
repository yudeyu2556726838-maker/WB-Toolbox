using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WBToolbox.Native.Services;
using WBToolbox.Native.UI;

namespace WBToolbox.Native.Tests
{
    internal static class IdlePerformanceSmoke
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message);
                Console.Error.WriteLine(error.StackTrace);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            string outputDirectory = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.Combine(Path.GetTempPath(), "WBToolbox-IdleSmoke-" + Guid.NewGuid().ToString("N"));
            Application application = new Application();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            System.Threading.SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            MainWindow window = new MainWindow(new SettingsStore(Path.Combine(outputDirectory, "settings")));
            window.Left = 80;
            window.Top = 80;
            window.Show();
            Wait(window.Dispatcher, 1200);

            MethodInfo enterMiniMode = typeof(MainWindow).GetMethod(
                "EnterMiniMode",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo miniWindowField = typeof(MainWindow).GetField(
                "miniWindow",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo edgeDockField = typeof(MainWindow).GetField(
                "edgeDock",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (enterMiniMode == null || miniWindowField == null || edgeDockField == null)
            {
                throw new InvalidOperationException("无法启动窗口性能测试");
            }

            EdgeDockController edgeDock = (EdgeDockController)edgeDockField.GetValue(window);
            window.Left = SystemParameters.WorkArea.Right - window.ActualWidth;
            Wait(window.Dispatcher, 500);
            edgeDock.CompleteMove();
            WaitUntil(window.Dispatcher, delegate { return edgeDock.IsHidden && !edgeDock.IsAnimating; }, 1000,
                "性能测试没有完成贴边缩回");
            Wait(window.Dispatcher, 500);
            double dockedCpu = MeasureSingleCoreCpu(window.Dispatcher, 3000);
            Console.WriteLine("Dock hidden: CPU(one core)={0:F2}%", dockedCpu);
            if (dockedCpu > 8)
                throw new InvalidOperationException("贴边隐藏状态空闲 CPU 超过预算：" + dockedCpu.ToString("F2") + "%");
            edgeDock.Detach();
            Wait(window.Dispatcher, 200);

            enterMiniMode.Invoke(window, null);
            Wait(window.Dispatcher, 1200);
            if (window.IsVisible || miniWindowField.GetValue(window) == null)
            {
                throw new InvalidOperationException("性能测试没有进入迷你状态");
            }

            Process process = Process.GetCurrentProcess();
            double singleCorePercent = MeasureSingleCoreCpu(window.Dispatcher, 5000);
            process.Refresh();
            Console.WriteLine(
                "Mini idle: CPU(one core)={0:F2}% WS={1:F1}MB Private={2:F1}MB Handles={3}",
                singleCorePercent,
                process.WorkingSet64 / 1048576.0,
                process.PrivateMemorySize64 / 1048576.0,
                process.HandleCount);

            window.Close();
            application.Shutdown();
            if (singleCorePercent > 8)
            {
                throw new InvalidOperationException(
                    "迷你状态空闲 CPU 超过预算：" + singleCorePercent.ToString("F2") + "%");
            }
            return 0;
        }

        private static double MeasureSingleCoreCpu(Dispatcher dispatcher, int milliseconds)
        {
            Process process = Process.GetCurrentProcess();
            process.Refresh();
            TimeSpan cpuStart = process.TotalProcessorTime;
            Stopwatch wall = Stopwatch.StartNew();
            Wait(dispatcher, milliseconds);
            wall.Stop();
            process.Refresh();
            return (process.TotalProcessorTime - cpuStart).TotalMilliseconds /
                Math.Max(1, wall.Elapsed.TotalMilliseconds) * 100;
        }

        private static void Wait(Dispatcher dispatcher, int milliseconds)
        {
            DispatcherFrame frame = new DispatcherFrame();
            DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
            timer.Tick += delegate
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void WaitUntil(Dispatcher dispatcher, Func<bool> condition, int timeoutMilliseconds, string message)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (!condition() && DateTime.UtcNow < deadline) Wait(dispatcher, 20);
            if (!condition()) throw new InvalidOperationException(message);
        }
    }
}
