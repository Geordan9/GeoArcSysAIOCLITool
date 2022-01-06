using System.Collections.Generic;
using System.IO;

namespace GeoArcSysAIOCLITool.Util.Extensions;

public static class DirectoryInfoExtension
{
    public static FileInfo[] GetFilesRecursive(this DirectoryInfo dInfo)
    {
        var fileInfoList = new List<FileInfo>();
        foreach (var f in dInfo.GetFiles()) fileInfoList.Add(f);
        foreach (var d in dInfo.GetDirectories()) fileInfoList.AddRange(GetFilesRecursive(d));

        return fileInfoList.ToArray();
    }
}