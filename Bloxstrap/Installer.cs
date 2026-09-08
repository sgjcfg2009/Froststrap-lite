using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Bloxstrap.AppData;
using Bloxstrap.Models.Persistable;
using Microsoft.Win32;
namespace Bloxstrap
{
    internal class Installer
    {
        private static string DesktopShortcut => Path.Combine(Paths.Desktop, $"{App.ProjectName}.lnk");
        private static string StartMenuShortcut => Path.Combine(Paths.WindowsStartMenu, $"{App.ProjectName}.lnk");
        public string BloxstrapInstallDirectory = Path.Combine(Paths.LocalAppData, "Bloxstrap");  
        public string InstallLocation = Path.Combine(Paths.LocalAppData, App.ProjectName);
        public bool ExistingDataPresent => File.Exists(Path.Combine(InstallLocation, "Settings.json"));
        public bool CreateDesktopShortcuts = true;
        public bool CreateStartMenuShortcuts = true;
        public static readonly string[] OtherLaunchers = { "Bloxstrap", "Fishstrap", "Lunastrap", "Luczystrap" };
        public bool ImportSettings { get; set; } = OtherLaunchers.Any(name => Directory.Exists(Path.Combine(Paths.LocalAppData, name)));
        public bool IsImplicitInstall = false;
        public string InstallLocationError { get; set; } = "";
        public ImportSettingsFrom ImportSource { get; set; } = ImportSettingsFrom.Bloxstrap;
        public string[] FilesForImporting = {
            "CustomThemes",  
            "Modifications",
            "Settings.json",
        };
        public void DoInstall()
        {
            const string LOG_IDENT = "Installer::DoInstall";
            App.Logger.WriteLine(LOG_IDENT, "Beginning installation");
            Directory.CreateDirectory(InstallLocation);
            Paths.Initialize(InstallLocation);
            if (!IsImplicitInstall)
            {
                Filesystem.AssertReadOnly(Paths.Application);
                try
                {
                    File.Copy(Paths.Process, Paths.Application, true);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not overwrite executable");
                    App.Logger.WriteException(LOG_IDENT, ex);
                    Frontend.ShowMessageBox(Strings.Installer_Install_CannotOverwrite, MessageBoxImage.Error);
                    App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
                }
            }
            using (var uninstallKey = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
            {
                uninstallKey.SetValueSafe("DisplayIcon", $"{Paths.Application},0");
                uninstallKey.SetValueSafe("DisplayName", App.ProjectName);
                uninstallKey.SetValueSafe("DisplayVersion", App.Version);
                if (uninstallKey.GetValue("InstallDate") is null)
                    uninstallKey.SetValueSafe("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                uninstallKey.SetValueSafe("InstallLocation", Paths.Base);
                uninstallKey.SetValueSafe("NoRepair", 1);
                uninstallKey.SetValueSafe("Publisher", App.ProjectOwner);
                uninstallKey.SetValueSafe("ModifyPath", $"\"{Paths.Application}\" -settings");
                uninstallKey.SetValueSafe("QuietUninstallString", $"\"{Paths.Application}\" -uninstall -quiet");
                uninstallKey.SetValueSafe("UninstallString", $"\"{Paths.Application}\" -uninstall");
                uninstallKey.SetValueSafe("HelpLink", App.ProjectHelpLink);
                uninstallKey.SetValueSafe("URLInfoAbout", App.ProjectSupportLink);
                uninstallKey.SetValueSafe("URLUpdateInfo", App.ProjectDownloadLink);
            }
            WindowsRegistry.RegisterApis();
            WindowsRegistry.RegisterPlayer();
            if (App.IsStudioInstalled)
                WindowsRegistry.RegisterStudio();
            if (CreateDesktopShortcuts)
                Shortcut.Create(Paths.Application, "", DesktopShortcut);
            if (CreateStartMenuShortcuts)
                Shortcut.Create(Paths.Application, "", StartMenuShortcut);
            if (ImportSource != ImportSettingsFrom.None)
            {
                try
                {
                    ImportSettingsFromSelectedApp();
                }
                catch (Exception ex)
                {
                    Frontend.ShowMessageBox(
                        String.Format(Strings.Installer_FailedToImportSettings, ex.Message),
                        MessageBoxImage.Error,
                        MessageBoxButton.OK
                    );
                }
            }
            else
            {
                string statePath = App.State.FileLocation;
                if (File.Exists(statePath))
                    App.State.Load(false);
            }
            App.Settings.Load(false);
            App.State.Load(false);
            App.FastFlags.Load(false);
            App.Settings.Save();
            App.Logger.WriteLine(LOG_IDENT, "Installation finished");
        }
        private bool ValidateLocation()
        {
            if (InstallLocation.StartsWith(Path.GetTempPath(), StringComparison.InvariantCultureIgnoreCase))
                return false;
            if (InstallLocation.Contains("OneDrive", StringComparison.InvariantCultureIgnoreCase))
                return false;
            if (InstallLocation == "C:\\")
                return false;
            if (InstallLocation.Contains("Local\\Bloxstrap"))
                return false;
            return true;
        }
        public bool CheckInstallLocation()
        {
            if (string.IsNullOrEmpty(InstallLocation))
            {
                InstallLocationError = Strings.Menu_InstallLocation_NotSet;
            }
            else if (!ValidateLocation())
            {
                InstallLocationError = Strings.Menu_InstallLocation_CantInstall;
            }
            else
            {
                if (!IsImplicitInstall
                    && !InstallLocation.EndsWith(App.ProjectName, StringComparison.InvariantCultureIgnoreCase)
                    && Directory.Exists(InstallLocation)
                    && Directory.EnumerateFileSystemEntries(InstallLocation).Any())
                {
                    string suggestedChange = Path.Combine(InstallLocation, App.ProjectName);
                    MessageBoxResult result = Frontend.ShowMessageBox(
                        String.Format(Strings.Menu_InstallLocation_NotEmpty, suggestedChange),
                        MessageBoxImage.Warning,
                        MessageBoxButton.YesNoCancel,
                        MessageBoxResult.Yes
                    );
                    if (result == MessageBoxResult.Yes)
                        InstallLocation = suggestedChange;
                    else if (result == MessageBoxResult.Cancel || result == MessageBoxResult.None)
                        return false;
                }
                try
                {
                    string testFile = Path.Combine(InstallLocation, $"{App.ProjectName}WriteTest.txt");
                    Directory.CreateDirectory(InstallLocation);
                    File.WriteAllText(testFile, "");
                    File.Delete(testFile);
                }
                catch (UnauthorizedAccessException)
                {
                    InstallLocationError = Strings.Menu_InstallLocation_NoWritePerms;
                }
                catch (Exception ex)
                {
                    InstallLocationError = ex.Message;
                }
            }
            return String.IsNullOrEmpty(InstallLocationError);
        }
        public static void DoUninstall(bool keepData)
        {
            const string LOG_IDENT = "Installer::DoUninstall";
            var processes = new List<Process>();
            if (!String.IsNullOrEmpty(App.PlayerState.Prop.VersionGuid))
                processes.AddRange(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(App.RobloxPlayerAppName)));
            if (App.IsStudioInstalled)
                processes.AddRange(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(App.RobloxStudioAppName)));
            if (processes.Count > 0)
            {
                var result = Frontend.ShowMessageBox(
                    Strings.Bootstrapper_Uninstall_RobloxRunning,
                    MessageBoxImage.Information,
                    MessageBoxButton.OKCancel,
                    MessageBoxResult.OK
                );
                if (result != MessageBoxResult.OK)
                {
                    foreach (var process in processes) process.Dispose();
                    App.Terminate(ErrorCode.ERROR_CANCELLED);
                    return;
                }
                try
                {
                    foreach (var process in processes)
                    {
                        try { process.Kill(); } catch { }
                        process.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to close process! {ex}");
                }
            }
            string robloxFolder = Path.Combine(Paths.Roblox);
            bool playerStillInstalled = true;
            bool studioStillInstalled = true;
            using var playerKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player");
            using var studioKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-studio");
            var playerFolder = playerKey?.GetValue("InstallLocation");
            if (playerKey is null || playerFolder is not string)
            {
                playerStillInstalled = false;
                WindowsRegistry.Unregister("roblox");
                WindowsRegistry.Unregister("roblox-player");
            }
            else
            {
                string playerPath = Path.Combine((string)playerFolder, App.RobloxPlayerAppName);
                WindowsRegistry.RegisterPlayer(playerPath, "%1");
            }
            var studioFolder = studioKey?.GetValue("InstallLocation");
            if (studioKey is null || studioFolder is not string)
            {
                studioStillInstalled = false;
                WindowsRegistry.Unregister("roblox-studio");
                WindowsRegistry.Unregister("roblox-studio-auth");
                WindowsRegistry.Unregister("Roblox.Place");
                WindowsRegistry.Unregister(".rbxl");
                WindowsRegistry.Unregister(".rbxlx");
            }
            else
            {
                string studioPath = Path.Combine((string)studioFolder, App.RobloxStudioAppName);
                WindowsRegistry.RegisterStudioProtocol(studioPath, "%1");
                WindowsRegistry.RegisterStudioFileClass(studioPath, "-ide \"%1\"");
            }
            Registry.CurrentUser.DeleteSubKey(App.ApisKey);
            var cleanupSequence = new List<Action>
            {
                () =>
                {
                    if (Directory.Exists(Paths.Desktop))
                    {
                        foreach (var file in Directory.EnumerateFiles(Paths.Desktop, "*.lnk"))
                        {
                            var shortcut = ShellLink.Shortcut.ReadFromFile(file);
                            if (shortcut.ExtraData.EnvironmentVariableDataBlock?.TargetUnicode == Paths.Application)
                                File.Delete(file);
                        }
                    }
                },
                () => { if (File.Exists(StartMenuShortcut)) File.Delete(StartMenuShortcut); },
                () => { if (Directory.Exists(Paths.Versions)) Directory.Delete(Paths.Versions, true); },
                () => { if (Directory.Exists(Paths.Downloads)) Directory.Delete(Paths.Downloads, true); },
                () => { if (File.Exists(App.State.FileLocation)) File.Delete(App.State.FileLocation); },
                () =>
                {
                    if (Paths.Roblox == Path.Combine(Paths.Base, "Roblox") && Directory.Exists(Paths.Roblox))  
                        Directory.Delete(Paths.Roblox, true);                
                }
            };
            if (!keepData)
            {
                cleanupSequence.AddRange(new Action[]
                {
                    () => { if (Directory.Exists(Paths.Modifications)) Directory.Delete(Paths.Modifications, true); },
                    () => { if (Directory.Exists(Paths.CustomCursors)) Directory.Delete(Paths.CustomCursors, true); },
                    () => { if (File.Exists(App.Settings.FileLocation)) File.Delete(App.Settings.FileLocation); },
                    () => { if (File.Exists(App.State.FileLocation)) File.Delete(App.State.FileLocation); }
                });
                bool deleteFolder = Directory.Exists(Paths.Base) && Directory.GetFiles(Paths.Base).Length <= 3;
                if (deleteFolder)
                    cleanupSequence.Add(() => Directory.Delete(Paths.Base, true));
            }
            if (!playerStillInstalled && !studioStillInstalled && Directory.Exists(robloxFolder))
                cleanupSequence.Add(() => Directory.Delete(robloxFolder, true));
            cleanupSequence.Add(() => Registry.CurrentUser.DeleteSubKey(App.UninstallKey));
            foreach (var action in cleanupSequence)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Encountered exception when running cleanup sequence");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
            if (Directory.Exists(Paths.Base))
            {
                string deleteCommand;
                if (!keepData && Directory.GetFiles(Paths.Base).Length <= 3)
                    deleteCommand = $"del /Q \"{Paths.Base}\\*\" && rmdir \"{Paths.Base}\"";
                else
                    deleteCommand = $"del /Q \"{Paths.Application}\"";
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout 5 && {deleteCommand}",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
        }
        public static void HandleUpgrade()
        {
            const string LOG_IDENT = "Installer::HandleUpgrade";
            if (!File.Exists(Paths.Application) || Paths.Process == Paths.Application)
                return;
            bool isAutoUpgrade = App.LaunchSettings.UpgradeFlag.Active
                || Paths.Process.StartsWith(Path.Combine(Paths.Base, "Updates"))
                || Paths.Process.StartsWith(Path.Combine(Paths.LocalAppData, "Temp"))
                || Paths.Process.StartsWith(Paths.TempUpdates);
            var existingVer = FileVersionInfo.GetVersionInfo(Paths.Application).ProductVersion;
            var currentVer = FileVersionInfo.GetVersionInfo(Paths.Process).ProductVersion;
            if (MD5Hash.FromFile(Paths.Process) == MD5Hash.FromFile(Paths.Application))
                return;
            if (currentVer is not null && existingVer is not null)
            {
                if (Utilities.CompareVersions(currentVer, existingVer) == VersionComparison.LessThan)
                {
                    var result = Frontend.ShowMessageBox(
                        Strings.InstallChecker_VersionLessThanInstalled,
                        MessageBoxImage.Question,
                        MessageBoxButton.YesNo
                    );
                    if (result != MessageBoxResult.Yes)
                        return;
                }
            }
            if (!isAutoUpgrade)
            {
                var result = Frontend.ShowMessageBox(
                    Strings.InstallChecker_VersionDifferentThanInstalled,
                    MessageBoxImage.Question,
                    MessageBoxButton.YesNo
                );
                if (result != MessageBoxResult.Yes)
                    return;
            }
            App.Logger.WriteLine(LOG_IDENT, "Doing upgrade");
            Filesystem.AssertReadOnly(Paths.Application);
            using (var ipl = new InterProcessLock("AutoUpdater", TimeSpan.FromSeconds(5)))
            {
                if (!ipl.IsAcquired)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Failed to update! (Could not obtain singleton mutex)");
                    return;
                }
            }
            for (int i = 1; i <= 10; i++)
            {
                try
                {
                    File.Copy(Paths.Process, Paths.Application, true);
                    break;
                }
                catch (Exception ex)
                {
                    if (i == 1)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Waiting for write permissions to update version");
                    }
                    else if (i == 10)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to update! (Could not get write permissions after 10 tries/5 seconds)");
                        App.Logger.WriteException(LOG_IDENT, ex);
                        return;
                    }
                    Task.Delay(500).GetAwaiter().GetResult();
                }
            }
            using (var uninstallKey = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
            {
                uninstallKey.SetValueSafe("DisplayVersion", App.Version);
                uninstallKey.SetValueSafe("Publisher", App.ProjectOwner);
                uninstallKey.SetValueSafe("HelpLink", App.ProjectHelpLink);
                uninstallKey.SetValueSafe("URLInfoAbout", App.ProjectSupportLink);
                uninstallKey.SetValueSafe("URLUpdateInfo", App.ProjectDownloadLink);
            }
            if (existingVer is not null)
            {
                if (Utilities.CompareVersions(existingVer, "1.4.0.0") == VersionComparison.LessThan)
                {
                    JsonManager<RobloxState> legacyRobloxState = new();
                    if (legacyRobloxState.IsSaved)
                    {
                        if (legacyRobloxState.Load(false))
                        {
                            App.PlayerState.Prop.VersionGuid = legacyRobloxState.Prop.Player.VersionGuid;
                            App.PlayerState.Prop.PackageHashes = legacyRobloxState.Prop.Player.PackageHashes;
                            App.PlayerState.Prop.Size = legacyRobloxState.Prop.Player.Size;
                            App.StudioState.Prop.VersionGuid = legacyRobloxState.Prop.Studio.VersionGuid;
                            App.StudioState.Prop.PackageHashes = legacyRobloxState.Prop.Studio.PackageHashes;
                            App.StudioState.Prop.Size = legacyRobloxState.Prop.Studio.Size;
                        }
                        legacyRobloxState.Delete();
                    }
                }
                if (Utilities.CompareVersions(existingVer, "1.4.2") == VersionComparison.LessThan)
                {
                    string clientSettingsPath = Path.Combine(Paths.Modifications, "ClientSettings");
                    string migrationPath = Path.Combine(Paths.Modifications, "Migration from 1.4.1.0");
                    string genCacheDir = Path.Combine(Path.GetTempPath(), "Froststrap", "mod-generator");
                    string pluginCacheDir = Path.Combine(Paths.Roblox, "Plugins", "FroststrapStudioRPC.rbxmx");
                    string targetSettingsPath = Path.Combine(Paths.Base, "ClientSettings");
                    if (Directory.Exists(clientSettingsPath))
                    {
                        if (Directory.Exists(targetSettingsPath))
                            Directory.Delete(targetSettingsPath, true);
                        Directory.Move(clientSettingsPath, targetSettingsPath);
                    }
                    Directory.CreateDirectory(migrationPath);
                    var directoryInfo = new DirectoryInfo(Paths.Modifications);
                    foreach (FileSystemInfo info in directoryInfo.GetFileSystemInfos())
                    {
                        if (info.FullName == migrationPath)
                            continue;
                        string destPath = Path.Combine(migrationPath, info.Name);
                        try
                        {
                            if (info.Attributes.HasFlag(FileAttributes.Directory))
                            {
                                if (Directory.Exists(destPath)) Directory.Delete(destPath, true);
                                Directory.Move(info.FullName, destPath);
                            }
                            else
                            {
                                if (File.Exists(destPath)) File.Delete(destPath);
                                File.Move(info.FullName, destPath);
                            }
                        }
                        catch (IOException ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Could not migrate {info.Name}: {ex.Message}");
                        }
                    }
                    if (Directory.Exists(genCacheDir))
                    {
                        Directory.Delete(genCacheDir, true);
                        App.Logger.WriteLine(LOG_IDENT, "Deleted mod-generator cache for migration.");
                    }
                    if (Directory.Exists(pluginCacheDir))
                    {
                        Directory.Delete(pluginCacheDir, true);
                        App.Logger.WriteLine(LOG_IDENT, "Deleted studio plugin for migration.");
                    }
                    if (File.Exists(Path.Combine(Paths.Cache, "channelCache.json"))) File.Delete(Path.Combine(Paths.Cache, "channelCache.json"));
                    if (File.Exists(Path.Combine(Paths.Cache, "channelCacheMeta.json"))) File.Delete(Path.Combine(Path.Combine(Paths.Cache, "channelCacheMeta.json")));
                    if (File.Exists(Path.Combine(Paths.Cache, "datacenters_cache.json"))) File.Delete(Path.Combine(Path.Combine(Paths.Cache, "datacenters_cache.json")));
                    if (File.Exists(Path.Combine(Paths.Roblox, "LocalStorage", "RobloxCookies.dat"))) File.Delete(Path.Combine(Path.Combine(Paths.Roblox, "LocalStorage", "RobloxCookies.dat")));
                }
                App.Settings.Save();
                App.FastFlags.Save();
                App.State.Save();
                if (App.PlayerState.Loaded)
                    App.PlayerState.Save();
                if (App.StudioState.Loaded)
                    App.StudioState.Save();
            }
            if (currentVer is null)
                return;
            if (!isAutoUpgrade)
            {
                Frontend.ShowMessageBox(
                    string.Format(Strings.InstallChecker_Updated, currentVer),
                    MessageBoxImage.Information,
                    MessageBoxButton.OK
                );
            }
        }
        public void ImportSettingsFromSelectedApp()
        {
            if (ImportSource == ImportSettingsFrom.None)
            {
                string settingsPath = Path.Combine(InstallLocation, "Settings.json");
                if (File.Exists(settingsPath))
                    App.Settings.Load(false);
                return;
            }
            string sourceDir = Path.Combine(Paths.LocalAppData, ImportSource.ToString());
            if (!Directory.Exists(sourceDir))
            {
                Frontend.ShowMessageBox(Strings.Installer_InstallationNotFound, MessageBoxImage.Exclamation);
                return;
            }
            foreach (string fileName in FilesForImporting)
            {
                string sourcePath = Path.Combine(sourceDir, fileName);
                string destinationPath = Path.Combine(InstallLocation, fileName);
                if (fileName.Equals("Modifications", StringComparison.OrdinalIgnoreCase))
                {
                    destinationPath = Path.Combine(InstallLocation, "Modifications", $"{ImportSource} Mods");
                }
                try
                {
                    if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                    {
                        App.Logger.WriteLine("Installer::ImportSettings", $"Source path does not exist: {sourcePath}");
                        continue;
                    }
                    FileAttributes attr = File.GetAttributes(sourcePath);
                    bool isDirectory = attr.HasFlag(FileAttributes.Directory);
                    if (isDirectory)
                    {
                        Directory.CreateDirectory(destinationPath);
                        CopyDirectoryContents(sourcePath, destinationPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                        File.Copy(sourcePath, destinationPath, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("Installer::ImportSettings", $"Failed to import '{fileName}': {ex.Message}");
                }
            }
        }
        private void CopyDirectoryContents(string sourceDir, string destDir)
        {
            foreach (var directoryPath in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string targetDir = directoryPath.Replace(sourceDir, destDir);
                Directory.CreateDirectory(targetDir);
            }
            foreach (var filePath in Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                string targetFile = filePath.Replace(sourceDir, destDir);
                File.Copy(filePath, targetFile, overwrite: true);
            }
        }
    }
}
