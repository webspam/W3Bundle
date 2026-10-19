using System.Globalization;
using System.Text;

namespace W3Bundle.Tests;

static class TestData {
    public static byte[] Text(int length, int seed = 0) {
        var builder = new StringBuilder();
        while (builder.Length < length) {
            builder.Append(CultureInfo.InvariantCulture, $"<entry index=\"{seed}\" value=\"{seed * 7919 % 10007}\" />\n");
        }
        return Encoding.ASCII.GetBytes(builder.ToString(0, length));
    }

    public static byte[] Noise(int length, int seed = 0) {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public static IEnumerable<int> DocumentedDataOrder(int[] tocIndices) {
        if (tocIndices.Length <= 16) return tocIndices;
        int[] reversed = [.. Enumerable.Reverse(tocIndices)];
        int half = reversed.Length / 2;
        return DocumentedDataOrder(reversed[..half]).Concat(DocumentedDataOrder(reversed[half..]));
    }
}
