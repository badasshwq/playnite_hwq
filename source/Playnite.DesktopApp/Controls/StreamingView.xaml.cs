using Playnite.DesktopApp.ViewModels;
using System.Windows.Controls;

namespace Playnite.DesktopApp.Controls
{
    /// <summary>
    /// Interaction logic for StreamingView.xaml
    /// </summary>
    public partial class StreamingView : UserControl
    {
        public StreamingView()
        {
            InitializeComponent();
        }

        public StreamingView(DesktopAppViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
