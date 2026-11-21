using System.Buffers.Binary;
using System.IO.Compression;

namespace RimBridgeServer.LiveSmoke;

internal static class PngProof
{
    // Decode the bridge's RGB/RGBA8 screenshots, including all five PNG row filters.
    // Reject other encodings rather than treating an undecoded image as evidence.
    public static (byte r, byte g, byte b) Sample(string path, double centerX, double centerY)
    {
        using var input = File.OpenRead(path);
        using var reader = new BinaryReader(input);
        if (!reader.ReadBytes(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("Not a PNG.");
        int width = 0, height = 0, channels = 0;
        using var compressed = new MemoryStream();
        while (input.Position < input.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
            var type = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (length < 0 || length > 64 * 1024 * 1024) throw new InvalidDataException("PNG chunk too large.");
            var bytes = reader.ReadBytes(length); reader.ReadBytes(4);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4, 4));
                channels = bytes[9] switch { 2 => 3, 6 => 4, _ => throw new InvalidDataException("Expected RGB/RGBA screenshot.") };
                if (bytes[8] != 8 || bytes[12] != 0 || width < 1 || height < 1 || width > 8192 || height > 8192)
                    throw new InvalidDataException("Unsupported screenshot dimensions/encoding.");
            }
            if (type == "IDAT") compressed.Write(bytes);
            if (type == "IEND") break;
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var previous = new byte[width * channels];
        var row = new byte[previous.Length];
        int targetY = Math.Clamp((int)(centerY * height), 0, height - 1), targetX = Math.Clamp((int)(centerX * width), 0, width - 1);
        for (int y = 0; y <= targetY; y++)
        {
            int filter = zlib.ReadByte();
            zlib.ReadExactly(row);
            for (int i = 0; i < row.Length; i++)
            {
                int left = i >= channels ? row[i - channels] : 0;
                int up = previous[i];
                int upperLeft = i >= channels ? previous[i - channels] : 0;
                int predictor = filter switch
                {
                    0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2,
                    4 => Paeth(left, up, upperLeft), _ => throw new InvalidDataException("Invalid PNG filter.")
                };
                row[i] = unchecked((byte)(row[i] + predictor));
            }
            if (y == targetY) return (row[targetX * channels], row[targetX * channels + 1], row[targetX * channels + 2]);
            (row, previous) = (previous, row);
        }
        throw new InvalidDataException("PNG is empty.");
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
