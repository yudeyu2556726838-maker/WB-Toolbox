using System;
using System.IO;
using System.Text;

namespace WBToolbox.Native.Diagnostics
{
    internal static class CrashLogger
    {
        internal static string Log(Exception error)
        {
            if (error == null)
            {
                return null;
            }

            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WBToolbox",
                    "logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "crash-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                string entry = "[" + DateTime.Now.ToString("O") + "] " + error + Environment.NewLine + Environment.NewLine;
                File.AppendAllText(path, entry, new UTF8Encoding(false));
                return path;
            }
            catch
            {
                // Logging must never replace the original failure.
                return null;
            }
        }
    }
}
