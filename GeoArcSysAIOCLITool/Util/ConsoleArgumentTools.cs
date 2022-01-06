using System.Collections.Generic;
using System.Linq;
using static GeoArcSysAIOCLITool.Util.Dialogs;

namespace GeoArcSysAIOCLITool.Util;

public static class ConsoleArgumentTools
{
    public static bool SetFirstArgumentAsPath(ref string[] args, string[] excludedArgs = null,
        string Filter = "All files|*.*")
    {
        var firstArgNullWhitespace = string.IsNullOrWhiteSpace(args[0]);
        if (firstArgNullWhitespace || args[0].First() == '-' ||
            excludedArgs != null && excludedArgs.Select(ea => ea.ToLower()).Contains(args[0].ToLower()))
        {
            var inputPath = OpenFileDialog("Select input file...", Filter);
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                inputPath = OpenFolderDialog("Select input folder...");
                if (string.IsNullOrWhiteSpace(inputPath)) return false;
            }

            if (firstArgNullWhitespace)
            {
                args[0] = inputPath;
            }
            else
            {
                var argsList = new List<string>(args);
                argsList.Insert(0, inputPath);
                args = argsList.ToArray();
            }
        }

        return true;
    }
}