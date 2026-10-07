using System;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using WBToolbox.Native.Diagnostics;
using WBToolbox.Native.Infrastructure;
using WBToolbox.Native.UI;

namespace WBToolbox.Native
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                RunApplication();
            }
            catch (Exception error)
            {
                string logPath = CrashLogger.Log(error);
                string details = string.IsNullOrWhiteSpace(logPath)
                    ? ""
                    : "\n\n错误日志：\n" + logPath;
                MessageBox.Show(
                    "WB Toolbox 无法启动。\n\n" + error.Message + details,
                    "WB Toolbox 启动失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Environment.ExitCode = 1;
            }
        }

        private static void RunApplication()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            using (SingleInstanceCoordinator instance = new SingleInstanceCoordinator())
            {
                if (!instance.IsPrimaryInstance)
                {
                    instance.SignalPrimaryInstance();
                    return;
                }

                Application application = new Application();
                application.ShutdownMode = ShutdownMode.OnMainWindowClose;
                application.DispatcherUnhandledException += HandleDispatcherException;
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args)
                {
                    CrashLogger.Log(args.ExceptionObject as Exception);
                };
                MainWindow window = new MainWindow();
                instance.Attach(window, window.RestoreFromExternalActivation);
                application.MainWindow = window;
                application.Run(window);
            }
        }

        private static void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs args)
        {
            CrashLogger.Log(args.Exception);
            MessageBox.Show(
                "WB Toolbox 遇到未处理错误：\n\n" + args.Exception.Message,
                "WB Toolbox",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
            Application.Current.Shutdown(1);
        }
    }
}
