using System.Text;

namespace W3Bundle.Tests;

sealed record StoredFile(uint PathOffset, uint CompressedSize, uint Size, uint FirstEntry, uint FirstBuffer, uint BufferCount, uint Compression);

sealed record StoredEntry(uint Offset, uint CompressedSize, uint File, uint Bundle);

sealed record StoredBundle(uint DataBlockSize, uint DataStart, uint NameOffset, uint FirstFile, uint FileCount);

sealed record StoredDirectory(uint NameOffset, uint Parent);

sealed record StoredFileDirectory(uint File, uint Directory, uint NameOffset);

sealed record StoredHash(ulong Hash, uint File, int Unknown);

sealed record ParsedMetadataStore(
    uint Magic,
    uint Version,
    uint MaxCompressedSize,
    uint MaxSize,
    byte[] Strings,
    StoredFile[] Files,
    StoredEntry[] Entries,
    StoredBundle[] Bundles,
    uint[] Buffers,
    StoredDirectory[] Directories,
    StoredFileDirectory[] FileDirectories,
    StoredHash[] Hashes) {

    public string StringAt(uint offset) {
        int end = Array.IndexOf(Strings, (byte)0, (int)offset);
        return Encoding.UTF8.GetString(Strings, (int)offset, end - (int)offset);
    }

    public string PathOf(uint file) => StringAt(Files[file].PathOffset);
}

static class MetadataStoreReader {
    public const byte EmptyCount = 0x80;

    public static ParsedMetadataStore Parse(byte[] store) {
        using var reader = new BinaryReader(new MemoryStream(store));
        uint magic = reader.ReadUInt32();
        uint version = reader.ReadUInt32();
        uint maxCompressedSize = reader.ReadUInt32();
        uint maxSize = reader.ReadUInt32();
        byte[] strings = reader.ReadBytes(ReadCount(reader));

        var files = ReadArray(reader, () => {
            uint pathOffset = reader.ReadUInt32();
            ReadZero(reader);
            return new StoredFile(pathOffset, reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
        });
        var entries = ReadArray(reader, () => {
            uint offset = reader.ReadUInt32();
            ReadZero(reader);
            uint compressedSize = reader.ReadUInt32();
            ReadZero(reader);
            return new StoredEntry(offset, compressedSize, reader.ReadUInt32(), reader.ReadUInt32());
        });
        var bundles = ReadArray(reader, () => {
            uint dataBlockSize = reader.ReadUInt32();
            ReadZero(reader);
            return new StoredBundle(dataBlockSize, reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
        });
        var buffers = ReadArray(reader, reader.ReadUInt32);
        var directories = ReadArray(reader, () => new StoredDirectory(reader.ReadUInt32(), reader.ReadUInt32()));
        var fileDirectories = ReadArray(reader, () => new StoredFileDirectory(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()));
        var hashes = ReadArray(reader, () => new StoredHash(reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadInt32()));

        if (reader.BaseStream.Position != store.Length) {
            throw new InvalidDataException($"{store.Length - reader.BaseStream.Position} trailing bytes");
        }
        return new ParsedMetadataStore(magic, version, maxCompressedSize, maxSize, strings, files, entries, bundles, buffers, directories, fileDirectories, hashes);
    }

    static T[] ReadArray<T>(BinaryReader reader, Func<T> readElement) {
        int count = ReadCount(reader);
        var elements = new T[count];
        for (int i = 0; i < count; i++) {
            elements[i] = readElement();
        }
        return elements;
    }

    static int ReadCount(BinaryReader reader) {
        byte b = reader.ReadByte();
        if (b == EmptyCount) {
            return 0;
        }
        if ((b & 0x80) != 0) {
            throw new InvalidDataException("Count with bit7 set");
        }
        int value = b & 0x3F;
        int shift = 6;
        bool more = (b & 0x40) != 0;
        while (more) {
            b = reader.ReadByte();
            value |= (b & 0x7F) << shift;
            shift += 7;
            more = (b & 0x80) != 0;
        }
        return value;
    }

    static void ReadZero(BinaryReader reader) {
        uint value = reader.ReadUInt32();
        if (value != 0) {
            throw new InvalidDataException($"Expected 0, found {value} at {reader.BaseStream.Position - 4}");
        }
    }
}
