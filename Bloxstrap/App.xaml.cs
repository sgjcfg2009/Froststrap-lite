using Microsoft.Win32;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Hardware;
namespace Bloxstrap
{
    public partial class App : Application
    {
        public const string ProjectName = "Froststrap";
        public const string ProjectOwner = "Froststrap";
        public const string ProjectRepository = "Froststrap/Froststrap";
        public const string ProjectDownloadLink = "https://github.com/Froststrap/Froststrap/releases";
        public const string ProjectHelpLink = "https://github.com/bloxstraplabs/bloxstrap/wiki";
        public const string ProjectSupportLink = "https://github.com/Froststrap/Froststrap/issues/new";
        public const string ProjectRemoteDataLink = "https://raw.githubusercontent.com/RealMeddsam/config/refs/heads/main/Data.json";
        public const string RobloxPlayerAppName = "RobloxPlayerBeta.exe";
        public const string RobloxStudioAppName = "RobloxStudioBeta.exe";
        public const string UninstallKey = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{ProjectName}";
        public const string ApisKey = $"Software\\{ProjectName}";
        public static LaunchSettings LaunchSettings { get; private set; } = null!;
        public static BuildMetadataAttribute BuildMetadata = Assembly.GetExecutingAssembly().GetCustomAttribute<BuildMetadataAttribute>()!;
        public static string Version = Assembly.GetExecutingAssembly().GetName().Version!.ToString()[..^2];
        public static Bootstrapper? Bootstrapper { get; set; } = null!;
        public static bool IsActionBuild => !String.IsNullOrEmpty(BuildMetadata.CommitRef);
        public static bool IsProductionBuild => IsActionBuild && BuildMetadata.CommitRef.StartsWith("tag", StringComparison.Ordinal);
        public static bool IsPlayerInstalled => App.PlayerState.IsSaved && !String.IsNullOrEmpty(App.PlayerState.Prop.VersionGuid);
        public static bool IsStudioInstalled => App.StudioState.IsSaved && !String.IsNullOrEmpty(App.StudioState.Prop.VersionGuid);
        public static readonly Logger Logger = new();
        public static readonly Dictionary<string, BaseTask> PendingSettingTasks = new();
        public static readonly JsonManager<Settings> Settings = new();
        public static readonly JsonManager<State> State = new();
        public static readonly LazyJsonManager<DistributionState> PlayerState = new(nameof(PlayerState));
        public static readonly LazyJsonManager<DistributionState> StudioState = new(nameof(StudioState));
        public static readonly RemoteDataManager RemoteData = new();
        public static readonly FastFlagManager FastFlags = new();
        public static readonly HttpClient HttpClient = new(new HttpClientLoggingHandler(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            EnableMultipleHttp2Connections = true
        }));
        private static bool _showingExceptionDialog = false;
        public static void Terminate(ErrorCode exitCode = ErrorCode.ERROR_SUCCESS)
        {
            int exitCodeNum = (int)exitCode;
            Logger.WriteLine("App::Terminate", $"Terminating with exit code {exitCodeNum} ({exitCode})");
            Environment.Exit(exitCodeNum);
        }
        public static void SoftTerminate(ErrorCode exitCode = ErrorCode.ERROR_SUCCESS)
        {
            int exitCodeNum = (int)exitCode;
            Logger.WriteLine("App::SoftTerminate", $"Terminating with exit code {exitCodeNum} ({exitCode})");
            Current.Dispatcher.Invoke(() => Current.Shutdown(exitCodeNum));
        }
        void GlobalExceptionHandler(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            Logger.WriteLine("App::GlobalExceptionHandler", "An exception occurred");
            FinalizeExceptionHandling(e.Exception);
        }
        public static void FinalizeExceptionHandling(AggregateException ex)
        {
            foreach (var innerEx in ex.InnerExceptions)
                Logger.WriteException("App::FinalizeExceptionHandling", innerEx);
            FinalizeExceptionHandling(ex.GetBaseException(), false);
        }
        public static void FinalizeExceptionHandling(Exception ex, bool log = true)
        {
            if (log)
                Logger.WriteException("App::FinalizeExceptionHandling", ex);
            if (_showingExceptionDialog)
                return;
            _showingExceptionDialog = true;
            if (Bootstrapper?.Dialog != null)
            {
                if (Bootstrapper.Dialog.TaskbarProgressValue == 0)
                    Bootstrapper.Dialog.TaskbarProgressValue = 1;
                Bootstrapper.Dialog.TaskbarProgressState = TaskbarItemProgressState.Error;
            }
            Frontend.ShowExceptionDialog(ex);
            Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
        }
        public static void AssertWindowsOSVersion()
        {
            const string LOG_IDENT = "App::AssertWindowsOSVersion";
            int major = Environment.OSVersion.Version.Major;
            if (major < 10)
            {
                Logger.WriteLine(LOG_IDENT, $"Detected unsupported Windows version ({Environment.OSVersion.Version}).");
                if (!LaunchSettings.QuietFlag.Active)
                    Frontend.ShowMessageBox(Strings.App_OSDeprecation_Win7_81, MessageBoxImage.Error);
                Terminate(ErrorCode.ERROR_INVALID_FUNCTION);
            }
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            const string LOG_IDENT = "App::OnStartup";
            Locale.Initialize();
            base.OnStartup(e);
            Logger.WriteLine(LOG_IDENT, $"Starting {ProjectName} v{Version}");
            var userAgent = new StringBuilder($"{ProjectName}/{Version}");
            if (IsActionBuild)
            {
                Logger.WriteLine(LOG_IDENT, $"Compiled {BuildMetadata.Timestamp.ToFriendlyString()} from commit {BuildMetadata.CommitHash} ({BuildMetadata.CommitRef})");
                if (IsProductionBuild)
                    userAgent.Append(" (Production)");
                else
                    userAgent.Append($" (Artifact {BuildMetadata.CommitHash}, {BuildMetadata.CommitRef})");
            }
            else
            {
                Logger.WriteLine(LOG_IDENT, $"Compiled {BuildMetadata.Timestamp.ToFriendlyString()} from {BuildMetadata.Machine}");
                userAgent.Append($" (Build {Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildMetadata.Machine))})");
            }
            Logger.WriteLine(LOG_IDENT, $"OSVersion: {Environment.OSVersion}");
            Logger.WriteLine(LOG_IDENT, $"Loaded from {Paths.Process}");
            Logger.WriteLine(LOG_IDENT, $"Temp path is {Paths.Temp}");
            Logger.WriteLine(LOG_IDENT, $"WindowsStartMenu path is {Paths.WindowsStartMenu}");
            ApplicationConfiguration.Initialize();
            HttpClient.Timeout = TimeSpan.FromSeconds(60);
            if (!HttpClient.DefaultRequestHeaders.UserAgent.Any())
                HttpClient.DefaultRequestHeaders.Add("User-Agent", userAgent.ToString());
            LaunchSettings = new LaunchSettings(e.Args);
            string? installLocation = null;
            bool fixInstallLocation = false;
            using var uninstallKey = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (uninstallKey?.GetValue("InstallLocation") is string installLocValue)
            {
                if (Directory.Exists(installLocValue))
                {
                    installLocation = installLocValue;
                }
                else
                {
                    var match = Regex.Match(installLocValue, @"^[a-zA-Z]:\\Users\\([^\\]+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string newLocation = installLocValue.Replace(match.Value, Paths.UserProfile, StringComparison.InvariantCultureIgnoreCase);
                        if (Directory.Exists(newLocation))
                        {
                            installLocation = newLocation;
                            fixInstallLocation = true;
                        }
                    }
                }
            }
            if (installLocation == null && Directory.GetParent(Paths.Process)?.FullName is string processDir)
            {
                var files = Directory.GetFiles(processDir).Select(Path.GetFileName).ToArray();
                if (files.Length <= 3 && files.Contains("Settings.json") && files.Contains("State.json"))
                {
                    installLocation = processDir;
                    fixInstallLocation = true;
                }
            }
            if (fixInstallLocation && installLocation != null)
            {
                var installer = new Installer
                {
                    InstallLocation = installLocation,
                    IsImplicitInstall = true
                };
                if (installer.CheckInstallLocation())
                {
                    Logger.WriteLine(LOG_IDENT, $"Changing install location to '{installLocation}'");
                    installer.DoInstall();
                }
                else
                {
                    installLocation = null;
                }
            }
            if (installLocation == null)
            {
                Logger.Initialize(true);
                AssertWindowsOSVersion();
                Logger.WriteLine(LOG_IDENT, "Not installed, launching the installer");
                LaunchHandler.LaunchInstaller();
            }
            else
            {
                Paths.Initialize(installLocation);
                if (Paths.Process != Paths.Application && !File.Exists(Paths.Application))
                    File.Copy(Paths.Process, Paths.Application);
                Logger.Initialize(LaunchSettings.UninstallFlag.Active);
                if (!Logger.Initialized && !Logger.NoWriteMode)
                {
                    Logger.WriteLine(LOG_IDENT, "Possible duplicate launch detected, terminating.");
                    Terminate();
                }
                Task.Run(RemoteData.LoadData);
                Settings.Load();
                State.Load();
                FastFlags.Load();
                if (Settings.Prop.Theme > Enums.Theme.Custom)
                {
                    Settings.Prop.Theme = Enums.Theme.Dark;
                    Settings.Save();
                }
                if (!Locale.SupportedLocales.ContainsKey(Settings.Prop.Locale))
                {
                    Settings.Prop.Locale = "nil";
                    Settings.Save();
                }
                Locale.Set(Settings.Prop.Locale);
                if (!LaunchSettings.BypassUpdateCheck)
                    Installer.HandleUpgrade();
                WindowsRegistry.RegisterApis();
                LaunchHandler.ProcessLaunchArgs();
            }
        }
    }
}
