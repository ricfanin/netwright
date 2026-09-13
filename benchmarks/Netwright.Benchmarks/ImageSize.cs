namespace Netwright.Benchmarks;

/// <summary>Reads image dimensions from PNG or JPEG headers without decoding pixels.</summary>
public static class ImageSize
{
    public static (int Width, int Height) Read(ReadOnlySpan<byte> data)
    {
        // PNG: 8-byte signature, then IHDR chunk with big-endian width/height at offsets 16/20.
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            return (ReadBigEndian(data[16..20]), ReadBigEndian(data[20..24]));
        }

        // JPEG: scan for a start-of-frame marker (SOF0..SOF15 except DHT/JPG/DAC).
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            var offset = 2;
            while (offset + 9 < data.Length)
            {
                if (data[offset] != 0xFF)
                {
                    offset++;
                    continue;
                }

                var marker = data[offset + 1];
                var length = (data[offset + 2] << 8) | data[offset + 3];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    var height = (data[offset + 5] << 8) | data[offset + 6];
                    var width = (data[offset + 7] << 8) | data[offset + 8];
                    return (width, height);
                }

                offset += 2 + length;
            }
        }

        return (0, 0);
    }

    private static int ReadBigEndian(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
}
