# How W3Bundle works

## LZ4HC

wcc_lite's output is consistent with a pre-1.8 LZ4HC. [lz4net](https://www.nuget.org/packages/lz4net) `LZ4Codec.EncodeHC` matched `wcc_lite` byte-for-byte on every file tested.

Modern LZ4HC (K4os, any level) did not: it allows a final match at `iend - MFLIMIT`, and resolves overlapping matches differently.

## Bundle format (`blob0.bundle`)

All integers little-endian.

### Header (0x20 bytes)

| Offset | Type    | Value                                         |
| ------ | ------- | --------------------------------------------- |
| 0x00   | char[8] | `POTATO70`                                    |
| 0x08   | u32     | Bundle size, rounded up to 16                 |
| 0x0C   | u32     | 0                                             |
| 0x10   | u32     | TOC size (entries × 304)                      |
| 0x14   | u16     | 5 (meaning unknown, copied from wcc_lite)     |
| 0x16   | u32     | Data start (0x20 + TOC size). Unaligned field |
| 0x1A   | 6 bytes | 0                                             |

### TOC entry (304 bytes)

| Offset | Type      | Value                                            |
| ------ | --------- | ------------------------------------------------ |
| 0x000  | char[256] | Path, UTF-8, backslash separated, original case, NUL padded |
| 0x100  | byte[16]  | 0 (hash slot, unused by wcc_lite)                |
| 0x110  | u32       | Data offset                                      |
| 0x114  | u32       | 0                                                |
| 0x118  | u32       | Uncompressed size                                |
| 0x11C  | u32       | Compressed size                                  |
| 0x120  | u32       | CRC32 of uncompressed data                       |
| 0x124  | u32       | Compression: 0 = stored, 5 = LZ4HC raw block     |
| 0x128  | byte[8]   | 0                                                |

### Compression

- Stored uncompressed (type 0, compressed size = size) when LZ4HC output is not smaller than the input
- 0-byte files are omitted

### TOC order

Per directory: files first, then subdirectories, each sorted `OrdinalIgnoreCase` (so `_` sorts after letters).

### Data order

Data blobs are 16-byte aligned. The file ends at the last blob without trailing padding; the header size is rounded up.

Data is not written in TOC order. Order depends only on file count (empirical):

```csharp
static IEnumerable<int> Order(int[] tocIndices) {
    if (tocIndices.Length <= 16) return tocIndices;
    int[] reversed = [.. Enumerable.Reverse(tocIndices)];
    int half = reversed.Length / 2;
    return Order(reversed[..half]).Concat(Order(reversed[half..]));
}
```

## metadata.store format

Arrays are prefixed with a CR2W-style compressed count: first byte `bit7` sign, `bit6` continue, 6 value bits; following bytes `bit7` continue, 7 value bits.

Files are listed in **data offset order**, 1-based (index 0 is an all-zero null record).

| Section          | Layout                                                                                                                                               |
| ---------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| Header           | u32 magic `0x4D545603` (`\x03VTM`), u32 version 7, u32 max compressed size, u32 max uncompressed size                                                |
| Strings          | count = byte length; NUL-terminated strings                                                                                                          |
| Files            | count = N + 1; 8 × u32: path string offset, 0, compressed size, uncompressed size, first entry index (= file index), 0, 0, compression               |
| Entries          | count = N + 1; 6 × u32: data offset, 0, compressed size, 0, file index, bundle index (1)                                                             |
| Bundles          | count = 2; 6 × u32: data block size (aligned bundle size − data start), 0, data start, name string offset, first file (1), file count                |
| Unknown          | 1 byte `0x80` (meaning unknown, copied from wcc_lite, possible terminator)                                                                           |
| Directories      | count = D; 2 × u32: name string offset, parent index. Index 0 is root (`""`, parent 0). Order of first appearance walking files                      |
| File directories | count = N; 3 × u32: file index, directory index, string offset of file name (byte offset inside the full path string)                               |
| Hashes           | count = N; u64 path hash, u32 file index, i32 `0` for file 1 and `-1` for all others (meaning unknown, copied from wcc_lite). Sorted by hash     |

Strings are UTF-8 in original case.

String table order: `""`, bundle name, file paths (file order), `""` (root directory, not deduplicated), directory names (directory order).

### Hashes

- FNV-1a 64 over the UTF-8 path with ASCII `A-Z` lowercased only (`É` is unchanged)
