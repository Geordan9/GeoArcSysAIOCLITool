using System.IO;
using System.Linq;
using VFSILib.Core.IO;

namespace GeoArcSysAIOCLITool.Util.Extensions;

public static class VirtualFileSystemInfoExtension
{
    public static string ExtendedToSimplePath(this VirtualFileSystemInfo vfsi)
    {
        var extPaths = vfsi.GetExtendedPaths();
        return Path.Combine(
            string.Join("\\",
                extPaths.Take(extPaths.Length - 1).Select(ep => Path.GetFileNameWithoutExtension(ep))), vfsi.Name);
    }
}