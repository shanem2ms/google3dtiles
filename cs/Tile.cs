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

namespace googletiles
{
    public class Tile : INotifyPropertyChanged
    {

        Bounds bounds;
        public GlbMesh mesh;
        public Vector3 Center => bounds.center;
        public Vector3 Scale => bounds.scale;
        public Vector3 Rx => bounds.rot[0];
        public Vector3 Ry => bounds.rot[1];
        public Vector3 Rz => bounds.rot[2];

        public Matrix4x4 RotMat => bounds.rotMat;
        public float[] Transform { get; set; }

        public Tile[] ChildTiles { get; set; }

        public string GlbFile { get; set; }
        public string ChildJson { get; set; }

        public bool IsExpanded { get; set; } = false;

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
        public Tile(GoogleTile.Node node, int l)
        {
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
            
            level = l;

            if (node.children != null)
            {
                List<Tile> tiles = new List<Tile>();
                foreach (GoogleTile.Node n in node.children)
                {
                    Tile tile = new Tile(n, level + 1);
                    tiles.Add(tile);
                }
                ChildTiles = tiles.ToArray();
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
            mesh = new GlbMesh(stream);
            return true;
        }

        public async Task<bool> DownloadChildren(string sessionkey, CameraView view)
        {
            if (this.childrenDownloaded)
                return false;

            List<Task<bool>> allTasks = new List<Task<bool>>();
            if (GlbFile != null)
            {
                return await DownloadGlb(sessionkey);
            }

            this.childrenDownloaded = true;
            List<Tile> tiles = new List<Tile>();
            if (ChildJson != null)
            {
                if (ChildTiles?.Length > 0)
                    Debugger.Break();
                GoogleTile tile = await GoogleTile.CreateFromUri(ChildJson, sessionkey);
                Tile t = new Tile(tile.root, level + 1);
                tiles.Add(t);
                ChildTiles = tiles.ToArray();
            }
            
            if (ChildTiles != null)
            {
                foreach (Tile tile in ChildTiles)
                {
                    allTasks.Add(
                        tile.DownloadChildren(sessionkey, view));
                }
            }
            await Task.WhenAll(allTasks);
            return true;
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
        private DeviceBuffer _worldBuffer;
        public ResourceSet _worldTextureSet;
        Matrix4x4 worldMat;

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
        public GlbMesh(Stream stream)
        {
            byte[] buf;
            var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            buf = memoryStream.ToArray();
            memoryStream.Seek(0, SeekOrigin.Begin);
            nint nativeBuf = Marshal.AllocHGlobal(buf.Length);
            Marshal.Copy(buf, 0, nativeBuf, buf.Length);
            nint meshptr = LoadMesh(nativeBuf, (uint)buf.Length);

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
                imageBuf = Marshal.AllocHGlobal(256*256*4);
                bool success = GetTexture(meshptr, imageBuf, 256 * 256 * 4);
            }
            FreeMesh(meshptr);
            CreateIBVB();
        }
        void CreateIBVB()
        {
            ResourceFactory factory = VeldridComponent.Graphics.ResourceFactory;
            _worldBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            worldMat =
                Matrix4x4.CreateTranslation(translation);
            VeldridComponent.Graphics.UpdateBuffer(_worldBuffer, 0, ref worldMat);

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
    public class Bounds : IEquatable<Bounds>
    {
        public Vector3 center;
        public Vector3 scale;
        public Vector3[] rot;
        public Matrix4x4 rotMat;
        public Vector3[] pts;
        public Bounds(GoogleTile.BoundingVolume bv)
        {
            center = new Vector3(bv.box[0], bv.box[1], bv.box[2]);
            scale = new Vector3();
            rot = new Vector3[3];
            rotMat = Matrix4x4.Identity;
            Vector3[] scaledvecs = new Vector3[3]; 
            for (int i = 0; i < 3; ++i)
            {
                Vector3 vx = new Vector3(bv.box[3 + i * 3], bv.box[3 + i * 3 + 1], bv.box[3 + i * 3 + 2]);
                scale[i] = vx.Length();
                rot[i] = Vector3.Normalize(vx);
                rotMat[i, 0] = rot[i].X;
                rotMat[i, 1] = rot[i].Y;
                rotMat[i, 2] = rot[i].Z;
            }
            pts = new Vector3[8];
            Matrix4x4 worldMat =
                    Matrix4x4.CreateScale(scale) *
                    rotMat *
                    Matrix4x4.CreateTranslation(center);
            for (int i = 0; i < 8; ++i)
            {
                Vector3 pt = new Vector3((i & 1) != 0 ? -1 : 1,
                                ((i >> 1) & 1) != 0 ? -1 : 1,
                                ((i >> 2) & 1) != 0 ? -1 : 1);
                pts[i] = Vector3.Transform(pt, worldMat);
            }
        }

        public bool Equals(Bounds? other)
        {
            return center == other?.center &&
                scale == other?.scale;
        }
    }
}

