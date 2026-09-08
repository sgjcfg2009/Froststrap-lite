using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Bloxstrap.UI.Elements.Bootstrapper;
using Bloxstrap.UI.Elements.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Mvvm.Contracts;
namespace Bloxstrap.UI
{
    static class Frontend
    {
        public static MessageBoxResult ShowMessageBox(string message, MessageBoxImage icon = MessageBoxImage.None, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            App.Logger.WriteLine("Frontend::ShowMessageBox", message);
            if (App.LaunchSettings.QuietFlag.Active)
                return defaultResult;
            return ShowFluentMessageBox(message, icon, buttons);
        }
        public static void ShowPlayerErrorDialog(bool crash = false)
        {
            if (App.LaunchSettings.QuietFlag.Active)
                return;
            string topLine = Strings.Dialog_PlayerError_FailedLaunch;
            if (crash)
                topLine = Strings.Dialog_PlayerError_Crash;
            string info = String.Format(
                Strings.Dialog_PlayerError_HelpInformation,
                $"https://github.com/{App.ProjectRepository}/wiki/Roblox-crashes-or-does-not-launch",
                $"https://github.com/{App.ProjectRepository}/wiki/Switching-between-Roblox-and-Bloxstrap"
            );
            ShowMessageBox($"{topLine}\n\n{info}", MessageBoxImage.Error);
        }
        public static void ShowExceptionDialog(Exception exception)
        {
            if (App.LaunchSettings.QuietFlag.Active)
                return;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                new ExceptionDialog(exception).ShowDialog();
            });
        }
        public static void ShowConnectivityDialog(string title, string description, MessageBoxImage image, Exception exception)
        {
            if (App.LaunchSettings.QuietFlag.Active)
                return;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                new ConnectivityDialog(title, description, image, exception).ShowDialog();
            });
        }
        public static IBootstrapperDialog GetBootstrapperDialog(BootstrapperStyle style)
        {
            return style switch
            {
                BootstrapperStyle.VistaDialog => new VistaDialog(),
                BootstrapperStyle.LegacyDialog2008 => new LegacyDialog2008(),
                BootstrapperStyle.LegacyDialog2011 => new LegacyDialog2011(),
                BootstrapperStyle.ProgressDialog => new ProgressDialog(),
                BootstrapperStyle.ClassicFluentDialog => new ClassicFluentDialog(),
                BootstrapperStyle.TwentyFiveDialog => new TwentyFiveDialog(),
                BootstrapperStyle.ByfronDialog => new ByfronDialog(),
                BootstrapperStyle.FroststrapDialog => new FroststrapDialog(),
                BootstrapperStyle.FluentDialog => new FluentDialog(false),
                BootstrapperStyle.FluentAeroDialog => new FluentDialog(true),
                _ => new FluentDialog(false)
            };
        }
        private static MessageBoxResult ShowFluentMessageBox(string message, MessageBoxImage icon, MessageBoxButton buttons)
        {
            return System.Windows.Application.Current.Dispatcher.Invoke(new Func<MessageBoxResult>(() =>
            {
                var messagebox = new FluentMessageBox(message, icon, buttons);
                messagebox.ShowDialog();
                return messagebox.Result;
            }));
        }
        public static void ShowBalloonTip(string title, string message, System.Windows.Forms.ToolTipIcon icon = System.Windows.Forms.ToolTipIcon.None, int timeout = 5)
        {
            var notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = Properties.Resources.IconBloxstrap,
                Text = App.ProjectName,
                Visible = true
            };
            notifyIcon.ShowBalloonTip(timeout, title, message, icon);
        }
    }
}
namespace Bloxstrap.UI.ViewModels.Bootstrapper
{
    public class BootstrapperDialogViewModel : NotifyPropertyChangedViewModel
    {
        private readonly IBootstrapperDialog _dialog;
        public ICommand CancelInstallCommand => new RelayCommand(CancelInstall);
        public string Title => App.Settings.Prop.BootstrapperTitle;
        public ImageSource Icon { get; set; } = App.Settings.Prop.BootstrapperIcon.GetIcon().GetImageSource();
        public string Message { get; set; } = "Please wait...";
        public bool ProgressIndeterminate { get; set; } = true;
        public int ProgressMaximum { get; set; } = 0;
        public int ProgressValue { get; set; } = 0;
        public TaskbarItemProgressState TaskbarProgressState { get; set; } = TaskbarItemProgressState.Indeterminate;
        public double TaskbarProgressValue { get; set; } = 0;
        public bool CancelEnabled { get; set; } = false;
        public Visibility CancelButtonVisibility => CancelEnabled ? Visibility.Visible : Visibility.Collapsed;
        [Obsolete("Do not use this! This is for the designer only.", true)]
        public BootstrapperDialogViewModel()
        {
            _dialog = null!;
        }
        public BootstrapperDialogViewModel(IBootstrapperDialog dialog)
        {
            _dialog = dialog;
        }
        private void CancelInstall()
        {
            _dialog.Bootstrapper?.Cancel();
            _dialog.CloseBootstrapper();
        }
    }
}
namespace Bloxstrap.UI.ViewModels.Bootstrapper
{
    public class ByfronDialogViewModel : BootstrapperDialogViewModel
    {
        public ImageSource ByfronLogoLocation { get; set; } = new BitmapImage(new Uri("pack://application:,,,/Resources/BootstrapperStyles/ByfronDialog/ByfronLogoDark.jpg"));
        public Thickness DialogBorder { get; set; } = new Thickness(0);
        public Brush Background { get; set; } = Brushes.Black;
        public Brush Foreground { get; set; } = new SolidColorBrush(Color.FromRgb(239, 239, 239));
        public Brush IconColor { get; set; } = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        public Brush ProgressBarBackground { get; set; } = new SolidColorBrush(Color.FromRgb(86, 86, 86));
        public Visibility VersionTextVisibility => CancelEnabled ? Visibility.Collapsed : Visibility.Visible;
        public string VersionText { get; init; }
        public ByfronDialogViewModel(IBootstrapperDialog dialog, string version) : base(dialog)
        {
            VersionText = version;
        }
    }
}
namespace Bloxstrap.UI.ViewModels.Bootstrapper
{
    public class ClassicFluentDialogViewModel : BootstrapperDialogViewModel
    {
        public double FooterOpacity => Environment.OSVersion.Version.Build >= 22000 ? 0.4 : 1;
        public ClassicFluentDialogViewModel(IBootstrapperDialog dialog) : base(dialog)
        {
        }
    }
}
namespace Bloxstrap.UI.ViewModels.Bootstrapper
{
    public class FluentDialogViewModel : BootstrapperDialogViewModel
    {
        public BackgroundType WindowBackdropType { get; set; } = BackgroundType.Mica;
        public SolidColorBrush BackgroundColourBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        public string VersionText { get; set; }
        public string ChannelText
        {
            get => _channelText;
            set
            {
                _channelText = value;
                OnPropertyChanged(nameof(ChannelText));
            }
        }
        private string _channelText = string.Empty;
        public FluentDialogViewModel(IBootstrapperDialog dialog, bool aero, string version) : base(dialog)
        {
            const int alpha = 128;
            WindowBackdropType = aero ? BackgroundType.Acrylic : BackgroundType.Mica;
            if (aero)
            {
                BackgroundColourBrush = App.Settings.Prop.Theme.GetFinal() == Enums.Theme.Light ?
                    new SolidColorBrush(Color.FromArgb(alpha, 225, 225, 225)) :
                    new SolidColorBrush(Color.FromArgb(alpha, 30, 30, 30));
            }
            VersionText = $"{Strings.Common_Version}: V{ExtractMajorVersion(version)}";
            ChannelText = $"{Strings.Common_Channel}: {Deployment.Channel}";
            Deployment.ChannelChanged += (_, newChannel) =>
            {
                ChannelText = $"{Strings.Common_Channel}: {newChannel}";
            };
        }
        private static string ExtractMajorVersion(string versionStr)
        {
            string[] parts = versionStr.Split('.');
            return (parts.Length >= 2) ? parts[1] : "???";
        }
    }
}
namespace Bloxstrap.UI.ViewModels.Bootstrapper
{
    public class FroststrapDialogViewModel : BootstrapperDialogViewModel
    {
        public BackgroundType WindowBackdropType { get; set; } = BackgroundType.Mica;
        public SolidColorBrush BackgroundColourBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        public FroststrapDialogViewModel(IBootstrapperDialog dialog) : base(dialog)
        {
        }
    }
}

namespace Bloxstrap.UI.ViewModels.Dialogs
{
    public class AdvancedSettingViewModel : NotifyPropertyChangedViewModel
    {
        public static event EventHandler? ShowPresetColumnChanged;
        public static event EventHandler? ShowFlagCountChanged;
        public bool ShowPresetColumnSetting
        {
            get => App.Settings.Prop.ShowPresetColumn;
            set
            {
                App.Settings.Prop.ShowPresetColumn = value;
                OnPropertyChanged(nameof(ShowPresetColumnSetting));
                ShowPresetColumnChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public bool ShowFlagCount
        {
            get => App.Settings.Prop.ShowFlagCount;
            set
            {
                App.Settings.Prop.ShowFlagCount = value;
                OnPropertyChanged(nameof(ShowFlagCount));
                ShowFlagCountChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public bool UseAltManually
        {
            get => App.Settings.Prop.UseAltManually;
            set
            {
                App.Settings.Prop.UseAltManually = value;
                OnPropertyChanged(nameof(UseAltManually));
            }
        }
    }
}
namespace Bloxstrap.UI.ViewModels.Dialogs
{
    internal class LanguageSelectorViewModel
    {
        public event EventHandler? CloseRequestEvent;
        public ICommand SetLocaleCommand => new RelayCommand(SetLocale);
        public static List<string> Languages => Locale.GetLanguages();
        public string SelectedLanguage { get; set; } = Locale.SupportedLocales.TryGetValue(App.Settings.Prop.Locale, out var name) ? name : Locale.SupportedLocales["nil"];
        private void SetLocale()
        {
            string identifier = Locale.GetIdentifierFromName(SelectedLanguage);
            Locale.Set(identifier);
            App.Settings.Prop.Locale = identifier;
            CloseRequestEvent?.Invoke(this, new());
        }
    }
}
namespace Bloxstrap.UI.ViewModels.Installer
{
    public class LaunchMenuViewModel
    {
        public string Version => string.Format(Strings.Menu_About_Version, App.Version);
        public ICommand LaunchSettingsCommand => new RelayCommand(LaunchSettings);
        public ICommand LaunchRobloxCommand => new RelayCommand(LaunchRoblox);
        public ICommand LaunchRobloxStudioCommand => new RelayCommand(LaunchRobloxStudio);
        public event EventHandler<NextAction>? CloseWindowRequest;
        private void LaunchSettings() => CloseWindowRequest?.Invoke(this, NextAction.LaunchSettings);
        private void LaunchRoblox() => CloseWindowRequest?.Invoke(this, NextAction.LaunchRoblox);
        private void LaunchRobloxStudio() => CloseWindowRequest?.Invoke(this, NextAction.LaunchRobloxStudio);
    }
}
namespace Bloxstrap.UI.ViewModels.Dialogs
{
    public class UninstallerViewModel
    {
        public string Text => String.Format(
            Strings.Uninstaller_Text, 
            "https://github.com/bloxstraplabs/bloxstrap/wiki/Roblox-crashes-or-does-not-launch",
            Paths.Base
        );
        public bool KeepData { get; set; } = true;
        public ICommand ConfirmUninstallCommand => new RelayCommand(ConfirmUninstall);
        public event EventHandler? ConfirmUninstallRequest;
        private void ConfirmUninstall() => ConfirmUninstallRequest?.Invoke(this, new EventArgs());
    }
}
namespace Bloxstrap.UI.ViewModels
{
    public static class GlobalViewModel
    {
        public static ICommand OpenWebpageCommand => new RelayCommand<string>(OpenWebpage);
        private static void OpenWebpage(string? location)
        {
            if (location is null)
                return;
            Utilities.ShellExecute(location);
        }
    }
}
namespace Bloxstrap.UI.ViewModels.Installer
{
    public class CompletionViewModel
    {
        public ICommand LaunchSettingsCommand => new RelayCommand(LaunchSettings);
        public ICommand LaunchRobloxCommand => new RelayCommand(LaunchRoblox);
        public event EventHandler<NextAction>? CloseWindowRequest;
        private void LaunchSettings() => CloseWindowRequest?.Invoke(this, NextAction.LaunchSettings);
        private void LaunchRoblox() => CloseWindowRequest?.Invoke(this, NextAction.LaunchRoblox);
    }
}
namespace Bloxstrap.UI.ViewModels.Installer
{
    public class InstallViewModel : NotifyPropertyChangedViewModel
    {
        private readonly Bloxstrap.Installer installer = new();
        private readonly string _originalInstallLocation;
        public event EventHandler<bool>? SetCanContinueEvent;
        public string InstallLocation
        {
            get => installer.InstallLocation;
            set
            {
                if (!string.IsNullOrEmpty(ErrorMessage))
                {
                    SetCanContinueEvent?.Invoke(this, true);
                    installer.InstallLocationError = "";
                    OnPropertyChanged(nameof(ErrorMessage));
                }
                installer.InstallLocation = value;
                OnPropertyChanged(nameof(InstallLocation));
                OnPropertyChanged(nameof(DataFoundMessageVisibility));
            }
        }
        private List<ImportSettingsFrom> _availableImportSources = new();
        public List<ImportSettingsFrom> AvailableImportSources
        {
            get => _availableImportSources;
            set
            {
                _availableImportSources = value;
                OnPropertyChanged(nameof(AvailableImportSources));
                OnPropertyChanged(nameof(ImportSettingsEnabled));
                OnPropertyChanged(nameof(ShowNotFound));  
            }
        }
        public bool ImportSettingsEnabled => AvailableImportSources.Count > 1;
        public bool ShowNotFound => AvailableImportSources.Count <= 1;
        public Visibility DataFoundMessageVisibility => installer.ExistingDataPresent ? Visibility.Visible : Visibility.Collapsed;
        public string ErrorMessage => installer.InstallLocationError;
        public bool CreateDesktopShortcuts
        {
            get => installer.CreateDesktopShortcuts;
            set => installer.CreateDesktopShortcuts = value;
        }
        public bool CreateStartMenuShortcuts
        {
            get => installer.CreateStartMenuShortcuts;
            set => installer.CreateStartMenuShortcuts = value;
        }
        public bool ImportSettings
        {
            get => installer.ImportSettings;
            set
            {
                installer.ImportSettings = value;
                OnPropertyChanged(nameof(ImportSettings));
                if (!value)
                {
                    installer.InstallLocationError = "";
                    SetCanContinueEvent?.Invoke(this, true);
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            }
        }
        public ICommand BrowseInstallLocationCommand => new RelayCommand(BrowseInstallLocation);
        public ICommand ResetInstallLocationCommand => new RelayCommand(ResetInstallLocation);
        public ICommand OpenFolderCommand => new RelayCommand(OpenFolder);
        public ImportSettingsFrom SelectedImportSource
        {
            get => installer.ImportSource;
            set
            {
                installer.ImportSource = value;
                OnPropertyChanged(nameof(SelectedImportSource));
            }
        }
        public InstallViewModel()
        {
            _originalInstallLocation = installer.InstallLocation;
            UpdateAvailableImportSources();
            OnPropertyChanged(nameof(SelectedImportSource));
        }
        private void UpdateAvailableImportSources()
        {
            var availableSources = new List<ImportSettingsFrom> { ImportSettingsFrom.None };
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (Directory.Exists(Path.Combine(localAppData, "Bloxstrap")))
                availableSources.Add(ImportSettingsFrom.Bloxstrap);
            if (Directory.Exists(Path.Combine(localAppData, "Fishstrap")))
                availableSources.Add(ImportSettingsFrom.Fishstrap);
            if (Directory.Exists(Path.Combine(localAppData, "Lunastrap")))
                availableSources.Add(ImportSettingsFrom.Lunastrap);
            if (Directory.Exists(Path.Combine(localAppData, "Luczystrap")))
                availableSources.Add(ImportSettingsFrom.Luczystrap);
            AvailableImportSources = availableSources;
            SelectedImportSource = ImportSettingsFrom.None;
        }
        public bool DoInstall()
        {
            if (!installer.CheckInstallLocation())
            {
                SetCanContinueEvent?.Invoke(this, false);
                OnPropertyChanged(nameof(ErrorMessage));
                return false;
            }
            installer.DoInstall();
            return true;
        }
        private void BrowseInstallLocation()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;
            InstallLocation = dialog.SelectedPath;
            OnPropertyChanged(nameof(InstallLocation));
        }
        private void ResetInstallLocation()
        {
            InstallLocation = _originalInstallLocation;
            OnPropertyChanged(nameof(InstallLocation));
        }
        private void OpenFolder() => Process.Start("explorer.exe", Paths.Base);
    }
}
namespace Bloxstrap.UI.ViewModels.Installer
{
    public class MainWindowViewModel : NotifyPropertyChangedViewModel
    {
        public string NextButtonText { get; private set; } = Strings.Common_Navigation_Next;
        public bool BackButtonEnabled { get; private set; } = false;
        public bool NextButtonEnabled { get; private set; } = false;
        public int ButtonWidth { get; } = Locale.CurrentCulture.Name.StartsWith("bg") ? 112 : 96;
        public ICommand BackPageCommand => new RelayCommand(BackPage);
        public ICommand NextPageCommand => new RelayCommand(NextPage);
        public ICommand CloseWindowCommand => new RelayCommand(CloseWindow);
        public event EventHandler<string>? PageRequest;
        public event EventHandler? CloseWindowRequest;
        public void SetButtonEnabled(string type, bool state)
        {
            if (type == "next")
            {
                NextButtonEnabled = state;
                OnPropertyChanged(nameof(NextButtonEnabled));
            }
            else if (type == "back")
            {
                BackButtonEnabled = state;
                OnPropertyChanged(nameof(BackButtonEnabled));
            }
        }
        public void SetNextButtonText(string text)
        {
            NextButtonText = text;
            OnPropertyChanged(nameof(NextButtonText));
        }
        private void BackPage() => PageRequest?.Invoke(this, "back");
        private void NextPage() => PageRequest?.Invoke(this, "next");
        private void CloseWindow() => CloseWindowRequest?.Invoke(this, new EventArgs());
    }
}
namespace Bloxstrap.UI.ViewModels.Installer
{
    public class WelcomeViewModel : NotifyPropertyChangedViewModel
    {
        public string MainText => String.Format(
            Strings.Installer_Welcome_MainText,
            "[github.com/Froststrap/Froststrap](https://github.com/Froststrap/Froststrap)"
        );
        public bool CanContinue { get; set; } = false;
    }
}
namespace Bloxstrap.UI.ViewModels
{
    public class NotifyPropertyChangedViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
namespace Bloxstrap.UI.ViewModels.Settings
{
    public class FastFlagsViewModel : NotifyPropertyChangedViewModel
    {
        private Dictionary<string, object>? _preResetFlags;
        public event EventHandler? RequestPageReloadEvent;
        public event EventHandler? OpenFlagEditorEvent;
        private void OpenFastFlagEditor() => OpenFlagEditorEvent?.Invoke(this, EventArgs.Empty);
        public ICommand OpenFastFlagEditorCommand => new RelayCommand(OpenFastFlagEditor);
        private static readonly string[] _grassPresets = { "Rendering.RemoveGrass1", "Rendering.RemoveGrass2", "Rendering.RemoveGrass3" };
        private static readonly string[] _lowPolyPresets = { "Rendering.LowPolyMeshes1", "Rendering.LowPolyMeshes2", "Rendering.LowPolyMeshes3", "Rendering.LowPolyMeshes4" };
        private static readonly int[] _lowPolyBaseValues = { 2000, 1500, 1000, 500 };
        public bool RemoveGrass
        {
            get => App.FastFlags?.GetPreset("Rendering.RemoveGrass1") == "0";
            set
            {
                foreach (var flag in _grassPresets)
                    App.FastFlags.SetPreset(flag, value ? "0" : null);
            }
        }
        public bool LowPolyMeshesEnabled
        {
            get => App.FastFlags.GetPreset("Rendering.LowPolyMeshes1") != null;
            set
            {
                if (value)
                {
                    LowPolyMeshesLevel = 5;
                }
                else
                {
                    foreach (var flag in _lowPolyPresets)
                        App.FastFlags.SetPreset(flag, null);
                }
                OnPropertyChanged(nameof(LowPolyMeshesEnabled));
            }
        }
        public int LowPolyMeshesLevel
        {
            get
            {
                if (int.TryParse(App.FastFlags.GetPreset("Rendering.LowPolyMeshes1"), out var storedValue))
                    return (storedValue * 9) / 2000;
                return 0;
            }
            set
            {
                int clamped = Math.Clamp(value, 0, 9);
                for (int i = 0; i < 4; i++)
                    App.FastFlags.SetPreset(_lowPolyPresets[i], ((_lowPolyBaseValues[i] * clamped) / 9).ToString());
                OnPropertyChanged(nameof(LowPolyMeshesLevel));
                OnPropertyChanged(nameof(LowPolyMeshesEnabled));
            }
        }
        public bool PauseVoxelizer
        {
            get => App.FastFlags.GetPreset("Rendering.PauseVoxerlizer") == "True";
            set => App.FastFlags.SetPreset("Rendering.PauseVoxerlizer", value ? "True" : null);
        }
        public bool GraySky
        {
            get => App.FastFlags.GetPreset("Graphic.GraySky") == "True";
            set => App.FastFlags.SetPreset("Graphic.GraySky", value ? "True" : null);
        }
        public bool UseFastFlagManager
        {
            get => App.Settings.Prop.UseFastFlagManager;
            set => App.Settings.Prop.UseFastFlagManager = value;
        }
        public IReadOnlyDictionary<MSAAMode, string?> MSAALevels => FastFlagManager.MSAAModes;
        public MSAAMode SelectedMSAALevel
        {
            get => MSAALevels.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("Rendering.MSAA1")).Key;
            set => App.FastFlags.SetPreset("Rendering.MSAA1", MSAALevels[value]);
        }
        public IReadOnlyDictionary<RenderingMode, string> RenderingModes => FastFlagManager.RenderingModes;
        public RenderingMode SelectedRenderingMode
        {
            get => App.FastFlags.GetPresetEnum(RenderingModes, "Rendering.Mode", "True");
            set
            {
                App.FastFlags.SetPresetEnum("Rendering.Mode", value.ToString(), "True");
                App.FastFlags.SetPreset("Rendering.Mode.DisableD3D11", (value == RenderingMode.Vulkan || value == RenderingMode.OpenGL) ? "True" : null);
            }
        }
        public bool FixDisplayScaling
        {
            get => App.FastFlags.GetPreset("Rendering.DisableScaling") == "True";
            set => App.FastFlags.SetPreset("Rendering.DisableScaling", value ? "True" : null);
        }
        public IReadOnlyDictionary<QualityLevel, string?> QualityLevels => FastFlagManager.QualityLevels;
        public QualityLevel SelectedQualityLevel
        {
            get => FastFlagManager.QualityLevels.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("Rendering.FrmQuality")).Key;
            set
            {
                if (value == QualityLevel.Disabled)
                {
                    App.FastFlags.SetPreset("Rendering.FrmQuality", null);
                }
                else
                {
                    App.FastFlags.SetPreset("Rendering.FrmQuality", FastFlagManager.QualityLevels[value]);
                }
            }
        }
        public bool GetFlagAsBool(string flagKey, string falseValue = "False")
        {
            return App.FastFlags.GetPreset(flagKey) != falseValue;
        }
        public void SetFlagFromBool(string flagKey, bool value, string falseValue = "False")
        {
            App.FastFlags.SetPreset(flagKey, value ? null : falseValue);
        }
        public bool ResetConfiguration
        {
            get => _preResetFlags is not null;
            set
            {
                if (value)
                {
                    _preResetFlags = new(App.FastFlags.Prop);
                    App.FastFlags.Prop.Clear();
                }
                else
                {
                    App.FastFlags.Prop = _preResetFlags!;
                    _preResetFlags = null;
                }
                RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
namespace Bloxstrap.UI.ViewModels.Settings
{
    public class MainWindowViewModel : NotifyPropertyChangedViewModel
    {
        public ICommand SaveSettingsCommand => new RelayCommand(SaveSettings);
        public ICommand SaveAndLaunchPlayerCommand => new RelayCommand(() => SaveAndLaunch("player"));
        public ICommand SaveAndLaunchStudioCommand => new RelayCommand(() => SaveAndLaunch("studio"));
        public ICommand RestartAppCommand => new RelayCommand(RestartApp);
        public ICommand CloseWindowCommand => new RelayCommand(CloseWindow);
        public EventHandler? RequestSaveNoticeEvent;
        public EventHandler? RequestCloseWindowEvent;
        public event EventHandler? SettingsSaved;
        public bool IsSidebarExpanded
        {
            get => App.Settings.Prop.IsNavigationSidebarExpanded;
            set => App.Settings.Prop.IsNavigationSidebarExpanded = value;
        }
        private void CloseWindow() => RequestCloseWindowEvent?.Invoke(this, EventArgs.Empty);
        public void SaveSettings()
        {
            const string LOG_IDENT = "MainWindowViewModel::SaveSettings";
            App.Settings.Save();
            App.State.Save();
            App.FastFlags.Save();
            foreach (var pair in App.PendingSettingTasks)
            {
                var task = pair.Value;
                if (task.Changed)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Executing pending task '{task}'");
                    task.Execute();
                }
            }
            App.PendingSettingTasks.Clear();
            RequestSaveNoticeEvent?.Invoke(this, EventArgs.Empty);
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        public void SaveAndLaunch(string mode)
        {
            SaveSettings();
            string targetExe = File.Exists(Paths.Application) ? Paths.Application : Paths.Process;
            Process.Start(new ProcessStartInfo
            {
                FileName = targetExe,
                Arguments = $"-{mode.ToLower()}",
                UseShellExecute = true
            });
        }
        public void RestartApp()
        {
            string targetExe = File.Exists(Paths.Application) ? Paths.Application : Paths.Process;
            Process.Start(new ProcessStartInfo
            {
                FileName = targetExe,
                Arguments = "-menu",
                UseShellExecute = true
            });
        }
    }
    public class AppearanceViewModel : NotifyPropertyChangedViewModel
    {
        public IReadOnlyCollection<Enums.Theme> Themes { get; } = new[]
        {
            Enums.Theme.Default,
            Enums.Theme.Dark,
            Enums.Theme.Light,
            Enums.Theme.Custom
        };
        public Enums.Theme SelectedTheme
        {
            get => App.Settings.Prop.Theme;
            set
            {
                App.Settings.Prop.Theme = value;
                OnPropertyChanged(nameof(SelectedTheme));
            }
        }
        public IReadOnlyCollection<Enums.BootstrapperStyle> BootstrapperStyles => Extensions.BootstrapperStyleEx.Selections;
        public Enums.BootstrapperStyle SelectedBootstrapperStyle
        {
            get => App.Settings.Prop.BootstrapperStyle;
            set
            {
                App.Settings.Prop.BootstrapperStyle = value;
                OnPropertyChanged(nameof(SelectedBootstrapperStyle));
            }
        }
        public IReadOnlyCollection<Enums.BootstrapperIcon> BootstrapperIcons => Extensions.BootstrapperIconEx.Selections;
        public Enums.BootstrapperIcon SelectedBootstrapperIcon
        {
            get => App.Settings.Prop.BootstrapperIcon;
            set
            {
                App.Settings.Prop.BootstrapperIcon = value;
                OnPropertyChanged(nameof(SelectedBootstrapperIcon));
            }
        }
        public string Title
        {
            get => App.Settings.Prop.BootstrapperTitle;
            set
            {
                App.Settings.Prop.BootstrapperTitle = value;
                OnPropertyChanged(nameof(Title));
            }
        }
        public IReadOnlyDictionary<string, string> Languages => Locale.SupportedLocales;
        public string SelectedLanguage
        {
            get => App.Settings.Prop.Locale;
            set
            {
                App.Settings.Prop.Locale = value;
                Locale.Set(value);
                OnPropertyChanged(nameof(SelectedLanguage));
            }
        }
    }
    public class ShortcutsViewModel : NotifyPropertyChangedViewModel
    {
        public ICommand CreatePlayerShortcutCommand => new RelayCommand(CreatePlayerShortcut);
        public ICommand CreateStudioShortcutCommand => new RelayCommand(CreateStudioShortcut);
        private void CreatePlayerShortcut()
        {
            string appPath = File.Exists(Paths.Application) ? Paths.Application : Paths.Process;
            string playerShortcutPath = Path.Combine(Paths.Desktop, "Play Roblox.lnk");
            if (File.Exists(playerShortcutPath))
                File.Delete(playerShortcutPath);
            global::Bloxstrap.Utility.Shortcut.Create(appPath, "-player", playerShortcutPath);
            Frontend.ShowMessageBox("Play Roblox shortcut created on Desktop!", MessageBoxImage.Information);
        }
        private void CreateStudioShortcut()
        {
            string appPath = File.Exists(Paths.Application) ? Paths.Application : Paths.Process;
            string studioShortcutPath = Path.Combine(Paths.Desktop, "Roblox Studio.lnk");
            if (File.Exists(studioShortcutPath))
                File.Delete(studioShortcutPath);
            global::Bloxstrap.Utility.Shortcut.Create(appPath, "-studio", studioShortcutPath);
            Frontend.ShowMessageBox("Roblox Studio shortcut created on Desktop!", MessageBoxImage.Information);
        }
    }
}
namespace Bloxstrap.UI.Elements.Base
{
    public abstract class WpfUiWindow : UiWindow
    {
        public WpfUiWindow()
        {
            ApplyTheme();
        }
        public virtual void ApplyTheme()
        {
            try
            {
                var theme = App.Settings.Prop.Theme;
                string themeFile = theme switch
                {
                    Enums.Theme.Light => "Light.xaml",
                    Enums.Theme.Custom => "Froststrap.xaml",
                    _ => "Dark.xaml"
                };
                var themeDictionary = new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/UI/Style/{themeFile}", UriKind.Absolute)
                };
                System.Windows.Application.Current.Resources.MergedDictionaries.Add(themeDictionary);
            }
            catch
            {
            }
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (App.Settings.Prop.WPFSoftwareRender || App.LaunchSettings.NoGPUFlag.Active)
            {
                if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource hwndSource)
                    hwndSource.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            }
        }
    }
}
namespace Bloxstrap.UI.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
namespace Bloxstrap.UI.Converters
{
    class EnumNameConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<Enum, string> _nameCache = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not Enum enumVal)
                return value?.ToString() ?? "Unknown";
            return _nameCache.GetOrAdd(enumVal, e =>
            {
                var stringVal = e.ToString();
                var type = e.GetType();
                var typeName = type.FullName;
                if (string.IsNullOrEmpty(typeName))
                    return stringVal;
                var memberInfo = type.GetMember(stringVal).FirstOrDefault();
                if (memberInfo != null)
                {
                    var attribute = memberInfo
                        .GetCustomAttributes(typeof(EnumNameAttribute), false)
                        .FirstOrDefault() as EnumNameAttribute;
                    if (attribute != null)
                    {
                        if (!string.IsNullOrEmpty(attribute.StaticName))
                            return attribute.StaticName;
                        if (!string.IsNullOrEmpty(attribute.FromTranslation))
                            return Strings.ResourceManager.GetStringSafe(attribute.FromTranslation);
                    }
                }
                var dotIndex = typeName.IndexOf('.');
                var trimmedTypeName = dotIndex >= 0 ? typeName.Substring(dotIndex + 1) : typeName;
                return Strings.ResourceManager.GetStringSafe($"{trimmedTypeName}.{stringVal}");
            });
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
namespace Bloxstrap.UI.Converters
{
    internal class RangeConverter : IValueConverter
    {
        public int? From { get; set; }
        public int? To { get; set; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int length;
            if (value is string str)
            {
                length = str.Length;
            }
            else if (value is int intVal)
            {
                length = intVal;
            }
            else
            {
                return false;
            }
            if (From is null)
                return To is null || length < To;
            if (To is null)
                return length > From;
            return length > From && length < To;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

namespace Bloxstrap.UI.Utility
{
    static class Rendering
    {
        public static double GetTextWidth(TextBlock textBlock)
        {
            return new FormattedText(
                textBlock.Text,
                CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
                textBlock.FontSize,
                Brushes.Black,
                new NumberSubstitution(),
                VisualTreeHelper.GetDpi(textBlock).PixelsPerDip
            ).Width;
        }
    }
}
namespace Bloxstrap.UI.Utility
{
    internal static class TaskbarProgress
    {
        private enum TaskbarStates
        {
            NoProgress = 0,
            Indeterminate = 0x1,
            Normal = 0x2,
            Error = 0x4,
            Paused = 0x8,
        }
        [ComImport()]
        [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            [PreserveSig]
            int HrInit();
            [PreserveSig]
            int AddTab(IntPtr hwnd);
            [PreserveSig]
            int DeleteTab(IntPtr hwnd);
            [PreserveSig]
            int ActivateTab(IntPtr hwnd);
            [PreserveSig]
            int SetActiveAlt(IntPtr hwnd);
            [PreserveSig]
            int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
            [PreserveSig]
            int SetProgressValue(IntPtr hwnd, UInt64 ullCompleted, UInt64 ullTotal);
            [PreserveSig]
            int SetProgressState(IntPtr hwnd, TaskbarStates state);
        }
        [ComImport()]
        [Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
        [ClassInterface(ClassInterfaceType.None)]
        private class TaskbarInstance
        {
        }
        private static Lazy<ITaskbarList3> _taskbarInstance = new Lazy<ITaskbarList3>(() => (ITaskbarList3)new TaskbarInstance());
        private static TaskbarStates ConvertEnum(TaskbarItemProgressState state)
        {
            return state switch
            {
                TaskbarItemProgressState.None => TaskbarStates.NoProgress,
                TaskbarItemProgressState.Indeterminate => TaskbarStates.Indeterminate,
                TaskbarItemProgressState.Normal => TaskbarStates.Normal,
                TaskbarItemProgressState.Error => TaskbarStates.Error,
                TaskbarItemProgressState.Paused => TaskbarStates.Paused,
                _ => throw new Exception($"Unrecognised TaskbarItemProgressState: {state}")
            };
        }
        private static int SetProgressState(IntPtr windowHandle, TaskbarStates taskbarState)
        {
            return _taskbarInstance.Value.SetProgressState(windowHandle, taskbarState);
        }
        public static int SetProgressState(IntPtr windowHandle, TaskbarItemProgressState taskbarState)
        {
            return SetProgressState(windowHandle, ConvertEnum(taskbarState));
        }
        public static int SetProgressValue(IntPtr windowHandle, int progressValue, int progressMax)
        {
            return _taskbarInstance.Value.SetProgressValue(windowHandle, (ulong)progressValue, (ulong)progressMax);
        }
    }
}
namespace Bloxstrap.UI.Utility
{
    public static class WindowScaling
    {
        public static double ScaleFactor
        {
            get
            {
                var screen = Screen.PrimaryScreen;
                if (screen == null) return 1.0;
                return screen.Bounds.Width / SystemParameters.PrimaryScreenWidth;
            }
        }
        public static int GetScaledNumber(int number)
        {
            return (int)Math.Ceiling(number * ScaleFactor);
        }
        public static System.Drawing.Size GetScaledSize(System.Drawing.Size size)
        {
            return new System.Drawing.Size(GetScaledNumber(size.Width), GetScaledNumber(size.Height));
        }
        public static System.Drawing.Point GetScaledPoint(System.Drawing.Point point)
        {
            return new System.Drawing.Point(GetScaledNumber(point.X), GetScaledNumber(point.Y));
        }
        public static Padding GetScaledPadding(Padding padding)
        {
            return new Padding(GetScaledNumber(padding.Left), GetScaledNumber(padding.Top), GetScaledNumber(padding.Right), GetScaledNumber(padding.Bottom));
        }
    }
}
namespace Bloxstrap.UI
{
    public interface IBootstrapperDialog
    {
        public Bootstrapper? Bootstrapper { get; set; }
        string Message { get; set; }
        ProgressBarStyle ProgressStyle { get; set; }
        int ProgressValue { get; set; }
        int ProgressMaximum { get; set; }
        TaskbarItemProgressState TaskbarProgressState { get; set; }
        double TaskbarProgressValue { get; set; }
        bool CancelEnabled { get; set; }
        void ShowBootstrapper();
        void CloseBootstrapper();
        void ShowSuccess(string message, Action? callback = null);
    }
}
