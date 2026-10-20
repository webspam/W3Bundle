using System.Text;

using TUnit.Assertions.Enums;

namespace W3Bundle.Tests;

public sealed class MetadataStoreTests : IDisposable {
    const string BundleName = "blob0.bundle";

    readonly TempDirectory source = new();
    readonly TempDirectory content = new();

    public void Dispose() {
        source.Dispose();
        content.Dispose();
    }

    [Test]
    public async Task WritesHeaderWithIndependentMaximumSizes() {
        var store = Build(
            Entry("a.txt", 0, size: 500, compressedSize: 60),
            Entry("b.txt", 1, size: 200, compressedSize: 200, BundleFormat.CompressionNone));

        using (Assert.Multiple()) {
            await Assert.That(store.Magic).IsEqualTo(0x4D545603u);
            await Assert.That(store.Version).IsEqualTo(7u);
            await Assert.That(store.MaxCompressedSize).IsEqualTo(200u);
            await Assert.That(store.MaxSize).IsEqualTo(500u);
        }
    }

    [Test]
    public async Task ListsFilesInDataOffsetOrder() {
        var store = Build(
            Entry("second.txt", 1),
            Entry("first.txt", 0));

        using (Assert.Multiple()) {
            await Assert.That(store.Files.Skip(1).Select(f => store.StringAt(f.PathOffset))).IsEquivalentTo(["first.txt", "second.txt"], CollectionOrdering.Matching);
            await Assert.That(store.Entries.Skip(1).Select(e => e.Offset)).IsInOrder();
            await Assert.That(store.Files[0]).IsEqualTo(new StoredFile(0, 0, 0, 0, 0));
            await Assert.That(store.Entries[0]).IsEqualTo(new StoredEntry(0, 0, 0, 0));
            for (uint i = 1; i < store.Files.Length; i++) {
                await Assert.That(store.Files[i].FirstEntry).IsEqualTo(i);
                await Assert.That(store.Entries[i].File).IsEqualTo(i);
                await Assert.That(store.Entries[i].Bundle).IsEqualTo(1u);
            }
        }
    }

    [Test]
    public async Task WritesSingleBundleRecord() {
        const uint alignedBundleSize = 0x1000;
        var store = Build(alignedBundleSize, Entry("a.txt", 0), Entry("b.txt", 1), Entry("c.txt", 2));
        uint dataStart = BundleFormat.HeaderSize + 3 * BundleFormat.TocEntrySize;

        using (Assert.Multiple()) {
            await Assert.That(store.Bundles).Count().IsEqualTo(2);
            await Assert.That(store.Bundles[0]).IsEqualTo(new StoredBundle(0, 0, 0, 0, 0));
            await Assert.That(store.Bundles[1] with { NameOffset = 0 }).IsEqualTo(new StoredBundle(alignedBundleSize - dataStart, dataStart, 0, 1, 3));
            await Assert.That(store.StringAt(store.Bundles[1].NameOffset)).IsEqualTo(BundleName);
            await Assert.That(store.UnknownByte).IsEqualTo((byte)0x80);
        }
    }

    [Test]
    public async Task BuildsDirectoriesInOrderOfFirstAppearance() {
        var store = Build(
            Entry("root.txt", 0),
            Entry(@"a\b\x.txt", 1),
            Entry(@"c\z.txt", 2),
            Entry(@"a\y.txt", 3));

        var directories = store.Directories.Select(d => (store.StringAt(d.NameOffset), d.Parent));
        var fileDirectories = store.FileDirectories.Select(f => (f.File, f.Directory, store.StringAt(f.NameOffset)));

        using (Assert.Multiple()) {
            await Assert.That(directories).IsEquivalentTo([("", 0u), ("a", 0u), ("b", 1u), ("c", 0u)], CollectionOrdering.Matching);
            await Assert.That(fileDirectories).IsEquivalentTo([(1u, 0u, "root.txt"), (2u, 2u, "x.txt"), (3u, 3u, "z.txt"), (4u, 1u, "y.txt")], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task ListsDirectoriesDepthFirstInOrderOfFirstAppearance() {
        var store = Build(
            Entry(@"a\x.txt", 0),
            Entry(@"b\y.txt", 1),
            Entry(@"a\c\z.txt", 2));

        var directories = store.Directories.Select(d => (store.StringAt(d.NameOffset), d.Parent));
        var fileDirectories = store.FileDirectories.Select(f => (f.File, f.Directory));

        using (Assert.Multiple()) {
            await Assert.That(directories).IsEquivalentTo([("", 0u), ("a", 0u), ("c", 1u), ("b", 0u)], CollectionOrdering.Matching);
            await Assert.That(fileDirectories).IsEquivalentTo([(1u, 1u), (2u, 3u), (3u, 2u)], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task WritesStringTableInDocumentedOrder() {
        var store = Build(
            Entry(@"a\b\x.txt", 0),
            Entry("root.txt", 1));

        await Assert.That(Strings(store)).IsEquivalentTo(["", BundleName, @"a\b\x.txt", "root.txt", "", "a", "b"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task KeepsOriginalCaseAndUtf8InStrings() {
        var store = Build(
            Entry("Dir\\reginald’s.w2mesh", 0),
            Entry("ÜDir\\Ä_OLD.txt", 1));

        var fileNames = store.FileDirectories.Select(f => store.StringAt(f.NameOffset));

        using (Assert.Multiple()) {
            await Assert.That(Strings(store)).IsEquivalentTo(["", BundleName, "Dir\\reginald’s.w2mesh", "ÜDir\\Ä_OLD.txt", "", "Dir", "ÜDir"], CollectionOrdering.Matching);
            await Assert.That(fileNames).IsEquivalentTo(["reginald’s.w2mesh", "Ä_OLD.txt"], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task SortsHashesByFnv1a64OfPath() {
        var store = Build(
            Entry("a", 0),
            Entry(@"b\c.txt", 1),
            Entry("d.txt", 2));

        var hashes = store.Hashes.Select(h => h.Hash);

        using (Assert.Multiple()) {
            await Assert.That(hashes).IsInOrder();
            await Assert.That(store.Hashes.Single(h => h.File == 1).Hash).IsEqualTo(0xaf63dc4c8601ec8cUL);
            await Assert.That(store.Hashes).All(h => h.Unknown == (h.File == 1 ? 0 : -1));
        }
    }

    [Test]
    [Arguments("Dir\\f0.txt", 0x4a5f1cbd5b9e8c58UL)]
    [Arguments("dir\\F0.TXT", 0x4a5f1cbd5b9e8c58UL)]
    [Arguments("Dir\\ÉÀ_OLD.txt", 0x05ef0a7c762435ffUL)]
    [Arguments("ÜDir\\Ä.txt", 0x16e26955c8ab43feUL)]
    public async Task HashesUtf8PathWithAsciiOnlyLowercase(string path, ulong wccLiteHash) {
        var store = Build(Entry(path, 0));

        await Assert.That(store.Hashes.Single().Hash).IsEqualTo(wccLiteHash);
    }

    [Test]
    [Arguments(63)]
    [Arguments(64)]
    [Arguments(10000)]
    public async Task EncodesCountsOfAnySize(int fileCount) {
        var entries = Enumerable.Range(0, fileCount).Select(i => Entry($"d\\f{i:D5}.txt", i)).ToArray();

        var store = Build(entries);

        using (Assert.Multiple()) {
            await Assert.That(store.Files).Count().IsEqualTo(fileCount + 1);
            await Assert.That(store.Hashes).Count().IsEqualTo(fileCount);
        }
    }

    [Test]
    public async Task GenerateWritesStoreForPackedBundle() {
        source.Write(@"d\a.txt", TestData.Text(1000));
        source.Write(@"d\b.bin", TestData.Noise(100));
        string bundlePath = BundlePacker.Pack(source.Path, content.Path).Single();

        string storePath = MetadataStore.Generate(content.Path);

        var (alignedBundleSize, entries) = BundleFormat.ReadToc(bundlePath);
        byte[] expected = MetadataStore.Build(BundleName, alignedBundleSize, entries);
        await Assert.That(File.ReadAllBytes(storePath)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    public async Task GenerateRequiresExactlyOneBundle(int bundleCount) {
        for (int i = 0; i < bundleCount; i++) {
            content.Write($"blob{i}.bundle", []);
        }

        await Assert.That(() => MetadataStore.Generate(content.Path)).Throws<NotSupportedException>();
    }

    static string[] Strings(ParsedMetadataStore store) => Encoding.UTF8.GetString(store.Strings).TrimEnd('\0').Split('\0');

    static BundleEntry Entry(string path, int dataIndex, uint size = 64, uint compressedSize = 32, uint compression = BundleFormat.CompressionLz4Hc) =>
        new(path, 0x10000 + (uint)dataIndex * 0x100, size, compressedSize, 0, compression);

    static ParsedMetadataStore Build(params BundleEntry[] tocEntries) => Build(0x100000, tocEntries);

    static ParsedMetadataStore Build(uint alignedBundleSize, params BundleEntry[] tocEntries) =>
        MetadataStoreReader.Parse(MetadataStore.Build(BundleName, alignedBundleSize, tocEntries));
}
