using LZ4;

namespace W3Bundle;

public static class Lz4Hc {
    public static byte[] Encode(byte[] source) {
        byte[] buffer = new byte[2 * LZ4Codec.MaximumOutputLength(source.Length)];
        int length = LZ4Codec.EncodeHC(source, 0, source.Length, buffer, 0, buffer.Length);
        if (length <= 0) {
            throw new InvalidOperationException($"LZ4HC failed to encode {source.Length} bytes");
        }
        return buffer[..length];
    }
}
