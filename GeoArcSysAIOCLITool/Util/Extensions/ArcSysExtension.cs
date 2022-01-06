using System;
using System.Collections.Generic;
using ArcSysLib.Core.IO.File.ArcSys;
using static ArcSysLib.Core.IO.File.ArcSys.ArcSysFileSystemInfo;

namespace GeoArcSysAIOCLITool.Util.Extensions;

public static class ArcSysExtension
{
    public static ConsoleColor? GetTextColor(this ArcSysFileSystemInfo vfsi)
    {
        return vfsi.Obfuscation switch
        {
            FileObfuscation.BBTAGEncryption or FileObfuscation.FPACEncryption => ConsoleColor.Green,
            FileObfuscation.FPACDeflation or FileObfuscation.SwitchCompression => ConsoleColor.Cyan,
            FileObfuscation.FPACEncryption |
                FileObfuscation.FPACDeflation => ConsoleColor.Magenta,
            _ => null
        };
    }

    public static ArcSysFileSystemInfo[] GetFilesRecursive(this PACFileInfo pfi)
    {
        var vfiles = new List<ArcSysFileSystemInfo>();
        vfiles.AddRange(pfi.GetFiles());

        var len = vfiles.Count;

        for (var i = 0; i < len; i++)
            if (vfiles[i] is PACFileInfo pacFileInfo)
                vfiles.AddRange(GetFilesRecursive(pacFileInfo));

        return vfiles.ToArray();
    }
}