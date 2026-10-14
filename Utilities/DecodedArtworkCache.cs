using System.Windows.Media.Imaging;

namespace EarningsTracker.Utilities;

// Frozen images can be shared across dispatchers. Limit both entry count and decoded pixel bytes.
internal sealed class DecodedArtworkCache(int capacity = 128, long byteLimit = 16 * 1024 * 1024)
{
    private sealed record Entry(string Key, BitmapSource Image, long Bytes);
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();
    internal long RetainedBytes { get; private set; }

    internal BitmapSource? Get(string key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry)) return null;
            _recent.Remove(entry); _recent.AddFirst(entry);
            return entry.Value.Image;
        }
    }

    internal void Add(string key, BitmapSource image)
    {
        if (!image.IsFrozen) throw new ArgumentException("Artwork must be frozen before it is shared.", nameof(image));
        var bytes = (long)image.PixelWidth * image.PixelHeight * Math.Max(4, (image.Format.BitsPerPixel + 7) / 8);
        if (bytes > byteLimit || capacity < 1) return;
        lock (_gate)
        {
            if (_entries.Remove(key, out var previous)) { _recent.Remove(previous); RetainedBytes -= previous.Value.Bytes; }
            var entry = _recent.AddFirst(new Entry(key, image, bytes));
            _entries.Add(key, entry); RetainedBytes += bytes;
            while (_entries.Count > capacity || RetainedBytes > byteLimit)
            {
                var oldest = _recent.Last!;
                _entries.Remove(oldest.Value.Key); _recent.RemoveLast(); RetainedBytes -= oldest.Value.Bytes;
            }
        }
    }
}
