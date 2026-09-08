using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Bloxstrap.UI.Elements.Dialogs;
namespace Bloxstrap.RobloxInterfaces
{
    public class ApplicationSettings
    {
        private readonly string _applicationName;
        private readonly string _channelName;
        private bool _initialised = false;
        private Dictionary<string, string>? _flags;
        private readonly SemaphoreSlim semaphoreSlim = new(1, 1);
        private static readonly ConcurrentDictionary<string, ApplicationSettings> _cache = new(StringComparer.OrdinalIgnoreCase);
        private ApplicationSettings(string applicationName, string channelName)
        {
            _applicationName = applicationName;
            _channelName = channelName;
        }
        private async Task Fetch()
        {
            if (_initialised)
                return;
            await semaphoreSlim.WaitAsync();
            try
            {
                if (_initialised)
                    return;
                string logIndent = $"ApplicationSettings::Fetch.{_applicationName}.{_channelName}";
                App.Logger.WriteLine(logIndent, "Fetching fast flags");
                string path = $"/v2/settings/application/{_applicationName}";
                if (_channelName != Deployment.DefaultChannel.ToLowerInvariant())
                    path += $"/bucket/{_channelName}";
                var clientSettings = await Http.GetClientSettingsJson<ClientFlagSettings>(Deployment.RobloxDomain, path);
                if (clientSettings?.ApplicationSettings == null)
                    throw new Exception("Deserialised application settings is null!");
                _flags = clientSettings.ApplicationSettings;
                _initialised = true;
            }
            finally
            {
                semaphoreSlim.Release();
            }
        }
        public async Task<T?> GetAsync<T>(string name)
        {
            await Fetch();
            if (_flags == null || !_flags.TryGetValue(name, out string? value) || value == null)
                return default;
            try
            {
                var converter = TypeDescriptor.GetConverter(typeof(T));
                if (converter == null)
                    return default;
                return (T?)converter.ConvertFromString(value);
            }
            catch (NotSupportedException)  
            {
                return default;
            }
        }
        public T? Get<T>(string name) => GetAsync<T>(name).GetAwaiter().GetResult();
        public static ApplicationSettings PCDesktopClient => GetSettings("PCDesktopClient");
        public static ApplicationSettings PCClientBootstrapper => GetSettings("PCClientBootstrapper");
        public static ApplicationSettings GetSettings(string applicationName, string channelName = Deployment.DefaultChannel, bool shouldCache = true)
        {
            channelName = channelName.ToLowerInvariant();
            string key = $"{applicationName}:{channelName}";
            if (shouldCache)
                return _cache.GetOrAdd(key, _ => new ApplicationSettings(applicationName, channelName));
            return new ApplicationSettings(applicationName, channelName);
        }
    }
    public static class Deployment
    {
        public const string DefaultRobloxDomain = "roblox.com";
        public const string DefaultChannel = "production";
        private const string VersionStudioHash = "version-012732894899482c";
        public static EventHandler<string>? ChannelChanged;
        private static string _channel = App.Settings.Prop.Channel;
        public static string Channel
        {
            get => _channel;
            set
            {
                _channel = value;
                App.Settings.Prop.Channel = Channel;
                App.Settings.Save();
                ChannelChanged?.Invoke(null, value);
            }
        }
        public static string ChannelToken = string.Empty;
        public static string BinaryType = "WindowsPlayer";
        public static string RobloxDomain => App.Settings.Prop.RobloxDomain;
        public static bool IsDefaultChannel => Channel.Equals(DefaultChannel, StringComparison.OrdinalIgnoreCase) || Channel.Equals("live", StringComparison.OrdinalIgnoreCase);
        public static bool IsDefaultRobloxDomain => RobloxDomain.Equals(DefaultRobloxDomain, StringComparison.OrdinalIgnoreCase);
        public static string BaseUrl { get; private set; } = null!;
        public static readonly List<HttpStatusCode?> BadChannelCodes = new()
        {
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden,
            HttpStatusCode.NotFound
        };
        private static readonly ConcurrentDictionary<string, ClientVersion> ClientVersionCache = new();
        private static readonly Dictionary<string, int> BaseUrls = new()
        {
            { "https://setup.rbxcdn.com", 0 },
            { "https://setup-aws.rbxcdn.com", 2 },
            { "https://setup-ak.rbxcdn.com", 2 },
            { "https://roblox-setup.cachefly.net", 2 },
            { "https://s3.amazonaws.com/setup.roblox.com", 4 }
        };
        private static async Task<string?> TestConnection(string url, int priority, CancellationToken token)
        {
            string LOG_IDENT = $"Deployment::TestConnection<{url}>";
            await Task.Delay(priority * 1000, token);
            App.Logger.WriteLine(LOG_IDENT, "Connecting...");
            try
            {
                var response = await App.HttpClient.GetAsync($"{url}/versionStudio", token);
                response.EnsureSuccessStatusCode();
                string content = await response.Content.ReadAsStringAsync(token);
                if (content != VersionStudioHash)
                    throw new InvalidHTTPResponseException($"versionStudio response does not match (expected \"{VersionStudioHash}\", got \"{content}\")");
            }
            catch (TaskCanceledException)
            {
                App.Logger.WriteLine(LOG_IDENT, "Connectivity test cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                throw;
            }
            return url;
        }
        public static async Task<Exception?> InitializeConnectivity()
        {
            const string LOG_IDENT = "Deployment::InitializeConnectivity";
            using var tokenSource = new CancellationTokenSource();
            var exceptions = new List<Exception>();
            var tasks = (from entry in BaseUrls select TestConnection(entry.Key, entry.Value, tokenSource.Token)).ToList();
            App.Logger.WriteLine(LOG_IDENT, "Testing connectivity...");
            while (tasks.Any() && string.IsNullOrEmpty(BaseUrl))
            {
                var finishedTask = await Task.WhenAny(tasks);
                tasks.Remove(finishedTask);
                if (finishedTask.IsFaulted)
                    exceptions.Add(finishedTask.Exception!.InnerException!);
                else if (!finishedTask.IsCanceled)
                    BaseUrl = finishedTask.Result!;
            }
            tokenSource.Cancel();
            if (string.IsNullOrEmpty(BaseUrl))
            {
                if (exceptions.Any())
                    return exceptions[0];
                return new TaskCanceledException("All connection attempts timed out.");
            }
            App.Logger.WriteLine(LOG_IDENT, $"Got {BaseUrl} as the optimal base URL");
            return null;
        }
        public static string GetLocation(string resource)
        {
            string location = BaseUrl;
            if (!IsDefaultChannel)
                location += "/channel/common";
            location += resource;
            return location;
        }
        public static async Task<bool> IsChannelPrivate(string channel)
        {
            if (channel == "production")
                channel = "live";
            try
            {
                var response = await App.HttpClient.GetAsync($"https://clientsettingscdn.{RobloxDomain}/v2/client-version/WindowsPlayer/channel/{channel}");
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                if (BadChannelCodes.Contains(ex.StatusCode))
                    return true;
            }
            return false;
        }
        public static async Task<DateTime?> GetVersionTimestamp(string version)
        {
            const string LOG_IDENT = "Deployment::GetVersionTimestamp";
            const string header = "last-modified";
            if (string.IsNullOrEmpty(BaseUrl))
                await InitializeConnectivity();
            try
            {
                string location = GetLocation($"/{version}-rbxPkgManifest.txt");
                var response = await App.HttpClient.GetAsync(location);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.TryGetValues(header, out var values))
                {
                    string lastModified = values.First();
                    return DateTime.Parse(lastModified);
                }
            }
            catch (HttpRequestException ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to get timestamp for {version}");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
            return null;
        }
        public static async Task<ClientVersion> GetInfo(string? channel = null, bool behindProductionCheck = false, bool includeTimestamp = false)
        {
            const string LOG_IDENT = "Deployment::GetInfo";
            if (string.IsNullOrEmpty(channel))
                channel = Channel;
            bool isDefaultChannel = string.Compare(channel, DefaultChannel, StringComparison.OrdinalIgnoreCase) == 0;
            App.Logger.WriteLine(LOG_IDENT, $"Getting deploy info for channel {channel}");
            string cacheKey = $"{channel}-{BinaryType}";
            if (ClientVersionCache.TryGetValue(cacheKey, out var cachedVersion))
            {
                App.Logger.WriteLine(LOG_IDENT, "Deploy information is cached");
                return cachedVersion;
            }
            string path = isDefaultChannel
                ? $"/v2/client-version/{BinaryType}"
                : $"/v2/client-version/{BinaryType}/channel/{channel}";
            ClientVersion clientVersion;
            try
            {
                clientVersion = await Http.GetClientSettingsJson<ClientVersion>(RobloxDomain, path, ChannelToken);
            }
            catch (HttpRequestException httpEx) when (!isDefaultChannel && BadChannelCodes.Contains(httpEx.StatusCode))
            {
                throw new InvalidChannelException(httpEx.StatusCode);
            }
            if (!isDefaultChannel && behindProductionCheck)
            {
                var defaultClientVersion = await GetInfo(DefaultChannel);
                if (Utilities.CompareVersions(clientVersion.Version, defaultClientVersion.Version) == VersionComparison.LessThan)
                    clientVersion.IsBehindDefaultChannel = true;
            }
            else
            {
                clientVersion.IsBehindDefaultChannel = false;
            }
            ClientVersionCache[cacheKey] = clientVersion;
            if (includeTimestamp && clientVersion.Timestamp is null)
                clientVersion.Timestamp = await GetVersionTimestamp(clientVersion.VersionGuid);
            return clientVersion;
        }
    }
    public class FastFlagManager : JsonManager<Dictionary<string, object>>
    {
        private Dictionary<string, object> OriginalProp = new();
        public override string ClassName => nameof(FastFlagManager);
        public override string LOG_IDENT_CLASS => ClassName;
        public override string FileName => "ClientAppSettings.json";
        public override string FileLocation => Path.Combine(Paths.Base, "ClientSettings", FileName);
        public bool Changed => OriginalProp.Count != Prop.Count || !OriginalProp.SequenceEqual(Prop);
        public static readonly IReadOnlyDictionary<string, string> PresetFlags = new Dictionary<string, string>
        {
            { "Rendering.ManualFullscreen", "FFlagHandleAltEnterFullscreenManually" },
            { "Rendering.PauseVoxerlizer", "DFFlagDebugPauseVoxelizer" },
            { "Rendering.DisableScaling", "DFFlagDisableDPIScale" },
            { "Rendering.TextureQuality.OverrideEnabled", "DFFlagTextureQualityOverrideEnabled" },
            { "Rendering.TextureQuality.Level", "DFIntTextureQualityOverride" },
            { "Rendering.FrmQuality", "DFIntDebugFRMQualityLevelOverride" },
            { "Rendering.LowPolyMeshes1", "DFIntCSGLevelOfDetailSwitchingDistance" },
            { "Rendering.LowPolyMeshes2", "DFIntCSGLevelOfDetailSwitchingDistanceL12" },
            { "Rendering.LowPolyMeshes3", "DFIntCSGLevelOfDetailSwitchingDistanceL23" },
            { "Rendering.LowPolyMeshes4", "DFIntCSGLevelOfDetailSwitchingDistanceL34" },
            { "Rendering.Mode.D3D11", "FFlagDebugGraphicsPreferD3D11" },
            { "Rendering.Mode.Vulkan", "FFlagDebugGraphicsPreferVulkan" },
            { "Rendering.Mode.OpenGL", "FFlagDebugGraphicsPreferOpenGL" },
            { "Graphic.GraySky", "FFlagDebugSkyGray" },
            { "Rendering.MSAA1", "FIntDebugForceMSAASamples" },
            { "Rendering.RemoveGrass1", "FIntFRMMinGrassDistance" },
            { "Rendering.RemoveGrass2", "FIntFRMMaxGrassDistance" },
            { "Rendering.RemoveGrass3", "FIntGrassMovementReducedMotionFactor" },
        };
        private static readonly HashSet<string> _presetFlagValues = new(PresetFlags.Values, StringComparer.OrdinalIgnoreCase);
        public static readonly IReadOnlyDictionary<RenderingMode, string> RenderingModes = new Dictionary<RenderingMode, string>
        {
            { RenderingMode.Default, "None" },
            { RenderingMode.Vulkan, "Vulkan" },
            { RenderingMode.OpenGL, "OpenGL" },
        };
        public static readonly IReadOnlyDictionary<MSAAMode, string?> MSAAModes = new Dictionary<MSAAMode, string?>
        {
            { MSAAMode.Default, null },
            { MSAAMode.x1, "1" },
            { MSAAMode.x2, "2" },
            { MSAAMode.x4, "4" }
        };
        public static readonly IReadOnlyDictionary<QualityLevel, string?> QualityLevels = new Dictionary<QualityLevel, string?>
        {
            { QualityLevel.Disabled, null },
            { QualityLevel.Level1, "1" },
            { QualityLevel.Level2, "2" },
            { QualityLevel.Level3, "3" },
            { QualityLevel.Level4, "4" },
            { QualityLevel.Level5, "5" },
            { QualityLevel.Level6, "6" },
            { QualityLevel.Level7, "7" },
            { QualityLevel.Level8, "8" },
            { QualityLevel.Level9, "9" },
            { QualityLevel.Level10, "10" },
            { QualityLevel.Level11, "11" },
            { QualityLevel.Level12, "12" },
            { QualityLevel.Level13, "13" },
            { QualityLevel.Level14, "14" },
            { QualityLevel.Level15, "15" },
            { QualityLevel.Level16, "16" },
            { QualityLevel.Level17, "17" },
            { QualityLevel.Level18, "18" },
            { QualityLevel.Level19, "19" },
            { QualityLevel.Level20, "20" },
            { QualityLevel.Level21, "21" }
        };
        public bool suspendUndoSnapshot = false;
        public void SetValue(string key, object? value)
        {
            const string LOG_IDENT = "FastFlagManager::SetValue";
            if (!suspendUndoSnapshot)
                SaveUndoSnapshot();
            if (value is null)
            {
                if (Prop.Remove(key))
                    App.Logger.WriteLine(LOG_IDENT, $"Deletion of '{key}' is pending");
            }
            else
            {
                string valStr = value.ToString()!;
                if (Prop.TryGetValue(key, out var existingVal))
                {
                    if (existingVal?.ToString() == valStr)
                        return;
                    App.Logger.WriteLine(LOG_IDENT, $"Changing of '{key}' from '{existingVal}' to '{valStr}' is pending");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Setting of '{key}' to '{valStr}' is pending");
                }
                Prop[key] = valStr;
            }
        }
        public string? GetValue(string key)
        {
            if (Prop.TryGetValue(key, out object? value) && value is not null)
                return value.ToString();
            return null;
        }
        public void SetPreset(string prefix, object? value)
        {
            foreach (var pair in PresetFlags.Where(x => x.Key.StartsWith(prefix)))
                SetValue(pair.Value, value);
        }
        public void SetPresetEnum(string prefix, string target, object? value)
        {
            foreach (var pair in PresetFlags.Where(x => x.Key.StartsWith(prefix)))
            {
                if (pair.Key.StartsWith($"{prefix}.{target}"))
                    SetValue(pair.Value, value);
                else
                    SetValue(pair.Value, null);
            }
        }
        public string? GetPreset(string name)
        {
            if (!PresetFlags.TryGetValue(name, out string? flagKey))
            {
                App.Logger.WriteLine("FastFlagManager::GetPreset", $"Could not find preset {name}");
                return null;
            }
            return GetValue(flagKey);
        }
        public T GetPresetEnum<T>(IReadOnlyDictionary<T, string> mapping, string prefix, string value) where T : Enum
        {
            foreach (var pair in mapping)
            {
                if (pair.Value == "None")
                    continue;
                if (GetPreset($"{prefix}.{pair.Value}") == value)
                    return pair.Key;
            }
            return mapping.First().Key;
        }
        public bool IsPreset(string Flag) => _presetFlagValues.Contains(Flag);
        public override void Save()
        {
            foreach (var pair in Prop)
                Prop[pair.Key] = pair.Value!.ToString()!;
            base.Save();
            OriginalProp = new(Prop);
        }
        public override bool Load(bool alertFailure = true)
        {
            bool result = base.Load(alertFailure);
            OriginalProp = new(Prop);
            if (App.Settings.Prop.UseAltManually)
            {
                if (GetPreset("Rendering.ManualFullscreen") != "False")
                    SetPreset("Rendering.ManualFullscreen", "False");
            }
            return result;
        }
        public void DeleteProfile(string Profile)
        {
            try
            {
                string profilesDirectory = Paths.SavedFlagProfiles;
                if (!Directory.Exists(profilesDirectory))
                    Directory.CreateDirectory(profilesDirectory);
                if (string.IsNullOrEmpty(Profile))
                    return;
                File.Delete(Path.Combine(profilesDirectory, Profile));
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(ex.Message, MessageBoxImage.Error);
            }
        }
        public IEnumerable<FastFlag> GetAllFlags()
        {
            var list = new List<FastFlag>(Prop.Count);
            foreach (var kvp in Prop)
            {
                list.Add(new FastFlag
                {
                    Name = kvp.Key,
                    Value = kvp.Value?.ToString() ?? "",
                    Preset = ""
                });
            }
            return list;
        }
        private readonly Stack<Dictionary<string, object?>> undoStack = new();
        private readonly Stack<Dictionary<string, object?>> redoStack = new();
        public void SaveUndoSnapshot()
        {
            if (undoStack.Count > 0 && DictionaryEquals(undoStack.Peek(), Prop!))
                return;
            undoStack.Push(new Dictionary<string, object?>(Prop!));
            redoStack.Clear();
        }
        private static bool DictionaryEquals(Dictionary<string, object?> a, Dictionary<string, object?> b)
        {
            if (a.Count != b.Count)
                return false;
            foreach (var pair in a)
            {
                if (!b.TryGetValue(pair.Key, out var bValue))
                    return false;
                if (!Equals(pair.Value, bValue))
                    return false;
            }
            return true;
        }
        public void Undo()
        {
            if (undoStack.Count == 0)
                return;
            redoStack.Push(new Dictionary<string, object?>(Prop!));
            var previous = undoStack.Pop();
            Prop.Clear();
            foreach (var kvp in previous)
                Prop[kvp.Key] = kvp.Value!;
        }
        public void Redo()
        {
            if (redoStack.Count == 0)
                return;
            undoStack.Push(new Dictionary<string, object?>(Prop!));
            var next = redoStack.Pop();
            Prop.Clear();
            foreach (var kvp in next)
                Prop[kvp.Key] = kvp.Value!;
        }
    }
    public class JsonManager<T> where T : class, new()
    {
        protected static readonly JsonSerializerOptions _defaultJsonOptions = new() { WriteIndented = true };
        protected T _prop = new();
        public virtual T Prop
        {
            get => _prop;
            set => _prop = value;
        }
        public string? LastFileHash { get; private set; }
        public bool Loaded { get; protected set; } = false;
        public virtual string ClassName { get; }
        public virtual string FileName => $"{ClassName}.json";
        public virtual string FileLocation => Path.Combine(Paths.Base, FileName);
        public bool IsSaved => File.Exists(FileLocation);
        public virtual string LOG_IDENT_CLASS => $"JsonManager<{ClassName}>";
        public JsonManager(string? className = null)
        {
            ClassName = string.IsNullOrEmpty(className) ? typeof(T).Name : className;
        }
        public virtual bool Load(bool alertFailure = true)
        {
            string LOG_IDENT = $"{LOG_IDENT_CLASS}::Load";
            App.Logger.WriteLine(LOG_IDENT, $"Loading from {FileLocation}...");
            try
            {
                if (File.Exists(FileLocation))
                {
                    using (var stream = File.OpenRead(FileLocation))
                    {
                        T? settings = JsonSerializer.Deserialize<T>(stream);
                        if (settings is null)
                            throw new ArgumentNullException("Deserialization returned null");
                        _prop = settings;
                    }
                    Loaded = true;
                    LastFileHash = MD5Hash.FromFile(FileLocation);
                    App.Logger.WriteLine(LOG_IDENT, "Loaded successfully!");
                    return true;
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not find {FileLocation}.");
                    Loaded = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to load!");
                App.Logger.WriteException(LOG_IDENT, ex);
                if (alertFailure)
                {
                    string message = "";
                    if (ClassName == nameof(Settings))
                        message = Strings.JsonManager_SettingsLoadFailed;
                    else if (ClassName == nameof(FastFlagManager))
                        message = Strings.JsonManager_FastFlagsLoadFailed;
                    if (!string.IsNullOrEmpty(message))
                        Frontend.ShowMessageBox($"{message}\n\n{ex.Message}", System.Windows.MessageBoxImage.Warning);
                    try
                    {
                        File.Copy(FileLocation, FileLocation + ".bak", true);
                    }
                    catch (Exception copyEx)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to create backup file: {FileLocation}.bak");
                        App.Logger.WriteException(LOG_IDENT, copyEx);
                    }
                }
                Loaded = true;
                Save();
                return false;
            }
        }
        public virtual void Save()
        {
            string LOG_IDENT = $"{LOG_IDENT_CLASS}::Save";
            App.Logger.WriteLine(LOG_IDENT, $"Saving to {FileLocation}...");
            Directory.CreateDirectory(Path.GetDirectoryName(FileLocation)!);
            try
            {
                using (var stream = File.Create(FileLocation))
                {
                    JsonSerializer.Serialize(stream, Prop, _defaultJsonOptions);
                }
                LastFileHash = MD5Hash.FromFile(FileLocation);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to save");
                App.Logger.WriteException(LOG_IDENT, ex);
                string errorMessage = string.Format(Resources.Strings.Bootstrapper_JsonManagerSaveFailed, ClassName, ex.Message);
                Frontend.ShowMessageBox(errorMessage, System.Windows.MessageBoxImage.Warning);
                return;
            }
            App.Logger.WriteLine(LOG_IDENT, "Save complete!");
        }
        public virtual void Delete()
        {
            string LOG_IDENT = $"{LOG_IDENT_CLASS}::Delete";
            try
            {
                if (File.Exists(FileLocation))
                {
                    File.Delete(FileLocation);
                    Loaded = false;
                    App.Logger.WriteLine(LOG_IDENT, "Delete complete!");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, "File does not exist on disk");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to delete");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }
        public void SaveProfile(string name)
        {
            string LOGGER_STRING = "SaveProfile::Profiles";
            string BaseDir = Paths.SavedFlagProfiles;
            try
            {
                string FileDirectory = Path.Combine(BaseDir, name);
                if (string.IsNullOrEmpty(name))
                    return;
                if (!Directory.Exists(BaseDir))
                    Directory.CreateDirectory(BaseDir);
                App.Logger.WriteLine(LOGGER_STRING, $"Writing flag profile {name}");
                using var stream = File.Create(FileDirectory);
                JsonSerializer.Serialize(stream, Prop, _defaultJsonOptions);
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(ex.Message, MessageBoxImage.Error);
            }
        }
        public void LoadProfile(string? name, bool? clearFlags)
        {
            string LOGGER_STRING = "LoadProfile::Profiles";
            string BaseDir = Paths.SavedFlagProfiles;
            if (string.IsNullOrEmpty(name))
                return;
            try
            {
                if (!Directory.Exists(BaseDir))
                    Directory.CreateDirectory(BaseDir);
                string FoundFile = Path.Combine(BaseDir, name);
                if (!File.Exists(FoundFile))
                    return;
                App.Logger.WriteLine(LOGGER_STRING, $"Loading {FoundFile}");
                T? settings;
                using (var stream = File.OpenRead(FoundFile))
                {
                    settings = JsonSerializer.Deserialize<T>(stream);
                }
                if (settings is null)
                    throw new ArgumentNullException("Deserialization returned null");
                App.FastFlags.suspendUndoSnapshot = true;
                App.FastFlags.SaveUndoSnapshot();
                if (clearFlags == true)
                {
                    Prop = settings;
                }
                else
                {
                    if (settings is IDictionary<string, object> settingsDict && Prop is IDictionary<string, object> propDict)
                    {
                        foreach (var kvp in settingsDict)
                        {
                            if (kvp.Value != null)
                                propDict[kvp.Key] = kvp.Value;
                        }
                    }
                }
                App.FastFlags.suspendUndoSnapshot = false;
                App.FastFlags.Save();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(ex.Message, MessageBoxImage.Error);
            }
        }
        public void LoadPresetProfile(string? name, bool? clearFlags)
        {
            string LOGGER_STRING = "LoadProfile::Profiles";
            if (string.IsNullOrEmpty(name))
                return;
            try
            {
                T? settings;
                var assembly = Assembly.GetExecutingAssembly();
                string resourcePrefix = "Bloxstrap.Resources.PresetFlags.";
                string resourceFullName = resourcePrefix + name;
                string? foundResource = assembly.GetManifestResourceNames()
                                               .FirstOrDefault(r => r.Equals(resourceFullName, StringComparison.OrdinalIgnoreCase));
                if (foundResource != null)
                {
                    App.Logger.WriteLine(LOGGER_STRING, $"Loading embedded preset profile {name}");
                    using var stream = assembly.GetManifestResourceStream(foundResource)!;
                    settings = JsonSerializer.Deserialize<T>(stream);
                }
                else
                {
                    string BaseDir = Paths.SavedFlagProfiles;
                    if (!Directory.Exists(BaseDir))
                        Directory.CreateDirectory(BaseDir);
                    string FoundFile = Path.Combine(BaseDir, name);
                    if (!File.Exists(FoundFile))
                        throw new FileNotFoundException($"Profile file '{name}' not found.");
                    App.Logger.WriteLine(LOGGER_STRING, $"Loading user profile from file {name}");
                    using var stream = File.OpenRead(FoundFile);
                    settings = JsonSerializer.Deserialize<T>(stream);
                }
                if (settings is null)
                    throw new ArgumentNullException("Deserialization returned null");
                App.FastFlags.suspendUndoSnapshot = true;
                App.FastFlags.SaveUndoSnapshot();
                if (clearFlags == true)
                {
                    Prop = settings;
                }
                else
                {
                    if (settings is IDictionary<string, object> settingsDict && Prop is IDictionary<string, object> propDict)
                    {
                        foreach (var kvp in settingsDict)
                        {
                            if (kvp.Value != null)
                                propDict[kvp.Key] = kvp.Value;
                        }
                    }
                }
                App.FastFlags.suspendUndoSnapshot = false;
                App.FastFlags.Save();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(ex.Message, MessageBoxImage.Error);
            }
        }
        public bool HasFileOnDiskChanged()
        {
            if (string.IsNullOrEmpty(LastFileHash) && File.Exists(FileLocation))
                return true;
            return LastFileHash != MD5Hash.FromFile(FileLocation);
        }
    }
    public class LazyJsonManager<T> : JsonManager<T> where T : class, new()
    {
        public override T Prop
        {
            get
            {
                if (!Loaded)
                    Load();
                return _prop;
            }
            set
            {
                _prop = value;
                Loaded = true;
            }
        }
        public LazyJsonManager(string? className)
            : base(className)
        {
        }
    }
    public static class LaunchHandler
    {
        public static void ProcessNextAction(NextAction action, bool isUnfinishedInstall = false)
        {
            const string LOG_IDENT = "LaunchHandler::ProcessNextAction";
            switch (action)
            {
                case NextAction.LaunchSettings:
                    App.Logger.WriteLine(LOG_IDENT, "Opening settings");
                    LaunchSettings();
                    break;
                case NextAction.LaunchRoblox:
                    App.Logger.WriteLine(LOG_IDENT, "Opening Roblox");
                    LaunchRoblox(LaunchMode.Player);
                    break;
                case NextAction.LaunchRobloxStudio:
                    App.Logger.WriteLine(LOG_IDENT, "Opening Roblox Studio");
                    LaunchRoblox(LaunchMode.Studio);
                    break;
                default:
                    App.Logger.WriteLine(LOG_IDENT, "Closing");
                    App.Terminate(isUnfinishedInstall ? ErrorCode.ERROR_INSTALL_USEREXIT : ErrorCode.ERROR_SUCCESS);
                    break;
            }
        }
        public static void ProcessLaunchArgs()
        {
            const string LOG_IDENT = "LaunchHandler::ProcessLaunchArgs";
            if (App.LaunchSettings.UninstallFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening uninstaller");
                LaunchUninstaller();
            }
            else if (App.LaunchSettings.MenuFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening settings");
                LaunchSettings();
            }
            else if (App.LaunchSettings.WatcherFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening watcher");
                LaunchWatcher();
            }
            else if (App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening background updater");
                LaunchBackgroundUpdater();
            }
            else if (App.LaunchSettings.RobloxLaunchMode != LaunchMode.None)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Opening bootstrapper ({App.LaunchSettings.RobloxLaunchMode})");
                LaunchRoblox(App.LaunchSettings.RobloxLaunchMode);
            }
            else if (!App.LaunchSettings.QuietFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening settings");
                LaunchSettings();
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "Closing - quiet flag active");
                App.Terminate();
            }
        }
        public static void LaunchInstaller()
        {
            using var interlock = new InterProcessLock("Installer");
            if (!interlock.IsAcquired)
            {
                Frontend.ShowMessageBox(Strings.Dialog_AlreadyRunning_Installer, MessageBoxImage.Stop);
                App.Terminate();
                return;
            }
            if (App.LaunchSettings.UninstallFlag.Active)
            {
                Frontend.ShowMessageBox(Strings.Bootstrapper_FirstRunUninstall, MessageBoxImage.Error);
                App.Terminate(ErrorCode.ERROR_INVALID_FUNCTION);
                return;
            }
            if (App.LaunchSettings.QuietFlag.Active)
            {
                var installer = new Installer();
                if (!installer.CheckInstallLocation())
                    App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
                installer.DoInstall();
                interlock.Dispose();
                ProcessLaunchArgs();
            }
            else
            {
                new LanguageSelectorDialog().ShowDialog();
                var installer = new UI.Elements.Installer.MainWindow();
                installer.ShowDialog();
                interlock.Dispose();
                ProcessNextAction(installer.CloseAction, !installer.Finished);
            }
        }
        public static void LaunchUninstaller()
        {
            using var interlock = new InterProcessLock("Uninstaller");
            if (!interlock.IsAcquired)
            {
                Frontend.ShowMessageBox(Strings.Dialog_AlreadyRunning_Uninstaller, MessageBoxImage.Stop);
                App.Terminate();
                return;
            }
            bool confirmed;
            bool keepData = true;
            if (App.LaunchSettings.QuietFlag.Active)
            {
                confirmed = true;
            }
            else
            {
                var dialog = new UninstallerDialog();
                dialog.ShowDialog();
                confirmed = dialog.Confirmed;
                keepData = dialog.KeepData;
            }
            if (!confirmed)
            {
                App.Terminate();
                return;
            }
            Installer.DoUninstall(keepData);
            Frontend.ShowMessageBox(Strings.Bootstrapper_SuccessfullyUninstalled, MessageBoxImage.Information);
            App.Terminate();
        }
        public static void LaunchSettings()
        {
            const string LOG_IDENT = "LaunchHandler::LaunchSettings";
            using var interlock = new InterProcessLock("Settings");
            if (interlock.IsAcquired)
            {
                var procs = Process.GetProcessesByName(App.ProjectName);
                bool showAlreadyRunningWarning = procs.Length > 1;
                foreach (var p in procs) p.Dispose();
                if (!App.PlayerState.Loaded)
                    App.PlayerState.Load();
                if (!App.StudioState.Loaded)
                    App.StudioState.Load();
                var window = new UI.Elements.Settings.MainWindow(showAlreadyRunningWarning);
                window.ShowDialog();
                App.Terminate();
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "Found an already existing menu window");
                var procs = Utilities.GetProcessesSafe();
                var process = procs.FirstOrDefault(x => x.MainWindowTitle == Strings.Menu_Title);
                if (process is not null)
                {
                    PInvoke.SetForegroundWindow((HWND)process.MainWindowHandle);
                }
                foreach (var p in procs) p.Dispose();
                App.Terminate();
            }
        }
        private static void RunTaskAndTerminate(Action actionRunner, string taskName, Action? onFinish = null)
        {
            Task.Run(actionRunner).ContinueWith(t =>
            {
                App.Logger.WriteLine(taskName, $"{taskName} task has finished");
                onFinish?.Invoke();
                if (t.IsFaulted && t.Exception is not null)
                {
                    App.Logger.WriteLine(taskName, $"An exception occurred when running {taskName}");
                    App.FinalizeExceptionHandling(t.Exception);
                }
                App.Terminate();
            });
        }
        private static void RunTaskAndTerminate(Func<Task> taskRunner, string taskName, Action? onFinish = null)
        {
            Task.Run(taskRunner).ContinueWith(t =>
            {
                App.Logger.WriteLine(taskName, $"{taskName} task has finished");
                onFinish?.Invoke();
                if (t.IsFaulted && t.Exception is not null)
                {
                    App.Logger.WriteLine(taskName, $"An exception occurred when running {taskName}");
                    App.FinalizeExceptionHandling(t.Exception);
                }
                App.Terminate();
            });
        }
        public static void LaunchRoblox(LaunchMode launchMode)
        {
            const string LOG_IDENT = "LaunchHandler::LaunchRoblox";
            if (launchMode == LaunchMode.None)
                throw new InvalidOperationException("No Roblox launch mode set");
            if (!File.Exists(Path.Combine(Paths.System, "mfplat.dll")))
            {
                Frontend.ShowMessageBox(Strings.Bootstrapper_WMFNotFound, MessageBoxImage.Error);
                if (!App.LaunchSettings.QuietFlag.Active)
                    Utilities.ShellExecute("https://support.microsoft.com/en-us/topic/media-feature-pack-list-for-windows-n-editions-c1c6fffa-d052-8338-7a79-a4bb980a700a");
                App.Terminate(ErrorCode.ERROR_FILE_NOT_FOUND);
            }
            App.Logger.WriteLine(LOG_IDENT, "Initializing bootstrapper");
            App.Bootstrapper = new Bootstrapper(launchMode);
            IBootstrapperDialog? dialog = null;
            if (!App.LaunchSettings.QuietFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Initializing bootstrapper dialog");
                dialog = App.Settings.Prop.BootstrapperStyle.GetNew();
                App.Bootstrapper.Dialog = dialog;
                dialog.Bootstrapper = App.Bootstrapper;
            }
            RunTaskAndTerminate(App.Bootstrapper.Run, "Bootstrapper");
            dialog?.ShowBootstrapper();
            App.Logger.WriteLine(LOG_IDENT, "Exiting");
        }
        public static void LaunchWatcher()
        {
            var watcher = new Watcher();
            RunTaskAndTerminate(watcher.Run, "Watcher", watcher.Dispose);
        }
        public static void LaunchBackgroundUpdater()
        {
            const string LOG_IDENT = "LaunchHandler::LaunchBackgroundUpdater";
            App.LaunchSettings.QuietFlag.Active = true;
            App.LaunchSettings.NoLaunchFlag.Active = true;
            App.Logger.WriteLine(LOG_IDENT, "Initializing bootstrapper");
            App.Bootstrapper = new Bootstrapper(LaunchMode.Player)
            {
                MutexName = "Bloxstrap-BackgroundUpdater",
                QuitIfMutexExists = true
            };
            CancellationTokenSource cts = new();
            using var handle = new EventWaitHandle(false, EventResetMode.AutoReset, "Bloxstrap-CloseBackgroundUpdater");
            Task.Run(() =>
            {
                App.Logger.WriteLine(LOG_IDENT, "Started event waiter");
                handle.WaitOne();
                App.Logger.WriteLine(LOG_IDENT, "Received close event, killing it all!");
                App.Bootstrapper.Cancel();
            }, cts.Token);
            RunTaskAndTerminate(App.Bootstrapper.Run, "Bootstrapper", cts.Cancel);
            App.Logger.WriteLine(LOG_IDENT, "Exiting");
        }
    }
    public class LaunchSettings
    {
        public LaunchFlag MenuFlag                  { get; } = new("preferences,menu,settings");
        public LaunchFlag WatcherFlag               { get; } = new("watcher");
        public LaunchFlag BackgroundUpdaterFlag     { get; } = new("backgroundupdater");
        public LaunchFlag QuietFlag                 { get; } = new("quiet");
        public LaunchFlag UninstallFlag             { get; } = new("uninstall");
        public LaunchFlag NoLaunchFlag              { get; } = new("nolaunch");
        public LaunchFlag TestModeFlag              { get; } = new("testmode");
        public LaunchFlag NoGPUFlag                 { get; } = new("nogpu");
        public LaunchFlag UpgradeFlag               { get; } = new("upgrade");
        public LaunchFlag PlayerFlag                { get; } = new("player");
        public LaunchFlag StudioFlag                { get; } = new("studio");
        public LaunchFlag VersionFlag               { get; } = new("version");
        public LaunchFlag ChannelFlag               { get; } = new("channel");
        public LaunchFlag ForceFlag                 { get; } = new("force");
        public LaunchFlag GameShortcutFlag          { get; } = new("gameshortcut");
        public bool BypassUpdateCheck => UninstallFlag.Active || WatcherFlag.Active;
        public LaunchMode RobloxLaunchMode { get; set; } = LaunchMode.None;
        public string RobloxLaunchArgs { get; set; } = "";
        public string[] Args { get; private set; }
        public LaunchSettings(string[] args)
        {
            const string LOG_IDENT = "LaunchSettings::LaunchSettings";
            Args = args;
            var flagMap = new Dictionary<string, LaunchFlag>(StringComparer.OrdinalIgnoreCase)
            {
                { "preferences", MenuFlag },
                { "menu", MenuFlag },
                { "settings", MenuFlag },
                { "watcher", WatcherFlag },
                { "backgroundupdater", BackgroundUpdaterFlag },
                { "quiet", QuietFlag },
                { "uninstall", UninstallFlag },
                { "nolaunch", NoLaunchFlag },
                { "testmode", TestModeFlag },
                { "nogpu", NoGPUFlag },
                { "upgrade", UpgradeFlag },
                { "player", PlayerFlag },
                { "studio", StudioFlag },
                { "version", VersionFlag },
                { "channel", ChannelFlag },
                { "force", ForceFlag },
                { "gameshortcut", GameShortcutFlag }
            };
            int startIdx = 0;
            if (Args.Length >= 1)
            {
                string arg = Args[0];
                if (arg.StartsWith("roblox:", StringComparison.OrdinalIgnoreCase) 
                    || arg.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Got Roblox player argument");
                    RobloxLaunchMode = LaunchMode.Player;
                    RobloxLaunchArgs = arg;
                    startIdx = 1;
                }
                else if (arg.StartsWith("version-"))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Got version argument");
                    VersionFlag.Active = true;
                    VersionFlag.Data = arg;
                    startIdx = 1;
                }
            }
            for (int i = startIdx; i < Args.Length; i++)
            {
                string arg = Args[i];
                if (!arg.StartsWith('-'))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Invalid argument: {arg}");
                    continue;
                }
                string identifier = arg[1..];
                if (!flagMap.TryGetValue(identifier, out LaunchFlag? flag) || flag is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Unknown argument: {identifier}");
                    continue;
                }
                if (flag.Active)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Tried to set {identifier} flag twice");
                    continue;
                }
                flag.Active = true;
                if (i < Args.Length - 1 && Args[i+1] is string nextArg && !nextArg.StartsWith('-'))
                {
                    flag.Data = nextArg;
                    i++;
                    App.Logger.WriteLine(LOG_IDENT, $"Identifier '{identifier}' is active with data");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Identifier '{identifier}' is active");
                }
            }
            if (VersionFlag.Active)
                RobloxLaunchMode = LaunchMode.Unknown;  
            if (PlayerFlag.Active)
                ParsePlayer(PlayerFlag.Data);
            else if (StudioFlag.Active)
                ParseStudio(StudioFlag.Data);
            if (GameShortcutFlag.Active && !string.IsNullOrEmpty(GameShortcutFlag.Data))
                ParseGameShortcut(GameShortcutFlag.Data);
        }
        private void ParsePlayer(string? data)
        {
            const string LOG_IDENT = "LaunchSettings::ParsePlayer";
            RobloxLaunchMode = LaunchMode.Player;
            if (!string.IsNullOrEmpty(data))
            {
                App.Logger.WriteLine(LOG_IDENT, "Got Roblox launch arguments");
                RobloxLaunchArgs = data;
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "No Roblox launch arguments were provided");
            }
        }
        private void ParseStudio(string? data)
        {
            const string LOG_IDENT = "LaunchSettings::ParseStudio";
            RobloxLaunchMode = LaunchMode.Studio;
            if (string.IsNullOrEmpty(data))
            {
                App.Logger.WriteLine(LOG_IDENT, "No Roblox launch arguments were provided");
                return;
            }
            if (data.StartsWith("roblox-studio:"))
            {
                App.Logger.WriteLine(LOG_IDENT, "Got Roblox Studio launch arguments");
                RobloxLaunchArgs = data;
            }
            else if (data.StartsWith("roblox-studio-auth:"))
            {
                App.Logger.WriteLine(LOG_IDENT, "Got Roblox Studio Auth launch arguments");
                RobloxLaunchMode = LaunchMode.StudioAuth;
                RobloxLaunchArgs = data;
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "Got Roblox Studio local place file");
                RobloxLaunchArgs = $"-task EditFile -localPlaceFile \"{data}\"";
            }
        }
        private void ParseGameShortcut(string data)
        {
            const string LOG_IDENT = "LaunchSettings::ParseGameShortcut";
            var parts = data.Split(';');
            if (parts.Length < 1)
            {
                App.Logger.WriteLine(LOG_IDENT, "Insufficient data for game shortcut");
                return;
            }
            string placeId = parts[0];
            string jobId = parts.Length > 1 ? parts[1] : "";
            string accessCode = parts.Length > 2 ? parts[2] : "";
            string deeplink = $"roblox://experiences/start?placeId={placeId}";
            if (!string.IsNullOrEmpty(accessCode))
                deeplink += "&accessCode=" + accessCode;
            else if (!string.IsNullOrEmpty(jobId))
                deeplink += "&gameInstanceId=" + jobId;
            App.Logger.WriteLine(LOG_IDENT, $"Generated shortcut deeplink: {deeplink}");
            RobloxLaunchMode = LaunchMode.Player;
            RobloxLaunchArgs = deeplink;
        }
    }
    public class RemoteDataManager : JsonManager<RemoteDataBase>
    {
        public override string ClassName => nameof(RemoteDataManager);
        public override string LOG_IDENT_CLASS => ClassName;
        public override string FileLocation => Path.Combine(Paths.Base, "Data.json");
        public GenericTriState LoadedState = GenericTriState.Unknown;
        public event EventHandler? DataLoaded;
        private readonly TaskCompletionSource<bool> _dataLoadTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Subscribe(EventHandler Handler)
        {
            switch (LoadedState)
            {
                case GenericTriState.Unknown:
                    DataLoaded += Handler;
                    break;
                default:
                    Handler(this, EventArgs.Empty);
                    break;
            }
        }
        public async Task WaitUntilDataFetched(int timeoutMs = 3000)
        {
            if (LoadedState != GenericTriState.Unknown)
                return;
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await _dataLoadTcs.Task.WaitAsync(cts.Token);
            }
            catch { }
        }
        private HashSet<string>? _cachedAllowedFlags;
        public HashSet<string> GetAllowedFastFlags()
        {
            if (_cachedAllowedFlags != null)
                return _cachedAllowedFlags;
            _cachedAllowedFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(Prop.AllowedFastFlags))
            {
                try
                {
                    string clean = Prop.AllowedFastFlags.Trim().Replace("\n", "").Replace("\r", "");
                    byte[] bytes = Convert.FromBase64String(clean);
                    string json = Encoding.UTF8.GetString(bytes);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Allowed", out var allowed))
                    {
                        foreach (var elem in allowed.EnumerateArray())
                        {
                            string? flag = elem.GetString()?.Trim();
                            if (!string.IsNullOrEmpty(flag))
                                _cachedAllowedFlags.Add(flag);
                        }
                    }
                }
                catch { }
            }
            return _cachedAllowedFlags;
        }
        public async Task LoadData()
        {
            const string LOG_IDENT = $"{nameof(RemoteDataManager)}::LoadData";
            _cachedAllowedFlags = null;
            if (App.Settings.Prop.ForceLocalData || App.LaunchSettings.WatcherFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Force loading local data");
                this.Load(false);
                LoadedState = GenericTriState.Successful;
            }
            else
            {
                try
                {
                    Prop = await Http.GetJson<RemoteDataBase>(App.ProjectRemoteDataLink);
                    LoadedState = GenericTriState.Successful;
                    App.Logger.WriteLine(LOG_IDENT, "Remote data loaded");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not load remote data");
                    App.Logger.WriteException(LOG_IDENT, ex);
                    App.Logger.WriteLine(LOG_IDENT, "Loading local data");
                    this.Load(false);
                    LoadedState = GenericTriState.Failed;
                }
            }
            _cachedAllowedFlags = null;
            _dataLoadTcs.TrySetResult(true);
            DataLoaded?.Invoke(this, EventArgs.Empty);
            if (LoadedState == GenericTriState.Successful)
                this.Save();
            App.Logger.WriteLine(LOG_IDENT, $"Loading finished with status: {LoadedState}");
        }
    }
    public class Watcher : IDisposable
    {
        private readonly InterProcessLock _lock = new("Watcher");
        private readonly WatcherData? _watcherData;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _isDisposed = false;
        public Watcher()
        {
            const string LOG_IDENT = "Watcher";
            if (!_lock.IsAcquired)
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher instance already exists");
                return;
            }
            string? watcherDataArg = App.LaunchSettings.WatcherFlag.Data;
            if (string.IsNullOrEmpty(watcherDataArg))
            {
                throw new Exception("Watcher data not specified");
            }
            else
            {
                _watcherData = JsonSerializer.Deserialize<WatcherData>(Encoding.UTF8.GetString(Convert.FromBase64String(watcherDataArg)));
            }
            if (_watcherData is null)
                throw new Exception("Watcher data is invalid");
        }
        public void Run()
        {
            const string LOG_IDENT = "Watcher::Run";
            if (!_lock.IsAcquired)
                return;
            App.Logger.WriteLine(LOG_IDENT, "Starting watcher loop");
            try
            {
                using var process = Process.GetProcessById(_watcherData!.ProcessId);
                process.WaitForExit();
                App.Logger.WriteLine(LOG_IDENT, "Roblox process exited");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error waiting for process: {ex.Message}");
            }
        }
        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _lock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
