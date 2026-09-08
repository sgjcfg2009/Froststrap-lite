using Bloxstrap.UI.ViewModels.Settings;
namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class AppearancePage
    {
        public AppearanceViewModel ViewModel { get; } = new();
        public AppearancePage()
        {
            DataContext = ViewModel;
            InitializeComponent();
        }
    }
}
