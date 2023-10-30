using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace googletiles
{
    
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {

        public List<Tile> Tiles { get; } 
        public MainWindow()
        {
            Tiles = new List<Tile>();
            this.DataContext = this;
            InitializeComponent();
            DoDownload();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        async Task<bool> DoDownload()
        {
            GoogleTile rootTile = await
                GoogleTile.CreateFromUri("/v1/3dtiles/root.json", string.Empty);
            string sessionkey = rootTile.GetSession();
            Tile root = new Tile(rootTile.root);
            Tiles.Add(root);
            await root.DownloadChildren(sessionkey);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tiles)));
            return true;
        }
    }
}
