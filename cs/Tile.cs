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

        public bool IsInView { get; set; } = false;
        public int LastVisitedFrame { get; set; } = 0;

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
            mesh = new GlbMesh(stream);
            return true;
        }

        public async Task<bool> DownloadChildren(string sessionkey, Matrix4x4 viewProj, int frameIdx)
        {
            LastVisitedFrame = frameIdx;
            IsInView = bounds.IsInView(viewProj);
            if (!IsInView)
                return false;

            float span = bounds.GetScreenSpan(viewProj);

            if (span < 5)
                return false;

            List<Task<bool>> allTasks = new List<Task<bool>>();
            if (!this.childrenDownloaded)
            {
                if (GlbFile != null && mesh == null)
                {
                    //allTasks.Add(DownloadGlb(sessionkey));
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
                        tile.DownloadChildren(sessionkey, viewProj, frameIdx));
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
                imageBuf = Marshal.AllocHGlobal(256 * 256 * 4);
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

    public class Quad
    {
        Vector3[] pts;
        Vector3 u;
        Vector3 v;
        float du;
        float dv;
        Vector3 nrm;
        Vector3 center;
        public Quad(Vector3[] _pts )
        {
            pts = _pts;
            center = (pts[0] + pts[1] + pts[2] + pts[3]) * 0.25f;
            u = Vector3.Normalize(pts[2] - pts[1]);
            du = MathF.Abs(Vector3.Dot(pts[2] - center, u));
            v = Vector3.Normalize(pts[1] - pts[0]);
            dv = MathF.Abs(Vector3.Dot(pts[1] - center, v));
            nrm = Vector3.Cross(u, v);
            nrm = Vector3.Normalize(nrm);
        }

        public bool Intersect(Vector3 l0, Vector3 l, out float t)
        {
            // assuming vectors are all normalized
            float denom = Vector3.Dot(nrm, l);
            if (denom < 1e-6)
            {
                t = float.MaxValue;
                return false;
            }

            Vector3 p0l0 = center - l0;
            t = Vector3.Dot(p0l0, nrm) / denom;
            if (t < 0)
                return false;

            Vector3 ipt = l0 + l * t;
            float ddu = Vector3.Dot(ipt - center, u);
            float ddv = Vector3.Dot(ipt - center, v);
            if (MathF.Abs(ddu) < du && MathF.Abs(ddv) < dv)
                return true;
            return false;
        }
    }

    public class Bounds : IEquatable<Bounds>
    {
        static Vector3 GlobalScale = new Vector3(7972671, 7972671, 7945940.5f);
        public Vector3 center;
        public Vector3 scale;
        public Vector3[] rot;
        public Matrix4x4 rotMat;
        public Matrix4x4 worldMat;
        public Vector3[] pts;
        public Quad[] quads;
        bool IsGlobal => scale == GlobalScale;

        static int[][] PlaneIndices ={
            new int[]{ 0,1,3,2},
            new int[]{ 5,4,6,7},
            new int[]{ 0,2,6,4},
            new int[]{ 1,5,7,3},
            new int[]{ 0,4,5,1},
            new int[]{ 2,3,7,6}
        };
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

            worldMat =
                    Matrix4x4.CreateScale(scale * 2) *
                    rotMat *
                    Matrix4x4.CreateTranslation(center);

            for (int i = 0; i < 8; ++i)
            {
                Vector3 pt = new Vector3((i & 1) != 0 ? -0.5f : 0.5f,
                                ((i >> 1) & 1) != 0 ? -0.5f : 0.5f,
                                ((i >> 2) & 1) != 0 ? -0.5f : 0.5f);
                pts[i] = Vector3.Transform(pt, worldMat);
            }

            quads = new Quad[6];
            for (int idx = 0; idx < 6; ++idx)
            {
                Vector3[] vpts = new Vector3[4];
                for (int vidx = 0; vidx < 4; ++vidx)
                {
                    vpts[vidx] = pts[PlaneIndices[idx][vidx]];
                }
                quads[idx] = new Quad(vpts);
            }
        }

        public bool Intersect(Vector3 l0, Vector3 l, out float t)
        {
            float mint = float.MaxValue;
            bool foundinteresection = false;
            
            for (int i = 0; i < quads.Length; ++i)
            {
                float tt;
                if (quads[i].Intersect(l0, l, out tt) && tt > 0)
                {
                    Vector3 intersectPt = l0 + l * tt;
                    mint = Math.Min(tt, mint);
                    foundinteresection = true;
                }
            }
            t = mint;
            return foundinteresection;
        }
        public float GetScreenSpan(Matrix4x4 viewProj)
        {
            Vector4 spt0 = Vector4.Transform(new Vector4(pts[0], 1), viewProj);
            spt0 /= spt0.W;
            Vector4 spt7 = Vector4.Transform(new Vector4(pts[7], 1), viewProj);
            spt7 /= spt7.W;
            if (spt0.Z < 0 || spt7.Z < 0)
                return 0;
            return (new Vector2(spt7.X, spt7.Y) - new Vector2(spt0.X, spt0.Y)).LengthSquared();
        }
        public bool IsInView(Matrix4x4 viewProj)
        {
            if (IsGlobal)
                return true;
            int[] sides = new int[6];
            for (int idx = 0; idx < pts.Length; ++idx)
            {
                Vector4 spt = Vector4.Transform(new Vector4(pts[idx], 1), viewProj);
                spt /= spt.W;
                if (spt.X < -1)
                    sides[0]++;
                else if (spt.X > 1)
                    sides[1]++;
                if (spt.Y < -1)
                    sides[2]++;
                else if (spt.Y > 1)
                    sides[3]++;
                if (spt.Z < 0)
                    sides[4]++;
                else if (spt.Z > 1)
                    sides[5]++;
            }
            for (int i = 0; i < sides.Length; ++i)
            {
                if (sides[i] == 8)
                    return false;
            }

            return true;
        }
        public bool Equals(Bounds? other)
        {
            return center == other?.center &&
                scale == other?.scale;
        }
    }
}

