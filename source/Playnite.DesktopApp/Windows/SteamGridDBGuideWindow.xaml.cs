using Playnite.Common;
using Playnite.Controls;
using Playnite.Windows;
using System.Windows;
using System.Windows.Navigation;

namespace Playnite.DesktopApp.Windows
{
    public class SteamGridDBGuideWindowFactory : WindowFactory
    {
        public override WindowBase CreateNewWindowInstance()
        {
            return new SteamGridDBGuideWindow();
        }
    }

    /// <summary>
    /// Interaction logic for SteamGridDBGuideWindow.xaml
    /// </summary>
    public partial class SteamGridDBGuideWindow : WindowBase
    {
        public SteamGridDBGuideWindow() : base()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            ProcessStarter.StartUrl(e.Uri.AbsoluteUri);
            e.Handled = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
