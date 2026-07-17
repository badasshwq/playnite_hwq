using Playnite.Common;
using Playnite.DesktopApp.ViewModels;
using Playnite.SDK;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Playnite.DesktopApp.Controls
{
    // 游戏列表空白处的右键菜单：直接平铺“添加游戏”那一组（手动/自动扫描/模拟端/MS Store），
    // 复用 MainMenu 里的同一批命令，行为与主菜单一致。落在游戏条目上的右键由 GameMenu 接管，二者不冲突。
    public class AddGameMenu : ContextMenu
    {
        private readonly DesktopAppViewModel model;

        static AddGameMenu()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(AddGameMenu), new FrameworkPropertyMetadata(typeof(AddGameMenu)));
        }

        public AddGameMenu() : this(DesktopApplication.Current?.MainModel)
        {
        }

        public AddGameMenu(DesktopAppViewModel model)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                this.model = DesignMainViewModel.DesignIntance;
            }
            else if (model != null)
            {
                this.model = model;
            }

            Opened += AddGameMenu_Opened;
        }

        private void AddGameMenu_Opened(object sender, RoutedEventArgs e)
        {
            InitializeItems();
        }

        public void InitializeItems()
        {
            Items.Clear();
            if (model == null)
            {
                return;
            }

            // 与主菜单一致的两级结构：先“添加游戏”，展开才是各来源
            var addGameItem = MainMenu.AddMenuChild(Items, "LOCMenuAddGame", null, null, "AddGameIcon");
            MainMenu.AddMenuChild(addGameItem.Items, "LOCMenuAddGameManual", model.AddCustomGameCommand);
            MainMenu.AddMenuChild(addGameItem.Items, "LOCMenuAddGameInstalled", model.AddInstalledGamesCommand);
            MainMenu.AddMenuChild(addGameItem.Items, "LOCMenuAddGameEmulated", model.AddEmulatedGamesCommand);
            if (Computer.WindowsVersion == WindowsVersion.Win10 || Computer.WindowsVersion == WindowsVersion.Win11)
            {
                MainMenu.AddMenuChild(addGameItem.Items, "LOCMenuAddWindowsStore", model.AddWindowsStoreGamesCommand);
            }
        }
    }
}
