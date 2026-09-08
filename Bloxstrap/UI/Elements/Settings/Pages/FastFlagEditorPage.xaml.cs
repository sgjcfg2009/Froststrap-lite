using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Collections.ObjectModel;
using Wpf.Ui.Mvvm.Contracts;
using Bloxstrap.UI.Elements.Dialogs;
using Microsoft.Win32;
using System.Windows.Media.Animation;
using System.Windows.Input;
using Bloxstrap.UI.ViewModels.Dialogs;
using System.Windows.Media;
namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class FastFlagEditorPage
    {
        private readonly ObservableCollection<FastFlag> _fastFlagList = new();
        private bool _showPresets = true;
        private string _searchFilter = string.Empty;
        private string _lastSearch = string.Empty;
        private DateTime _lastSearchTime = DateTime.MinValue;
        private const int _debounceDelay = 70;
        private bool LoadShowPresetColumnSetting()
        {
            return App.Settings.Prop.ShowPresetColumn;
        }
        private void FastFlagEditorPage_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.Z)
                {
                    App.FastFlags.Undo();
                    ReloadList();
                    e.Handled = true;
                }
                else if (e.Key == Key.Y)
                {
                    App.FastFlags.Redo();
                    ReloadList();
                    e.Handled = true;
                }
            }
        }
        public FastFlagEditorPage()
        {
            AdvancedSettingViewModel.ShowPresetColumnChanged += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    PresetColumn.Visibility = LoadShowPresetColumnSetting()
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                });
            };
            AdvancedSettingViewModel.ShowFlagCountChanged += (_, _) =>
            {
                Dispatcher.Invoke(UpdateTotalFlagsCount);
            };
            InitializeComponent();
        }
        private const string CheckmarkIcon = "pack://application:,,,/Resources/Checkmark.ico";
        private const string CrossmarkIcon = "pack://application:,,,/Resources/CrossMark.ico";
        public void ReloadList()
        {
            PresetColumn.Visibility = LoadShowPresetColumnSetting() ? Visibility.Visible : Visibility.Collapsed;
            var items = new List<FastFlag>(App.FastFlags.Prop.Count);
            foreach (var pair in App.FastFlags.Prop.OrderBy(x => x.Key))
            {
                bool isPreset = App.FastFlags.IsPreset(pair.Key);
                if (!_showPresets && isPreset)
                    continue;
                if (!string.IsNullOrEmpty(_searchFilter) && !pair.Key.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                items.Add(new FastFlag
                {
                    Name = pair.Key,
                    Value = pair.Value?.ToString() ?? string.Empty,
                    Preset = isPreset ? CheckmarkIcon : CrossmarkIcon
                });
            }
            DataGrid.ItemsSource = null;
            _fastFlagList.Clear();
            foreach (var item in items)
                _fastFlagList.Add(item);
            DataGrid.ItemsSource = _fastFlagList;
            UpdateTotalFlagsCount();
        }
        public string FlagCountText => $"Total flags: {_fastFlagList.Count}";
        public void UpdateTotalFlagsCount()
        {
            int count = 0;
            if (DataGrid.ItemsSource is IEnumerable<FastFlag> flags)
                count = flags.Count();
            TotalFlagsTextBlock.Text = $"Total Flags: {count}";
            TotalFlagsTextBlock.Visibility = App.Settings.Prop.ShowFlagCount
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is INavigationWindow window)
                window.Navigate(typeof(FastFlagsPage));
        }
        private void ClearSearch(bool refresh = true)
        {
            SearchTextBox.Text = "";
            _searchFilter = "";
            if (refresh)
                ReloadList();
        }
        private async void ShowAddDialog()
        {
            var dialog = new AddFastFlagDialog();
            dialog.ShowDialog();
            if (dialog.Result != MessageBoxResult.OK)
                return;
            if (dialog.Tabs.SelectedIndex == 0)
                await AddSingle(dialog.FlagNameTextBox.Text.Trim(), dialog.FlagValueComboBox.Text);
            else if (dialog.Tabs.SelectedIndex == 1)
                await ImportJSON(dialog.JsonTextBox.Text);
        }
        private void AdvancedSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AdvancedSettingsDialog();
            dialog.Owner = Window.GetWindow(this);
            dialog.SettingsSaved += (_, _) =>
            {
                if (Window.GetWindow(this) is MainWindow mainWindow)
                {
                    mainWindow.SettingsSavedSnackbar.Show();
                }
            };
            dialog.ShowDialog();
        }
        private MainWindow GetMainWindow() => (MainWindow)Application.Current.MainWindow;
        public async void CleanListButton_Click(object sender, RoutedEventArgs e)
        {
            App.FastFlags.suspendUndoSnapshot = true;
            App.FastFlags.SaveUndoSnapshot();
            try
            {
                await App.RemoteData.WaitUntilDataFetched();
                var base64Flags = App.RemoteData.GetAllowedFastFlags();
                App.Logger.WriteLine("CleanList", $"Loaded {base64Flags.Count} allowed flags.");
                var allFlags = App.FastFlags.GetAllFlags();
                var invalidRemoved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var flag in allFlags)
                {
                    var name = flag.Name.Trim();
                    if (!base64Flags.Contains(name))
                    {
                        invalidRemoved[name] = flag.Value;
                        App.FastFlags.SetValue(name, null);
                    }
                }
                int totalChanges = invalidRemoved.Count;
                if (totalChanges == 0)
                {
                    Frontend.ShowMessageBox("No invalid FastFlags detected.", MessageBoxImage.Information);
                    return;
                }
                Frontend.ShowMessageBox($"{totalChanges} have been removed due to not being in allow list.",
                    MessageBoxImage.Information
                    );
                ReloadList();
                UpdateTotalFlagsCount();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox($"An error occurred during FastFlag cleanup: {ex.Message}", MessageBoxImage.Error);
            }
            finally
            {
                App.FastFlags.suspendUndoSnapshot = false;
            }
        }
        private void ShowProfilesDialog()
        {
            var dialog = new FlagProfilesDialog();
            dialog.ShowDialog();
            if (dialog.Result != MessageBoxResult.OK)
                return;
            ReloadList();
        }
        private async Task AddSingle(string name, string value)
        {
            double? val = null;
            if (!string.IsNullOrEmpty(value))
            {
                if (double.TryParse(value, out double parsed))
                    val = parsed;
            }
            FastFlag? entry;
            if (App.FastFlags.GetValue(name) is null)
            {
                entry = new FastFlag
                {
                    Name = name,
                    Value = value
                };
                if (!name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                    ClearSearch();
                App.FastFlags.SetValue(entry.Name, entry.Value);
                _fastFlagList.Add(entry);
            }
            else
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_AlreadyExists, MessageBoxImage.Information);
                bool refresh = false;
                if (!_showPresets && App.FastFlags.IsPreset(name))
                {
                    TogglePresetsButton.IsChecked = true;
                    _showPresets = true;
                    refresh = true;
                }
                if (!name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                {
                    ClearSearch(false);
                    refresh = true;
                }
                if (refresh)
                    ReloadList();
                entry = _fastFlagList.FirstOrDefault(x => x.Name == name);
            }
            await App.RemoteData.WaitUntilDataFetched();
            var base64Flags = App.RemoteData.GetAllowedFastFlags();
            if (!base64Flags.Contains(name))
            {
                if (Frontend.ShowMessageBox($"'{name}' is not in roblox allowlist and won't work.\n\nRemove it now?", MessageBoxImage.Warning, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    App.FastFlags.SetValue(name, null);
                    if (entry != null)
                    {
                        _fastFlagList.Remove(entry);
                    }
                    else
                    {
                        ReloadList();
                    }
                    UpdateTotalFlagsCount();
                    return;
                }
            }
            DataGrid.SelectedItem = entry;
            DataGrid.ScrollIntoView(entry);
            UpdateTotalFlagsCount();
        }
        private async Task ImportJSON(string json)
        {
            Dictionary<string, object>? list = null;
            json = json.Trim();
            if (!json.StartsWith('{'))
                json = '{' + json;
            if (!json.EndsWith('}'))
            {
                int lastIndex = json.LastIndexOf('}');
                if (lastIndex == -1)
                    json += '}';
                else
                    json = json.Substring(0, lastIndex + 1);
            }
            try
            {
                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                list = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options);
                if (list is null)
                    throw new Exception("JSON deserialization returned null");
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(
                    string.Format(Strings.Menu_FastFlagEditor_InvalidJSON, ex.Message),
                    MessageBoxImage.Error
                );
                ShowAddDialog();
                return;
            }
            App.FastFlags.suspendUndoSnapshot = true;
            App.FastFlags.SaveUndoSnapshot();
            var bannableFlagPairs = list
                .Select(kvp =>
                {
                    double? val = null;
                    if (kvp.Value != null)
                    {
                        if (kvp.Value is double d)
                            val = d;
                        else if (double.TryParse(kvp.Value.ToString(), out double parsed))
                            val = parsed;
                    }
                    return (Name: kvp.Key, Value: val);
                });
            var conflictingFlags = App.FastFlags.Prop.Where(x => list.ContainsKey(x.Key)).Select(x => x.Key);
            bool overwriteConflicting = false;
            if (conflictingFlags.Any())
            {
                int count = conflictingFlags.Count();
                string message = string.Format(
                    Strings.Menu_FastFlagEditor_ConflictingImport,
                    count,
                    string.Join(", ", conflictingFlags.Take(25))
                );
                if (count > 25)
                    message += "...";
                var result = Frontend.ShowMessageBox(message, MessageBoxImage.Question, MessageBoxButton.YesNo);
                overwriteConflicting = result == MessageBoxResult.Yes;
            }
            foreach (var pair in list)
            {
                if (App.FastFlags.Prop.ContainsKey(pair.Key) && !overwriteConflicting)
                    continue;
                if (pair.Value is null)
                    continue;
                var val = pair.Value.ToString();
                if (val is null)
                    continue;
                App.FastFlags.SetValue(pair.Key, val);
            }
            App.FastFlags.suspendUndoSnapshot = false;
            await App.RemoteData.WaitUntilDataFetched();
            var base64Flags = App.RemoteData.GetAllowedFastFlags();
            var invalidFlags = list.Keys.Where(flag => !base64Flags.Contains(flag)).ToList();
            if (invalidFlags.Any())
            {
                if (Frontend.ShowMessageBox($"{invalidFlags.Count} imported flags are not in the allowlist and won't work.\n\nRemove them now?", MessageBoxImage.Warning, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    foreach (var flagName in invalidFlags)
                    {
                        App.FastFlags.SetValue(flagName, null);
                    }
                }
            }
            ClearSearch();
        }
        private void Editor_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.All(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                {
                    DragOverlay.Visibility = Visibility.Visible;
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            DragOverlay.Visibility = Visibility.Collapsed;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
        private void Editor_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.All(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
        private void Editor_DragLeave(object sender, DragEventArgs e)
        {
            DragOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
        private async void Editor_Drop(object sender, DragEventArgs e)
        {
            DragOverlay.Visibility = Visibility.Collapsed;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var file in files)
            {
                if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                    !file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    Frontend.ShowMessageBox(
                        $"Invalid file type: {Path.GetFileName(file)}\nOnly .json and .txt are supported.",
                        MessageBoxImage.Error
                    );
                    continue;
                }
                try
                {
                    string content = File.ReadAllText(file);
                    await ImportJSON(content);
                }
                catch (Exception ex)
                {
                    Frontend.ShowMessageBox(
                        $"Failed to import \"{Path.GetFileName(file)}\": {ex.Message}",
                        MessageBoxImage.Error
                    );
                }
            }
            e.Handled = true;
        }
        private void Page_Loaded(object sender, RoutedEventArgs e) => ReloadList();
        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            App.FastFlags.suspendUndoSnapshot = true;
            App.FastFlags.SaveUndoSnapshot();
            if (e.Row.DataContext is not FastFlag entry)
                return;
            if (e.EditingElement is not TextBox textbox)
                return;
            switch (e.Column.Header)
            {
                case "Name":
                    string oldName = entry.Name;
                    string newName = textbox.Text;
                    if (newName == oldName)
                        break;
                    if (App.FastFlags.GetValue(newName) is not null)
                    {
                        Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_AlreadyExists, MessageBoxImage.Information);
                        e.Cancel = true;
                        textbox.Text = oldName;
                        break;
                    }
                    App.FastFlags.SetValue(oldName, null);
                    App.FastFlags.SetValue(newName, entry.Value);
                    if (!newName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                        ClearSearch();
                    entry.Name = newName;
                    break;
                case "Value":
                    string newValue = textbox.Text;
                    App.FastFlags.SetValue(entry.Name, newValue);
                    break;
            }
            App.FastFlags.suspendUndoSnapshot = false;
            UpdateTotalFlagsCount();
        }
        private void AddButton_Click(object sender, RoutedEventArgs e) => ShowAddDialog();
        private void FlagProfiles_Click(object sender, RoutedEventArgs e) => ShowProfilesDialog();
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            App.FastFlags.SaveUndoSnapshot();
            App.FastFlags.suspendUndoSnapshot = true;
            var tempList = new List<FastFlag>();
            foreach (FastFlag entry in DataGrid.SelectedItems)
                tempList.Add(entry);
            foreach (FastFlag entry in tempList)
            {
                _fastFlagList.Remove(entry);
                App.FastFlags.SetValue(entry.Name, null);
            }
            App.FastFlags.suspendUndoSnapshot = false;
            UpdateTotalFlagsCount();
        }
        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton button)
                return;
            DataGrid.Columns[0].Visibility = button.IsChecked ?? false ? Visibility.Visible : Visibility.Collapsed;
            _showPresets = button.IsChecked ?? true;
            ReloadList();
        }
        private void ExportJSONButton_Click(object sender, RoutedEventArgs e)
        {
            var flags = App.FastFlags.Prop;
            var groupedFlags = flags
                .GroupBy(kvp =>
                {
                    var match = Regex.Match(kvp.Key, @"^[A-Z]+[a-z]*");
                    return match.Success ? match.Value : "Other";
                })
                .OrderBy(g => g.Key);
            var formattedJson = new StringBuilder();
            formattedJson.AppendLine("{");
            int totalItems = flags.Count;
            int writtenItems = 0;
            int groupIndex = 0;
            foreach (var group in groupedFlags)
            {
                if (groupIndex > 0)
                    formattedJson.AppendLine();
                var sortedGroup = group
                    .OrderByDescending(kvp => kvp.Key.Length + (kvp.Value?.ToString()?.Length ?? 0));
                foreach (var kvp in sortedGroup)
                {
                    writtenItems++;
                    bool isLast = (writtenItems == totalItems);
                    string line = $"    \"{kvp.Key}\": \"{kvp.Value}\"";
                    if (!isLast)
                        line += ",";
                    formattedJson.AppendLine(line);
                }
                groupIndex++;
            }
            formattedJson.AppendLine("}");
            SaveJSONToFile(formattedJson.ToString());
        }
        private void CopyJSONButton_Click(object sender, RoutedEventArgs e)
        {
            var flags = App.FastFlags.Prop;
            var groupedFlags = flags
                .GroupBy(kvp =>
                {
                    var match = Regex.Match(kvp.Key, @"^[A-Z]+[a-z]*");
                    return match.Success ? match.Value : "Other";
                })
                .OrderBy(g => g.Key);
            var formattedJson = new StringBuilder();
            formattedJson.AppendLine("{");
            int totalItems = flags.Count;
            int writtenItems = 0;
            int groupIndex = 0;
            foreach (var group in groupedFlags)
            {
                if (groupIndex > 0)
                    formattedJson.AppendLine();
                var sortedGroup = group
                    .OrderByDescending(kvp => kvp.Key.Length + (kvp.Value?.ToString()?.Length ?? 0));
                foreach (var kvp in sortedGroup)
                {
                    writtenItems++;
                    bool isLast = (writtenItems == totalItems);
                    string line = $"    \"{kvp.Key}\": \"{kvp.Value}\"";
                    if (!isLast)
                        line += ",";
                    formattedJson.AppendLine(line);
                }
                groupIndex++;
            }
            formattedJson.AppendLine("}");
            Clipboard.SetText(formattedJson.ToString());
        }
        private void SaveJSONToFile(string json)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|Text files (*.txt)|*.txt",
                Title = "Save JSON or TXT File",
                FileName = "FroststrapExport.json"
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var filePath = saveFileDialog.FileName;
                    if (string.IsNullOrEmpty(Path.GetExtension(filePath)))
                    {
                        filePath += ".json";
                    }
                    File.WriteAllText(filePath, json);
                    Frontend.ShowMessageBox("JSON file saved successfully!", MessageBoxImage.Information);
                }
                catch (IOException ioEx)
                {
                    Frontend.ShowMessageBox($"Error saving file: {ioEx.Message}", MessageBoxImage.Error);
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    Frontend.ShowMessageBox($"Permission error: {uaEx.Message}", MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    Frontend.ShowMessageBox($"Unexpected error: {ex.Message}", MessageBoxImage.Error);
                }
            }
        }
        private void ShowDeleteAllFlagsConfirmation()
        {
            if (Frontend.ShowMessageBox(
                "Are you sure you want to delete all flags?",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return;  
            }
            if (!HasFlagsToDelete())
            {
                Frontend.ShowMessageBox(
                "There are no flags to delete.",
                MessageBoxImage.Information,
                MessageBoxButton.YesNo);
                return;
            }
            try
            {
                App.FastFlags.SaveUndoSnapshot();
                App.FastFlags.suspendUndoSnapshot = true;
                _fastFlagList.Clear();
                foreach (var key in App.FastFlags.Prop.Keys.ToList())
                {
                    App.FastFlags.SetValue(key, null);
                }
                App.FastFlags.suspendUndoSnapshot = false;
                ReloadList();
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
        }
        private bool HasFlagsToDelete()
        {
            return _fastFlagList.Any() || App.FastFlags.Prop.Any();
        }
        private void HandleError(Exception ex)
        {
            Frontend.ShowMessageBox($"An error occurred while deleting flags:\n{ex.Message}", MessageBoxImage.Error, MessageBoxButton.OK);
            LogError(ex);  
        }
        private void LogError(Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
        private void DeleteAllButton_Click(object sender, RoutedEventArgs e) => ShowDeleteAllFlagsConfirmation();
        private CancellationTokenSource? _searchCancellationTokenSource;
        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textbox) return;
            string newSearch = textbox.Text.Trim();
            if (newSearch == _lastSearch && (DateTime.Now - _lastSearchTime).TotalMilliseconds < _debounceDelay)
                return;
            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource = new CancellationTokenSource();
            _searchFilter = newSearch;
            _lastSearch = newSearch;
            _lastSearchTime = DateTime.Now;
            try
            {
                await Task.Delay(_debounceDelay, _searchCancellationTokenSource.Token);
                if (_searchCancellationTokenSource.Token.IsCancellationRequested)
                    return;
                Dispatcher.Invoke(() =>
                {
                    ReloadList();
                    ShowSearchSuggestion(newSearch);
                });
            }
            catch (TaskCanceledException)
            {
            }
        }
        private void ShowSearchSuggestion(string searchFilter)
        {
            if (string.IsNullOrWhiteSpace(searchFilter))
            {
                AnimateSuggestionVisibility(0);
                return;
            }
            string? bestMatch = null;
            int bestStartsWith = 1;
            int bestIndex = int.MaxValue;
            int bestLength = int.MaxValue;
            foreach (var flag in App.FastFlags.Prop.Keys)
            {
                int index = flag.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    continue;
                int startsWith = index == 0 ? 0 : 1;
                int length = flag.Length;
                if (bestMatch is null ||
                    startsWith < bestStartsWith ||
                    (startsWith == bestStartsWith && index < bestIndex) ||
                    (startsWith == bestStartsWith && index == bestIndex && length < bestLength))
                {
                    bestMatch = flag;
                    bestStartsWith = startsWith;
                    bestIndex = index;
                    bestLength = length;
                }
            }
            if (!string.IsNullOrEmpty(bestMatch))
            {
                SuggestionKeywordRun.Text = bestMatch;
                AnimateSuggestionVisibility(1);
            }
            else
            {
                AnimateSuggestionVisibility(0);
            }
        }
        private void SuggestionTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var suggestion = SuggestionKeywordRun.Text;
            if (!string.IsNullOrEmpty(suggestion))
            {
                SearchTextBox.Text = suggestion;
                SearchTextBox.CaretIndex = suggestion.Length;
            }
        }
        private void AnimateSuggestionVisibility(double targetOpacity)
        {
            var opacityAnimation = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            var translateAnimation = new DoubleAnimation
            {
                To = targetOpacity > 0 ? 0 : 10,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            opacityAnimation.Completed += (s, e) =>
            {
                if (targetOpacity == 0)
                {
                    SuggestionTextBlock.Visibility = Visibility.Collapsed;
                }
            };
            if (targetOpacity > 0)
                SuggestionTextBlock.Visibility = Visibility.Visible;
            SuggestionTextBlock.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            SuggestionTranslateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }
    }
}
