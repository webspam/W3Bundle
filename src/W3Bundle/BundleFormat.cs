using System.Buffers.Binary;
using System.Text;

namespace W3Bundle;

public static class BundleFormat {
    public static ReadOnlySpan<byte> Magic => "POTATO70"u8;
    public const int HeaderSize = 0x20;
    public const int TocEntrySize = 304;
    public const int NameSize = 256;
    public const uint CompressionNone = 0;
    public const uint CompressionLz4Hc = 5;
    public const int DataAlignment = 16;
    public const string BlobBundleName = "blob0.bundle";
    public const string BuffersBundleName = "buffers0.bundle";
    const string BufferExtension = ".buffer";
    const ushort HeaderVersion = 5;

    public static uint Align(uint value) => (value + DataAlignment - 1) & ~(uint)(DataAlignment - 1);

    public static bool IsBuffer(string path) => path.EndsWith(BufferExtension, StringComparison.OrdinalIgnoreCase);

    public static void WriteHeader(Span<byte> header, uint alignedBundleSize, uint tocSize) {
        header.Clear();
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], alignedBundleSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], tocSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header[20..], HeaderVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header[22..], HeaderSize + tocSize);
    }

    public static void WriteTocEntry(Span<byte> slot, BundleEntry entry) {
        slot.Clear();
        if (!Encoding.UTF8.TryGetBytes(entry.Path, slot[..(NameSize - 1)], out _)) {
            throw new InvalidDataException($"Path too long for bundle: {entry.Path}");
        }
        var fields = slot[(NameSize + 16)..];
        BinaryPrimitives.WriteUInt32LittleEndian(fields, entry.Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[8..], entry.Size);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[12..], entry.CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[16..], entry.Crc32);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[20..], entry.Compression);
    }

    public static (uint AlignedBundleSize, List<BundleEntry> Entries) ReadToc(string bundlePath) {
        using var stream = File.OpenRead(bundlePath);
        Span<byte> header = stackalloc byte[HeaderSize];
        stream.ReadExactly(header);
        if (!header[..8].SequenceEqual(Magic)) {
            throw new InvalidDataException($"Not a bundle: {bundlePath}");
        }
        uint alignedBundleSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        uint tocSize = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);

        byte[] toc = new byte[tocSize];
        stream.ReadExactly(toc);
        var entries = new List<BundleEntry>((int)(tocSize / TocEntrySize));
        for (int position = 0; position + TocEntrySize <= toc.Length; position += TocEntrySize) {
            var slot = toc.AsSpan(position, TocEntrySize);
            var name = slot[..NameSize];
            int terminator = name.IndexOf((byte)0);
            var fields = slot[(NameSize + 16)..];
            entries.Add(new BundleEntry(
                Encoding.UTF8.GetString(terminator < 0 ? name : name[..terminator]),
                BinaryPrimitives.ReadUInt32LittleEndian(fields),
                BinaryPrimitives.ReadUInt32LittleEndian(fields[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(fields[12..]),
                BinaryPrimitives.ReadUInt32LittleEndian(fields[16..]),
                BinaryPrimitives.ReadUInt32LittleEndian(fields[20..])));
        }
        return (alignedBundleSize, entries);
    }
}
