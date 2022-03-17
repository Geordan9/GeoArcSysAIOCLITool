using System;
using System.Linq;

namespace GeoArcSysAIOCLITool.Util.Extensions;

public static class StringExtension
{
    public static string FirstCharToUpper(this string input)
    {
        return input switch
        {
            null => throw new ArgumentNullException(nameof(input)),
            "" => throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input)),
            _ => input.First().ToString().ToUpper() + input.Substring(1)
        };
    }

    public static string BulkRemove(this string str, string[] removeArray)
    {
        var newString = str;
        foreach (var rmv in removeArray) newString = newString.Replace(rmv, string.Empty);
        return newString;
    }
}