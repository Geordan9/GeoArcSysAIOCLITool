using System;
using System.IO;
using System.IO.Compression;

namespace GeoArcSysAIOCLITool.Util.Extensions;

public static class StreamExtention
{
    public static MemoryStream GZipDecompressStream(this Stream s)
    {
        using Stream input = new GZipStream(s,
            CompressionMode.Decompress, true);
        using var output = new MemoryStream();
        input.CopyTo(output);
        input.Close();
        return new MemoryStream(output.ToArray());
    }

    public static MemoryStream GZipCompressStream(this Stream s)
    {
        var output = new MemoryStream();
        using (Stream input = new GZipStream(output,
                   CompressionLevel.Optimal, true))
        {
            s.CopyTo(input);
            input.Close();
        }

        return output;
    }

    public static byte[] ReadToEnd(this Stream stream)
    {
        long originalPosition = 0;

        if (stream.CanSeek)
        {
            originalPosition = stream.Position;
            stream.Position = 0;
        }

        try
        {
            var readBuffer = new byte[4096];

            var totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = stream.Read(readBuffer, totalBytesRead, readBuffer.Length - totalBytesRead)) > 0)
            {
                totalBytesRead += bytesRead;

                if (totalBytesRead == readBuffer.Length)
                {
                    var nextByte = stream.ReadByte();
                    if (nextByte != -1)
                    {
                        var temp = new byte[readBuffer.Length * 2];
                        Buffer.BlockCopy(readBuffer, 0, temp, 0, readBuffer.Length);
                        Buffer.SetByte(temp, totalBytesRead, (byte) nextByte);
                        readBuffer = temp;
                        totalBytesRead++;
                    }
                }
            }

            var buffer = readBuffer;
            if (readBuffer.Length != totalBytesRead)
            {
                buffer = new byte[totalBytesRead];
                Buffer.BlockCopy(readBuffer, 0, buffer, 0, totalBytesRead);
            }

            return buffer;
        }
        finally
        {
            if (stream.CanSeek) stream.Position = originalPosition;
        }
    }
}