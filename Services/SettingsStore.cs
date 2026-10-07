using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using WBToolbox.Native.Core;

namespace WBToolbox.Native.Services
{
    internal sealed class AppSettings
    {
        internal const int CurrentVersion = 7;
        internal const string BackgroundPlain = "plain";
        internal const string BackgroundBuiltInSkin = "skin-sky-girl";
        internal const string BackgroundCustomImage = "custom-image";
        internal const string BackgroundCustomVideo = "custom-video";

        public AppSettings()
        {
            SettingsVersion = CurrentVersion;
            AdjustmentRate = 0.05m;
            PricingCommissionRate = PricingEngine.DefaultCommissionRate;
            PricingProfitRate = PricingEngine.DefaultProfitRate;
            RateBaseCurrency = Currencies.Cny.Code;
            RateQuoteCurrency = Currencies.Rub.Code;
            HasMiniPosition = false;
            DarkTheme = false;
            WindowPinned = false;
            BackgroundMode = BackgroundPlain;
            VideoCropWidth = 1;
            VideoCropHeight = 1;
        }

        public int SettingsVersion { get; set; }
        public decimal AdjustmentRate { get; set; }
        public decimal PricingCommissionRate { get; set; }
        public decimal PricingProfitRate { get; set; }
        public string PricingCategoryKey { get; set; }
        public string RateBaseCurrency { get; set; }
        public string RateQuoteCurrency { get; set; }
        public bool HasMiniPosition { get; set; }
        public double MiniLeft { get; set; }
        public double MiniTop { get; set; }
        public bool DarkTheme { get; set; }
        public bool WindowPinned { get; set; }
        public string BackgroundMode { get; set; }
        public string CustomBackgroundPath { get; set; }
        public bool CustomBackgroundIsVideo { get; set; }
        public double VideoCropX { get; set; }
        public double VideoCropY { get; set; }
        public double VideoCropWidth { get; set; }
        public double VideoCropHeight { get; set; }
    }

    internal sealed class SettingsStore
    {
        private readonly string directoryPath;
        private readonly string settingsPath;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        internal SettingsStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WBToolbox"))
        {
        }

        internal SettingsStore(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("设置目录不能为空", "directoryPath");
            }
            this.directoryPath = Path.GetFullPath(directoryPath);
            settingsPath = Path.Combine(this.directoryPath, "settings.json");
        }

        internal AppSettings Load()
        {
            try
            {
                if (!File.Exists(settingsPath))
                {
                    return new AppSettings();
                }

                string json = File.ReadAllText(settingsPath, Encoding.UTF8);
                AppSettings settings = serializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                if (settings.SettingsVersion < 2)
                {
                    settings.PricingCommissionRate = PricingEngine.DefaultCommissionRate;
                }
                if (settings.SettingsVersion < 3)
                {
                    settings.RateBaseCurrency = Currencies.Cny.Code;
                    settings.RateQuoteCurrency = Currencies.Rub.Code;
                }
                if (settings.SettingsVersion < 4)
                {
                    settings.HasMiniPosition = false;
                }
                if (settings.SettingsVersion < 6)
                {
                    settings.CustomBackgroundIsVideo = false;
                    settings.VideoCropX = 0;
                    settings.VideoCropY = 0;
                    settings.VideoCropWidth = 1;
                    settings.VideoCropHeight = 1;
                }
                if (settings.SettingsVersion < 7)
                {
                    settings.BackgroundMode = string.IsNullOrWhiteSpace(settings.CustomBackgroundPath)
                        ? AppSettings.BackgroundPlain
                        : (settings.CustomBackgroundIsVideo
                            ? AppSettings.BackgroundCustomVideo
                            : AppSettings.BackgroundCustomImage);
                }
                settings.SettingsVersion = AppSettings.CurrentVersion;
                if (settings.AdjustmentRate < 0 || settings.AdjustmentRate > 1)
                {
                    settings.AdjustmentRate = 0.05m;
                }
                if (!PricingEngine.IsRatePlanValid(settings.PricingCommissionRate, settings.PricingProfitRate))
                {
                    settings.PricingCommissionRate = PricingEngine.DefaultCommissionRate;
                    settings.PricingProfitRate = PricingEngine.DefaultProfitRate;
                }
                if (!Currencies.IsSupportedPair(settings.RateBaseCurrency, settings.RateQuoteCurrency))
                {
                    settings.RateBaseCurrency = Currencies.Cny.Code;
                    settings.RateQuoteCurrency = Currencies.Rub.Code;
                }
                if (double.IsNaN(settings.MiniLeft) || double.IsInfinity(settings.MiniLeft) ||
                    double.IsNaN(settings.MiniTop) || double.IsInfinity(settings.MiniTop))
                {
                    settings.HasMiniPosition = false;
                    settings.MiniLeft = 0;
                    settings.MiniTop = 0;
                }
                if (!string.IsNullOrWhiteSpace(settings.CustomBackgroundPath) && !File.Exists(settings.CustomBackgroundPath))
                {
                    settings.CustomBackgroundPath = null;
                    settings.CustomBackgroundIsVideo = false;
                    settings.BackgroundMode = AppSettings.BackgroundPlain;
                }
                NormalizeBackgroundMode(settings);
                NormalizeVideoCrop(settings);
                return settings;
            }
            catch
            {
                return new AppSettings();
            }
        }

        internal void Save(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            Directory.CreateDirectory(directoryPath);
            settings.SettingsVersion = AppSettings.CurrentVersion;
            NormalizeBackgroundMode(settings);
            NormalizeVideoCrop(settings);
            string temporaryPath = settingsPath + ".tmp";
            string json = serializer.Serialize(settings);
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));

            if (File.Exists(settingsPath))
            {
                File.Replace(temporaryPath, settingsPath, null);
            }
            else
            {
                File.Move(temporaryPath, settingsPath);
            }
        }

        internal string ImportBackground(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("未找到背景图片", sourcePath);
            }

            Directory.CreateDirectory(directoryPath);
            string extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".jpg";
            }
            string destinationPath = Path.Combine(directoryPath, "custom-background" + extension.ToLowerInvariant());
            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, destinationPath, true);
            }
            return destinationPath;
        }

        internal string ImportVideoBackground(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("未找到背景视频", sourcePath);
            }

            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension != ".mp4" && extension != ".wmv" && extension != ".avi" &&
                extension != ".mov" && extension != ".m4v")
            {
                throw new InvalidDataException("不支持的视频格式");
            }

            Directory.CreateDirectory(directoryPath);
            string destinationPath = Path.Combine(directoryPath, "custom-background-video" + extension);
            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            {
                string temporaryPath = destinationPath + ".tmp";
                File.Copy(sourcePath, temporaryPath, true);
                if (File.Exists(destinationPath)) File.Replace(temporaryPath, destinationPath, null);
                else File.Move(temporaryPath, destinationPath);
            }
            return destinationPath;
        }

        private static void NormalizeVideoCrop(AppSettings settings)
        {
            const double minimum = 0.15;
            if (!IsFinite(settings.VideoCropX)) settings.VideoCropX = 0;
            if (!IsFinite(settings.VideoCropY)) settings.VideoCropY = 0;
            if (!IsFinite(settings.VideoCropWidth)) settings.VideoCropWidth = 1;
            if (!IsFinite(settings.VideoCropHeight)) settings.VideoCropHeight = 1;
            settings.VideoCropX = Math.Max(0, Math.Min(1 - minimum, settings.VideoCropX));
            settings.VideoCropY = Math.Max(0, Math.Min(1 - minimum, settings.VideoCropY));
            settings.VideoCropWidth = Math.Max(minimum,
                Math.Min(1 - settings.VideoCropX, settings.VideoCropWidth));
            settings.VideoCropHeight = Math.Max(minimum,
                Math.Min(1 - settings.VideoCropY, settings.VideoCropHeight));
        }

        private static void NormalizeBackgroundMode(AppSettings settings)
        {
            string mode = settings.BackgroundMode;
            bool known = mode == AppSettings.BackgroundPlain ||
                mode == AppSettings.BackgroundBuiltInSkin ||
                mode == AppSettings.BackgroundCustomImage ||
                mode == AppSettings.BackgroundCustomVideo;
            if (!known)
            {
                mode = AppSettings.BackgroundPlain;
            }

            bool custom = mode == AppSettings.BackgroundCustomImage ||
                mode == AppSettings.BackgroundCustomVideo;
            if (custom && (string.IsNullOrWhiteSpace(settings.CustomBackgroundPath) ||
                !File.Exists(settings.CustomBackgroundPath)))
            {
                mode = AppSettings.BackgroundPlain;
                custom = false;
            }

            settings.BackgroundMode = mode;
            settings.CustomBackgroundIsVideo = mode == AppSettings.BackgroundCustomVideo;
            if (!custom)
            {
                settings.CustomBackgroundPath = null;
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
