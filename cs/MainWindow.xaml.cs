using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
        bool saveVisibleGlb = false;
        public uint GlbCnt => Tile.GlbCnt;
        public uint JSONCnt => Tile.JSONCnt;

        public float TargetDist { get; set; }
        public int TargetTile { get; set; }
        public Vector3 CameraPos => cameraView?.Pos ?? Vector3.Zero;
        public Vector3 CameraLook => cameraView?.LookDir ?? Vector3.Zero;
        public string CameraRot
        {
            get
            {
                Quaternion q = cameraView?.ViewRot ?? Quaternion.Zero;
                return $"{q.X}f, {q.Y}f, {q.Z}f, {q.W}f";
            }
        }

        public bool DownloadEnabled { get; set; } = true;
        public MainWindow()
        {
            this.DataContext = this;
            InitializeComponent();
            cameraView = new CameraView();
            earthViz = new EarthViz();
            //boundsViz = new BoundsViz();
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
                bool saveVis = saveVisibleGlb;
                saveVisibleGlb = false;
                frameIdx++;
                root.DownloadChildren(sessionkey, cameraView, frameIdx, saveVis);
            }
            float t;
            root.FindIntersection(cameraView.Pos, cameraView.LookDir, out t, out Tile _intersectedTile);
            /*
            if (_intersectedTile != null && _intersectedTile.LastVisitedFrame < frameIdx)
            {
                Tile ptile = _intersectedTile;
                while (ptile != null)
                {
                    if (ptile.LastVisitedFrame == frameIdx && !ptile.IsInView)
                        break;
                    ptile = ptile.Parent;
                }
                if (ptile != null)
                {
                    bool isInView = ptile.Bounds.IsInView(cameraView);
                    Debug.WriteLine($"Bad tile {ptile.Idx}");
                }
                
            }*/
            TargetDist = t;
            TargetTile = this.intersectedTile?.Idx ?? -1;
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CameraRot)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetTile)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetDist)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GlbCnt)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JSONCnt)));
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
            if (e.Key == Key.P)
            {
                saveVisibleGlb = true;
            }
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
