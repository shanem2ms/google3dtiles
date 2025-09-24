using SharpGen.Runtime;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Transactions;
using Veldrid;

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
        public bool IsDownloading { get; set; } = false;
        public int LastVisibleFrame { get; set; } = 0;
        public Vector3 MeshLoc => mesh?.translation ?? Vector3.Zero;

        public static uint JSONCnt = 1;
        public static uint GlbCnt = 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        static int tileCount = 0;
        public int Idx => tileIdx;
        int tileIdx = 0;
        int level;
        bool childrenDownloaded = false;
        public float DistSqFromCam = 0;
        public float DistFromCam => DistSqFromCam > 0 ? MathF.Sqrt(DistSqFromCam) : -1;

        public int Margin => level * 5;
        public float GeometricError { get; }

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
            GeometricError = node.geometricError;
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
            GlbCnt++;
            GlbMesh mmesh = new GlbMesh();
            if (!mmesh.Load(stream, this.bounds))
                return false;

            this.mesh = mmesh;
            return true;
        }

        public float GetGeometricError(CameraView cv)
        {
            if (Bounds.IsInside(cv))
                return float.PositiveInfinity; // Always refine if camera is inside
            
            float distSq = Bounds.DistanceSqFromPt(cv.Pos);
            if (distSq <= 0)
                return float.PositiveInfinity;
            
            float dist = MathF.Sqrt(distSq);
            if (GeometricError == 0)
                return float.PositiveInfinity; // Leaf node that can't be refined
            
            return dist / GeometricError;
        }

        public string GetUri()
        {
            if (Parent == null) return "root";
            if (!string.IsNullOrEmpty(ChildJson)) return ChildJson;
            if (!string.IsNullOrEmpty(GlbFile)) return GlbFile;
            return $"tile_{tileIdx}";
        }

        public async Task<bool> DownloadChildren(string sessionkey, CameraView cv, int frameIdx, bool saveGlb)
        {
            LastVisitedFrame = frameIdx;
            IsInView = bounds.IsInView(cv);
            if (!IsInView)
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
                    
                    IsDownloading = true; // Set flag before download
                    GoogleTile tile = await GoogleTile.CreateFromUri(ChildJson, sessionkey);
                    IsDownloading = false; // Clear flag after download
                    
                    JSONCnt++;
                    Tile t = new Tile(tile.root, this);
                    tiles.Add(t);
                    ChildTiles = tiles.ToArray();
                }
            }

            if (saveGlb && mesh != null)
            {
                System.IO.File.WriteAllBytes($"g{tileIdx}.glb", mesh.buf);
            }

            bool isInside = bounds.IsInside(cv);
            DistSqFromCam = isInside ? -1 : bounds.DistanceSqFromPt(cv.Pos);

            if (ChildTiles != null)
            {
                float errorDist = float.PositiveInfinity;
                if (!isInside)
                {
                    float dsq = MathF.Sqrt(DistSqFromCam);
                    errorDist = float.IsInfinity(GeometricError) ? float.PositiveInfinity : dsq / GeometricError;
                    float span = bounds.GetScreenSpan(cv.ViewProj);
                }
                if (isInside || errorDist < 40)
                {
                    foreach (Tile tile in ChildTiles)
                    {
                        allTasks.Add(
                            tile.DownloadChildren(sessionkey, cv, frameIdx, saveGlb));
                    }
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
        uint imgWidth;
        uint imgHeight;
        public byte[] buf;
        string name;
        public DeviceBuffer _vertexBuffer;
        public DeviceBuffer _indexBuffer;
        private Texture _surfaceTexture;
        private TextureView _surfaceTextureView;
        public DeviceBuffer _worldBuffer;
        public ResourceSet _worldTextureSet;
        public Matrix4x4 worldMat;
        

        public int triangleCnt => triangleList.Length / 3;
        public Matrix4x4 mat;
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
        static extern uint GetTextureWidth(nint pmesh);

        [DllImport("libglb.dll")]
        static extern uint GetTextureHeight(nint pmesh);

        [DllImport("libglb.dll")]
        static extern void FreeMesh(nint pmesh);

        public bool Load(Stream stream, Bounds b)
        {
            var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            buf = memoryStream.ToArray();
            memoryStream.Seek(0, SeekOrigin.Begin);
            nint nativeBuf = Marshal.AllocHGlobal(buf.Length);
            Marshal.Copy(buf, 0, nativeBuf, buf.Length);
            nint meshptr = LoadMesh(nativeBuf, (uint)buf.Length);
            if (meshptr == IntPtr.Zero)
                return false;
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
                nint pmatrix = Marshal.AllocHGlobal(16 * sizeof(float));
                bool success = GetPoints(meshptr, ptBuf, npoints * 5 * sizeof(float), pmatrix);
                ptList = new float[npoints * 5];
                Marshal.Copy(ptBuf, ptList, 0, (int)npoints * 5);
                mat = Marshal.PtrToStructure<Matrix4x4>(pmatrix);
                translation = Vector3.Transform(Vector3.Zero, mat);

                mat.M41 = 0;
                mat.M42 = 0;
                mat.M43 = 0;
                Marshal.FreeHGlobal(ptBuf);
                Marshal.FreeHGlobal(pmatrix);
            }
            {
                imgWidth = GetTextureWidth(meshptr);
                imgHeight = GetTextureHeight(meshptr);
                imageBuf = Marshal.AllocHGlobal((int)(imgWidth * imgHeight * 4));
                bool success = GetTexture(meshptr, imageBuf, imgWidth * imgHeight * 4);
            }
            FreeMesh(meshptr);
            
            Matrix4x4 zUp = new Matrix4x4(
                1.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 1.0f, 0.0f,
                0.0f, -1.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f);
            Vector3 t = Vector3.Transform(translation, zUp);
            translation = t;
         
            for (int i = 0; i < ptList.Length; i += 5)
            {
                Vector3 pt = new Vector3(ptList[i], ptList[i + 1], ptList[i + 2]);
                //pt = Vector3.Transform(pt, zUp);
                ptList[i] = pt.X;
                ptList[i+1] = pt.Y;
                ptList[i+2] = pt.Z;
            }
            CreateIBVB();
            return true;
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

            _surfaceTexture = factory.CreateTexture(new TextureDescription(imgWidth, imgHeight, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled, TextureType.Texture2D, 0));
            VeldridComponent.Graphics.UpdateTexture(_surfaceTexture, imageBuf, imgWidth * imgHeight * 4, 0, 0, 0, imgWidth, imgHeight, 1, 0, 0);
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

