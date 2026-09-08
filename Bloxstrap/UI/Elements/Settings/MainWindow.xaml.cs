using Bloxstrap.UI.Elements.Settings.Pages;
using Bloxstrap.UI.ViewModels.Settings;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Common;
using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Mvvm.Contracts;
namespace Bloxstrap.UI.Elements.Settings
{
    public partial class MainWindow : INavigationWindow
    {
        private Models.Persistable.WindowState _state => App.State.Prop.SettingsWindow;
        private MainWindowViewModel viewModel = null!;
        public MainWindow(bool showAlreadyRunningWarning)
        {
            viewModel = new MainWindowViewModel();
            viewModel.RequestSaveNoticeEvent += (_, _) => SettingsSavedSnackbar.Show();
            viewModel.RequestCloseWindowEvent += (_, _) => Close();
            DataContext = viewModel;
            InitializeComponent();
            App.Logger.WriteLine("MainWindow", "Initializing settings window");
            if (showAlreadyRunningWarning)
                ShowAlreadyRunningSnackbar();
            LoadState();
            App.RemoteData.Subscribe((object? sender, EventArgs e) => {
                Dispatcher.Invoke(() =>
                {
                    RemoteDataBase Data = App.RemoteData.Prop;
                    AlertBar.Visibility = Data.AlertEnabled ? Visibility.Visible : Visibility.Collapsed;
                    AlertBar.Message = Data.AlertContent;
                    AlertBar.Severity = Data.AlertSeverity;
                });
            });
            RootNavigation.SelectedPageIndex = 0;
        }
        public void LoadState()
        {
            if (_state.Left > SystemParameters.VirtualScreenWidth)
                _state.Left = 0;
            if (_state.Top > SystemParameters.VirtualScreenHeight)
                _state.Top = 0;
            if (_state.Width > 0)
                this.Width = _state.Width;
            if (_state.Height > 0)
                this.Height = _state.Height;
            if (_state.Left > 0 && _state.Top > 0)
            {
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Left = _state.Left;
                this.Top = _state.Top;
            }
        }
        private async void ShowAlreadyRunningSnackbar()
        {
            await Task.Delay(100);
            AlreadyRunningSnackbar.Show();
        }
        public Frame GetFrame() => RootFrame;
        public INavigation GetNavigation() => RootNavigation;
        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);
        public void SetPageService(IPageService pageService) => RootNavigation.PageService = pageService;
        public void ShowWindow() => Show();
        public void CloseWindow() => Close();
        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            viewModel.SaveAndLaunch("player");
        }
        private void WpfUiWindow_Closing(object sender, CancelEventArgs e)
        {
            _state.Width = Width;
            _state.Height = Height;
            _state.Top = Top;
            _state.Left = Left;
            App.State.Save();
        }
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            App.Terminate();
        }
    }
}
