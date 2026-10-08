using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WBToolbox.Native.Services
{
    internal struct VideoTargetSize
    {
        private readonly int width;
        private readonly int height;

        internal VideoTargetSize(int width, int height)
        {
            this.width = width;
            this.height = height;
        }

        internal int Width { get { return width; } }
        internal int Height { get { return height; } }
    }

    internal sealed class VideoOptimizationResult
    {
        internal VideoOptimizationResult(string path, bool optimized)
        {
            Path = path;
            Optimized = optimized;
        }

        internal string Path { get; private set; }
        internal bool Optimized { get; private set; }
    }

    internal static class VideoBackgroundOptimizer
    {
        private const int MaximumLandscapeWidth = 1280;
        private const int MaximumLandscapeHeight = 720;
        private const int TargetFramesPerSecond = 30;
        private const uint TargetBitrate = 3500000;
        private const ulong MaximumUnchangedBitrate = 5000000;

        internal static bool NeedsOptimization(
            int width,
            int height,
            double framesPerSecond,
            ulong bitrate,
            string subtype)
        {
            if (width <= 0 || height <= 0) return true;
            VideoTargetSize target = CalculateTargetSize(width, height);
            bool oversized = width > target.Width || height > target.Height;
            bool highFrameRate = framesPerSecond > TargetFramesPerSecond + 0.5;
            bool highBitrate = bitrate > MaximumUnchangedBitrate;
            bool incompatibleCodec = !string.Equals(
                subtype, "H264", StringComparison.OrdinalIgnoreCase);
            return oversized || highFrameRate || highBitrate || incompatibleCodec;
        }

        internal static VideoTargetSize CalculateTargetSize(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException("width", "视频尺寸必须大于零");

            bool landscape = width >= height;
            double maximumWidth = landscape ? MaximumLandscapeWidth : MaximumLandscapeHeight;
            double maximumHeight = landscape ? MaximumLandscapeHeight : MaximumLandscapeWidth;
            double scale = Math.Min(1, Math.Min(maximumWidth / width, maximumHeight / height));
            int targetWidth = MakeEven(width * scale);
            int targetHeight = MakeEven(height * scale);
            return new VideoTargetSize(targetWidth, targetHeight);
        }

        internal static async Task<VideoOptimizationResult> PrepareAsync(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("未找到视频背景", sourcePath);

            Type storageFileType = GetWindowsRuntimeType(
                "Windows.Storage.StorageFile, Windows.Storage");
            MethodInfo getFile = storageFileType.GetMethod("GetFileFromPathAsync");
            object inputFile = await AwaitWindowsRuntimeOperation(
                Invoke(getFile, null, sourcePath), getFile.ReturnType).ConfigureAwait(false);

            Type clipType = GetWindowsRuntimeType(
                "Windows.Media.Editing.MediaClip, Windows.Media");
            MethodInfo createClip = clipType.GetMethod("CreateFromFileAsync");
            object clip = await AwaitWindowsRuntimeOperation(
                Invoke(createClip, null, inputFile), createClip.ReturnType).ConfigureAwait(false);
            PropertyInfo audioEnabled = clipType.GetProperty("AudioEnabled");
            if (audioEnabled != null) audioEnabled.SetValue(clip, false, null);

            MethodInfo getProperties = clipType.GetMethod("GetVideoEncodingProperties");
            object properties = Invoke(getProperties, clip);
            int width = Convert.ToInt32(GetProperty(properties, "Width"));
            int height = Convert.ToInt32(GetProperty(properties, "Height"));
            ulong bitrate = Convert.ToUInt64(GetProperty(properties, "Bitrate"));
            string subtype = Convert.ToString(GetProperty(properties, "Subtype"));
            object frameRate = GetProperty(properties, "FrameRate");
            double framesPerSecond = ReadFrameRate(frameRate);
            if (!NeedsOptimization(width, height, framesPerSecond, bitrate, subtype))
                return new VideoOptimizationResult(sourcePath, false);

            string temporaryPath = Path.Combine(
                Path.GetTempPath(),
                "WBToolbox-video-" + Guid.NewGuid().ToString("N") + ".mp4");
            try
            {
                object outputFile = await CreateOutputFile(temporaryPath).ConfigureAwait(false);
                VideoTargetSize target = CalculateTargetSize(width, height);
                object profile = CreateEncodingProfile(target);

                Type compositionType = GetWindowsRuntimeType(
                    "Windows.Media.Editing.MediaComposition, Windows.Media");
                object composition = Activator.CreateInstance(compositionType);
                PropertyInfo clipsProperty = compositionType.GetProperty("Clips");
                object clips = clipsProperty.GetValue(composition, null);
                MethodInfo addClip = clipsProperty.PropertyType.GetInterfaces()
                    .SelectMany(type => type.GetMethods())
                    .First(method => method.Name == "Add" &&
                        method.GetParameters().Length == 1);
                Invoke(addClip, clips, clip);

                Type trimmingType = GetWindowsRuntimeType(
                    "Windows.Media.Editing.MediaTrimmingPreference, Windows.Media");
                object precise = Enum.Parse(trimmingType, "Precise");
                MethodInfo render = compositionType.GetMethods()
                    .First(method => method.Name == "RenderToFileAsync" &&
                        method.GetParameters().Length == 3);
                object failureReason = await AwaitWindowsRuntimeOperation(
                    Invoke(render, composition, outputFile, precise, profile),
                    render.ReturnType).ConfigureAwait(false);
                if (!string.Equals(Convert.ToString(failureReason), "None", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Windows 视频优化失败：" + Convert.ToString(failureReason));

                FileInfo output = new FileInfo(temporaryPath);
                if (!output.Exists || output.Length < 1024)
                    throw new InvalidDataException("优化后的视频文件无效");
                return new VideoOptimizationResult(temporaryPath, true);
            }
            catch
            {
                DeleteTemporaryFile(temporaryPath);
                throw;
            }
        }

        internal static void DeleteTemporaryFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path) && string.Equals(
                    Path.GetDirectoryName(Path.GetFullPath(path)).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // A failed cleanup must not discard a successfully imported background.
            }
            catch (UnauthorizedAccessException)
            {
                // Temporary cleanup can be retried by Windows later.
            }
        }

        private static async Task<object> CreateOutputFile(string path)
        {
            Type folderType = GetWindowsRuntimeType(
                "Windows.Storage.StorageFolder, Windows.Storage");
            MethodInfo getFolder = folderType.GetMethod("GetFolderFromPathAsync");
            object folder = await AwaitWindowsRuntimeOperation(
                Invoke(getFolder, null, Path.GetDirectoryName(path)),
                getFolder.ReturnType).ConfigureAwait(false);
            Type collisionType = GetWindowsRuntimeType(
                "Windows.Storage.CreationCollisionOption, Windows.Storage");
            object replaceExisting = Enum.Parse(collisionType, "ReplaceExisting");
            MethodInfo createFile = folderType.GetMethods().First(method =>
                method.Name == "CreateFileAsync" && method.GetParameters().Length == 2);
            return await AwaitWindowsRuntimeOperation(
                Invoke(createFile, folder, Path.GetFileName(path), replaceExisting),
                createFile.ReturnType).ConfigureAwait(false);
        }

        private static object CreateEncodingProfile(VideoTargetSize target)
        {
            Type qualityType = GetWindowsRuntimeType(
                "Windows.Media.MediaProperties.VideoEncodingQuality, Windows.Media");
            Type profileType = GetWindowsRuntimeType(
                "Windows.Media.MediaProperties.MediaEncodingProfile, Windows.Media");
            object quality = Enum.Parse(qualityType, "HD720p");
            object profile = Invoke(profileType.GetMethod("CreateMp4"), null, quality);
            object video = GetProperty(profile, "Video");
            SetProperty(video, "Width", (uint)target.Width);
            SetProperty(video, "Height", (uint)target.Height);
            SetProperty(video, "Bitrate", TargetBitrate);
            object frameRate = GetProperty(video, "FrameRate");
            SetProperty(frameRate, "Numerator", (uint)TargetFramesPerSecond);
            SetProperty(frameRate, "Denominator", 1u);
            return profile;
        }

        private static double ReadFrameRate(object frameRate)
        {
            if (frameRate == null) return 0;
            double numerator = Convert.ToDouble(GetProperty(frameRate, "Numerator"));
            double denominator = Convert.ToDouble(GetProperty(frameRate, "Denominator"));
            return denominator <= 0 ? 0 : numerator / denominator;
        }

        private static int MakeEven(double value)
        {
            int rounded = Math.Max(2, (int)Math.Floor(value));
            return rounded % 2 == 0 ? rounded : rounded - 1;
        }

        private static Type GetWindowsRuntimeType(string name)
        {
            Type type = Type.GetType(name + ", ContentType=WindowsRuntime", false);
            if (type == null)
                throw new PlatformNotSupportedException("当前 Windows 版本不支持视频背景优化");
            return type;
        }

        private static object GetProperty(object target, string propertyName)
        {
            if (target == null) return null;
            PropertyInfo property = target.GetType().GetProperty(propertyName);
            if (property == null)
                throw new MissingMemberException(target.GetType().FullName, propertyName);
            return property.GetValue(target, null);
        }

        private static void SetProperty(object target, string propertyName, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName);
            if (property == null)
                throw new MissingMemberException(target.GetType().FullName, propertyName);
            property.SetValue(
                target,
                Convert.ChangeType(value, property.PropertyType),
                null);
        }

        private static object Invoke(MethodInfo method, object target, params object[] arguments)
        {
            if (method == null) throw new MissingMethodException("缺少所需的 Windows 媒体接口");
            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException error)
            {
                ExceptionDispatchInfo.Capture(error.InnerException ?? error).Throw();
                throw;
            }
        }

        private static async Task<object> AwaitWindowsRuntimeOperation(
            object operation,
            Type operationInterface)
        {
            Assembly extensionsAssembly = Assembly.LoadFrom(Path.Combine(
                RuntimeEnvironment.GetRuntimeDirectory(),
                "System.Runtime.WindowsRuntime.dll"));
            Type extensions = extensionsAssembly.GetType(
                "System.WindowsRuntimeSystemExtensions", true);
            Type[] genericArguments = operationInterface.GetGenericArguments();
            string operationTypeName = operationInterface.GetGenericTypeDefinition().FullName;
            MethodInfo asTask = extensions.GetMethods().First(method =>
            {
                if (method.Name != "AsTask" || !method.IsGenericMethodDefinition ||
                    method.GetGenericArguments().Length != genericArguments.Length) return false;
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsGenericType &&
                    parameters[0].ParameterType.GetGenericTypeDefinition().FullName ==
                    operationTypeName;
            });
            Task task = (Task)Invoke(
                asTask.MakeGenericMethod(genericArguments), null, operation);
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }
    }
}
