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
            public float geometricError { get; set; }

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
            // Check disk cache first
            Stream cachedStream = await DiskCache.GetCachedStreamAsync(url, sessionkey);
            if (cachedStream != null)
            {
                System.Diagnostics.Debug.WriteLine($"Using cached content for: {url}");
                return cachedStream;
            }

            // Download from API
            HttpClient httpClient = new HttpClient();
            string sessionqr = sessionkey.Length > 0 ? '&' + "session=" + sessionkey : "";
            var response = await httpClient.GetAsync(site + url + '?' + key + sessionqr);
            Stream responseStream = await response.Content.ReadAsStreamAsync();
            
            // Create a memory stream to cache the content
            var memoryStream = new MemoryStream();
            await responseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            
            // Cache the content
            await DiskCache.CacheStreamAsync(url, memoryStream, sessionkey);
            
            // Reset position for return
            memoryStream.Position = 0;
            System.Diagnostics.Debug.WriteLine($"Downloaded and cached content for: {url}");
            
            return memoryStream;
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

            // Check disk cache first
            string cachedJson = await DiskCache.GetCachedStringAsync(url, sessionkey);
            string responseJson;
            
            if (cachedJson != null)
            {
                // Use cached version
                responseJson = cachedJson;
                System.Diagnostics.Debug.WriteLine($"Using cached JSON for: {url}");
            }
            else
            {
                // Download from API and cache
                HttpClient httpClient = new HttpClient();
                string sessionqr = sessionkey.Length > 0 ? '&' + "session=" + sessionkey : "";
                var response = await httpClient.GetAsync(site + url + '?' + key + sessionqr);
                responseJson = await response.Content.ReadAsStringAsync();
                
                // Cache the response
                await DiskCache.CacheStringAsync(url, responseJson, sessionkey);
                System.Diagnostics.Debug.WriteLine($"Downloaded and cached JSON for: {url}");
            }
            
            GoogleTile tile = JsonSerializer.Deserialize<GoogleTile>(responseJson);
            return tile;
        }
    }
}
