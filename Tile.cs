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
using glTFLoader;
using glTFLoader.Schema;
using Vortice.DXCore;
using System.ComponentModel;
using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using static googletiles.GoogleTile;
using System.Windows.Input;

namespace googletiles
{
    public class Tile : INotifyPropertyChanged
    {

        Bounds bounds;
        public Gltf gltf;
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
            if (GlbFile != null)
            {                
                Stream stream = await GoogleTile.GetContentStream(sessionkey, GlbFile);
                gltf = glTFLoader.Interface.LoadModel(stream);                
                /*
                byte[] buf = new byte[stream.Length];
                await stream.ReadAsync(buf, 0, buf.Length);
                string filename = System.IO.Path.GetFileName(GlbFile);
                await System.IO.File.WriteAllBytesAsync(filename, buf);*/
            }
            return true;
        }

        public async Task<bool> DownloadChildren(string sessionkey)
        {
            if (this.childrenDownloaded)
                return false;

            List<Task<bool>> allTasks = new List<Task<bool>>();
            Task<bool> task = DownloadGlb(sessionkey);
            allTasks.Add(task);

            this.childrenDownloaded = true;
            List<Tile> tiles = new List<Tile>();
            if (ChildJson != null)
            {
                if (ChildTiles?.Length > 0)
                    Debugger.Break();
                GoogleTile tile = await GoogleTile.CreateFromUri(ChildJson, sessionkey);
                Debug.WriteLine(ChildJson);
                Tile t = new Tile(tile.root, level + 1);
                tiles.Add(t);
                ChildTiles = tiles.ToArray();
            }
            else
            {
                foreach (Tile tile in ChildTiles)
                {
                    allTasks.Add(
                        tile.DownloadChildren(sessionkey));
                }
            }
            await Task.WhenAll(allTasks);
            return true;
        }
        
    }

    public class Bounds : IEquatable<Bounds>
    {
        public Vector3 center;
        public Vector3 scale;
        public Vector3[] rot;
        public Matrix4x4 rotMat;
        public Bounds(GoogleTile.BoundingVolume bv)
        {
            center = new Vector3(bv.box[0], bv.box[1], bv.box[2]);
            scale = new Vector3();
            rot = new Vector3[3];
            rotMat = Matrix4x4.Identity;
            for (int i = 0; i < 3; ++i)
            {
                Vector3 vx = new Vector3(bv.box[3 + i * 3], bv.box[3 + i * 3 + 1], bv.box[3 + i * 3 + 2]);
                scale[i] = vx.Length();
                rot[i] = Vector3.Normalize(vx);
                rotMat[i, 0] = rot[i].X;
                rotMat[i, 1] = rot[i].Y;
                rotMat[i, 2] = rot[i].Z;
            }
        }

        public bool Equals(Bounds? other)
        {
            return center == other?.center &&
                scale == other?.scale;
        }
    }
}

