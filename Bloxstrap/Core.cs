using System.Collections.Concurrent;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Wpf.Ui.Controls;
namespace Bloxstrap.Enums
{
    public enum BootstrapperIcon
    {
        [EnumName(StaticName = "Froststrap")]
        IconBloxstrap,
        [EnumName(StaticName = "2008")]
        Icon2008,
        [EnumName(StaticName = "2011")]
        Icon2011,
        [EnumName(StaticName = "Early 2015")]
        IconEarly2015,
        [EnumName(StaticName = "Late 2015")]
        IconLate2015,
        [EnumName(StaticName = "2017")]
        Icon2017,
        [EnumName(StaticName = "2019")]
        Icon2019,
        [EnumName(StaticName = "2022")]
        Icon2022,
        [EnumName(StaticName = "2025")]
        Icon2025,
        [EnumName(FromTranslation = "Common.Custom")]
        IconCustom,
        [EnumName(StaticName = "Bloxstrap (Classic)")]
        IconBloxstrapClassic
    }
    public enum BootstrapperStyle
    {
        VistaDialog,
        LegacyDialog2008,
        LegacyDialog2011,
        ProgressDialog,
        ClassicFluentDialog,
        TwentyFiveDialog,
        ByfronDialog,
        FluentDialog,
        FroststrapDialog,
        FluentAeroDialog
    }
    public enum ChannelChangeMode
    {
        Automatic,
        Prompt,
        Ignore
    }
    public enum ErrorCode
    {
        ERROR_SUCCESS = 0,
        ERROR_INVALID_FUNCTION = 1,
        ERROR_FILE_NOT_FOUND = 2,
        ERROR_CANCELLED = 1223,
        ERROR_INSTALL_USEREXIT = 1602,
        ERROR_INSTALL_FAILURE = 1603,
        CO_E_APPNOTFOUND = -2147221003
    }
    public enum QualityLevel
    {
        [EnumName(FromTranslation = "Common.Disabled")]
        Disabled,
        [EnumName(StaticName = "Level 1")]
        Level1,
        [EnumName(StaticName = "Level 2")]
        Level2,
        [EnumName(StaticName = "Level 3")]
        Level3,
        [EnumName(StaticName = "Level 4")]
        Level4,
        [EnumName(StaticName = "Level 5")]
        Level5,
        [EnumName(StaticName = "Level 6")]
        Level6,
        [EnumName(StaticName = "Level 7")]
        Level7,
        [EnumName(StaticName = "Level 8")]
        Level8,
        [EnumName(StaticName = "Level 9")]
        Level9,
        [EnumName(StaticName = "Level 10")]
        Level10,
        [EnumName(StaticName = "Level 11")]
        Level11,
        [EnumName(StaticName = "Level 12")]
        Level12,
        [EnumName(StaticName = "Level 13")]
        Level13,
        [EnumName(StaticName = "Level 14")]
        Level14,
        [EnumName(StaticName = "Level 15")]
        Level15,
        [EnumName(StaticName = "Level 16")]
        Level16,
        [EnumName(StaticName = "Level 17")]
        Level17,
        [EnumName(StaticName = "Level 18")]
        Level18,
        [EnumName(StaticName = "Level 19")]
        Level19,
        [EnumName(StaticName = "Level 20")]
        Level20,
        [EnumName(StaticName = "Level 21")]
        Level21,
    }
    public enum MSAAMode
    {
        [EnumName(StaticName = "0x (Default)")]
        Default,
        [EnumName(StaticName = "1x")]
        x1,
        [EnumName(StaticName = "2x")]
        x2,
        [EnumName(StaticName = "4x")]
        x4
    }
    public enum RenderingMode
    {
        [EnumName(StaticName = "Direct3D11 (Default)")]
        Default,
        Vulkan,
        OpenGL
    }
    public enum GenericTriState
    {
        Successful,
        Failed,
        Unknown
    }
    public enum ImportSettingsFrom
    {
        None,
        Bloxstrap,
        Fishstrap,
        Lunastrap,
        Luczystrap
    }
    public enum LaunchMode
    {
        None,
        Unknown,
        Player,
        Studio,
        StudioAuth
    }
    public enum NextAction
    {
        Terminate,
        LaunchSettings,
        LaunchRoblox,
        LaunchRobloxStudio
    }
    public enum Theme
    {
        [EnumName(FromTranslation = "Common.SystemDefault")]
        Default,
        Dark,
        Light,
        Froststrap,
        Purple,
        Blue,
        Green,
        Orange,
        Pink,
        [EnumName(FromTranslation = "Common.Custom")]
        Custom
    }
    public enum VersionComparison
    {
        LessThan = -1,
        Equal = 0,
        GreaterThan = 1
    }
}
namespace Bloxstrap.Exceptions
{
    internal class ChecksumFailedException : Exception
    {
        public ChecksumFailedException(string message) : base(message) 
        { 
        }
    }
    public class InvalidChannelException : Exception
    {
        public HttpStatusCode? StatusCode;
        public InvalidChannelException(HttpStatusCode? statusCode) : base()
            => StatusCode = statusCode;
    }
    internal class InvalidHTTPResponseException : Exception
    {
        public InvalidHTTPResponseException(string message) : base(message) { }
    }
}
namespace Bloxstrap.Extensions
{
    static class BootstrapperIconEx
    {
        public static IReadOnlyCollection<BootstrapperIcon> Selections => new BootstrapperIcon[]
        {
            BootstrapperIcon.IconBloxstrap,
            BootstrapperIcon.Icon2025,
            BootstrapperIcon.Icon2022,
            BootstrapperIcon.Icon2019,
            BootstrapperIcon.Icon2017,
            BootstrapperIcon.IconLate2015,
            BootstrapperIcon.IconEarly2015,
            BootstrapperIcon.Icon2011,
            BootstrapperIcon.Icon2008,
            BootstrapperIcon.IconBloxstrapClassic,
            BootstrapperIcon.IconCustom
        };
        public static Icon GetIcon(this BootstrapperIcon icon)
        {
            const string LOG_IDENT = "BootstrapperIconEx::GetIcon";
            if (icon == BootstrapperIcon.IconCustom)
            {
                Icon? customIcon = null;
                string location = App.Settings.Prop.BootstrapperIconCustomLocation;
                if (String.IsNullOrEmpty(location))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Warning: custom icon is not set.");
                }
                else
                {
                    try
                    {
                        customIcon = new Icon(location);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to load custom icon!");
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }
                return customIcon ?? Properties.Resources.IconBloxstrap;
            }
            return icon switch
            {
                BootstrapperIcon.IconBloxstrap => Properties.Resources.IconBloxstrap,
                BootstrapperIcon.Icon2008 => Properties.Resources.Icon2008,
                BootstrapperIcon.Icon2011 => Properties.Resources.Icon2011,
                BootstrapperIcon.IconEarly2015 => Properties.Resources.IconEarly2015,
                BootstrapperIcon.IconLate2015 => Properties.Resources.IconLate2015,
                BootstrapperIcon.Icon2017 => Properties.Resources.Icon2017,
                BootstrapperIcon.Icon2019 => Properties.Resources.Icon2019,
                BootstrapperIcon.Icon2022 => Properties.Resources.Icon2022,
                BootstrapperIcon.Icon2025 => Properties.Resources.Icon2025,
                BootstrapperIcon.IconBloxstrapClassic => Properties.Resources.IconBloxstrapClassic,
                _ => Properties.Resources.IconBloxstrap
            };
        }
    }
    static class BootstrapperStyleEx
    {
        public static IBootstrapperDialog GetNew(this BootstrapperStyle bootstrapperStyle) => Frontend.GetBootstrapperDialog(bootstrapperStyle);
        public static IReadOnlyCollection<BootstrapperStyle> Selections => new BootstrapperStyle[]
        {
            BootstrapperStyle.FroststrapDialog,
            BootstrapperStyle.FluentAeroDialog,
            BootstrapperStyle.FluentDialog,
            BootstrapperStyle.ClassicFluentDialog,
            BootstrapperStyle.ByfronDialog,
            BootstrapperStyle.TwentyFiveDialog,
            BootstrapperStyle.ProgressDialog,
            BootstrapperStyle.LegacyDialog2011,
            BootstrapperStyle.LegacyDialog2008,
            BootstrapperStyle.VistaDialog
        };
    }
    static class DateTimeEx
    {
        public static string ToFriendlyString(this DateTime dateTime) => dateTime.ToString("dddd, d MMMM yyyy 'at' h:mm:ss tt", CultureInfo.InvariantCulture);
    }
    public static class IconEx
    {
        public static Icon GetSized(this Icon icon, int width, int height) => new(icon, new System.Drawing.Size(width, height));
        public static ImageSource GetImageSource(this Icon icon, bool handleException = true)
        {
            using var stream = new MemoryStream();
            icon.Save(stream);
            stream.Seek(0, SeekOrigin.Begin);
            if (handleException)
            {
                try
                {
                    return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("IconEx::GetImageSource", ex);
                    Frontend.ShowMessageBox(string.Format(Strings.Dialog_IconLoadFailed, ex.Message));
                    return BootstrapperIcon.IconBloxstrap.GetIcon().GetImageSource(false);
                }
            }
            else
            {
                return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
        }
    }
    public static class RegistryKeyEx
    {
        public static void SetValueSafe(this RegistryKey registryKey, string? name, object value)
        {
            try
            {
                App.Logger.WriteLine("RegistryKeyEx::SetValueSafe", $"Writing '{value}' to {registryKey}\\{name}");
                registryKey.SetValue(name, value);
            }
            catch (UnauthorizedAccessException)
            {
                Frontend.ShowMessageBox(Strings.Dialog_RegistryWriteError, System.Windows.MessageBoxImage.Error);
                App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
            }
        }
        public static void DeleteValueSafe(this RegistryKey registryKey, string name)
        {
            try
            {
                App.Logger.WriteLine("RegistryKeyEx::DeleteValueSafe", $"Deleting {registryKey}\\{name}");
                registryKey.DeleteValue(name);
            }
            catch (UnauthorizedAccessException)
            {
                Frontend.ShowMessageBox(Strings.Dialog_RegistryWriteError, System.Windows.MessageBoxImage.Error);
                App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
            }
        }
    }
    static class ResourceManagerEx
    {
        public static string GetStringSafe(this ResourceManager manager, string name) => manager.GetStringSafe(name, null);
        public static string GetStringSafe(this ResourceManager manager, string name, CultureInfo? culture)
        {
            string? resourceValue = manager.GetString(name, culture);
            return resourceValue ?? name;
        }
    }
    internal static class TEnumEx
    {
        private static readonly ConcurrentDictionary<(Type, string), string?> _descriptionCache = new();
        public static string? GetDescription<TEnum>(this TEnum e)
        {
            if (e == null) return null;
            string? enumName = e.ToString();
            if (enumName == null) return null;
            return _descriptionCache.GetOrAdd((typeof(TEnum), enumName), key =>
            {
                FieldInfo? field = key.Item1.GetField(key.Item2);
                if (field == null) return null;
                DescriptionAttribute? attribute = field.GetCustomAttribute<DescriptionAttribute>();
                return attribute?.Description;
            });
        }
    }
    public static class ThemeEx
    {
        public static Theme GetFinal(this Theme dialogTheme)
        {
            if (dialogTheme != Theme.Default)
                return dialogTheme;
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value && value == 0)
                return Theme.Dark;
            return Theme.Light;
        }
    }
}
namespace Bloxstrap.Models
{
    public class PackageMaps
    {
        [JsonPropertyName("common")]
        public Dictionary<string, string> CommonPackageMap { get; set; } = new Dictionary<string, string>()
        {
            { "Libraries.zip",                 @"" },
            { "redist.zip",                    @"" },
            { "shaders.zip",                   @"shaders\" },
            { "ssl.zip",                       @"ssl\" },
            { "WebView2.zip",                  @"" },
            { "WebView2RuntimeInstaller.zip",  @"WebView2RuntimeInstaller\" },
            { "content-avatar.zip",            @"content\avatar\" },
            { "content-configs.zip",           @"content\configs\" },
            { "content-fonts.zip",             @"content\fonts\" },
            { "content-sky.zip",               @"content\sky\" },
            { "content-sounds.zip",            @"content\sounds\" },
            { "content-textures2.zip",         @"content\textures\" },
            { "content-models.zip",            @"content\models\" },
            { "content-textures3.zip",         @"PlatformContent\pc\textures\" },
            { "content-terrain.zip",           @"PlatformContent\pc\terrain\" },
            { "content-platform-fonts.zip",    @"PlatformContent\pc\fonts\" },
            { "content-platform-dictionaries.zip", @"PlatformContent\pc\shared_compression_dictionaries\" },
            { "extracontent-luapackages.zip",  @"ExtraContent\LuaPackages\" },
            { "extracontent-translations.zip", @"ExtraContent\translations\" },
            { "extracontent-models.zip",       @"ExtraContent\models\" },
            { "extracontent-textures.zip",     @"ExtraContent\textures\" },
            { "extracontent-places.zip",       @"ExtraContent\places\" },
        };
        [JsonPropertyName("player")]
        public Dictionary<string, string> PlayerPackageMap { get; set; } = new Dictionary<string, string>()
        {
            { "RobloxApp.zip", @"" }
        };
        [JsonPropertyName("studio")]
        public Dictionary<string, string> StudioPackageMap { get; set; } = new Dictionary<string, string>()
        {
            { "RobloxStudio.zip",                @"" },
            { "LibrariesQt5.zip",                @"" },
            { "content-studio_svg_textures.zip", @"content\studio_svg_textures\"},
            { "content-qt_translations.zip",     @"content\qt_translations\" },
            { "content-api-docs.zip",            @"content\api_docs\" },
            { "extracontent-scripts.zip",        @"ExtraContent\scripts\" },
            { "studiocontent-models.zip",        @"StudioContent\models\" },
            { "studiocontent-textures.zip",      @"StudioContent\textures\" },
            { "BuiltInPlugins.zip",              @"BuiltInPlugins\" },
            { "BuiltInStandalonePlugins.zip",    @"BuiltInStandalonePlugins\" },
            { "ApplicationConfig.zip",           @"ApplicationConfig\" },
            { "Plugins.zip",                     @"Plugins\" },
            { "Qml.zip",                         @"Qml\" },
            { "StudioFonts.zip",                 @"StudioFonts\" },
            { "RibbonConfig.zip",                @"RibbonConfig\" }
        };
        public Dictionary<string, string> this[string key] =>
        key switch
        {
            "common" => CommonPackageMap,
            "player" => PlayerPackageMap,
            "studio" => StudioPackageMap,
            _ => null!
        };
    }
    public class RemoteDataBase
    {
        [JsonPropertyName("alertEnabled")]
        public bool AlertEnabled { get; set; } = false!;
        [JsonPropertyName("alertContent")]
        public string AlertContent { get; set; } = null!;
        [JsonPropertyName("alertSeverity")]
        public InfoBarSeverity AlertSeverity { get; set; } = InfoBarSeverity.Warning;
        [JsonPropertyName("packageMaps")]
        public PackageMaps PackageMaps { get; set; } = new();
        [JsonPropertyName("allowedFastFlags")]
        public string AllowedFastFlags { get; set; } = null!;
        [JsonPropertyName("mappings")]
        public Dictionary<string, string[]> Mappings { get; set; } = new Dictionary<string, string[]>();
        [JsonPropertyName("dummyCookie")]
        public string Dummy { get; set; } = string.Empty;
    }
    public class ClientFlagSettings
    {
        [JsonPropertyName("applicationSettings")]
        public Dictionary<string, string>? ApplicationSettings { get; set; }
    }
    public class ClientVersion
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = null!;
        [JsonPropertyName("clientVersionUpload")]
        public string VersionGuid { get; set; } = null!;
        [JsonPropertyName("bootstrapperVersion")]
        public string BootstrapperVersion { get; set; } = null!;
        public DateTime? Timestamp { get; set; }
        public bool IsBehindDefaultChannel { get; set; } = false;
    }
    public class FastFlag
    {
        public string Preset { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Value { get; set; } = null!;
    }
    public class LaunchFlag
    {
        public string Identifiers { get; private set; }
        public bool Active = false;
        public string? Data;
        public LaunchFlag(string identifiers)
        {
            Identifiers = identifiers;
        }
    }
    internal class WatcherData
    {
        public int ProcessId { get; set; }
    }
}
namespace Bloxstrap.Models.Attributes
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public class BuildMetadataAttribute : Attribute
    {
        public DateTime Timestamp { get; set; }
        public string Machine { get; set; }
        public string CommitHash { get; set; }
        public string CommitRef { get; set; }
        public BuildMetadataAttribute(string timestamp, string machine, string commitHash, string commitRef)
        {
            Timestamp = DateTime.Parse(timestamp).ToLocalTime();
            Machine = machine;
            CommitHash = commitHash;
            CommitRef = commitRef;
        }
    }
    class EnumNameAttribute : Attribute
    {
        public string? StaticName { get; set; }
        public string? FromTranslation { get; set; }
    }
    class EnumSortAttribute : Attribute
    {
        public int Order { get; set; }
    }
}
namespace Bloxstrap.Models.Manifest
{
    public class FileManifest : List<ManifestFile>
    {
        private FileManifest(string data)
        {
            using var reader = new StringReader(data);
            while (true)
            {
                string? fileName = reader.ReadLine();
                string? signature = reader.ReadLine();
                if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(signature))
                    break;
                Add(new ManifestFile
                {
                    Name = fileName,
                    Signature = signature
                });
            }
        }
        public static async Task<FileManifest> Get(string versionGuid)
        {
            string pkgManifestUrl = Deployment.GetLocation($"/{versionGuid}-rbxManifest.txt");
            var pkgManifestData = await App.HttpClient.GetStringAsync(pkgManifestUrl);
            return new FileManifest(pkgManifestData);
        }
    }
    public class ManifestFile
    {
        public string Name { get; set; } = "";
        public string Signature { get; set; } = "";
        public override string ToString() => $"[{Signature}] {Name}";
    }
    public class Package
    {
        public string Name { get; set; } = "";
        public string Signature { get; set; } = "";
        public int PackedSize { get; set; }
        public int Size { get; set; }
        public string DownloadPath => Path.Combine(Paths.Downloads, Signature);
        public override string ToString() => $"[{Signature}] {Name}";
    }
    public class PackageManifest : List<Package>
    {
        public PackageManifest(string data)
        {
            using var reader = new StringReader(data);
            string? version = reader.ReadLine();
            if (version != "v0")
                throw new NotSupportedException($"Unexpected package manifest version: {version} (expected v0!)");
            while (true)
            {
                string? fileName = reader.ReadLine();
                string? signature = reader.ReadLine();
                string? rawPackedSize = reader.ReadLine();
                string? rawSize = reader.ReadLine();
                if (string.IsNullOrEmpty(fileName) ||
                    string.IsNullOrEmpty(signature) ||
                    string.IsNullOrEmpty(rawPackedSize) ||
                    string.IsNullOrEmpty(rawSize))
                    break;
                if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    break;
                int packedSize = int.Parse(rawPackedSize);
                int size = int.Parse(rawSize);
                Add(new Package
                {
                    Name = fileName,
                    Signature = signature,
                    PackedSize = packedSize,
                    Size = size
                });
            }
        }
    }
}
namespace Bloxstrap.Models.Persistable
{
    public class AppState
    {
        public string VersionGuid { get; set; } = string.Empty;
        public Dictionary<string, string> PackageHashes { get; set; } = new();
        public int Size { get; set; }
    }
    public class DistributionState
    {
        public string VersionGuid { get; set; } = string.Empty;
        public Dictionary<string, string> PackageHashes { get; set; } = new();
        public int Size { get; set; }
    }
    public class RobloxState
    {
        public AppState Player { get; set; } = new();
        public AppState Studio { get; set; } = new();
    }
    public class Settings
    {
        public bool BackgroundUpdatesEnabled { get; set; } = false;
        public bool UseFastFlagManager { get; set; } = true;
        public bool ShowPresetColumn { get; set; } = false;
        public bool ShowFlagCount { get; set; } = true;
        public bool UseAltManually { get; set; } = true;
        public BootstrapperStyle BootstrapperStyle { get; set; } = BootstrapperStyle.FroststrapDialog;
        public BootstrapperIcon BootstrapperIcon { get; set; } = BootstrapperIcon.IconBloxstrap;
        public string Locale { get; set; } = "nil";
        public string BootstrapperTitle { get; set; } = App.ProjectName;
        public string BootstrapperIconCustomLocation { get; set; } = "";
        public string DownloadingStringFormat { get; set; } = Strings.Bootstrapper_Status_Downloading + " {0} - {1}MB / {2}MB";
        public Theme Theme { get; set; } = Theme.Default;
        public bool SaveAndLaunchToPlayer { get; set; } = true;
        public bool WPFSoftwareRender { get; set; } = false;
        public bool UpdateRoblox { get; set; } = true;
        public string RobloxDomain { get; set; } = RobloxInterfaces.Deployment.DefaultRobloxDomain;
        public bool StaticDirectory { get; set; } = false;
        public string Channel { get; set; } = RobloxInterfaces.Deployment.DefaultChannel;
        public ChannelChangeMode ChannelChangeMode { get; set; } = ChannelChangeMode.Prompt;
        public bool IsNavigationSidebarExpanded { get; set; } = true;
        public bool ForceLocalData { get; set; } = false;
        public bool DebugDisableVersionPackageCleanup { get; set; } = false;
    }
    public class State
    {
        public bool PromptWebView2Install { get; set; } = true;
        public bool ForceReinstall { get; set; } = false;
        public WindowState SettingsWindow { get; set; } = new();
    }
    public class WindowState
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
    }
}
namespace Bloxstrap.Models.SettingTasks
{
    public abstract class BaseTask
    {
        public string Name { get; private set; }
        public abstract bool Changed { get; }
        public BaseTask(string prefix, string name) : this($"{prefix}.{name}") { }
        public BaseTask(string name) => Name = name;
        public override string ToString() => Name;
        public abstract void Execute();
    }
    public abstract class BoolBaseTask : BaseTask
    {
        private bool _originalState;
        private bool _newState;
        public virtual bool OriginalState
        {
            get => _originalState;
            set
            {
                _originalState = value;
                _newState = value;
            }
        }
        public virtual bool NewState
        {
            get => _newState;
            set
            {
                _newState = value;
                if (Changed)
                    App.PendingSettingTasks[Name] = this;
                else
                    App.PendingSettingTasks.Remove(Name);
            }
        }
        public override bool Changed => _newState != OriginalState;
        public BoolBaseTask(string prefix, string name) : base(prefix, name) { }
        public BoolBaseTask(string name) : base(name) { }
    }
    public abstract class EnumBaseTask<T> : BaseTask where T : struct, Enum
    {
        private T _originalState = default!;
        private T _newState = default!;
        public virtual T OriginalState
        {
            get => _originalState;
            set
            {
                _originalState = value;
                _newState = value;
            }
        }
        public virtual T NewState
        {
            get => _newState;
            set
            {
                _newState = value;
                if (Changed)
                    App.PendingSettingTasks[Name] = this;
                else
                    App.PendingSettingTasks.Remove(Name);
            }
        }
        public override bool Changed => !_newState.Equals(OriginalState);
        public IEnumerable<T> Selections { get; private set; } 
            = Enum.GetValues(typeof(T)).Cast<T>().OrderBy(x =>
                {
                    var attributes = x.GetType().GetMember(x.ToString())[0].GetCustomAttributes(typeof(EnumSortAttribute), false);
                    if (attributes.Length > 0)
                    {
                        var attribute = (EnumSortAttribute)attributes[0];
                        return attribute.Order;
                    }
                    return 0;
                });
        public EnumBaseTask(string prefix, string name) : base(prefix, name) { }
    }
    public abstract class StringBaseTask : BaseTask
    {
        private string _originalState = "";
        private string _newState = "";
        public virtual string OriginalState
        {
            get => _originalState;
            set
            {
                _originalState = value;
                _newState = value;
            }
        }
        public virtual string NewState
        {
            get => _newState;
            set
            {
                _newState = value;
                if (Changed)
                    App.PendingSettingTasks[Name] = this;
                else
                    App.PendingSettingTasks.Remove(Name);
            }
        }
        public override bool Changed => _newState != OriginalState;
        public StringBaseTask(string prefix, string name) : base(prefix, name) { }
    }
    public class ExtractIconsTask : BoolBaseTask
    {
        private string _path => Path.Combine(Paths.Base, Strings.Paths_Icons);
        private static readonly string[] AllowedIconNames =
        {
            "Icon2008.ico",
            "Icon2011.ico",
            "Icon2017.ico",
            "Icon2019.ico",
            "Icon2022.ico",
            "Icon2025.ico",
            "IconBloxstrap.ico",
            "IconEarly2015.ico",
            "IconLate2015.ico"
        };
        public ExtractIconsTask() : base("ExtractIcons")
        {
            OriginalState = Directory.Exists(_path);
        }
        public override void Execute()
        {
            if (NewState)
            {
                Directory.CreateDirectory(_path);
                var assembly = Assembly.GetExecutingAssembly();
                foreach (string iconName in AllowedIconNames)
                {
                    string fullResourceName = $"Bloxstrap.Resources.{iconName}";
                    using var stream = assembly.GetManifestResourceStream(fullResourceName);
                    if (stream == null)
                        continue;
                    string filePath = Path.Combine(_path, iconName);
                    Filesystem.AssertReadOnly(filePath);
                    using var fileStream = File.Create(filePath);
                    stream.CopyTo(fileStream);
                }
            }
            else if (Directory.Exists(_path))
            {
                Directory.Delete(_path, true);
            }
            OriginalState = NewState;
        }
    }
    public class ShortcutTask : BoolBaseTask
    {
        private string _shortcutPath;
        private string _exeFlags;
        public ShortcutTask(string name, string lnkFolder, string lnkName, string exeFlags = "") : base("Shortcut", name)
        {
            _shortcutPath = Path.Combine(lnkFolder, lnkName);
            _exeFlags = exeFlags;
            OriginalState = File.Exists(_shortcutPath);
        }
        public override void Execute()
        {
            if (NewState)
                Shortcut.Create(Paths.Application, _exeFlags, _shortcutPath);
            else if (File.Exists(_shortcutPath))
                File.Delete(_shortcutPath);
            OriginalState = NewState;
        }
    }
}
namespace Bloxstrap.AppData
{
    public abstract class CommonAppData
    {
        public virtual string ExecutableName { get; } = null!;
        public virtual string BinaryType { get; } = null!;
        public string StaticDirectory => Path.Combine(Paths.Versions, BinaryType);
        public string DynamicDirectory => Path.Combine(Paths.Versions, DistributionState.VersionGuid);
        public string Directory => App.Settings.Prop.StaticDirectory ? StaticDirectory : DynamicDirectory;
        public string ExecutablePath => Path.Combine(Directory, ExecutableName);
        public virtual JsonManager<DistributionState> DistributionStateManager { get; } = null!;
        public DistributionState DistributionState => DistributionStateManager.Prop;
    }
    internal interface IAppData
    {
        string ProductName { get; }
        string BinaryType { get; }
        string RegistryName { get; }
        string ProcessName { get; }
        string ExecutableName { get; }
        string StaticDirectory { get; }
        string DynamicDirectory { get; }
        string Directory { get; }
        string ExecutablePath { get; }
        JsonManager<DistributionState> DistributionStateManager { get; }
        DistributionState DistributionState { get; }
    }
    public class RobloxPlayerData : CommonAppData, IAppData
    {
        public string ProductName => "Roblox";
        public override string BinaryType => "WindowsPlayer";
        public string RegistryName => "RobloxPlayer";
        public string ProcessName => "RobloxPlayerBeta";
        public override string ExecutableName => App.RobloxPlayerAppName;
        public override JsonManager<DistributionState> DistributionStateManager => App.PlayerState;
    }
    public class RobloxStudioData : CommonAppData, IAppData
    {
        public string ProductName => "Roblox Studio";
        public override string BinaryType => "WindowsStudio64";
        public string RegistryName => "RobloxStudio";
        public string ProcessName => "RobloxStudioBeta";
        public override string ExecutableName => App.RobloxStudioAppName;
        public override JsonManager<DistributionState> DistributionStateManager => App.StudioState;
    }
}
