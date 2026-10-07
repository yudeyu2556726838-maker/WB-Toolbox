using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace WBToolbox.Native.UI
{
    internal static class EmbeddedAssets
    {
        internal static BitmapImage Load(string resourceName)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException("未找到内嵌资源：" + resourceName);
            }

            using (stream)
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        internal static BitmapImage LoadFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 2560;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
    }
}
