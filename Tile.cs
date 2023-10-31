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
using Vortice.DXCore;
using System.ComponentModel;

namespace googletiles
{
    public class Tile : INotifyPropertyChanged
    {

        public GoogleTile.BoundingVolume boundingVolume => node.boundingVolume;
        public float[] Transform => node.transform;

        public Tile[] ChildTiles { get; set; }

        GoogleTile.Node node;

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
        public Tile(GoogleTile.Node n, int l)
        {
            node = n;
            tileIdx = tileCount++;
            this.level = l;
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

        public async Task<bool> DownloadChildren(string sessionkey)
        {
            if (this.childrenDownloaded)
                return false;

            bool hasGlb = false;
            this.childrenDownloaded = true;
            List<Tile> tiles = new List<Tile>();

            if (node.content != null)
            {
                if (node.content.uri.Contains(".json"))
                {
                    GoogleTile tile = await GoogleTile.CreateFromUri(node.content.UriNoQuery(), sessionkey);
                    Tile t = new Tile(tile.root, level + 1);
                    tiles.Add(t);
                }
                else if (node.content.uri.Contains(".glb"))
                {
                    hasGlb = true;
                    //Stream stream = await node.GetContentStream(sessionkey);
                    //var gltfModel = glTFLoader.Interface.LoadModel(stream);
                    /*
                    byte[]buf = new byte[stream.Length];
                    await stream.ReadAsync(buf, 0, buf.Length);
                    string filename = content.UriNoQuery();
                    filename = System.IO.Path.GetFileName(filename);
                    await System.IO.File.WriteAllBytesAsync(filename, buf);*/
                }
            }

            if (node.children != null)
            {
                List<Task<bool>> allTasks = new List<Task<bool>>();
                foreach (GoogleTile.Node n in node.children)
                {
                    Tile tile = new Tile(n, level + 1);
                    tiles.Add(tile);
                    allTasks.Add(tile.DownloadChildren(sessionkey));
                }

                await Task.WhenAll(allTasks);
            }
            ChildTiles = tiles.ToArray();
            return true;
        }
        
    }
}

