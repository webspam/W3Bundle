using System.Buffers.Binary;
using System.Text;

namespace W3Bundle;

public static class MetadataStore {
    const uint Magic = 0x4D545603;
    const uint Version = 7;
    const byte BundlesTerminator = 0x80;
    const ulong FnvOffsetBasis = 0xcbf29ce484222325;
    const ulong FnvPrime = 0x100000001b3;

    public static string Generate(string contentDirectory) {
        string[] bundlePaths = Directory.GetFiles(contentDirectory, "*.bundle");
        if (bundlePaths.Length != 1) {
            throw new NotSupportedException($"Expected exactly one .bundle in {contentDirectory}, found {bundlePaths.Length}");
        }
        string bundlePath = bundlePaths[0];
        var (alignedBundleSize, entries) = BundleFormat.ReadToc(bundlePath);
        byte[] store = Build(Path.GetFileName(bundlePath), alignedBundleSize, entries);
        string outputPath = Path.Combine(contentDirectory, "metadata.store");
        File.WriteAllBytes(outputPath, store);
        return outputPath;
    }

    public static byte[] Build(string bundleName, uint alignedBundleSize, IReadOnlyList<BundleEntry> tocEntries) {
        var files = tocEntries.OrderBy(e => e.Offset).ToArray();
        uint dataStart = BundleFormat.HeaderSize + (uint)(tocEntries.Count * BundleFormat.TocEntrySize);

        var strings = new StringTable();
        strings.Add("");
        uint bundleNameOffset = strings.Add(bundleName);
        var pathOffsets = files.Select(f => strings.Add(f.Path)).ToArray();

        var directories = new List<(string FullPath, string Name, int Parent)> { ("", "", 0) };
        var directoryIndex = new Dictionary<string, int>(StringComparer.Ordinal) { [""] = 0 };
        var fileDirectories = new int[files.Length];
        for (int i = 0; i < files.Length; i++) {
            fileDirectories[i] = ResolveDirectory(files[i].Path, directories, directoryIndex);
        }
        var directoryNameOffsets = directories.Select(d => strings.Add(d.Name)).ToArray();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(files.Max(f => f.CompressedSize));
        writer.Write(files.Max(f => f.Size));

        byte[] stringBytes = strings.ToArray();
        WriteCompressedInt(writer, stringBytes.Length);
        writer.Write(stringBytes);

        WriteCompressedInt(writer, files.Length + 1);
        writer.Write(new byte[32]);
        for (int i = 0; i < files.Length; i++) {
            var file = files[i];
            writer.Write(pathOffsets[i]);
            writer.Write(0u);
            writer.Write(file.CompressedSize);
            writer.Write(file.Size);
            writer.Write((uint)(i + 1));
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(file.Compression);
        }

        WriteCompressedInt(writer, files.Length + 1);
        writer.Write(new byte[24]);
        for (int i = 0; i < files.Length; i++) {
            var file = files[i];
            writer.Write(file.Offset);
            writer.Write(0u);
            writer.Write(file.CompressedSize);
            writer.Write(0u);
            writer.Write((uint)(i + 1));
            writer.Write(1u);
        }

        WriteCompressedInt(writer, 2);
        writer.Write(new byte[24]);
        writer.Write(alignedBundleSize - dataStart);
        writer.Write(0u);
        writer.Write(dataStart);
        writer.Write(bundleNameOffset);
        writer.Write(1u);
        writer.Write((uint)files.Length);
        writer.Write(BundlesTerminator);

        WriteCompressedInt(writer, directories.Count);
        for (int i = 0; i < directories.Count; i++) {
            writer.Write(directoryNameOffsets[i]);
            writer.Write((uint)directories[i].Parent);
        }

        WriteCompressedInt(writer, files.Length);
        for (int i = 0; i < files.Length; i++) {
            string path = files[i].Path;
            int nameStart = Encoding.UTF8.GetByteCount(path.AsSpan(0, path.LastIndexOf('\\') + 1));
            writer.Write((uint)(i + 1));
            writer.Write((uint)fileDirectories[i]);
            writer.Write(pathOffsets[i] + (uint)nameStart);
        }

        var hashes = files
            .Select((f, i) => (Hash: PathHash(f.Path), FileIndex: (uint)(i + 1)))
            .OrderBy(h => h.Hash)
            .ToArray();
        WriteCompressedInt(writer, hashes.Length);
        foreach (var (hash, fileIndex) in hashes) {
            writer.Write(hash);
            writer.Write(fileIndex);
            writer.Write(fileIndex == 1 ? 0 : -1);
        }

        writer.Flush();
        return stream.ToArray();
    }

    static int ResolveDirectory(string filePath, List<(string FullPath, string Name, int Parent)> directories, Dictionary<string, int> directoryIndex) {
        int parent = 0;
        int separator = filePath.IndexOf('\\');
        while (separator >= 0) {
            string fullPath = filePath[..separator];
            if (!directoryIndex.TryGetValue(fullPath, out int index)) {
                int nameStart = fullPath.LastIndexOf('\\') + 1;
                index = directories.Count;
                directories.Add((fullPath, fullPath[nameStart..], parent));
                directoryIndex[fullPath] = index;
            }
            parent = index;
            separator = filePath.IndexOf('\\', separator + 1);
        }
        return parent;
    }

    static ulong PathHash(string path) {
        ulong hash = FnvOffsetBasis;
        foreach (byte b in Encoding.UTF8.GetBytes(path)) {
            hash ^= b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b | 0x20) : b;
            hash *= FnvPrime;
        }
        return hash;
    }

    static void WriteCompressedInt(BinaryWriter writer, int value) {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        uint remaining = (uint)value;
        byte first = (byte)(remaining & 0x3F);
        remaining >>= 6;
        if (remaining != 0) {
            first |= 0x40;
        }
        writer.Write(first);
        while (remaining != 0) {
            byte next = (byte)(remaining & 0x7F);
            remaining >>= 7;
            if (remaining != 0) {
                next |= 0x80;
            }
            writer.Write(next);
        }
    }

    sealed class StringTable {
        readonly List<byte> buffer = [];

        public uint Add(string value) {
            uint offset = (uint)buffer.Count;
            buffer.AddRange(Encoding.UTF8.GetBytes(value));
            buffer.Add(0);
            return offset;
        }

        public byte[] ToArray() => [.. buffer];
    }
}
