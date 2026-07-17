using Playnite.DesktopApp.ViewModels;
using System.Windows.Controls;

namespace Playnite.DesktopApp.Controls
{
    /// <summary>
    /// Interaction logic for GameFoldersView.xaml
    /// </summary>
    public partial class GameFoldersView : UserControl
    {
        public GameFoldersView()
        {
            InitializeComponent();
        }

        public GameFoldersView(DesktopAppViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
