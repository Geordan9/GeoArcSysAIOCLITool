using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GeoArcSysAIOCLITool.Steamless;
using Steamless.API;
using Steamless.API.Model;
using Steamless.API.Services;
using static GCLILib.Util.ConsoleTools;

namespace GeoArcSysAIOCLITool.Util;

public static class SteamlessTools
{
    private static readonly Version SteamlessApiVersion = new(1, 0);

    private static readonly LoggingService LoggingService = new();

    public static ObservableCollection<SteamlessPlugin> Plugins = new();

    private static int SelectedPluginIndex = -1;

    public static bool UnpackFile(string filePath, SteamlessOptions options)
    {
        LoadPlugins();

        // Validation checks..
        if (SelectedPluginIndex == -1)
            return false;
        if (SelectedPluginIndex > Plugins.Count)
            return false;
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            // Select the plugin..
            var plugin = Plugins[SelectedPluginIndex];
            if (plugin == null)
                throw new Exception("Invalid plugin selected.");

            // Allow the plugin to process the file..
            if (plugin.CanProcessFile(filePath))
                if (!plugin.ProcessFile(filePath, options))
                {
                    ErrorMessage("Failed to unpack file.");
                }
                else
                {
                    Console.WriteLine("Successfully unpacked file!");
                    return true;
                }
            else
                ErrorMessage("Failed to unpack file.");

            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage("Caught unhandled exception trying to unpack file.\r\n" +
                         "Exception:\r\n" +
                         ex.Message);

            return false;
        }
    }

    private static void LoadPlugins()
    {
        // Obtain the list of plugins..
        var plugins = GetSteamlessPlugins();

        // Sort the plugins..
        var sorted = plugins.OrderBy(p => p.Name).ToList();

        // Print out the loaded plugins..
        sorted.ForEach(p => { InfoMessage($"Loaded plugin: {p.Name} - by {p.Author} (v.{p.Version})"); });

        // Add the automatic plugin at the start of the list..
        sorted.Insert(0, new AutomaticPlugin());

        // Set the plugins..
        Plugins = new ObservableCollection<SteamlessPlugin>(sorted);
        SelectedPluginIndex = 0;
    }

    private static List<SteamlessPlugin> GetSteamlessPlugins()
    {
        try
        {
            // The list of valid plugins..
            var plugins = new List<SteamlessPlugin>();

            // Build a path to the plugins folder..
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Steamless", "Plugins");

            // Loop the DLL files and attempt to load them..
            foreach (var dll in Directory.GetFiles(path, "*.dll"))
            {
                // Skip the Steamless.API.dll file..
                if (dll.ToLower().Contains("steamless.api.dll"))
                    continue;

                try
                {
                    // Load the assembly..
                    var asm = Assembly.Load(File.ReadAllBytes(dll));

                    // Locate the class inheriting the plugin base..
                    var baseClass = asm.GetTypes().SingleOrDefault(t => t.BaseType == typeof(SteamlessPlugin));
                    if (baseClass == null)
                    {
                        WarningMessage(
                            $"Failed to load plugin; could not find SteamlessPlugin base class. ({Path.GetFileName(dll)})");
                        continue;
                    }

                    // Locate the SteamlessApiVersion attribute on the base class..
                    var baseAttr = baseClass.GetCustomAttributes(typeof(SteamlessApiVersionAttribute), false);
                    if (baseAttr.Length == 0)
                    {
                        WarningMessage(
                            $"Failed to load plugin; could not find SteamlessApiVersion attribute. ({Path.GetFileName(dll)})");
                        continue;
                    }

                    // Validate the interface version..
                    var apiVersion = (SteamlessApiVersionAttribute) baseAttr[0];
                    if (apiVersion.Version != SteamlessApiVersion)
                    {
                        WarningMessage(
                            $"Failed to load plugin; invalid API version is being used. ({Path.GetFileName(dll)})");
                        continue;
                    }

                    // Create an instance of the plugin..
                    var plugin = (SteamlessPlugin) Activator.CreateInstance(baseClass);
                    if (!plugin.Initialize(LoggingService))
                    {
                        WarningMessage(
                            $"Failed to load plugin; plugin failed to initialize. ({Path.GetFileName(dll)})");
                        continue;
                    }

                    plugins.Add(plugin);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    StringBuilder sb = new();
                    foreach (var exSub in ex.LoaderExceptions)
                    {
                        sb.AppendLine(exSub.Message);
                        if (exSub is FileNotFoundException exFileNotFound)
                            if (!string.IsNullOrEmpty(exFileNotFound.FusionLog))
                            {
                                sb.AppendLine("Fusion Log:");
                                sb.AppendLine(exFileNotFound.FusionLog);
                            }

                        sb.AppendLine();
                    }

                    var errorMessage = sb.ToString();
                    ErrorMessage(errorMessage);
                }
            }

            // Order the plugins by their name..
            return plugins.OrderBy(p => p.Name).ToList();
        }
        catch
        {
            return new List<SteamlessPlugin>();
        }
    }
}