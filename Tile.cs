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

        public event PropertyChangedEventHandler? PropertyChanged;
        static int tileCount = 0;
        int tileIdx = 0;

        public string Name => $"tile{tileIdx}";
        public Tile(GoogleTile.Node n)
        {
            node = n;
            tileIdx = tileCount++;
        }

        public async Task<bool> DownloadChildren(string sessionkey)
        {
            bool hasGlb = false;
            List<Tile> tiles = new List<Tile>();

            if (node.content != null)
            {
                if (node.content.uri.Contains(".json"))
                {
                    GoogleTile tile = await GoogleTile.CreateFromUri(node.content.UriNoQuery(), sessionkey);
                    Tile t = new Tile(tile.root);
                    tiles.Add(t);                        
                }
                else if (node.content.uri.Contains(".glb"))
                {
                    hasGlb = true;
                    Stream stream = await node.GetContentStream(sessionkey);
                    var gltfModel = glTFLoader.Interface.LoadModel(stream);
                    /*
                    byte[]buf = new byte[stream.Length];
                    await stream.ReadAsync(buf, 0, buf.Length);
                    string filename = content.UriNoQuery();
                    filename = System.IO.Path.GetFileName(filename);
                    await System.IO.File.WriteAllBytesAsync(filename, buf);*/
                }
            }

            if (node.children == null)
                return true;
            foreach (GoogleTile.Node n in node.children)
            {
                Tile tile = new Tile(n);
                tiles.Add(tile);
                await tile.DownloadChildren(sessionkey);
            }
            ChildTiles = tiles.ToArray();
            return true;
        }
        
    }
}

