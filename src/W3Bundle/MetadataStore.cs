using System.Text;

namespace W3Bundle;

public static class MetadataStore {
    const uint Magic = 0x4D545603;
    const uint Version = 7;
    const byte BundlesTerminator = 0x80;
    const ulong FnvOffsetBasis = 0xcbf29ce484222325;
    const ulong FnvPrime = 0x100000001b3;

    sealed record StoredFile(BundleEntry Entry, uint BundleIndex, uint FileIndex);

    public static string Generate(string contentDirectory) {
        var bundles = Directory.GetFiles(contentDirectory, "*.bundle")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => {
                var (alignedBundleSize, entries) = BundleFormat.ReadToc(path);
                return new BundleToc(Path.GetFileName(path), alignedBundleSize, entries);
            })
            .ToArray();
        if (bundles.Length == 0) {
            throw new NotSupportedException($"No .bundle files in {contentDirectory}");
        }
        byte[] store = Build(bundles);
        string outputPath = Path.Combine(contentDirectory, "metadata.store");
        File.WriteAllBytes(outputPath, store);
        return outputPath;
    }

    public static byte[] Build(IReadOnlyList<BundleToc> bundles) {
        var files = bundles
            .SelectMany((bundle, bundleIndex) => bundle.Entries.OrderBy(e => e.Offset).Select(entry => (Entry: entry, BundleIndex: (uint)(bundleIndex + 1))))
            .Select((f, i) => new StoredFile(f.Entry, f.BundleIndex, (uint)(i + 1)))
            .ToArray();

        var strings = new StringTable();
        strings.Add("");
        var bundleNameOffsets = new uint[bundles.Count];
        var pathOffsets = new uint[files.Length];
        int fileCursor = 0;
        for (int b = 0; b < bundles.Count; b++) {
            bundleNameOffsets[b] = strings.Add(bundles[b].Name);
            for (int i = 0; i < bundles[b].Entries.Count; i++, fileCursor++) {
                pathOffsets[fileCursor] = strings.Add(files[fileCursor].Entry.Path);
            }
        }

        var directories = new DirectoryTree();
        var fileDirectories = files.ToDictionary(f => f.FileIndex, f => directories.Resolve(f.Entry.Path));
        var directoryList = directories.InDepthFirstOrder();
        var directoryNameOffsets = directoryList.Select(d => strings.Add(d.Name)).ToArray();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(files.Max(f => f.Entry.CompressedSize));
        writer.Write(files.Max(f => f.Entry.Size));

        byte[] stringBytes = strings.ToArray();
        WriteCompressedInt(writer, stringBytes.Length);
        writer.Write(stringBytes);

        WriteCompressedInt(writer, files.Length + 1);
        writer.Write(new byte[32]);
        foreach (var file in files) {
            writer.Write(pathOffsets[file.FileIndex - 1]);
            writer.Write(0u);
            writer.Write(file.Entry.CompressedSize);
            writer.Write(file.Entry.Size);
            writer.Write(file.FileIndex);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(file.Entry.Compression);
        }

        WriteCompressedInt(writer, files.Length + 1);
        writer.Write(new byte[24]);
        foreach (var file in files) {
            writer.Write(file.Entry.Offset);
            writer.Write(0u);
            writer.Write(file.Entry.CompressedSize);
            writer.Write(0u);
            writer.Write(file.FileIndex);
            writer.Write(file.BundleIndex);
        }

        WriteCompressedInt(writer, bundles.Count + 1);
        writer.Write(new byte[24]);
        uint firstFile = 1;
        for (int b = 0; b < bundles.Count; b++) {
            var bundle = bundles[b];
            uint dataStart = BundleFormat.HeaderSize + (uint)(bundle.Entries.Count * BundleFormat.TocEntrySize);
            writer.Write(bundle.AlignedBundleSize - dataStart);
            writer.Write(0u);
            writer.Write(dataStart);
            writer.Write(bundleNameOffsets[b]);
            writer.Write(firstFile);
            writer.Write((uint)bundle.Entries.Count);
            firstFile += (uint)bundle.Entries.Count;
        }
        writer.Write(BundlesTerminator);

        WriteCompressedInt(writer, directoryList.Count);
        for (int i = 0; i < directoryList.Count; i++) {
            writer.Write(directoryNameOffsets[i]);
            writer.Write((uint)(directoryList[i].Parent?.Index ?? 0));
        }

        WriteCompressedInt(writer, files.Length);
        foreach (var file in files) {
            string path = file.Entry.Path;
            int nameStart = Encoding.UTF8.GetByteCount(path.AsSpan(0, path.LastIndexOf('\\') + 1));
            writer.Write(file.FileIndex);
            writer.Write((uint)fileDirectories[file.FileIndex].Index);
            writer.Write(pathOffsets[file.FileIndex - 1] + (uint)nameStart);
        }

        var hashes = files
            .Select(f => (Hash: PathHash(f.Entry.Path), f.FileIndex))
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

    sealed class DirectoryNode(string name, DirectoryNode? parent) {
        public string Name { get; } = name;
        public DirectoryNode? Parent { get; } = parent;
        public List<DirectoryNode> Children { get; } = [];
        public int Index { get; set; }
    }

    sealed class DirectoryTree {
        readonly DirectoryNode root = new("", null);
        readonly Dictionary<string, DirectoryNode> byPath = new(StringComparer.Ordinal);

        public DirectoryNode Resolve(string filePath) {
            var current = root;
            int separator = filePath.IndexOf('\\');
            while (separator >= 0) {
                string fullPath = filePath[..separator];
                if (!byPath.TryGetValue(fullPath, out var node)) {
                    node = new DirectoryNode(fullPath[(fullPath.LastIndexOf('\\') + 1)..], current);
                    current.Children.Add(node);
                    byPath[fullPath] = node;
                }
                current = node;
                separator = filePath.IndexOf('\\', separator + 1);
            }
            return current;
        }

        public List<DirectoryNode> InDepthFirstOrder() {
            var ordered = new List<DirectoryNode>(byPath.Count + 1);
            var pending = new Stack<DirectoryNode>([root]);
            while (pending.TryPop(out var node)) {
                node.Index = ordered.Count;
                ordered.Add(node);
                for (int i = node.Children.Count - 1; i >= 0; i--) {
                    pending.Push(node.Children[i]);
                }
            }
            return ordered;
        }
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
