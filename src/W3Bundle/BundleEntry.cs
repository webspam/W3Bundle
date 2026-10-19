namespace W3Bundle;

public sealed record BundleEntry(string Path, uint Offset, uint Size, uint CompressedSize, uint Crc32, uint Compression);
