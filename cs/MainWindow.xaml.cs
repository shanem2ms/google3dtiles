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

        public List<Tile> Tiles { get; private set; } = null;

        Tile root;
        EarthViz earthViz;
        CameraView cameraView;
        public MainWindow()
        {
            this.DataContext = this;
            InitializeComponent();
            
            DoDownload();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public string sessionkey;

        async Task<bool> DoDownload()
        {
            GoogleTile rootTile = await
                GoogleTile.CreateFromUri("/v1/3dtiles/root.json", string.Empty);
            sessionkey = rootTile.GetSession();
            root = new Tile(rootTile.root, 0);
            cameraView = new CameraView();
            earthViz = new EarthViz(root);
            veldridRenderer.earthViz = earthViz;
            veldridRenderer.cameraView = cameraView;
            var result = await root.DownloadChildren(sessionkey, cameraView);
            RefreshTiles();
            return true;
        }

        void RefreshTiles()
        {
            Tiles = new List<Tile>();
            //root.CollapseSameTiles();
            root.GetExpandedList(Tiles);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tiles)));
        }
        private void Expand_Click(object sender, RoutedEventArgs e)
        {
            Tile t = (sender as Button).DataContext as Tile;
            ExpandTile(t);
        }

        async Task<bool> ExpandTile(Tile t)
        {
            t.ToggleExpand();
            await t.DownloadChildren(sessionkey, cameraView);            
            RefreshTiles();
            return true;
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            cameraView.OnKeyDown(e);
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            cameraView.OnKeyUp(e);
            base.OnKeyUp(e);
        }

        private void SelectView_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
