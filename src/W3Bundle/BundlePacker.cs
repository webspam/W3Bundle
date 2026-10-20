using System.IO.Hashing;

namespace W3Bundle;

public static class BundlePacker {
    sealed record PackedFile(string BundlePath, byte[] Stored, int StoredLength, uint Size, uint Crc32, uint Compression);

    public static string Pack(string sourceDirectory, string outputDirectory, string bundleName = "blob0.bundle") {
        string root = Path.GetFullPath(sourceDirectory);
        var sourceFiles = EnumerateInBundleOrder(new DirectoryInfo(root)).ToArray();
        if (sourceFiles.Length == 0) {
            throw new InvalidOperationException($"No files to pack in {root}");
        }

        var packed = new PackedFile[sourceFiles.Length];
        Parallel.For(0, sourceFiles.Length, i => packed[i] = Compress(root, sourceFiles[i]));

        uint tocSize = (uint)(packed.Length * BundleFormat.TocEntrySize);
        uint offset = BundleFormat.HeaderSize + tocSize;
        uint end = offset;
        var entries = new BundleEntry[packed.Length];
        foreach (int i in DataOrder(packed.Length)) {
            var file = packed[i];
            entries[i] = new BundleEntry(file.BundlePath, offset, file.Size, (uint)file.StoredLength, file.Crc32, file.Compression);
            end = offset + (uint)file.StoredLength;
            offset = BundleFormat.Align(end);
        }

        byte[] output = new byte[end];
        BundleFormat.WriteHeader(output, BundleFormat.Align(end), tocSize);
        for (int i = 0; i < entries.Length; i++) {
            BundleFormat.WriteTocEntry(output.AsSpan(BundleFormat.HeaderSize + i * BundleFormat.TocEntrySize, BundleFormat.TocEntrySize), entries[i]);
            packed[i].Stored.AsSpan(0, packed[i].StoredLength).CopyTo(output.AsSpan((int)entries[i].Offset));
        }

        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, bundleName);
        File.WriteAllBytes(outputPath, output);
        return outputPath;
    }

    static int[] DataOrder(int count) {
        int[] tocIndices = new int[count];
        for (int i = 0; i < count; i++) {
            tocIndices[i] = i;
        }
        var order = new List<int>(count);
        AppendDataOrder(tocIndices, order);
        return [.. order];
    }

    static void AppendDataOrder(Span<int> tocIndices, List<int> order) {
        if (tocIndices.Length <= 16) {
            foreach (int index in tocIndices) {
                order.Add(index);
            }
            return;
        }
        tocIndices.Reverse();
        int half = tocIndices.Length / 2;
        AppendDataOrder(tocIndices[..half], order);
        AppendDataOrder(tocIndices[half..], order);
    }

    static PackedFile Compress(string root, string fullPath) {
        byte[] source = File.ReadAllBytes(fullPath);
        byte[] compressed = Lz4Hc.Encode(source);
        int length = compressed.Length;
        string bundlePath = Path.GetRelativePath(root, fullPath).Replace('/', '\\');
        uint crc32 = Crc32.HashToUInt32(source);
        return length < source.Length
            ? new PackedFile(bundlePath, compressed, length, (uint)source.Length, crc32, BundleFormat.CompressionLz4Hc)
            : new PackedFile(bundlePath, source, source.Length, (uint)source.Length, crc32, BundleFormat.CompressionNone);
    }

    static IEnumerable<string> EnumerateInBundleOrder(DirectoryInfo directory) {
        var files = directory.EnumerateFiles()
            .Where(f => f.Length > 0)
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            yield return file.FullName;
        }
        foreach (var subdirectory in directory.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)) {
            foreach (string file in EnumerateInBundleOrder(subdirectory)) {
                yield return file;
            }
        }
    }
}
