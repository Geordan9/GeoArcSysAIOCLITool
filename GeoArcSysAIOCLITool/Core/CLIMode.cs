using System;

namespace GeoArcSysAIOCLITool.Core;

public class CLIMode
{
    public string ID { get; set; }

    public string[] Aliases { get; set; } = new string[0];

    public string Description { get; set; }

    public Action<string[]> Func { get; set; }
}