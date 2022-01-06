using System.IO;
using System.Text;

namespace GeoArcSysAIOCLITool.Util.Extensions;

public static class BinaryReaderExtension
{
    public static bool GoToString(this BinaryReader reader, string str)
    {
        if (string.IsNullOrWhiteSpace(str))
            return false;

        var origPos = reader.BaseStream.Position;

        if (AWQ.Search(reader, str.ToCharArray())) return true;

        reader.BaseStream.Position = origPos;
        return false;
    }

    public static string ReadZeroTerminatedString(this BinaryReader reader)
    {
        var builder = new StringBuilder();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var b = reader.ReadByte();
            if (b == 0x0) return builder.ToString();
            builder.Append((char) b);
        }

        return builder.ToString();
    }
}