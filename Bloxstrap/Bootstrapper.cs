using System.Buffers;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Shell;
using Bloxstrap.AppData;
using Bloxstrap.Models;
using Bloxstrap.Models.Manifest;
using Bloxstrap.RobloxInterfaces;
using Bloxstrap.UI.Elements.Bootstrapper.Base;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Win32;
namespace Bloxstrap
{
    public class Bootstrapper
    {
        private const int ProgressBarMaximum = 10000;
        private const double TaskbarProgressMaximumWpf = 1;  
        private const int TaskbarProgressMaximumWinForms = WinFormsDialogBase.TaskbarProgressMaximum;
        private const string AppSettings =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
            "<Settings>\r\n" +
            "	<ContentFolder>content</ContentFolder>\r\n" +
            "	<BaseUrl>http://www.roblox.com</BaseUrl>\r\n" +
            "</Settings>\r\n";
        private static readonly Regex _channelRegex = new("channel:([a-zA-Z0-9-_]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private readonly FastZipEvents _fastZipEvents = new();
        private readonly CancellationTokenSource _cancelTokenSource = new();
        private IAppData AppData = default!;
        private Dictionary<string, string> PackageDirectoryMap = null!;
        private LaunchMode _launchMode;
        private string _launchCommandLine = App.LaunchSettings.RobloxLaunchArgs;
        private Version? _latestVersion = null;
        private string _latestVersionGuid = null!;
        private string _latestVersionDirectory = null!;
        private PackageManifest _versionPackageManifest = null!;
        public static bool _staticDirectory => App.Settings.Prop.StaticDirectory;
        private bool _isInstalling = false;
        private double _progressIncrement;
        private double _taskbarProgressIncrement;
        private double _taskbarProgressMaximum;
        private long _totalDownloadedBytes = 0;
        private bool _packageExtractionSuccess = true;
        private bool _mustUpgrade => App.LaunchSettings.ForceFlag.Active || App.State.Prop.ForceReinstall || String.IsNullOrEmpty(AppData.DistributionState.VersionGuid) || !File.Exists(AppData.ExecutablePath);
        private bool _noConnection = false;
        private AsyncMutex? _mutex;
        private int _appPid = 0;
        public IBootstrapperDialog? Dialog = null;
        public bool IsStudioLaunch => _launchMode != LaunchMode.Player;
        public string MutexName { get; set; } = "Bloxstrap-Bootstrapper";
        public bool QuitIfMutexExists { get; set; } = false;
        public Bootstrapper(LaunchMode launchMode)
        {
            _launchMode = launchMode;
            _fastZipEvents.FileFailure += (_, e) =>
            {
                if (!e.Name.EndsWith(".ttf"))
                    throw e.Exception;
                App.Logger.WriteLine("FastZipEvents::OnFileFailure", $"Failed to extract {e.Name}");
                _packageExtractionSuccess = false;
            };
            _fastZipEvents.DirectoryFailure += (_, e) => throw e.Exception;
            _fastZipEvents.ProcessFile += (_, e) => e.ContinueRunning = !_cancelTokenSource.IsCancellationRequested;
            SetupAppData();
        }
        private void SetupAppData()
        {
            AppData = IsStudioLaunch ? new RobloxStudioData() : new RobloxPlayerData();
            Deployment.BinaryType = AppData.BinaryType;
        }
        private async Task SetupPackageDictionaries()
        {
            await App.RemoteData.WaitUntilDataFetched();
            var localData = App.RemoteData.Prop.PackageMaps[IsStudioLaunch ? "studio" : "player"];
            var commonData = App.RemoteData.Prop.PackageMaps.CommonPackageMap;
            PackageDirectoryMap = new(commonData);
            foreach (var package in localData)
                PackageDirectoryMap[package.Key] = package.Value;
        }
        private void SetStatus(string message)
        {
            message = message.Replace("{product}", AppData.ProductName);
            if (Dialog is not null)
                Dialog.Message = message;
        }
        private void UpdateProgressBar()
        {
            if (Dialog is null)
                return;
            int progressValue = (int)Math.Floor(_progressIncrement * _totalDownloadedBytes);
            progressValue = Math.Clamp(progressValue, 0, ProgressBarMaximum);
            Dialog.ProgressValue = progressValue;
            double taskbarProgressValue = _taskbarProgressIncrement * _totalDownloadedBytes;
            taskbarProgressValue = Math.Clamp(taskbarProgressValue, 0, _taskbarProgressMaximum);
            Dialog.TaskbarProgressValue = taskbarProgressValue;
        }
        private void HandleConnectionError(Exception exception)
        {
            const string LOG_IDENT = "Bootstrapper::HandleConnectionError";
            _noConnection = true;
            App.Logger.WriteLine(LOG_IDENT, "Connectivity check failed");
            App.Logger.WriteException(LOG_IDENT, exception);
            string message = Strings.Dialog_Connectivity_BadConnection;
            if (exception is AggregateException)
                exception = exception.InnerException!;
            if (exception is HttpRequestException && exception.InnerException is null)
                message = String.Format(Strings.Dialog_Connectivity_RobloxDown, "[status.roblox.com](https://status.roblox.com)");
            if (_mustUpgrade)
                message += $"\n\n{Strings.Dialog_Connectivity_RobloxUpgradeNeeded}\n\n{Strings.Dialog_Connectivity_TryAgainLater}";
            else
                message += $"\n\n{Strings.Dialog_Connectivity_RobloxUpgradeSkip}";
            Frontend.ShowConnectivityDialog(
                String.Format(Strings.Dialog_Connectivity_UnableToConnect, "Roblox"),
                message,
                _mustUpgrade ? MessageBoxImage.Error : MessageBoxImage.Warning,
                exception);
            if (_mustUpgrade)
                App.Terminate(ErrorCode.ERROR_CANCELLED);
        }
        public async Task Run()
        {
            const string LOG_IDENT = "Bootstrapper::Run";
            App.Logger.WriteLine(LOG_IDENT, "Running bootstrapper");
            if (Dialog is not null)
                Dialog.CancelEnabled = true;
            SetStatus(Strings.Bootstrapper_Status_Connecting);
            var connectionResult = await Deployment.InitializeConnectivity();
            App.Logger.WriteLine(LOG_IDENT, "Connectivity check finished");
            if (connectionResult is not null)
                HandleConnectionError(connectionResult);
            bool mutexExists = Utilities.DoesMutexExist(MutexName);
            if (mutexExists)
            {
                if (!QuitIfMutexExists)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{MutexName} mutex exists, waiting...");
                    SetStatus(Strings.Bootstrapper_Status_WaitingOtherInstances);
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{MutexName} mutex exists, exiting!");
                    return;
                }
            }
            await using var mutex = new AsyncMutex(false, MutexName);
            await mutex.AcquireAsync(_cancelTokenSource.Token);
            _mutex = mutex;
            if (mutexExists)
            {
                App.Settings.Load();
                App.State.Load();
                AppData.DistributionStateManager.Load();
            }
            if (!_noConnection)
            {
                try
                {
                    await GetLatestVersionInfo();
                }
                catch (Exception ex)
                {
                    HandleConnectionError(ex);
                }
            }
            CleanupVersionsFolder();  
            bool allModificationsApplied = true;
            if (!_noConnection)
            {
                if (App.RemoteData.LoadedState == GenericTriState.Unknown)  
                    SetStatus(Strings.Bootstrapper_Status_WaitingForData);
                await SetupPackageDictionaries();  
                if (AppData.DistributionState.VersionGuid != _latestVersionGuid || _mustUpgrade)
                {
                    bool backgroundUpdaterMutexOpen = Utilities.DoesMutexExist("Bloxstrap-BackgroundUpdater");
                    if (App.LaunchSettings.BackgroundUpdaterFlag.Active)
                        backgroundUpdaterMutexOpen = false;  
                    App.Logger.WriteLine(LOG_IDENT, $"Background updater running: {backgroundUpdaterMutexOpen}");
                    if (backgroundUpdaterMutexOpen && _mustUpgrade)
                    {
                        Utilities.KillBackgroundUpdater();
                        backgroundUpdaterMutexOpen = false;
                    }
                    if (!backgroundUpdaterMutexOpen)
                    {
                        if (IsEligibleForBackgroundUpdate())
                            StartBackgroundUpdater();
                        else
                            await UpgradeRoblox();
                    }
                }
                if (_cancelTokenSource.IsCancellationRequested)
                    return;
                allModificationsApplied = await ApplyModifications();
            }
            if (IsStudioLaunch)
            {
                WindowsRegistry.RegisterStudio();
            }
            else
                WindowsRegistry.RegisterPlayer();
            WindowsRegistry.RegisterClientLocation(IsStudioLaunch, _latestVersionDirectory);  
            if (_launchMode != LaunchMode.Player)
                await mutex.ReleaseAsync();
            if (!App.LaunchSettings.NoLaunchFlag.Active && !_cancelTokenSource.IsCancellationRequested)
            {
                if (!App.LaunchSettings.QuietFlag.Active)
                {
                    if (!_packageExtractionSuccess)
                        Frontend.ShowBalloonTip(Strings.Bootstrapper_ExtractionFailed_Title, Strings.Bootstrapper_ExtractionFailed_Message, ToolTipIcon.Warning);
                    else if (!allModificationsApplied)
                        Frontend.ShowBalloonTip(Strings.Bootstrapper_ModificationsFailed_Title, Strings.Bootstrapper_ModificationsFailed_Message, ToolTipIcon.Warning);
                }
                await StartRoblox();
            }
            await mutex.ReleaseAsync();
            Dialog?.CloseBootstrapper();
        }
        private async Task GetLatestVersionInfo()
        {
            const string LOG_IDENT = "Bootstrapper::GetLatestVersionInfo";
            var match = _channelRegex.Match(App.LaunchSettings.RobloxLaunchArgs);
            bool ChannelFlag = App.LaunchSettings.ChannelFlag.Active && !string.IsNullOrEmpty(App.LaunchSettings.ChannelFlag.Data);
            void EnrollChannel(string Channel = "production") => Deployment.Channel = Channel;
            void RevertChannel() => Deployment.Channel = Deployment.DefaultChannel;
            string EnrolledChannel = match.Groups.Count == 2 ? match.Groups[1].Value.ToLowerInvariant() : Deployment.DefaultChannel;
            bool behindProductionCheck = App.Settings.Prop.ChannelChangeMode == ChannelChangeMode.Prompt;
            if (!ChannelFlag)
            {
                switch (App.Settings.Prop.ChannelChangeMode)
                {
                    case ChannelChangeMode.Automatic:
                        App.Logger.WriteLine(LOG_IDENT, "Enrolling into channel");
                        EnrollChannel(EnrolledChannel);
                        break;
                    case ChannelChangeMode.Prompt:
                        App.Logger.WriteLine(LOG_IDENT, "Prompting channel enrollment");
                        if
                        (
                        !match.Success ||
                        match.Groups.Count != 2 ||
                        match.Groups[1].Value.ToLowerInvariant() == Deployment.Channel
                        )
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Channel is either equal or incorrectly formatted");
                            break;
                        }
                        string DisplayChannel = !String.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : Deployment.DefaultChannel;
                        var Result = Frontend.ShowMessageBox(
                        String.Format(Strings.Bootstrapper_Bootstrapper_Dialog_PromptChannelChange,
                        DisplayChannel, App.Settings.Prop.Channel),
                        MessageBoxImage.Question,
                        MessageBoxButton.YesNo
                        );
                        if (Result == MessageBoxResult.Yes)
                            EnrollChannel(EnrolledChannel);
                        break;
                    case ChannelChangeMode.Ignore:
                        App.Logger.WriteLine(LOG_IDENT, "Ignoring channel enrollment");
                        break;
                }
            }
            else
            {
                string ChannelFlagData = App.LaunchSettings.ChannelFlag.Data!;
                if (!String.IsNullOrEmpty(ChannelFlagData))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Forcing channel {ChannelFlagData}");
                    EnrollChannel(ChannelFlagData);
                }
            }
            if (!App.LaunchSettings.VersionFlag.Active || string.IsNullOrEmpty(App.LaunchSettings.VersionFlag.Data))
            {
                ClientVersion clientVersion;
                try
                {
                    clientVersion = await Deployment.GetInfo(Deployment.Channel, behindProductionCheck);
                }
                catch (InvalidChannelException ex)
                {
                    if (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Reverting enrolled channel to {Deployment.DefaultChannel} because a WindowsPlayer build does not exist for {App.Settings.Prop.Channel}");
                    }
                    else if (ex.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Reverting enrolled channel to {Deployment.DefaultChannel} because {App.Settings.Prop.Channel} is restricted for public use.");
                        if (App.Settings.Prop.ChannelChangeMode != ChannelChangeMode.Automatic)
                        {
                            Frontend.ShowMessageBox(
                                String.Format(
                                    Strings.Boostrapper_Dialog_UnauthorizedChannel,
                                    Deployment.Channel,
                                    Deployment.DefaultChannel
                                ),
                                MessageBoxImage.Information
                            );
                        }
                    }
                    else
                    {
                        throw;
                    }
                    RevertChannel();
                    clientVersion = await Deployment.GetInfo(Deployment.DefaultChannel, behindProductionCheck);
                }
                if (clientVersion.IsBehindDefaultChannel && App.Settings.Prop.ChannelChangeMode == ChannelChangeMode.Prompt)
                {
                    MessageBoxResult action = Frontend.ShowMessageBox(
                            String.Format(Strings.Bootstrapper_Dialog_ChannelOutOfDate, Deployment.Channel, Deployment.DefaultChannel),
                            MessageBoxImage.Warning,
                            MessageBoxButton.YesNo
                        );
                    if (action == MessageBoxResult.Yes)
                    {
                        App.Logger.WriteLine("Bootstrapper::CheckLatestVersion", $"Changed Roblox channel from {App.Settings.Prop.Channel} to {Deployment.DefaultChannel}");
                        RevertChannel();
                        clientVersion = await Deployment.GetInfo(Deployment.DefaultChannel);
                    }
                }
                using var key = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\ROBLOX Corporation\Environments\{AppData.RegistryName}\Channel");
                key.SetValueSafe("www." + Deployment.RobloxDomain, Deployment.IsDefaultChannel ? "" : Deployment.Channel);
                _latestVersionGuid = clientVersion.VersionGuid;
                _latestVersion = Utilities.ParseVersionSafe(clientVersion.Version);
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"Version set to {App.LaunchSettings.VersionFlag.Data} from arguments");
                _latestVersionGuid = App.LaunchSettings.VersionFlag.Data;
            }
            if (_staticDirectory)
                _latestVersionDirectory = AppData.StaticDirectory;
            else
                _latestVersionDirectory = Path.Combine(Paths.Versions, _latestVersionGuid);
            string pkgManifestUrl = Deployment.GetLocation($"/{_latestVersionGuid}-rbxPkgManifest.txt");
            var pkgManifestData = await App.HttpClient.GetStringAsync(pkgManifestUrl);
            _versionPackageManifest = new(pkgManifestData);
            if (_launchMode == LaunchMode.Unknown)
            {
                App.Logger.WriteLine(LOG_IDENT, "Identifying launch mode from package manifest");
                bool isPlayer = _versionPackageManifest.Exists(x => x.Name == "RobloxApp.zip");
                App.Logger.WriteLine(LOG_IDENT, $"isPlayer: {isPlayer}");
                _launchMode = isPlayer ? LaunchMode.Player : LaunchMode.Studio;
                SetupAppData();  
            }
        }
        private bool IsEligibleForBackgroundUpdate()
        {
            const string LOG_IDENT = "Bootstrapper::IsEligibleForBackgroundUpdate";
            if (App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Is the background updater process");
                return false;
            }
            if (!App.Settings.Prop.BackgroundUpdatesEnabled)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Background updates disabled");
                return false;
            }
            if (IsStudioLaunch)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Studio launch");
                return false;
            }
            if (_mustUpgrade)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Must upgrade is true");
                return false;
            }
            const long minimumFreeSpace = 3_000_000_000;
            long space = Filesystem.GetFreeDiskSpace(Paths.Base);
            if (space < minimumFreeSpace)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Not eligible: User has {space} free space, at least {minimumFreeSpace} is required");
                return false;
            }
            if (_latestVersion == default)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Latest version is undefined");
                return false;
            }
            Version? currentVersion = Utilities.GetRobloxVersion(AppData);
            if (currentVersion == default)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Current version is undefined");
                return false;
            }
            if (currentVersion.Minor > _latestVersion.Minor)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Downgrade");
                return false;
            }
            int diff = _latestVersion.Minor - currentVersion.Minor;
            if (diff == 0 || diff == 1)
            {
                App.Logger.WriteLine(LOG_IDENT, "Eligible");
                return true;
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"Not eligible: Major version diff is {diff}");
                return false;
            }
        }
        private async Task StartRoblox()
        {
            const string LOG_IDENT = "Bootstrapper::StartRoblox";
            SetStatus(Strings.Bootstrapper_Status_Starting);
            if (_launchMode == LaunchMode.Player)
            {
                if (!Deployment.IsDefaultRobloxDomain && string.IsNullOrEmpty(_launchCommandLine))
                    _launchCommandLine = "roblox://navigation/home";
                else if (string.IsNullOrEmpty(_launchCommandLine))
                    _launchCommandLine = "--app";
            }
            string resolvedName = AppData.ExecutableName;
            string executablePath = Path.Combine(AppData.Directory, resolvedName);
            if (!File.Exists(executablePath))
            {
                await UpgradeRoblox();
            }
            string appSettingsPath = Path.Combine(AppData.Directory, "AppSettings.xml");
            if (!File.Exists(appSettingsPath))
            {
                try
                {
                    File.WriteAllText(appSettingsPath, AppSettings.Replace("roblox.com", Deployment.RobloxDomain));
                    App.Logger.WriteLine(LOG_IDENT, "AppSettings.xml written.");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Failed to write AppSettings.xml");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
            var startInfo = new ProcessStartInfo()
            {
                FileName = Path.Combine(AppData.Directory, resolvedName),
                Arguments = _launchCommandLine,
                WorkingDirectory = AppData.Directory,
                UseShellExecute = false
            };
            if (_launchMode == LaunchMode.Player && ShouldRunAsAdmin())
            {
                startInfo.Verb = "runas";
                startInfo.UseShellExecute = true;
            }
            else if (_launchMode == LaunchMode.StudioAuth)
            {
                using var p = Process.Start(startInfo);
                return;
            }
            try
            {
                using var process = Process.Start(startInfo);
                if (process is not null)
                    _appPid = process.Id;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to start Roblox process: {ex.Message}");
                App.Logger.WriteException(LOG_IDENT, ex);
                throw;
            }
            App.Logger.WriteLine(LOG_IDENT, $"Started Roblox (PID {_appPid})");
            if (_mutex is not null)
                await _mutex.ReleaseAsync();
        }
        private bool ShouldRunAsAdmin()
        {
            foreach (var root in WindowsRegistry.Roots)
            {
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
                if (key is null)
                    continue;
                string? flags = (string?)key.GetValue(AppData.ExecutablePath);
                if (flags is not null && flags.Contains("RUNASADMIN", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        public void Cancel()
        {
            const string LOG_IDENT = "Bootstrapper::Cancel";
            if (_cancelTokenSource.IsCancellationRequested)
                return;
            App.Logger.WriteLine(LOG_IDENT, "Cancelling launch...");
            _cancelTokenSource.Cancel();
            if (Dialog is not null)
                Dialog.CancelEnabled = false;
            if (_isInstalling)
            {
                try
                {
                    WindowsRegistry.RegisterClientLocation(IsStudioLaunch, null);
                    if (Directory.Exists(_latestVersionDirectory))
                        Directory.Delete(_latestVersionDirectory, true);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not fully clean up installation!");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
            else if (_appPid != 0)
            {
                try
                {
                    using var process = Process.GetProcessById(_appPid);
                    process.Kill();
                }
                catch (Exception) { }
            }
            Dialog?.CloseBootstrapper();
            App.SoftTerminate(ErrorCode.ERROR_CANCELLED);
        }
        private static bool TryDeleteRobloxInDirectory(string dir)
        {
            string[] executables = { App.RobloxPlayerAppName, App.RobloxStudioAppName };
            foreach (string exe in executables)
            {
                string path = Path.Combine(dir, exe);
                if (!File.Exists(path))
                    return true;
                try
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return true;
        }
        public static void CleanupVersionsFolder()
        {
            const string LOG_IDENT = "Bootstrapper::CleanupVersionsFolder";
            if (App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Background updater tried to cleanup, stopping!");
                return;
            }
            if (!Directory.Exists(Paths.Versions))
            {
                App.Logger.WriteLine(LOG_IDENT, "Versions directory does not exist, skipping cleanup.");
                return;
            }
            foreach (string dir in Directory.EnumerateDirectories(Paths.Versions))
            {
                string dirName = Path.GetFileName(dir);
                if (
                    !_staticDirectory && (dirName != App.PlayerState.Prop.VersionGuid && dirName != App.StudioState.Prop.VersionGuid) ||
                    _staticDirectory && (dirName != "WindowsPlayer" && dirName != "WindowsStudio64")
                    )
                {
                    if (!TryDeleteRobloxInDirectory(dir))
                        continue;
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        try
                        {
                            Filesystem.AssertReadOnlyDirectory(dir);
                            Directory.Delete(dir, true);
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Failed to delete {dir} after fixing attributes.");
                            App.Logger.WriteException(LOG_IDENT, ex);
                        }
                    }
                    catch (IOException ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to delete {dir}");
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }
            }
        }
        private void MigrateCompatibilityFlags()
        {
            const string LOG_IDENT = "Bootstrapper::MigrateCompatibilityFlags";
            using RegistryKey appFlagsKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
            string oldClientLocation = Path.Combine(Paths.Versions, AppData.DistributionState.VersionGuid, AppData.ExecutableName);
            string newClientLocation = Path.Combine(_latestVersionDirectory, AppData.ExecutableName);
            string? appFlags = appFlagsKey.GetValue(oldClientLocation) as string;
            if (appFlags is not null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Migrating app compatibility flags from {oldClientLocation} to {newClientLocation}...");
                appFlagsKey.SetValueSafe(newClientLocation, appFlags);
                appFlagsKey.DeleteValueSafe(oldClientLocation);
            }
        }
        private void KillRobloxPlayers()
        {
            const string LOG_IDENT = "Bootstrapper::KillRobloxPlayers";
            var procsToKill = Process.GetProcessesByName("RobloxPlayerBeta")
                .Concat(Process.GetProcessesByName("RobloxCrashHandler"))
                .ToArray();
            foreach (Process process in procsToKill)
            {
                try
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Terminating process {process.ProcessName} ({process.Id})");
                    process.Kill();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to close process {process.Id}");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
                finally
                {
                    process.Dispose();
                }
            }
            var studioProcesses = Process.GetProcessesByName("RobloxStudioBeta");
            if (studioProcesses.Length > 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "Waiting for Roblox Studio processes to exit...");
                SetStatus("Waiting for Roblox Studio...");
                foreach (var p in studioProcesses)
                {
                    try
                    {
                        if (!p.HasExited)
                            p.WaitForExit(5000);
                    }
                    catch { }
                    finally
                    {
                        p.Dispose();
                    }
                }
                App.Logger.WriteLine(LOG_IDENT, "All Roblox Studio processes closed.");
            }
        }
        private async Task UpgradeRoblox()
        {
            const string LOG_IDENT = "Bootstrapper::UpgradeRoblox";
            bool CancelUpgrade = !App.Settings.Prop.UpdateRoblox;
            if (CancelUpgrade)
            {
                SetStatus(Strings.Bootstrapper_Status_CancelUpgrade);
                App.Logger.WriteLine(LOG_IDENT, "Upgrading disabled, cancelling the upgrade.");
                await Task.Delay(2000);
            }
            if (CancelUpgrade && !Directory.Exists(_latestVersionDirectory))
            {
                Frontend.ShowMessageBox(Strings.Bootstrapper_Dialog_NoUpgradeWithoutClient, MessageBoxImage.Warning, MessageBoxButton.OK);
            }
            else if (CancelUpgrade)
            {
                return;
            }
            if (String.IsNullOrEmpty(AppData.DistributionState.VersionGuid))
                SetStatus(Strings.Bootstrapper_Status_Installing);
            else
                SetStatus(Strings.Bootstrapper_Status_Upgrading);
            Directory.CreateDirectory(Paths.Base);
            Directory.CreateDirectory(Paths.Downloads);
            Directory.CreateDirectory(Paths.Versions);
            _isInstalling = true;
            if (!App.LaunchSettings.BackgroundUpdaterFlag.Active)
                KillRobloxPlayers();
            if (!App.LaunchSettings.BackgroundUpdaterFlag.Active && Directory.Exists(_latestVersionDirectory))
            {
                try
                {
                    Directory.Delete(_latestVersionDirectory, true);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Failed to delete the latest version directory");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
            Directory.CreateDirectory(_latestVersionDirectory);
            var cachedPackageHashes = Directory.GetFiles(Paths.Downloads).Select(Path.GetFileName).ToHashSet();
            int totalSizeRequired = 0;
            totalSizeRequired += _versionPackageManifest.Where(x => !cachedPackageHashes.Contains(x.Signature)).Sum(x => x.PackedSize);
            totalSizeRequired += _versionPackageManifest.Sum(x => x.Size);
            if (Filesystem.GetFreeDiskSpace(Paths.Base) < totalSizeRequired)
            {
                Frontend.ShowMessageBox(Strings.Bootstrapper_NotEnoughSpace, MessageBoxImage.Error);
                App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
                return;
            }
            if (Dialog is not null)
            {
                Dialog.ProgressStyle = ProgressBarStyle.Continuous;
                Dialog.TaskbarProgressState = TaskbarItemProgressState.Normal;
                Dialog.ProgressMaximum = ProgressBarMaximum;
                int totalPackedSize = _versionPackageManifest.Sum(package => package.PackedSize);
                _progressIncrement = (double)ProgressBarMaximum / totalPackedSize;
                if (Dialog is WinFormsDialogBase)
                    _taskbarProgressMaximum = (double)TaskbarProgressMaximumWinForms;
                else
                    _taskbarProgressMaximum = (double)TaskbarProgressMaximumWpf;
                _taskbarProgressIncrement = _taskbarProgressMaximum / (double)totalPackedSize;
            }
            var extractionTasks = new List<Task>();
            foreach (var package in _versionPackageManifest)
            {
                if (_cancelTokenSource.IsCancellationRequested)
                    return;
                await DownloadPackage(package);
                if (package.Name == "WebView2RuntimeInstaller.zip")
                    continue;
                extractionTasks.Add(Task.Run(() => ExtractPackage(package), _cancelTokenSource.Token));
            }
            if (_cancelTokenSource.IsCancellationRequested)
                return;
            if (Dialog is not null)
            {
                Dialog.ProgressStyle = ProgressBarStyle.Marquee;
                Dialog.TaskbarProgressState = TaskbarItemProgressState.Indeterminate;
                SetStatus(Strings.Bootstrapper_Status_Configuring);
            }
            await Task.WhenAll(extractionTasks);
            if (_cancelTokenSource.IsCancellationRequested)
                return;
            if (App.State.Prop.PromptWebView2Install)
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                using var hkcuKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                if (hklmKey is not null || hkcuKey is not null)
                {
                    App.State.Prop.PromptWebView2Install = true;
                }
                else
                {
                    var result = Frontend.ShowMessageBox(Strings.Bootstrapper_WebView2NotFound, MessageBoxImage.Warning, MessageBoxButton.YesNo, MessageBoxResult.Yes);
                    if (result != MessageBoxResult.Yes)
                    {
                        App.State.Prop.PromptWebView2Install = false;
                    }
                    else
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Installing WebView2 runtime...");
                        var package = _versionPackageManifest.Find(x => x.Name == "WebView2RuntimeInstaller.zip");
                        if (package is null)
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Aborted runtime install because package does not exist, has WebView2 been added in this Roblox version yet?");
                            return;
                        }
                        string baseDirectory = Path.Combine(_latestVersionDirectory, PackageDirectoryMap[package.Name]);
                        ExtractPackage(package);
                        SetStatus(Strings.Bootstrapper_Status_InstallingWebView2);
                        var startInfo = new ProcessStartInfo()
                        {
                            WorkingDirectory = baseDirectory,
                            FileName = Path.Combine(baseDirectory, "MicrosoftEdgeWebview2Setup.exe"),
                            Arguments = "/silent /install"
                        };
                        using var webviewProc = Process.Start(startInfo);
                        if (webviewProc != null)
                            await webviewProc.WaitForExitAsync();
                        App.Logger.WriteLine(LOG_IDENT, "Finished installing runtime");
                        Directory.Delete(baseDirectory, true);
                    }
                }
            }
            MigrateCompatibilityFlags();
            AppData.DistributionState.VersionGuid = _latestVersionGuid;
            AppData.DistributionState.PackageHashes.Clear();
            foreach (var package in _versionPackageManifest)
                AppData.DistributionState.PackageHashes.Add(package.Name, package.Signature);
            CleanupVersionsFolder();
            var allPackageHashes = new HashSet<string>(App.PlayerState.Prop.PackageHashes.Values.Concat(App.StudioState.Prop.PackageHashes.Values));
            if (!App.Settings.Prop.DebugDisableVersionPackageCleanup)
            {
                foreach (string hash in cachedPackageHashes)
                {
                    if (!allPackageHashes.Contains(hash))
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Deleting unused package {hash}");
                        try
                        {
                            File.Delete(Path.Combine(Paths.Downloads, hash));
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Failed to delete {hash}!");
                            App.Logger.WriteException(LOG_IDENT, ex);
                        }
                    }
                }
            }
            App.Logger.WriteLine(LOG_IDENT, "Registering approximate program size...");
            int distributionSize = _versionPackageManifest.Sum(x => x.Size + x.PackedSize) / 1024;
            AppData.DistributionState.Size = distributionSize;
            int totalSize = App.PlayerState.Prop.Size + App.StudioState.Prop.Size;
            using (var uninstallKey = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
            {
                uninstallKey.SetValueSafe("EstimatedSize", totalSize);
            }
            WindowsRegistry.RegisterClientLocation(IsStudioLaunch, _latestVersionDirectory);
            string appSettingsFile = Path.Combine(_latestVersionDirectory, "AppSettings.xml");
            try
            {
                File.WriteAllText(appSettingsFile, AppSettings.Replace("roblox.com", Deployment.RobloxDomain));
                App.Logger.WriteLine(LOG_IDENT, "AppSettings.xml written.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to write AppSettings.xml");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
            App.Logger.WriteLine(LOG_IDENT, $"Registered as {totalSize} KB");
            App.State.Prop.ForceReinstall = false;
            App.State.Save();
            AppData.DistributionStateManager.Save();
            _isInstalling = false;
        }
        private static void StartBackgroundUpdater()
        {
            const string LOG_IDENT = "Bootstrapper::StartBackgroundUpdater";
            if (Utilities.DoesMutexExist("Bloxstrap-BackgroundUpdater"))
            {
                App.Logger.WriteLine(LOG_IDENT, "Background updater already running");
                return;
            }
            App.Logger.WriteLine(LOG_IDENT, "Starting background updater");
            using var proc = Process.Start(Paths.Process, "-backgroundupdater");
        }
        private async Task<bool> ApplyModifications()
        {
            const string LOG_IDENT = "Bootstrapper::ApplyModifications";
            string appSettingsFile = Path.Combine(_latestVersionDirectory, "AppSettings.xml");
            if (!File.Exists(appSettingsFile))
            {
                try
                {
                    File.WriteAllText(appSettingsFile, AppSettings.Replace("roblox.com", Deployment.RobloxDomain));
                    App.Logger.WriteLine(LOG_IDENT, "AppSettings.xml written.");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Failed to write AppSettings.xml");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
            if (Directory.Exists(Paths.Modifications))
            {
                foreach (string file in Directory.EnumerateFiles(Paths.Modifications, "*.*", SearchOption.AllDirectories))
                {
                    string relativeFile = file.Substring(Paths.Modifications.Length).TrimStart(Path.DirectorySeparatorChar);
                    if (relativeFile.EndsWith("ClientSettings") || relativeFile.EndsWith(".lock") || relativeFile.EndsWith(".mesh") || relativeFile == "README.txt")
                        continue;
                    string targetFile = Path.Combine(_latestVersionDirectory, relativeFile);
                    try
                    {
                        var sourceInfo = new FileInfo(file);
                        if (File.Exists(targetFile))
                        {
                            var targetInfo = new FileInfo(targetFile);
                            if (targetInfo.Length == sourceInfo.Length && targetInfo.LastWriteTimeUtc == sourceInfo.LastWriteTimeUtc)
                                continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                        File.Copy(file, targetFile, true);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to copy mod file ({relativeFile}): {ex.Message}");
                    }
                }
            }
            if (App.Settings.Prop.UseFastFlagManager)
            {
                string source = Path.Combine(Paths.Base, "ClientSettings", "ClientAppSettings.json");
                if (File.Exists(source))
                {
                    string rel = Path.Combine("ClientSettings", "ClientAppSettings.json");
                    string dest = Path.Combine(_latestVersionDirectory, rel);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Copy(source, dest, true);
                        App.Logger.WriteLine(LOG_IDENT, "FastFlags Applied.");
                    }
                    catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }
                }
            }
            return true;
        }
        private async Task DownloadPackage(Package package)
        {
            string LOG_IDENT = $"Bootstrapper::DownloadPackage.{package.Name}";
            if (_cancelTokenSource.IsCancellationRequested)
                return;
            Directory.CreateDirectory(Paths.Downloads);
            string packageUrl = Deployment.GetLocation($"/{_latestVersionGuid}-{package.Name}");
            string robloxPackageLocation = Path.Combine(Paths.Roblox, "Downloads", package.Signature);
            if (File.Exists(package.DownloadPath))
            {
                var file = new FileInfo(package.DownloadPath);
                string calculatedMD5 = MD5Hash.FromFile(package.DownloadPath);
                if (calculatedMD5 != package.Signature)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Package is corrupted ({calculatedMD5} != {package.Signature})! Deleting and re-downloading...");
                    file.Delete();
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Package is already downloaded, skipping...");
                    _totalDownloadedBytes += package.PackedSize;
                    UpdateProgressBar();
                    return;
                }
            }
            else if (File.Exists(robloxPackageLocation))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Found existing copy at '{robloxPackageLocation}'! Copying to Downloads folder...");
                File.Copy(robloxPackageLocation, package.DownloadPath);
                _totalDownloadedBytes += package.PackedSize;
                UpdateProgressBar();
                return;
            }
            if (File.Exists(package.DownloadPath))
                return;
            const int maxTries = 5;
            App.Logger.WriteLine(LOG_IDENT, "Downloading...");
            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                for (int i = 1; i <= maxTries; i++)
                {
                    if (_cancelTokenSource.IsCancellationRequested)
                        return;
                    int totalBytesRead = 0;
                    try
                    {
                        var response = await App.HttpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, _cancelTokenSource.Token);
                        await using var stream = await response.Content.ReadAsStreamAsync(_cancelTokenSource.Token);
                        await using var fileStream = new FileStream(package.DownloadPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete, 81920, useAsync: true);
                        long lastReportedTime = Environment.TickCount64;
                        int unthrottledBytes = 0;
                        while (true)
                        {
                            if (_cancelTokenSource.IsCancellationRequested)
                            {
                                stream.Close();
                                fileStream.Close();
                                return;
                            }
                            int bytesRead = await stream.ReadAsync(rentedBuffer.AsMemory(0, rentedBuffer.Length), _cancelTokenSource.Token);
                            if (bytesRead == 0)
                                break;
                            totalBytesRead += bytesRead;
                            await fileStream.WriteAsync(rentedBuffer.AsMemory(0, bytesRead), _cancelTokenSource.Token);
                            _totalDownloadedBytes += bytesRead;
                            unthrottledBytes += bytesRead;
                            long now = Environment.TickCount64;
                            if (now - lastReportedTime >= 80 || unthrottledBytes >= 262144)
                            {
                                lastReportedTime = now;
                                unthrottledBytes = 0;
                                SetStatus(
                                    String.Format(App.Settings.Prop.DownloadingStringFormat,
                                    package.Name,
                                    totalBytesRead / 1048576,
                                    package.Size / 1048576
                                    ));
                                UpdateProgressBar();
                            }
                        }
                        SetStatus(
                            String.Format(App.Settings.Prop.DownloadingStringFormat,
                            package.Name,
                            totalBytesRead / 1048576,
                            package.Size / 1048576
                            ));
                        UpdateProgressBar();
                        string hash = MD5Hash.FromStream(fileStream);
                        if (hash != package.Signature)
                            throw new ChecksumFailedException($"Failed to verify download of {packageUrl}\n\nExpected hash: {package.Signature}\nGot hash: {hash}");
                        App.Logger.WriteLine(LOG_IDENT, $"Finished downloading! ({totalBytesRead} bytes total)");
                        break;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"An exception occurred after downloading {totalBytesRead} bytes. ({i}/{maxTries})");
                        App.Logger.WriteException(LOG_IDENT, ex);
                        if (ex.GetType() == typeof(ChecksumFailedException))
                        {
                            Frontend.ShowConnectivityDialog(
                                Strings.Dialog_Connectivity_UnableToDownload,
                                String.Format(Strings.Dialog_Connectivity_UnableToDownloadReason, "[https://github.com/bloxstraplabs/bloxstrap/wiki/Bloxstrap-is-unable-to-download-Roblox](https://github.com/bloxstraplabs/bloxstrap/wiki/Bloxstrap-is-unable-to-download-Roblox)"),
                                MessageBoxImage.Error,
                                ex
                            );
                            App.Terminate(ErrorCode.ERROR_CANCELLED);
                        }
                        else if (i >= maxTries)
                            throw;
                        if (File.Exists(package.DownloadPath))
                            File.Delete(package.DownloadPath);
                        _totalDownloadedBytes -= totalBytesRead;
                        UpdateProgressBar();
                        if (ex.GetType() == typeof(IOException) && !packageUrl.StartsWith("http://"))
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Retrying download over HTTP...");
                            packageUrl = packageUrl.Replace("https://", "http://");
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
        private void ExtractPackage(Package package, List<string>? files = null)
        {
            const string LOG_IDENT = "Bootstrapper::ExtractPackage";
            string? packageDir = PackageDirectoryMap.GetValueOrDefault(package.Name);
            if (packageDir is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"WARNING: {package.Name} was not found in the package map!");
                return;
            }
            string packageFolder = Path.Combine(_latestVersionDirectory, packageDir);
            string? fileFilter = null;
            if (files is not null)
            {
                var regexList = new List<string>(files.Count);
                foreach (string file in files)
                    regexList.Add("^" + file.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + "$");
                fileFilter = String.Join(';', regexList);
            }
            App.Logger.WriteLine(LOG_IDENT, $"Extracting {package.Name}...");
            var fastZip = new FastZip(_fastZipEvents);
            fastZip.RestoreDateTimeOnExtract = false;
            fastZip.RestoreAttributesOnExtract = false;
            fastZip.ExtractZip(package.DownloadPath, packageFolder, fileFilter);
            App.Logger.WriteLine(LOG_IDENT, $"Finished extracting {package.Name}");
        }
    }
}
