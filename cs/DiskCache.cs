using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace googletiles
{
    public static class DiskCache
    {
        private static readonly string CacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoogleTiles", "Cache");
        
        static DiskCache()
        {
            // Ensure cache directory exists
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }
        }

        /// <summary>
        /// Generate a safe filename from a URL by hashing it
        /// </summary>
        private static string GetCacheFileName(string url, string sessionKey = "")
        {
            // Include session key in hash to avoid conflicts between sessions
            string input = url + "|" + sessionKey;
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                string hashString = Convert.ToHexString(hash);
                
                // Add appropriate extension based on URL
                string extension = ".cache";
                if (url.Contains(".json"))
                    extension = ".json";
                else if (url.Contains(".glb"))
                    extension = ".glb";
                else if (url.Contains(".gltf"))
                    extension = ".gltf";
                
                return hashString + extension;
            }
        }

        /// <summary>
        /// Get the full path to a cached file
        /// </summary>
        private static string GetCacheFilePath(string url, string sessionKey = "")
        {
            return Path.Combine(CacheDirectory, GetCacheFileName(url, sessionKey));
        }

        /// <summary>
        /// Check if a file exists in cache
        /// </summary>
        public static bool IsCached(string url, string sessionKey = "")
        {
            string filePath = GetCacheFilePath(url, sessionKey);
            return File.Exists(filePath);
        }

        /// <summary>
        /// Get cached file as a stream
        /// </summary>
        public static async Task<Stream> GetCachedStreamAsync(string url, string sessionKey = "")
        {
            string filePath = GetCacheFilePath(url, sessionKey);
            if (!File.Exists(filePath))
                return null;

            return new FileStream(filePath, FileMode.Open, FileAccess.Read);
        }

        /// <summary>
        /// Get cached file content as string (for JSON files)
        /// </summary>
        public static async Task<string> GetCachedStringAsync(string url, string sessionKey = "")
        {
            string filePath = GetCacheFilePath(url, sessionKey);
            if (!File.Exists(filePath))
                return null;

            return await File.ReadAllTextAsync(filePath);
        }

        /// <summary>
        /// Cache a stream to disk
        /// </summary>
        public static async Task CacheStreamAsync(string url, Stream stream, string sessionKey = "")
        {
            string filePath = GetCacheFilePath(url, sessionKey);
            
            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    // Reset stream position if possible
                    if (stream.CanSeek)
                        stream.Position = 0;
                    
                    await stream.CopyToAsync(fileStream);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't throw - caching is optional
                System.Diagnostics.Debug.WriteLine($"Failed to cache file {url}: {ex.Message}");
                
                // Clean up partial file
                if (File.Exists(filePath))
                {
                    try { File.Delete(filePath); } catch { }
                }
            }
        }

        /// <summary>
        /// Cache string content to disk (for JSON files)
        /// </summary>
        public static async Task CacheStringAsync(string url, string content, string sessionKey = "")
        {
            string filePath = GetCacheFilePath(url, sessionKey);
            
            try
            {
                await File.WriteAllTextAsync(filePath, content);
            }
            catch (Exception ex)
            {
                // Log error but don't throw - caching is optional
                System.Diagnostics.Debug.WriteLine($"Failed to cache file {url}: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all cached files
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    Directory.Delete(CacheDirectory, true);
                    Directory.CreateDirectory(CacheDirectory);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Get cache directory size in bytes
        /// </summary>
        public static long GetCacheSize()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                    return 0;

                long size = 0;
                var files = Directory.GetFiles(CacheDirectory, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    size += fileInfo.Length;
                }
                return size;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Get cache directory path for debugging
        /// </summary>
        public static string GetCacheDirectory()
        {
            return CacheDirectory;
        }
    }
}