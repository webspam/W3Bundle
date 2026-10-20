using LZ4;

namespace W3Bundle.Tests.WccLite;

sealed record WccLiteCase(string Name, Func<IEnumerable<(string RelativePath, byte[] Content)>> CreateFiles, bool KnownFailing = false) {
    public void WriteTo(TempDirectory directory) {
        foreach (var (relativePath, content) in CreateFiles()) {
            directory.Write(relativePath, content);
        }
    }

    public override string ToString() => Name;
}

static class WccLiteCases {
    public static IReadOnlyList<WccLiteCase> All { get; } = [.. DerivationCases(), .. HoldoutCases()];

    public static IEnumerable<string> Names() => All.Where(c => !c.KnownFailing).Select(c => c.Name);

    public static IEnumerable<string> KnownFailingNames() => All.Where(c => c.KnownFailing).Select(c => c.Name);

    public static WccLiteCase Get(string name) => All.Single(c => c.Name == name);

    static IEnumerable<WccLiteCase> DerivationCases() {
        int[] counts = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 15, 16, 17, 20, 24, 31, 32, 33, 48, 63, 64, 65, 100, 128, 129, 256, 485];
        foreach (int n in counts) {
            yield return new($"count-{n:D4}", () => Enumerable.Range(0, n).Select(i => ($"d\\f{i:D3}.txt", TestData.Text(64, i))));
        }

        yield return new("sizes-064", () => Enumerable.Range(0, 64).Select(i => ($"d\\f{i:D3}.txt", TestData.Text(16 + (i * 397 % 3000), i))));

        yield return new("names-064", () => {
            var random = new Random(1234);
            return Enumerable.Range(0, 64).Select(i => ($"d\\{random.Next(0x1000000):x6}.txt", TestData.Text(64, i)));
        });

        yield return new("reverse-created-064", () => Enumerable.Range(0, 64).Reverse().Select(i => ($"d\\f{i:D3}.txt", TestData.Text(64, i))));

        yield return new("two-dirs-064", () => Enumerable.Range(0, 64).Select(i => ($"{(i < 32 ? "a" : "b")}\\f{i:D3}.txt", TestData.Text(64, i))));
    }

    static IEnumerable<WccLiteCase> HoldoutCases() {
        foreach (int n in new[] { 11, 18, 47, 97, 300, 1000 }) {
            yield return CountCase(n);
        }

        int[] tinySizes = [.. Enumerable.Range(1, 32), 40, 48, 56, 64];
        yield return new("raw-tiny", () => tinySizes.Select(size => ($"d\\s{size:D3}.txt", TestData.Text(size, size))));

        yield return new("raw-random", () => RawRandomFiles(new Random(5678)));

        yield return new("raw-threshold", () => {
            var random = new Random(5678);
            _ = RawRandomFiles(random);
            return RawThresholdFiles(random);
        }, KnownFailing: true);

        yield return new("empty-file", () => [("d\\a_empty.txt", []), ("d\\b_text.txt", TestData.Text(64, 1))]);
    }

    static WccLiteCase CountCase(int n) =>
        new($"count-{n:D4}", () => Enumerable.Range(0, n).Select(i => ($"d\\f{i:D4}.txt", TestData.Text(64, i))));

    static List<(string, byte[])> RawRandomFiles(Random random) {
        int[] randomSizes = [16, 64, 256, 1024, 4096, 65536];
        return [.. randomSizes.Select(size => ($"d\\r{size:D5}.bin", RandomBytes(random, size)))];
    }

    static List<(string, byte[])> RawThresholdFiles(Random random) {
        var files = new List<(string, byte[])>();
        foreach (int size in new[] { 64, 1000, 10000 }) {
            foreach (int delta in new[] { -2, -1, 0, 1, 2 }) {
                var (content, compressed) = WithCompressedLength(random, size, size + delta);
                files.Add(($"d\\t{size:D5}_c{compressed:D5}.bin", content));
            }
        }
        foreach (double ratio in new[] { 0.90, 0.95, 0.98 }) {
            var (content, compressed) = WithCompressedLength(random, 1000, (int)(1000 * ratio));
            files.Add(($"d\\t01000_c{compressed:D5}.bin", content));
        }
        return files;
    }

    static (byte[] Content, int CompressedLength) WithCompressedLength(Random random, int size, int target) {
        byte[] noise = RandomBytes(random, size);
        (byte[] Content, int CompressedLength) best = (noise, int.MaxValue);
        for (int zeros = 0; zeros <= size; zeros++) {
            byte[] candidate = (byte[])noise.Clone();
            candidate.AsSpan(size - zeros).Clear();
            int length = LZ4Codec.EncodeHC(candidate, 0, candidate.Length).Length;
            if (length == target) return (candidate, length);
            if (Math.Abs(length - target) < Math.Abs(best.CompressedLength - target)) best = (candidate, length);
        }
        return best;
    }

    static byte[] RandomBytes(Random random, int size) {
        byte[] bytes = new byte[size];
        random.NextBytes(bytes);
        return bytes;
    }
}
