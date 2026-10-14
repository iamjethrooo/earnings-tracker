using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EarningsTracker.Utilities;

internal static class CatalogArtwork
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Downloads = new(4);
    private static readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> Pending = new();
    private static readonly DecodedArtworkCache Decoded = new();
    internal static string CacheDirectory => Path.Combine(AppDataPaths.DirectoryPath, "Artwork");

    internal static async Task<ImageSource?> LoadAsync(string url, string hash)
    {
        if (!CatalogService.IsHttps(url) || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) return null;
        var key = hash.ToLowerInvariant();
        if (Decoded.Get(key) is { } cached) return cached;
        var pending = Pending.GetOrAdd(key, _ => new Lazy<Task<ImageSource?>>(() => Task.Run(() => LoadCoreAsync(url, key))));
        try
        {
            var image = await pending.Value;
            if (image is BitmapSource bitmap) Decoded.Add(key, bitmap);
            return image;
        }
        finally { Pending.TryRemove(new KeyValuePair<string, Lazy<Task<ImageSource?>>>(key, pending)); }
    }

    private static async Task<ImageSource?> LoadCoreAsync(string url, string hash)
    {
        await Downloads.WaitAsync();
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var path = Path.Combine(CacheDirectory, hash + ".img");
            byte[] bytes;
            if (File.Exists(path)) bytes = await File.ReadAllBytesAsync(path);
            else if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Data", "Artwork", hash + ".img")))
                bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Artwork", hash + ".img"));
            else
            {
                using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 8 * 1024 * 1024) return null;
                using var input = await response.Content.ReadAsStreamAsync();
                using var output = new MemoryStream();
                var buffer = new byte[16384]; int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    if (output.Length + read > 8 * 1024 * 1024) return null;
                    await output.WriteAsync(buffer.AsMemory(0, read));
                }
                bytes = output.ToArray();
            }
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(path)) File.Delete(path);
                return null;
            }
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 160; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            if (!File.Exists(path))
            {
                var temporary = path + ".tmp";
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, path, true);
            }
            return bitmap;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        { return null; }
        finally { Downloads.Release(); }
    }
}
