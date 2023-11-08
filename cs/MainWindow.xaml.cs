using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Numerics;
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
        BoundsViz boundsViz;
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
            //earthViz = new EarthViz(root);
            boundsViz = new BoundsViz(root);
            veldridRenderer.earthViz = earthViz;
            veldridRenderer.boundsViz = boundsViz;
            veldridRenderer.cameraView = cameraView;
            Matrix4x4 viewProj = cameraView.ViewMat * cameraView.ProjMat;
            var result = await root.DownloadChildren(sessionkey, viewProj);
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
            Matrix4x4 viewProj = cameraView.ProjMat * cameraView.ViewMat;
            await t.DownloadChildren(sessionkey, viewProj);            
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
            Matrix4x4 viewProj = cameraView.ProjMat * cameraView.ViewMat;
            root.DownloadChildren(sessionkey, viewProj).ContinueWith(t =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    RefreshTiles();
                });
            });
            
        }
    }
}
