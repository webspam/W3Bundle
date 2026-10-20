using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

using LZ4;

using TUnit.Assertions.Enums;

namespace W3Bundle.Tests;

public sealed class BundlePackerTests : IDisposable {
    readonly TempDirectory source = new();
    readonly TempDirectory output = new();

    public void Dispose() {
        source.Dispose();
        output.Dispose();
    }

    [Test]
    public async Task WritesHeader() {
        source.Write("a.txt", TestData.Text(100));
        source.Write("b.txt", TestData.Text(200));

        byte[] bundle = File.ReadAllBytes(Pack());
        uint tocSize = 2 * BundleFormat.TocEntrySize;

        using (Assert.Multiple()) {
            await Assert.That(Encoding.ASCII.GetString(bundle, 0, 8)).IsEqualTo("POTATO70");
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bundle.AsSpan(0x08))).IsEqualTo(BundleFormat.Align((uint)bundle.Length));
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bundle.AsSpan(0x0C))).IsEqualTo(0u);
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bundle.AsSpan(0x10))).IsEqualTo(tocSize);
            await Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bundle.AsSpan(0x14))).IsEqualTo((ushort)5);
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bundle.AsSpan(0x16))).IsEqualTo(BundleFormat.HeaderSize + tocSize);
            await Assert.That(bundle.AsSpan(0x1A, 6).ToArray()).IsEquivalentTo(new byte[6], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task StoresLowercaseBackslashSeparatedPaths() {
        source.Write("Dir/Sub/File.TXT", TestData.Text(64));

        var entries = PackAndReadToc();

        await Assert.That(entries.Single().Path).IsEqualTo(@"dir\sub\file.txt");
    }

    [Test]
    public async Task OrdersTocFilesBeforeSubdirectoriesIgnoringCase() {
        foreach (string path in new[] { "b.txt", "A.txt", "_x.txt", "B/d.txt", "a/c.txt" }) {
            source.Write(path, TestData.Text(64));
        }

        var paths = PackAndReadToc().Select(e => e.Path);

        await Assert.That(paths).IsEquivalentTo([@"a.txt", @"b.txt", @"_x.txt", @"a\c.txt", @"b\d.txt"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task OmitsEmptyFiles() {
        source.Write("empty.txt", []);
        source.Write("text.txt", TestData.Text(64));

        var paths = PackAndReadToc().Select(e => e.Path);

        await Assert.That(paths).IsEquivalentTo(["text.txt"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task CompressesWhenLz4HcOutputIsSmaller() {
        byte[] content = TestData.Text(4096);
        source.Write("text.txt", content);

        var entry = PackAndReadToc().Single();

        using (Assert.Multiple()) {
            await Assert.That(entry.Compression).IsEqualTo(BundleFormat.CompressionLz4Hc);
            await Assert.That(entry.CompressedSize).IsEqualTo((uint)LZ4Codec.EncodeHC(content, 0, content.Length).Length);
            await Assert.That(entry.CompressedSize).IsLessThan(entry.Size);
        }
    }

    [Test]
    [Arguments(16)]
    [Arguments(4096)]
    public async Task StoresRawWhenLz4HcOutputIsNotSmaller(int size) {
        source.Write("noise.bin", TestData.Noise(size));

        var entry = PackAndReadToc().Single();

        using (Assert.Multiple()) {
            await Assert.That(entry.Compression).IsEqualTo(BundleFormat.CompressionNone);
            await Assert.That(entry.CompressedSize).IsEqualTo(entry.Size);
        }
    }

    [Test]
    public async Task StoresRawWhenLz4HcOutputIsCloseToTheBound() {
        byte[] content = TestData.Noise(10000);
        content.AsSpan(content.Length - 14).Clear();
        source.Write("noise.bin", content);

        var entry = PackAndReadToc().Single();

        using (Assert.Multiple()) {
            await Assert.That(entry.Compression).IsEqualTo(BundleFormat.CompressionNone);
            await Assert.That(entry.CompressedSize).IsEqualTo(entry.Size);
        }
    }

    [Test]
    public async Task PayloadsRoundTripWithMatchingCrc32() {
        var files = new Dictionary<string, byte[]> {
            ["text.txt"] = TestData.Text(5000),
            ["noise.bin"] = TestData.Noise(300),
            [@"d\tiny.txt"] = TestData.Text(3),
        };
        foreach (var (path, content) in files) {
            source.Write(path, content);
        }

        string bundlePath = Pack();
        byte[] bundle = File.ReadAllBytes(bundlePath);
        var (_, entries) = BundleFormat.ReadToc(bundlePath);

        using (Assert.Multiple()) {
            foreach (var entry in entries) {
                byte[] expected = files[entry.Path];
                await Assert.That(Decode(bundle, entry)).IsEquivalentTo(expected, CollectionOrdering.Matching);
                await Assert.That(entry.Crc32).IsEqualTo(Crc32.HashToUInt32(expected));
            }
        }
    }

    [Test]
    public async Task AlignsDataAndOmitsTrailingPadding() {
        for (int i = 0; i < 20; i++) {
            source.Write($"f{i:D2}.txt", TestData.Text(37 + i * 13, i));
        }

        string bundlePath = Pack();
        long bundleLength = new FileInfo(bundlePath).Length;
        var (alignedBundleSize, entries) = BundleFormat.ReadToc(bundlePath);
        uint dataEnd = entries.Max(e => e.Offset + e.CompressedSize);

        using (Assert.Multiple()) {
            await Assert.That(entries).All(e => e.Offset % BundleFormat.DataAlignment == 0);
            await Assert.That(bundleLength).IsEqualTo(dataEnd);
            await Assert.That(alignedBundleSize).IsEqualTo(BundleFormat.Align((uint)bundleLength));
        }
    }

    [Test]
    [Arguments(1)]
    [Arguments(16)]
    [Arguments(17)]
    [Arguments(33)]
    [Arguments(100)]
    [Arguments(485)]
    public async Task WritesDataInDocumentedOrder(int fileCount) {
        for (int i = 0; i < fileCount; i++) {
            source.Write($"d/f{i:D4}.txt", TestData.Text(64, i));
        }

        var entries = PackAndReadToc();
        var dataOrder = Enumerable.Range(0, entries.Count).OrderBy(i => entries[i].Offset);

        await Assert.That(dataOrder).IsEquivalentTo(TestData.DocumentedDataOrder([.. Enumerable.Range(0, fileCount)]), CollectionOrdering.Matching);
    }

    [Test]
    public async Task ThrowsWhenThereAreNoFilesToPack() {
        source.Write("empty.txt", []);

        await Assert.That(() => BundlePacker.Pack(source.Path, output.Path)).Throws<InvalidOperationException>();
    }

    string Pack() => BundlePacker.Pack(source.Path, output.Path);

    List<BundleEntry> PackAndReadToc() => BundleFormat.ReadToc(Pack()).Entries;

    static byte[] Decode(byte[] bundle, BundleEntry entry) =>
        entry.Compression == BundleFormat.CompressionNone
            ? bundle[(int)entry.Offset..(int)(entry.Offset + entry.Size)]
            : LZ4Codec.Decode(bundle, (int)entry.Offset, (int)entry.CompressedSize, (int)entry.Size);
}
