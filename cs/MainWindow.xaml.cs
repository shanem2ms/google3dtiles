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
using Veldrid;

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
        FrustumViz frustumViz;
        CameraView cameraView;
        bool earthVizInitialized = false;
        bool boundsVizInitialized = false;
        bool frustumVizInitialized = false;
        Tile intersectedTile = null;

        public Vector3 CameraPos => cameraView?.Pos ?? Vector3.Zero;
        public Vector3 CameraLook => cameraView?.LookDir ?? Vector3.Zero;

        public bool DownloadEnabled { get; set; } = true;
        public MainWindow()
        {
            this.DataContext = this;
            InitializeComponent();
            cameraView = new CameraView();
            earthViz = new EarthViz();
            boundsViz = new BoundsViz();
            frustumViz = new FrustumViz();
            veldridRenderer.cameraView = cameraView;
            GoogleTile.CreateFromUri("/v1/3dtiles/root.json", string.Empty).ContinueWith(t =>
            {
                GoogleTile rootTile = t.Result;
                sessionkey = rootTile.GetSession();
                root = new Tile(rootTile.root, null);
                RefreshTiles();
                veldridRenderer.OnRender = OnRender;
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public string sessionkey;

        int frameIdx = 1;
        void OnRender(CommandList _cl, GraphicsDevice _gd, Swapchain _sc)
        {
            cameraView?.Update();
            if (DownloadEnabled)
            {
                frameIdx++;
                root.DownloadChildren(sessionkey, cameraView, frameIdx);
            }
            float t;
            root.FindIntersection(cameraView.Pos, cameraView.LookDir, out t, out Tile _intersectedTile);
            this.intersectedTile = _intersectedTile;

            if (!float.IsInfinity(t)) { 
                cameraView.LookAtDist = t;
            }

            if (earthViz != null && !earthVizInitialized)
            {
                earthViz.CreateResources(_gd, _sc, _gd.ResourceFactory);
                earthVizInitialized = true;
            }
            if (boundsViz != null && !boundsVizInitialized)
            {
                boundsViz.CreateResources(_gd, _sc, _gd.ResourceFactory);
                boundsVizInitialized = true;
            }

            if (frustumViz != null && !frustumVizInitialized)
            {
                frustumViz.CreateResources(_gd, _sc, _gd.ResourceFactory);
                frustumVizInitialized = true;
            }
            if (earthViz != null)
                earthViz.Draw(_cl, cameraView, root, frameIdx);
            if (boundsViz != null)
                boundsViz.Draw(_cl, cameraView, root, frameIdx);
            if (frustumViz != null)
                frustumViz.Draw(_cl, cameraView);

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CameraLook)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CameraPos)));

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
            //Matrix4x4 viewProj = cameraView.ProjMat * cameraView.ViewMat;
            //await t.DownloadChildren(sessionkey, viewProj, frameIdx);            
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
            root.CollapseAll();
            Tile p = intersectedTile;
            while(p != null)
            {
                p.IsExpanded = true;
                p = p.Parent;
            }
            RefreshTiles();
        }

        private void TilesLB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var item in e.AddedItems)
            {
                (item as Tile).IsSelected = true;
            }

            foreach (var item in e.RemovedItems)
            {
                (item as Tile).IsSelected = false;
            }
        }
    }
}
