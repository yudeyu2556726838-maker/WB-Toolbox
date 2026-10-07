using System;
using System.IO;
using System.Windows;
using WBToolbox.Native.Services;
using WBToolbox.Native.UI;

namespace WBToolbox.Native.Tests
{
    internal static class MiniInteractionSmoke
    {
        [STAThread]
        private static int Main()
        {
            Application application = new Application();
            application.ShutdownMode = ShutdownMode.OnMainWindowClose;
            UiFactory.InstallPalette(application, true);
            string settingsDirectory = Path.Combine(
                Path.GetTempPath(),
                "WBToolbox-InteractionSmoke-" + Guid.NewGuid().ToString("N"));
            MainWindow window = new MainWindow(new SettingsStore(settingsDirectory));
            application.MainWindow = window;
            application.Run(window);
            return 0;
        }
    }
}
