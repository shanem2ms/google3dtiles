using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using System.IO;
using Vortice.DXCore;
using System.ComponentModel;
using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using static googletiles.GoogleTile;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Runtime;
using Veldrid;
using static System.Net.WebRequestMethods;
using System.Security.Cryptography.X509Certificates;
using Vortice.DXGI;
using System.Transactions;

namespace googletiles
{
    public class Tile : INotifyPropertyChanged
    {

        public Tile Parent;
        Bounds bounds;
        public Bounds Bounds => bounds;
        public GlbMesh mesh;
        public Vector3 Center => bounds.center;
        public Vector3 Scale => bounds.scale;
        public Vector3 Rx => bounds.rot[0];
        public Vector3 Ry => bounds.rot[1];
        public Vector3 Rz => bounds.rot[2];

        public Vector4 Color;

        public Matrix4x4 RotMat => bounds.rotMat;
        public float[] Transform { get; set; }

        public Tile[] ChildTiles { get; set; }

        public string GlbFile { get; set; }
        public string ChildJson { get; set; }

        public bool IsExpanded { get; set; } = false;

        public bool IsSelected { get; set; } = false;

        public bool IsInView { get; set; } = false;
        public int LastVisitedFrame { get; set; } = 0;
        public Vector3 MeshLoc => mesh?.translation ?? Vector3.Zero;

        public event PropertyChangedEventHandler? PropertyChanged;
        static int tileCount = 0;
        int tileIdx = 0;
        int level;
        bool childrenDownloaded = false;

        public int Margin => level * 5;

        public void ToggleExpand()
        {
            IsExpanded = !IsExpanded;
        }
        public string Name => $"tile{tileIdx}";
        public Tile(GoogleTile.Node node, Tile parent)
        {
            Parent = parent;
            bounds = new Bounds(node.boundingVolume);
            tileIdx = tileCount++;
            Transform = node.transform;
            if (node.content != null)
            {
                if (node.content.uri.Contains(".json"))
                    ChildJson = node.content.UriNoQuery();
                else if (node.content.uri.Contains(".glb"))
                    GlbFile = node.content.UriNoQuery();
            }
            Random r = new Random();
            Color = new Vector4(r.NextSingle(), r.NextSingle(), r.NextSingle(), 1);
            level = parent != null ? parent.level + 1 : 0;

            if (node.children != null)
            {
                List<Tile> tiles = new List<Tile>();
                foreach (GoogleTile.Node n in node.children)
                {
                    Tile tile = new Tile(n, this);
                    tiles.Add(tile);
                }
                ChildTiles = tiles.ToArray();
            }
        }

        public void CollapseAll()
        {
            IsExpanded = false;
            if (ChildTiles == null)
                return;
            foreach (Tile childTile in ChildTiles)
            {
                childTile.CollapseAll();
            }
        }

        public void GetExpandedList(List<Tile> tiles)
        {
            tiles.Add(this);
            if (IsExpanded && ChildTiles != null)
            {
                foreach (Tile childTile in ChildTiles)
                {
                    childTile.GetExpandedList(tiles);
                }
            }
        }

        public void CollapseSameTiles()
        {
            while (ChildTiles != null && ChildTiles.Length == 1 &&
                ChildTiles[0].bounds.Equals(bounds))
            {
                if (ChildJson != null &&
                    ChildTiles[0].ChildJson != null)
                    Debugger.Break();
                if (ChildJson == null)
                {
                    childrenDownloaded = ChildTiles[0].childrenDownloaded;
                    ChildJson = ChildTiles[0].ChildJson;
                }
                ChildTiles = ChildTiles[0].ChildTiles;
            }
            if (ChildTiles == null)
                return;

            foreach (Tile tile in ChildTiles)
            {
                tile.CollapseSameTiles();
            }
        }

        public async Task<bool> DownloadGlb(string sessionkey)
        {
            Stream stream = await GoogleTile.GetContentStream(sessionkey, GlbFile);            
            mesh = new GlbMesh(Path.GetFileName(GlbFile), stream, this.bounds);
            return true;
        }

        public async Task<bool> DownloadChildren(string sessionkey, CameraView cv, int frameIdx)
        {
            LastVisitedFrame = frameIdx;
            IsInView = bounds.IsInView(cv);
            if (!IsInView)
                return false;

            float span = bounds.GetScreenSpan(cv.ViewProj);

            if (span < 5)
                return false;

            List<Task<bool>> allTasks = new List<Task<bool>>();
            if (!this.childrenDownloaded)
            {
                if (GlbFile != null && mesh == null)
                {
                    allTasks.Add(DownloadGlb(sessionkey));
                }

                this.childrenDownloaded = true;
                List<Tile> tiles = new List<Tile>();
                if (ChildJson != null)
                {
                    if (ChildTiles?.Length > 0)
                        Debugger.Break();
                    GoogleTile tile = await GoogleTile.CreateFromUri(ChildJson, sessionkey);
                    Tile t = new Tile(tile.root, this);
                    tiles.Add(t);
                    ChildTiles = tiles.ToArray();
                }
            }

            if (ChildTiles != null)
            {
                foreach (Tile tile in ChildTiles)
                {
                    allTasks.Add(
                        tile.DownloadChildren(sessionkey, cv, frameIdx));
                }
            }
            await Task.WhenAll(allTasks);
            return true;
        }

        public bool FindIntersection(Vector3 pos, Vector3 dir, out float ot, out Tile outTile)
        {
            outTile = null;
            float t = float.PositiveInfinity;
            bool childDrawn = false;
            if (ChildTiles != null)
            {
                foreach (Tile childTile in ChildTiles)
                {
                    childDrawn |= childTile.FindIntersection(pos, dir, out float tt, out Tile intersectedTile);
                    if (tt < t)
                    {
                        outTile = intersectedTile;
                        t = tt;
                    }
                }
            }

            if (!childDrawn && GlbFile != null 
                    && Bounds.Intersect(pos, dir, out float _t))
            {
                if (_t < t)
                {
                    t = _t;
                    outTile = this;
                }
            }

            ot = t;

            return GlbFile != null || childDrawn;
        }
    }

    public class GlbMesh
    {
        int[] triangleList;
        float[] ptList;
        nint imageBuf;
        public DeviceBuffer _vertexBuffer;
        public DeviceBuffer _indexBuffer;
        private Texture _surfaceTexture;
        private TextureView _surfaceTextureView;
        public DeviceBuffer _worldBuffer;
        public ResourceSet _worldTextureSet;
        public Matrix4x4 worldMat;

        public int triangleCnt => triangleList.Length / 3;
        public Vector3 translation;

        [DllImport("libglb.dll")]
        static extern IntPtr LoadMesh(nint srcMem, uint size);

        [DllImport("libglb.dll")]
        static extern uint FaceCount(nint pmesh);

        [DllImport("libglb.dll")]
        static extern bool GetFaces(nint pmesh, nint pfaces, uint bufsize);

        [DllImport("libglb.dll")]
        static extern uint PtCount(nint pmesh);

        [DllImport("libglb.dll")]
        static extern bool GetPoints(nint pmesh, nint ppoints, uint bufsize, nint ptranslate);

        [DllImport("libglb.dll")]
        static extern bool GetTexture(nint pmesh, nint ptexture, uint bufsize);

        [DllImport("libglb.dll")]
        static extern void FreeMesh(nint pmesh);
        public GlbMesh(string name, Stream stream, Bounds b)
        {
            byte[] buf;
            var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            buf = memoryStream.ToArray();
            memoryStream.Seek(0, SeekOrigin.Begin);
            nint nativeBuf = Marshal.AllocHGlobal(buf.Length);
            Marshal.Copy(buf, 0, nativeBuf, buf.Length);
            nint meshptr = LoadMesh(nativeBuf, (uint)buf.Length);
            //System.IO.File.WriteAllBytes(name, buf);

            Marshal.FreeHGlobal(nativeBuf);
            {
                uint nfaces = FaceCount(meshptr);
                nint faceBuf = Marshal.AllocHGlobal((int)nfaces * 3 * sizeof(uint));
                bool success = GetFaces(meshptr, faceBuf, nfaces * 3 * sizeof(uint));
                triangleList = new int[nfaces * 3];
                Marshal.Copy(faceBuf, triangleList, 0, (int)nfaces * 3);
                Marshal.FreeHGlobal(faceBuf);
            }
            {
                uint npoints = PtCount(meshptr);
                nint ptBuf = Marshal.AllocHGlobal((int)npoints * 5 * sizeof(float));
                nint ptranslate = Marshal.AllocHGlobal(3 * sizeof(float));
                bool success = GetPoints(meshptr, ptBuf, npoints * 5 * sizeof(float), ptranslate);
                ptList = new float[npoints * 5];
                Marshal.Copy(ptBuf, ptList, 0, (int)npoints * 5);
                translation = Marshal.PtrToStructure<Vector3>(ptranslate);
                Marshal.FreeHGlobal(ptBuf);
                Marshal.FreeHGlobal(ptranslate);
            }
            {
                imageBuf = Marshal.AllocHGlobal(256 * 256 * 4);
                bool success = GetTexture(meshptr, imageBuf, 256 * 256 * 4);
            }
            FreeMesh(meshptr);
            /*
            float t = translation.Y;
            translation.Y = translation.Z;
            translation.Z = t;
            */
            Matrix4x4 zUp = new Matrix4x4(
                1.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 1.0f, 0.0f,
                0.0f, -1.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f);
            Vector3 t = Vector3.Transform(translation, zUp);
            translation = t;
            //Debug.WriteLine(name);
            //Debug.WriteLine($"c - {t}");
            /*
            float vx = Vector3.Dot(t - b.center, b.rot[0]);
            if (MathF.Abs(vx) > b.scale[0])
                Debugger.Break();
            float vy = Vector3.Dot(t - b.center, b.rot[1]);
            if (MathF.Abs(vy) > b.scale[1])
                Debugger.Break();
            float vz = Vector3.Dot(t - b.center, b.rot[2]);
            if (MathF.Abs(vz) > b.scale[2])
                Debugger.Break();
            
            Debug.WriteLine($"c - {b.center}");
            Debug.WriteLine($"m - {t}");
            Debug.WriteLine($"l - {(t - b.center).Length()}");
            Debug.WriteLine($"s - {b.scale.Length()}");
            */
            for (int i = 0; i < ptList.Length; i += 5)
            {
                Vector3 pt = new Vector3(ptList[i], ptList[i + 1], ptList[i + 2]);
                pt = Vector3.Transform(pt, zUp);
                ptList[i] = pt.X;
                ptList[i+1] = pt.Y;
                ptList[i+2] = pt.Z;
            }
            /*
            for (int j = 0; j < 6; ++j)
            {
                if (b.quads[j].Side(t))
                    Debugger.Break();
            }*/
            /*
            for (int i = 0; i < ptList.Length; i+=5)
            {
                Vector3 pt = new Vector3(ptList[i], ptList[i+1], ptList[i+2]);
                for (int j = 0; j < 6; ++j)
                {
                    if (b.quads[j].Side(pt + translation))
                        Debugger.Break();
                }
            }*/
            CreateIBVB();
        }
        void CreateIBVB()
        {
            ResourceFactory factory = VeldridComponent.Graphics.ResourceFactory;
            _worldBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            worldMat =
                Matrix4x4.CreateTranslation(translation);

            _vertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(ptList.Length * sizeof(float)), BufferUsage.VertexBuffer));
            VeldridComponent.Graphics.UpdateBuffer(_vertexBuffer, 0, ptList);

            _indexBuffer = factory.CreateBuffer(new BufferDescription(sizeof(int) * (uint)triangleList.Length, BufferUsage.IndexBuffer));
            VeldridComponent.Graphics.UpdateBuffer(_indexBuffer, 0, triangleList);

            _surfaceTexture = factory.CreateTexture(new TextureDescription(256, 256, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled, TextureType.Texture2D, 0));
            VeldridComponent.Graphics.UpdateTexture(_surfaceTexture, imageBuf, 256 * 256 * 4, 0, 0, 0, 256, 256, 1, 0, 0);
            Marshal.FreeHGlobal(imageBuf);
            _surfaceTextureView = factory.CreateTextureView(_surfaceTexture);

            ResourceLayout worldTextureLayout = factory.CreateResourceLayout(
                            new ResourceLayoutDescription(
                                new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                                new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                                new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));


            _worldTextureSet = factory.CreateResourceSet(new ResourceSetDescription(
                worldTextureLayout,
                _worldBuffer,
                _surfaceTextureView,
                VeldridComponent.Graphics.Aniso4xSampler));

        }
    }
}

