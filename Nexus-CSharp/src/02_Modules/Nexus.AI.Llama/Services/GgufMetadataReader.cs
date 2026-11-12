using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Nexus.AI.Llama.Services;

public sealed record GgufTechnicalMetadata(
    string? Name,
    string? Architecture,
    int? ContextLength);

public static class GgufMetadataReader
{
    private static readonly byte[] GgufMagic = "GGUF"u8.ToArray();

    public static bool IsValidGguf(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) return false;

        var initialPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            Span<byte> magic = stackalloc byte[4];
            var bytesRead = stream.Read(magic);
            return bytesRead == 4 && magic.SequenceEqual(GgufMagic);
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = initialPosition;
            }
        }
    }

    public static GgufTechnicalMetadata ReadMetadata(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new InvalidDataException("Stream tidak dapat dibaca.");
        }

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        Span<byte> magic = reader.ReadBytes(4);
        if (magic.Length != 4 || !magic.SequenceEqual(GgufMagic))
        {
            throw new InvalidDataException("Stream bukan merupakan format GGUF yang valid.");
        }

        uint version = reader.ReadUInt32();
        if (version is < 2 or > 3)
        {
            // Versi GGUF standar adalah v2 atau v3
            throw new InvalidDataException($"Versi GGUF {version} tidak didukung.");
        }

        ulong tensorCount = reader.ReadUInt64();
        ulong metadataKvCount = reader.ReadUInt64();

        string? name = null;
        string? architecture = null;
        int? contextLength = null;

        for (ulong i = 0; i < metadataKvCount; i++)
        {
            string key = ReadGgufString(reader);
            uint valueType = reader.ReadUInt32();

            if (string.Equals(key, "general.name", StringComparison.OrdinalIgnoreCase))
            {
                if (valueType == 8) // STRING
                {
                    name = ReadGgufString(reader);
                }
                else
                {
                    SkipValue(reader, valueType);
                }
            }
            else if (string.Equals(key, "general.architecture", StringComparison.OrdinalIgnoreCase))
            {
                if (valueType == 8) // STRING
                {
                    architecture = ReadGgufString(reader);
                }
                else
                {
                    SkipValue(reader, valueType);
                }
            }
            else if (key.EndsWith(".context_length", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(key, "context_length", StringComparison.OrdinalIgnoreCase))
            {
                contextLength = ReadNumericAsInt(reader, valueType);
            }
            else
            {
                SkipValue(reader, valueType);
            }

            // Jika seluruh metadata yang dicari sudah ditemukan, kita bisa berhenti membaca
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(architecture) && contextLength.HasValue)
            {
                break;
            }
        }

        return new GgufTechnicalMetadata(name, architecture, contextLength);
    }

    private static string ReadGgufString(BinaryReader reader)
    {
        ulong length = reader.ReadUInt64();
        if (length > int.MaxValue)
        {
            throw new InvalidDataException("Panjang string GGUF melebihi batas yang didukung.");
        }

        byte[] bytes = reader.ReadBytes((int)length);
        if (bytes.Length != (int)length)
        {
            throw new EndOfStreamException("GGUF string terpotong sebelum selesai dibaca.");
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static int? ReadNumericAsInt(BinaryReader reader, uint valueType)
    {
        return valueType switch
        {
            4 => (int)reader.ReadUInt32(),
            5 => reader.ReadInt32(),
            10 => (int)reader.ReadUInt64(),
            11 => (int)reader.ReadInt64(),
            _ => null
        };
    }

    private static void SkipValue(BinaryReader reader, uint valueType)
    {
        switch (valueType)
        {
            case 0: // UINT8
            case 1: // INT8
            case 7: // BOOL
                reader.BaseStream.Seek(1, SeekOrigin.Current);
                break;
            case 2: // UINT16
            case 3: // INT16
                reader.BaseStream.Seek(2, SeekOrigin.Current);
                break;
            case 4: // UINT32
            case 5: // INT32
            case 6: // FLOAT32
                reader.BaseStream.Seek(4, SeekOrigin.Current);
                break;
            case 8: // STRING
                ulong strLen = reader.ReadUInt64();
                reader.BaseStream.Seek((long)strLen, SeekOrigin.Current);
                break;
            case 9: // ARRAY
                uint itemType = reader.ReadUInt32();
                ulong count = reader.ReadUInt64();
                for (ulong i = 0; i < count; i++)
                {
                    SkipValue(reader, itemType);
                }
                break;
            case 10: // UINT64
            case 11: // INT64
            case 12: // FLOAT64
                reader.BaseStream.Seek(8, SeekOrigin.Current);
                break;
            default:
                throw new InvalidDataException($"Tipe value GGUF tidak dikenal: {valueType}");
        }
    }
}
