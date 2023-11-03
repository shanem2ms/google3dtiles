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

namespace googletiles
{
    public class GoogleTile
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

            public override string ToString()
            {
                return string.Join(',', box.Select(a => a.ToString()));
            }
        }

        public class Node
        {
            public BoundingVolume boundingVolume { get; set; }
            public float[] transform { get; set; }

            public Node[] children { get; set; }
            public Content content { get; set; }

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
        public static async Task<Stream> GetContentStream(string sessionkey, string url)
        {
            HttpClient httpClient = new HttpClient();
            string sessionqr = sessionkey.Length > 0 ? '&' + "session=" + sessionkey : "";
            var response = await httpClient.GetAsync(site + url + '?' + key + sessionqr);
            return await response.Content.ReadAsStreamAsync();
        }

        public string GetSession()
        {
            return root.GetSession();
        }

        static Dictionary<string, int> jsonIdx = new Dictionary<string, int>();
        static int nextIdx = 0;
        public static async Task<GoogleTile> CreateFromUri(string url, string sessionkey)
        {
            int jsonidx = -1;
            if (!jsonIdx.TryGetValue(url, out jsonidx))
            {
                jsonidx = nextIdx++;
                jsonIdx.Add(url, jsonidx);
            }
            HttpClient httpClient = new HttpClient();
            string sessionqr = sessionkey.Length > 0 ? '&' + "session=" + sessionkey : "";
            var response = await httpClient.GetAsync(site + url + '?' + key + sessionqr);
            string responseJson = await response.Content.ReadAsStringAsync();
            File.WriteAllText(Path.GetFileName(url), responseJson);
            GoogleTile tile = JsonSerializer.Deserialize<GoogleTile>(responseJson);
            return tile;
        }
    }
}
