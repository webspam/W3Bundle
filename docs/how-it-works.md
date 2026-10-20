# How W3Bundle works

## LZ4HC

wcc_lite's output is consistent with a pre-1.8 LZ4HC. [lz4net](https://www.nuget.org/packages/lz4net) `LZ4Codec.EncodeHC` matched `wcc_lite` byte-for-byte on every file tested.

Modern LZ4HC (K4os, any level) did not: it allows a final match at `iend - MFLIMIT`, and resolves overlapping matches differently.

## Bundles

| Bundle            | Contents                                    |
| ----------------- | ------------------------------------------- |
| `blob0.bundle`    | Every file except `*.buffer`                |
| `buffers0.bundle` | `*.buffer` files. Omitted when there are none |

Both use the same format, TOC order and data order rules, independently.

## Bundle format

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

Arrays are prefixed with a CR2W-style compressed count: first byte `bit7` sign, `bit6` continue, 6 value bits; following bytes `bit7` continue, 7 value bits. An empty array is written as `0x80`.

Strings are UTF-8 in original case.

Bundles are read in name order (`blob0.bundle`, `buffers0.bundle`). Files are listed bundle by bundle, each in **data offset order**, 1-based (index 0 is an all-zero null record).

N = all files, M = non-buffer files.

| Section          | Layout                                                                                                                                                                  |
| ---------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Header           | u32 magic `0x4D545603` (`\x03VTM`), u32 version 7, u32 max compressed size, u32 max uncompressed size                                                                   |
| Strings          | count = byte length; NUL-terminated strings                                                                                                                             |
| Files            | count = N + 1; 8 × u32: path string offset, 0, compressed size, uncompressed size, first entry index (= file index), first buffer, buffer count, compression           |
| Entries          | count = N + 1; 6 × u32: data offset, 0, compressed size, 0, file index, bundle index                                                                                    |
| Bundles          | count = bundles + 1; 6 × u32: data block size (aligned bundle size − data start, `0` for `buffers*.bundle`), 0, data start, name string offset, first file, file count |
| Buffers          | count = total slots; u32 file index per slot, `0xFFFFFFFF` for a missing buffer ID                                                                                      |
| Directories      | count = D; 2 × u32: name string offset, parent index. Index 0 is root (`""`, parent 0)                                                                                  |
| File directories | count = M; 3 × u32: file index, directory index, string offset of file name (byte offset inside the full path string)                                                  |
| Hashes           | count = M; u64 path hash, u32 file index, i32 unknown. Sorted by hash                                                                                                   |

String table order: `""`, then per bundle: bundle name, file paths (file order); then `""` (root directory, not deduplicated), directory names (directory order).

### Directories

Created walking non-buffer files in file order, children in order of first appearance. Written depth-first, pre-order.

### Buffers

- `<master>.<id>.buffer`, id ≥ 1, belongs to the non-buffer file `<master>`
- Per master, in file order: `buffer count` = highest id, `first buffer` = index of its first slot, slot `id − 1` = buffer file index
- Files without buffers: first buffer 0, buffer count 0
- wcc_lite fails (and so does W3Bundle) on a buffer without an id (`Buffer file without a number`) or without a master (`No master file for buffer`)

### Hashes

- FNV-1a 64 over the UTF-8 path with ASCII `A-Z` lowercased only (`É` is unchanged)
- Unknown i32 (copied from wcc_lite): with no buffers, `0` for file 1 and `-1` for all others; with buffers, `0` for all
