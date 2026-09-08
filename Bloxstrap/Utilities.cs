using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
namespace Bloxstrap.Utility
{
    static class Paths
    {
        public static readonly string Temp = Path.Combine(Path.GetTempPath(), App.ProjectName);
        public static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static readonly string Desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        public static readonly string WindowsStartMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        public static readonly string System = Environment.GetFolderPath(Environment.SpecialFolder.System);
        public static readonly string Process = Environment.ProcessPath!;
        public static readonly string TempUpdates = Path.Combine(Temp, "Updates");
        public static readonly string TempLogs = Path.Combine(Temp, "Logs");
        public static readonly string Roblox = Path.Combine(LocalAppData, "Roblox");
        public static readonly string RobloxLogs = Path.Combine(Roblox, "logs");
        public static readonly string RobloxCache = Path.Combine(Path.GetTempPath(), "Roblox");
        public static string Base { get; private set; } = "";
        public static string Downloads { get; private set; } = "";
        public static string Cache { get; private set; } = "";
        public static string SavedFlagProfiles { get; private set; } = "";
        public static string Logs { get; private set; } = "";
        public static string Integrations { get; private set; } = "";
        public static string Versions { get; private set; } = "";
        public static string Modifications { get; private set; } = "";
        public static string CustomThemes { get; private set; } = "";
        public static string CustomCursors { get; private set; } = "";
        public static string Application { get; private set; } = "";
        public static string CustomFont { get; private set; } = "";
        public static string PresetModifications { get; private set; } = "";
        public static bool Initialized => !string.IsNullOrEmpty(Base);
        public static void Initialize(string baseDirectory)
        {
            Base = baseDirectory;
            Downloads = Path.Combine(Base, "Downloads");
            SavedFlagProfiles = Path.Combine(Base, "SavedFlagProfiles");
            Logs = Path.Combine(Base, "Logs");
            Integrations = Path.Combine(Base, "Integrations");
            Versions = Path.Combine(Base, "Versions");
            Modifications = Path.Combine(Base, "Modifications");
            CustomThemes = Path.Combine(Base, "CustomThemes");
            CustomCursors = Path.Combine(Base, "CustomCursorsSets");
            Cache = Path.Combine(Base, "Cache");
            Application = Path.Combine(Base, $"{App.ProjectName}.exe");
            PresetModifications = Path.Combine(Modifications, "Preset Modifications");
            CustomFont = Path.Combine(PresetModifications, "content", "fonts", "CustomFont.ttf");
        }
    }
    static class Utilities
    {
        public static void ShellExecute(string website)
        {
            try
            {
                Process.Start(new ProcessStartInfo 
                { 
                    FileName = website, 
                    UseShellExecute = true 
                });
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != (int)ErrorCode.CO_E_APPNOTFOUND)
                    throw;
                Process.Start(new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = $"shell32,OpenAs_RunDLL {website}"
                });
            }
        }
        public static Version GetVersionFromString(string version)
        {
            if (version.StartsWith('v'))
                version = version[1..];
            int idx = version.IndexOf('+');  
            if (idx != -1)
                version = version[..idx];
            int dashIdx = version.IndexOf('-');
            if (dashIdx != -1)
                version = version[..dashIdx];
            return new Version(version);
        }
        public static VersionComparison CompareVersions(string versionStr1, string versionStr2)
        {
            try
            {
                var version1 = GetVersionFromString(versionStr1);
                var version2 = GetVersionFromString(versionStr2);
                return (VersionComparison)version1.CompareTo(version2);
            }
            catch (Exception)
            {
                App.Logger.WriteLine("Utilities::CompareVersions", "An exception occurred when comparing versions");
                App.Logger.WriteLine("Utilities::CompareVersions", $"versionStr1={versionStr1} versionStr2={versionStr2}");
                throw;
            }
        }
        public static Version? ParseVersionSafe(string versionStr)
        {
            const string LOG_IDENT = "Utilities::ParseVersionSafe";
            if (!Version.TryParse(versionStr, out Version? version))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to convert {versionStr} to a valid Version type.");
                return version;
            }
            return version;
        }
        public static string GetRobloxVersionStr(IAppData data)
        {
            string playerLocation = data.ExecutablePath;
            if (!File.Exists(playerLocation))
                return "";
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(playerLocation);
            if (versionInfo.ProductVersion is null)
                return "";
            return versionInfo.ProductVersion.Replace(", ", ".");
        }
        public static string GetRobloxVersionStr(bool studio)
        {
            IAppData data = studio ? new RobloxStudioData() : new RobloxPlayerData();
            return GetRobloxVersionStr(data);
        }
        public static Version? GetRobloxVersion(IAppData data)
        {
            string str = GetRobloxVersionStr(data);
            return ParseVersionSafe(str);
        }
        public static Process[] GetProcessesSafe()
        {
            const string LOG_IDENT = "Utilities::GetProcessesSafe";
            try
            {
                return Process.GetProcesses();
            }
            catch (ArithmeticException ex)  
            {
                App.Logger.WriteLine(LOG_IDENT, $"Unable to fetch processes!");
                App.Logger.WriteException(LOG_IDENT, ex);
                return Array.Empty<Process>();  
            }
        }
        public static bool DoesMutexExist(string name)
        {
            try
            {
                using var mutex = Mutex.OpenExisting(name);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }
        public static bool DoesEventExist(string name)
        {
            try
            {
                using var handle = EventWaitHandle.OpenExisting(name);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }
        public static bool IsRobloxRunning()
        {
            string processName = Path.GetFileNameWithoutExtension(App.RobloxPlayerAppName);
            var processes = Process.GetProcessesByName(processName);
            bool isRunning = processes.Length > 0;
            foreach (var p in processes)
                p.Dispose();
            return isRunning;
        }
        public static void KillBackgroundUpdater()
        {
            using var handle = new EventWaitHandle(false, EventResetMode.AutoReset, "Bloxstrap-CloseBackgroundUpdater");
            handle.Set();
        }
    }
    public sealed class AsyncMutex : IAsyncDisposable
    {
        private readonly bool _initiallyOwned;
        private readonly string _name;
        private Task? _mutexTask;
        private ManualResetEventSlim? _releaseEvent;
        private CancellationTokenSource? _cancellationTokenSource;
        private int _released = 0;
        public AsyncMutex(bool initiallyOwned, string name)
        {
            _initiallyOwned = initiallyOwned;
            _name = name;
        }
        public Task AcquireAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TaskCompletionSource taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _releaseEvent = new ManualResetEventSlim();
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _mutexTask = Task.Factory.StartNew(
                state =>
                {
                    using Mutex mutex = new(_initiallyOwned, _name);
                    try
                    {
                        CancellationToken cancellationToken = _cancellationTokenSource.Token;
                        try
                        {
                            if (WaitHandle.WaitAny(new[] { mutex, cancellationToken.WaitHandle }) != 0)
                            {
                                taskCompletionSource.SetCanceled(cancellationToken);
                                return;
                            }
                        }
                        catch (AbandonedMutexException)
                        {
                        }
                        taskCompletionSource.SetResult();
                        _releaseEvent.Wait();
                        mutex.ReleaseMutex();
                    }
                    catch (OperationCanceledException)
                    {
                        taskCompletionSource.TrySetCanceled(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        taskCompletionSource.TrySetException(ex);
                    }
                },
                state: null,
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            return taskCompletionSource.Task;
        }
        public async Task ReleaseAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                try
                {
                    _releaseEvent?.Set();
                }
                catch { }
            }
            if (_mutexTask != null)
            {
                try
                {
                    await _mutexTask;
                }
                catch { }
            }
        }
        public async ValueTask DisposeAsync()
        {
            _cancellationTokenSource?.Cancel();
            await ReleaseAsync();
            try
            {
                _releaseEvent?.Dispose();
            }
            catch { }
            _cancellationTokenSource?.Dispose();
        }
    }
    internal static class Filesystem
    {
        internal static long GetFreeDiskSpace(string path)
        {
            try
            {
                string? root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root))
                    return -1;
                var drive = new DriveInfo(root);
                return drive.AvailableFreeSpace;
            }
            catch
            {
                return -1;
            }
        }
        internal static void AssertReadOnly(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || !fileInfo.IsReadOnly)
                return;
            fileInfo.IsReadOnly = false;
            App.Logger.WriteLine("Filesystem::AssertReadOnly", $"The following file was set as read-only: {filePath}");
        }
        internal static void AssertReadOnlyDirectory(string directoryPath)
        {
            var directory = new DirectoryInfo(directoryPath) { Attributes = FileAttributes.Normal };
            foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
                info.Attributes = FileAttributes.Normal;
            App.Logger.WriteLine("Filesystem::AssertReadOnlyDirectory", $"The following directory was set as read-only: {directoryPath}");
        }
    }
    internal static class Http
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
        public static async Task<T> GetJson<T>(string url)
        {
            using var request = await App.HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            request.EnsureSuccessStatusCode();
            using var stream = await request.Content.ReadAsStreamAsync();
            return (await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions))!;
        }
        public static async Task<T> SendJson<T>(HttpRequestMessage requestMessage)
        {
            using var request = await App.HttpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
            request.EnsureSuccessStatusCode();
            using var stream = await request.Content.ReadAsStreamAsync();
            return (await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions))!;
        }
        public static async Task<T> GetClientSettingsJson<T>(string domain, string path, string? channelToken = null)
        {
            HttpRequestMessage CreateRequest(string host)
            {
                var msg = new HttpRequestMessage(HttpMethod.Get, $"https://{host}.{domain}{path}");
                if (!string.IsNullOrEmpty(channelToken))
                    msg.Headers.Add("Roblox-Channel-Token", channelToken);
                return msg;
            }
            try
            {
                using var req = CreateRequest("clientsettingscdn");
                return await SendJson<T>(req);
            }
            catch (HttpRequestException ex) when (Deployment.BadChannelCodes.Contains(ex.StatusCode))
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Http::GetClientSettingsJson", "Failed to contact clientsettingscdn! Falling back to clientsettings...");
                App.Logger.WriteException("Http::GetClientSettingsJson", ex);
                using var req = CreateRequest("clientsettings");
                return await SendJson<T>(req);
            }
        }
    }
    public class InterProcessLock : IDisposable
    {
        public Mutex Mutex { get; private set; }
        public bool IsAcquired { get; private set; }
        public InterProcessLock(string name) : this(name, TimeSpan.Zero) { }
        public InterProcessLock(string name, TimeSpan timeout)
        {
            Mutex = new Mutex(false, "Bloxstrap-" + name);
            try
            {
                IsAcquired = Mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                IsAcquired = true;
            }
        }
        public void Dispose()
        {
            if (IsAcquired)
            {
                Mutex.ReleaseMutex();
                IsAcquired = false;
            }
            Mutex.Dispose();
            GC.SuppressFinalize(this);
        }
    }
    public static class MD5Hash
    {
        private static string ToHexLower(byte[] hash)
        {
            return string.Create(hash.Length * 2, hash, static (chars, bytes) =>
            {
                const string hex = "0123456789abcdef";
                for (int i = 0; i < bytes.Length; i++)
                {
                    byte b = bytes[i];
                    chars[i * 2] = hex[b >> 4];
                    chars[i * 2 + 1] = hex[b & 0xF];
                }
            });
        }
        public static string FromBytes(byte[] data)
        {
            return ToHexLower(MD5.HashData(data));
        }
        public static string FromStream(Stream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);
            return ToHexLower(MD5.HashData(stream));
        }
        public static string FromFile(string filename)
        {
            using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            return FromStream(stream);
        }
        public static string Stringify(byte[] hash)
        {
            return ToHexLower(hash);
        }
        public static string FromString(string str)
        {
            return FromBytes(Encoding.UTF8.GetBytes(str));
        }
    }
    internal static class Shortcut
    {
        private static GenericTriState _loadStatus = GenericTriState.Unknown;
        public static void Create(string exePath, string exeArgs, string lnkPath, string? iconPath = null)
        {
            const string LOG_IDENT = "Shortcut::Create";
            if (File.Exists(lnkPath))
                return;
            try
            {
                string finalIconPath = string.IsNullOrEmpty(iconPath) ? exePath : iconPath;
                ShellLink.Shortcut.CreateShortcut(exePath, exeArgs, finalIconPath, 0).WriteToFile(lnkPath);
                if (_loadStatus != GenericTriState.Successful)
                    _loadStatus = GenericTriState.Successful;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to create a shortcut for {lnkPath}!");
                App.Logger.WriteException(LOG_IDENT, ex);
                if (_loadStatus == GenericTriState.Failed)
                    return;
                _loadStatus = GenericTriState.Failed;
                Frontend.ShowMessageBox(Strings.Dialog_CannotCreateShortcuts, MessageBoxImage.Warning);
            }
        }
    }
    static class WindowsRegistry
    {
        private const string RobloxPlaceKey = "Roblox.Place";
        public static readonly List<RegistryKey> Roots = new() { Registry.CurrentUser, Registry.LocalMachine };
        public static void RegisterProtocol(string key, string name, string handler, string handlerParam = "%1")
        {
            using var uriKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{key}");
            using var uriCommandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{key}\shell\open\command");
            using var uriIconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{key}\DefaultIcon");
            string handlerArgs = $"\"{handler}\" {handlerParam}";
            if (uriKey.GetValue("") is null)
            {
                uriKey.SetValueSafe("", $"URL: {name} Protocol");
                uriKey.SetValueSafe("URL Protocol", "");
            }
            if (uriCommandKey.GetValue("") as string != handlerArgs)
            {
                uriIconKey.SetValueSafe("", handler);
                uriCommandKey.SetValueSafe("", handlerArgs);
            }
        }
        public static void RegisterPlayer() => RegisterPlayer(Paths.Application, "-player \"%1\"");
        public static void RegisterPlayer(string handler, string handlerParam)
        {
            RegisterProtocol("roblox", "Roblox", handler, handlerParam);
            RegisterProtocol("roblox-player", "Roblox", handler, handlerParam);
        }
        public static void RegisterStudio()
        {
            RegisterStudioProtocol(Paths.Application, "-studio \"%1\"");
            RegisterStudioFileClass(Paths.Application, "-studio \"%1\"");
            RegisterStudioFileTypes();
        }
        public static void RegisterStudioProtocol(string handler, string handlerParam)
        {
            RegisterProtocol("roblox-studio", "Roblox", handler, handlerParam);
            RegisterProtocol("roblox-studio-auth", "Roblox", handler, handlerParam);
        }
        public static void RegisterStudioFileTypes()
        {
            RegisterStudioFileType(".rbxl");
            RegisterStudioFileType(".rbxlx");
        }
        public static void RegisterStudioFileClass(string handler, string handlerParam)
        {
            using var uriKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{RobloxPlaceKey}");
            using var uriCommandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{RobloxPlaceKey}\shell\open\command");
            using var uriOpenKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{RobloxPlaceKey}\shell\open");
            using var uriIconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{RobloxPlaceKey}\DefaultIcon");
            const string keyValue = "Roblox Place";
            string handlerArgs = $"\"{handler}\" {handlerParam}";
            string iconValue = $"{handler},0";
            if (uriKey.GetValue("") as string != keyValue)
                uriKey.SetValueSafe("", keyValue);
            if (uriCommandKey.GetValue("") as string != handlerArgs)
                uriCommandKey.SetValueSafe("", handlerArgs);
            if (uriOpenKey.GetValue("") as string != "Open")
                uriOpenKey.SetValueSafe("", "Open");
            if (uriIconKey.GetValue("") as string != iconValue)
                uriIconKey.SetValueSafe("", iconValue);
        }
        public static void RegisterStudioFileType(string key)
        {
            using var uriKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{key}");
            uriKey.CreateSubKey(RobloxPlaceKey + @"\ShellNew");
            if (uriKey.GetValue("") as string != RobloxPlaceKey)
                uriKey.SetValueSafe("", RobloxPlaceKey);
        }
        public static void RegisterApis()
        {
            using var apisKey = Registry.CurrentUser.CreateSubKey(App.ApisKey);
            apisKey.SetValueSafe("ApplicationPath", Paths.Application);
            apisKey.SetValueSafe("InstallationPath", Paths.Base);
        }
        public static void RegisterClientLocation(bool isStudio, string? clientPath)
        {
            using var apisKey = Registry.CurrentUser.CreateSubKey(App.ApisKey);
            string keyName = isStudio ? "StudioPath" : "PlayerPath";
            clientPath ??= "";
            apisKey.SetValueSafe(keyName, clientPath);
        }
        public static void Unregister(string key)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{key}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Protocol::Unregister", $"Failed to unregister {key}: {ex}");
            }
        }
    }
}
namespace Bloxstrap
{
    public class Logger
    {
        private readonly Channel<string> _logChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        private FileStream? _filestream;
        private StreamWriter? _streamWriter;
        private Task? _writerTask;
        public readonly List<string> History = new();
        public bool Initialized = false;
        public bool NoWriteMode = false;
        public string? FileLocation;
        public string AsDocument => string.Join('\n', History);
        public Logger()
        {
            _writerTask = Task.Run(ProcessLogQueueAsync);
        }
        private async Task ProcessLogQueueAsync()
        {
            var reader = _logChannel.Reader;
            while (await reader.WaitToReadAsync())
            {
                bool wroteAny = false;
                while (reader.TryRead(out string? message))
                {
                    if (_streamWriter != null && Initialized && message != null)
                    {
                        try
                        {
                            await _streamWriter.WriteLineAsync(message);
                            wroteAny = true;
                        }
                        catch { }
                    }
                }
                if (wroteAny && _streamWriter != null)
                {
                    try
                    {
                        await _streamWriter.FlushAsync();
                    }
                    catch { }
                }
            }
        }
        public void Initialize(bool useTempDir = false)
        {
            const string LOG_IDENT = "Logger::Initialize";
            string directory = useTempDir ? Path.Combine(Paths.TempLogs) : Path.Combine(Paths.Base, "Logs");
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
            string filename = $"{App.ProjectName}_{timestamp}.log";
            string location = Path.Combine(directory, filename);
            WriteLine(LOG_IDENT, $"Initializing at {location}");
            if (Initialized)
            {
                WriteLine(LOG_IDENT, "Failed to initialize because logger is already initialized");
                return;
            }
            Directory.CreateDirectory(directory);
            if (File.Exists(location))
            {
                WriteLine(LOG_IDENT, "Failed to initialize because log file already exists");
                return;
            }
            try
            {
                _filestream = File.Open(location, FileMode.Create, FileAccess.Write, FileShare.Read);
                _streamWriter = new StreamWriter(_filestream, new UTF8Encoding(false), 4096, leaveOpen: true);
            }
            catch (IOException)
            {
                WriteLine(LOG_IDENT, "Failed to initialize because log file already exists");
                return;
            }
            catch (UnauthorizedAccessException)
            {
                if (NoWriteMode)
                    return;
                WriteLine(LOG_IDENT, $"Failed to initialize because Bloxstrap cannot write to {directory}");
                Frontend.ShowMessageBox(
                    string.Format(Strings.Logger_NoWriteMode, directory), 
                    System.Windows.MessageBoxImage.Warning, 
                    System.Windows.MessageBoxButton.OK
                );
                NoWriteMode = true;
                return;
            }
            Initialized = true;
            if (History.Count > 0)
            {
                foreach (var line in History)
                    _logChannel.Writer.TryWrite(line);
            }
            WriteLine(LOG_IDENT, "Finished initializing!");
            FileLocation = location;
            if (Paths.Initialized && Directory.Exists(Paths.Logs))
            {
                foreach (FileInfo log in new DirectoryInfo(Paths.Logs).GetFiles())
                {
                    if (log.LastWriteTimeUtc.AddDays(7) > DateTime.UtcNow)
                        continue;
                    WriteLine(LOG_IDENT, $"Cleaning up old log file '{log.Name}'");
                    try
                    {
                       log.Delete();
                    }
                    catch (Exception ex)
                    {
                        WriteLine(LOG_IDENT, "Failed to delete log!");
                        WriteException(LOG_IDENT, ex);
                    }
                }
            }
        }
        private void WriteLine(string message)
        {
            string timestamp = DateTime.UtcNow.ToString("s") + "Z";
            string outcon = $"{timestamp} {message}";
            string outlog = Paths.UserProfile.Length > 0 && outcon.Contains(Paths.UserProfile, StringComparison.InvariantCultureIgnoreCase)
                ? outcon.Replace(Paths.UserProfile, "%UserProfile%", StringComparison.InvariantCultureIgnoreCase)
                : outcon;
            Debug.WriteLine(outcon);
            _logChannel.Writer.TryWrite(outlog);
            lock (History)
            {
                History.Add(outlog);
            }
        }
        public void WriteLine(string identifier, string message) => WriteLine($"[{identifier}] {message}");
        public void WriteException(string identifier, Exception ex)
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            string hresult = "0x" + ex.HResult.ToString("X8");
            WriteLine($"[{identifier}] ({hresult}) {ex}");
            Thread.CurrentThread.CurrentUICulture = Locale.CurrentCulture;
        }
    }
}
namespace Bloxstrap
{
    internal class HttpClientLoggingHandler : MessageProcessingHandler
    {
        public HttpClientLoggingHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }
        protected override HttpRequestMessage ProcessRequest(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            App.Logger.WriteLine("HttpClientLoggingHandler::ProcessRequest", $"{request.Method} {request.RequestUri}");
            return request;
        }
        protected override HttpResponseMessage ProcessResponse(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            App.Logger.WriteLine("HttpClientLoggingHandler::ProcessResponse", $"{(int)response.StatusCode} {response.ReasonPhrase} {response.RequestMessage!.RequestUri}");
            return response;
        }
    }
}
namespace Bloxstrap
{
    internal static class Locale
    {
        public static CultureInfo CurrentCulture { get; private set; } = CultureInfo.InvariantCulture;
        public static bool RightToLeft { get; private set; } = false;
        private static readonly string[] _rtlLocales = { "ar", "he", "fa" };
        public static readonly Dictionary<string, string> SupportedLocales = new()
        {
            { "nil", Strings.Common_SystemDefault },
            { "en", "English" },
            { "en-US", "English (United States)" },
            { "vi", "Tiếng Việt" }
        };
        public static string GetIdentifierFromName(string language) => SupportedLocales.FirstOrDefault(x => x.Value == language).Key ?? "nil";
        public static List<string> GetLanguages() => SupportedLocales.Values.ToList();
        public static void Set(string identifier)
        {
            if (!SupportedLocales.ContainsKey(identifier))
                identifier = "nil";
            if (identifier == "nil")
            {
                CurrentCulture = Thread.CurrentThread.CurrentUICulture;
            }
            else
            {
                CurrentCulture = new CultureInfo(identifier);
                CultureInfo.DefaultThreadCurrentUICulture = CurrentCulture;
                Thread.CurrentThread.CurrentUICulture = CurrentCulture;
            }
            RightToLeft = _rtlLocales.Any(CurrentCulture.Name.StartsWith);
        }
        public static void Initialize()
        {
            Set("nil");
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
            {
                var window = (Window)sender;
                if (RightToLeft)
                {
                    window.FlowDirection = FlowDirection.RightToLeft;
                    if (window.ContextMenu is not null)
                        window.ContextMenu.FlowDirection = FlowDirection.RightToLeft;
                }
            }));
        }
    }
}
namespace Bloxstrap
{
    static class Resource
    {
        private static readonly Assembly assembly = Assembly.GetExecutingAssembly();
        private static readonly string[] resourceNames = assembly.GetManifestResourceNames();
        private static readonly ConcurrentDictionary<string, string> _nameLookupCache = new();
        private static readonly ConcurrentDictionary<string, byte[]> _cache = new();
        public static Stream GetStream(string name)
        {
            string path = _nameLookupCache.GetOrAdd(name, static n => resourceNames.Single(str => str.EndsWith(n)));
            return assembly.GetManifestResourceStream(path)!;
        }
        public static async Task<byte[]> Get(string name)
        {
            if (_cache.TryGetValue(name, out var cached))
                return cached;
            using var stream = GetStream(name);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            byte[] bytes = memoryStream.ToArray();
            _cache[name] = bytes;
            return bytes;
        }
        public static async Task<string> GetString(string name)
        {
            return Encoding.UTF8.GetString(await Get(name));
        }
    }
}
