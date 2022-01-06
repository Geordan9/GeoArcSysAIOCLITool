using System;
using System.Linq;
using GeoArcSysAIOCLITool.Util;
using Steamless.API;
using Steamless.API.Model;
using Steamless.API.Services;

namespace GeoArcSysAIOCLITool.Steamless;

[SteamlessApiVersion(1, 0)]
public class AutomaticPlugin : SteamlessPlugin
{
    /// <summary>
    ///     Gets the author of this plugin.
    /// </summary>
    public override string Author => "Steamless Development Team";

    /// <summary>
    ///     Gets the name of this plugin.
    /// </summary>
    public override string Name => "Automatic";

    /// <summary>
    ///     Gets the description of this plugin.
    /// </summary>
    public override string Description => "Automatically finds which plugin to use for the given file.";

    /// <summary>
    ///     Gets the version of this plugin.
    /// </summary>
    public override Version Version => new(1, 0, 0, 0);

    /// <summary>
    ///     Initialize function called when this plugin is first loaded.
    /// </summary>
    /// <param name="logService"></param>
    /// <returns></returns>
    public override bool Initialize(LoggingService logService)
    {
        return true;
    }

    /// <summary>
    ///     Processing function called when a file is being unpacked. Allows plugins to check the file
    ///     and see if it can handle the file for its intended purpose.
    /// </summary>
    /// <param name="file"></param>
    /// <returns></returns>
    public override bool CanProcessFile(string file)
    {
        return true;
    }

    /// <summary>
    ///     Processing function called to allow the plugin to process the file.
    /// </summary>
    /// <param name="file"></param>
    /// <param name="options"></param>
    /// <returns></returns>
    public override bool ProcessFile(string file, SteamlessOptions options)
    {
        // Obtain the plugins..
        var plugins = SteamlessTools.Plugins;
        if (plugins == null || plugins.Count == 0)
            return false;

        // Query the plugin list for a plugin to process the file..
        return (from p in plugins where p != this where p.CanProcessFile(file) select p.ProcessFile(file, options))
            .FirstOrDefault();
    }
}