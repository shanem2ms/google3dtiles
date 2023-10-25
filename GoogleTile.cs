using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Animation;

namespace googletiles
{
    class GoogleTile
    {
        public class Content
        {
            public string uri { get; set; }

            public string UriNoQuery()
            {
                int idx = uri.IndexOf("?");
                if (idx >= 0)
                    return uri.Substring(0, idx);
                else
                    return uri;
            }
        }
        public class BoundingVolume
        {
            public float[] box { get; set; }
        }

        public class Node
        {
            public BoundingVolume boundingVolume { get; set; }
            public float[] transform { get; set; }

            public Node[] children { get; set; }
            public Content content { get; set; }

            async Task<bool> DownloadContent(string sessionkey)
            {
                if (content == null)
                    return true;
                if (content.uri.Contains(".json"))
                {
                    GoogleTile tile = await GoogleTile.CreateFromUri(content.UriNoQuery(), sessionkey);
                    await tile.DownloadChildren(sessionkey);
                }
                else if (content.uri.Contains(".glb"))
                {
                    HttpClient httpClient = new HttpClient();
                    string sessionqr = sessionkey.Length > 0 ? '&' + "session=" + sessionkey : "";
                    string url = //@"/v1/3dtiles/datasets/CgA/files/UlRPVEYubm9kZWRhdGEucGxhbmV0b2lkPWVhcnRoLG5vZGVfZGF0YV9lcG9jaD05NDYscGF0aD0yMTYwNCxjYWNoZV92ZXJzaW9uPTYsaW1hZ2VyeV9lcG9jaD05NjY.glb";
                    content.UriNoQuery();
                    var response = await httpClient.GetAsync(site + url + '?' + key + sessionqr);
                    var stream = await response.Content.ReadAsStreamAsync();
                    byte[]buf = new byte[stream.Length];
                    await stream.ReadAsync(buf, 0, buf.Length);
                    string filename = content.UriNoQuery();
                    filename = System.IO.Path.GetFileName(filename);
                    await System.IO.File.WriteAllBytesAsync(filename, buf);
                    return true;
                }
                return true;
            }
            public async Task<bool> DownloadChildren(string sessionkey)
            {
                await DownloadContent(sessionkey);
                if (children == null)
                    return true;
                foreach (Node n in children)
                {
                    await n.DownloadChildren(sessionkey);
                }
                return true;
            }


            public string GetSession()
            {
                if (content != null)
                {
                    int idx = content.uri.IndexOf('?');
                    if (idx > 0)
                    {
                        string query = content.uri.Substring(idx + 1);
                        int eq = content.uri.IndexOf('=');
                        string session = content.uri.Substring(eq + 1);
                        return session;
                    }
                }
                if (children == null)
                    return string.Empty;
                foreach (Node n in children)
                {
                    string s = n.GetSession();
                    if (s.Length != 0)
                        return s;
                }
                return string.Empty;
            }
        }
        public Node root { get; set; }

        static string site = "https://tile.googleapis.com";
        static string key = "key=AIzaSyB-uBxCvbThmf-lSIqbdMvE1wPJ8fVNbjs";
        public Task<bool> DownloadChildren(string sessionkey)
        {
            return root.DownloadChildren(sessionkey);
        }
        public string GetSession()
        {
            return root.GetSession();
        }
        public static async Task<GoogleTile> CreateFromUri(string url, string sessionkey)
        {
            HttpClient httpClient = new HttpClient();
            string sessionqr = sessionkey.Length > 0 ? '&' + "session=" + sessionkey : "";
            var response = await httpClient.GetAsync(site + url + '?' + key + sessionqr);
            string responseJson = await response.Content.ReadAsStringAsync();
            GoogleTile tile = JsonSerializer.Deserialize<GoogleTile>(responseJson);
            return tile;
        }
    }
}
