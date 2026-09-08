using Bloxstrap.UI.ViewModels.Settings;
namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class ShortcutsPage
    {
        public ShortcutsViewModel ViewModel { get; } = new();
        public ShortcutsPage()
        {
            DataContext = ViewModel;
            InitializeComponent();
        }
    }
}
