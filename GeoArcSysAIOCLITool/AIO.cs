using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ArcSysLib.Util;
using GCLILib.Common.Enum;
using GCLILib.Core;
using GeoArcSysAIOCLITool.Core;
using GeoArcSysAIOCLITool.Core.CLI;
using GeoArcSysAIOCLITool.Util.Extensions;
using VFSILib.Common.Enum;
using VFSILib.Core.IO;
using static GCLILib.Util.ConsoleTools;
using static GeoArcSysAIOCLITool.Util.Dialogs;

namespace GeoArcSysAIOCLITool;

internal class AIO
{
    [Flags]
    public enum FileOptions
    {
        Endianness = 0x1000000,
        Output = 0x2000000,
        OverwriteMode = 0x4000000,
        Backup = 0x8000000
    }

    [Flags]
    public enum GlobalOptions
    {
        Continue = 0x10000000
    }

    public static CLIMode[] CLIModes =
    {
        new()
        {
            ID = "Crypt",
            Aliases = new[]
            {
                "CryptTool"
            },
            Description = "Used to obfuscate and deobfuscate files.",
            Func = CryptTool.Main
        },
        new()
        {
            ID = "PAC",
            Aliases = new[]
            {
                "PACker",
                "PACTool"
            },
            Description = "Used to pack and unpack the PAC file format.",
            Func = PACker.Main
        },
        new()
        {
            ID = "HIP",
            Aliases = new[]
            {
                "HIPTool"
            },
            Description = "Used to encode and decode the HIP file format.",
            Func = HIPTool.Main
        },
        new()
        {
            ID = "Palette",
            Aliases = new[]
            {
                "PaletteConverter"
            },
            Description = "Used to convert a palette into another format.",
            Func = PaletteConverter.Main
        },
        new()
        {
            ID = "PS3",
            Aliases = new[]
            {
                "PS3Extractor"
            },
            Description = "Used to extract the game assets from the bddata.bin file.",
            Func = PS3Extractor.Main
        },
        new()
        {
            ID = "Patch",
            Aliases = new[]
            {
                "Patcher"
            },
            Description = "Used to apply patches to PE files or in memory.",
            Func = Patcher.Main
        }
    };

    public static ConsoleOption[] GlobalConsoleOptions =
    {
        new()
        {
            Name = "Continue",
            ShortOp = "-c",
            LongOp = "--continue",
            Description = "Don't pause the application when finished.",
            Flag = GlobalOptions.Continue
        }
    };

    public static ConsoleOption[] FileConsoleOptions =
    {
        new()
        {
            Name = "Endianness",
            ShortOp = "-en",
            LongOp = "--endianness",
            Description = "Specify the output file's endianness.",
            HasArg = true,
            Flag = FileOptions.Endianness,
            Func = delegate(string[] subArgs)
            {
                Endianness = subArgs.Length > 0 && (Enum.TryParse(subArgs[0], true, out ByteOrder endian) ||
                                                    Enum.TryParse(string.Join("", subArgs), true, out endian))
                    ? endian
                    : ByteOrder.LittleEndian;
            }
        },
        new()
        {
            Name = "Output",
            ShortOp = "-o",
            LongOp = "--output",
            Description = "Specifies the output directory for the output files.",
            HasArg = true,
            Flag = FileOptions.Output,
            Func = delegate(string[] subArgs)
            {
                if (subArgs.Length == 0)
                {
                    subArgs = new string[1];
                    subArgs[0] = OpenFolderDialog("Select output folder...");
                }

                foreach (var arg in subArgs)
                {
                    var subArg = Path.GetFullPath(arg.Replace("\"", "\\"));
                    if (subArgs.Length > 1)
                        WarningMessage(
                            $"Too many arguments for output path. Defaulting to \"{subArg}\"...");
                    OutputPath = Path.GetFullPath(subArg);
                    break;
                }

                if (string.IsNullOrWhiteSpace(OutputPath))
                {
                    if (subArgs.Length > 1)
                        WarningMessage(
                            "None of the given output paths exist or could be created. Ignoring...");
                    else if (subArgs.Length == 1)
                        WarningMessage(
                            "Given output path does not exist. Ignoring...");
                    else if (subArgs.Length == 0)
                        InfoMessage(
                            "No output path was given. Ignoring...");
                }
            }
        },
        new()
        {
            Name = "OverwriteMode",
            ShortOp = "-om",
            LongOp = "--overwritemode",
            Description =
                $"Specify whether to overwrite or skip all already existing files. {{{string.Join("|", Enum.GetNames(typeof(OverwriteMode)))}}}",
            HasArg = true,
            Flag = FileOptions.OverwriteMode,
            Func = delegate(string[] subArgs)
            {
                OverwriteMode = subArgs.Length > 0 && Enum.TryParse(subArgs[0], true, out OverwriteMode om)
                    ? om
                    : OverwriteMode.Default;
            }
        },
        new()
        {
            Name = "Backup",
            ShortOp = "-bak",
            LongOp = "--backup",
            Description =
                "If file is overwritten, create a backup.",
            Flag = FileOptions.Backup
        }
    };

    public static VirtualFileSystemInfo CurrentFile;

    public static ByteOrder? Endianness;
    public static OverwriteMode OverwriteMode = OverwriteMode.Default;
    public static string OutputPath = string.Empty;
    public static string AssemblyPath = string.Empty;
    public static string CLIArg = string.Empty;

    public static ConsoleColor DefaultConsoleColor = Console.ForegroundColor;

    public static Dictionary<string, string> FileFilterDict = new()
    {
        {"Palette", "*.act;*.pal"},
        {"Swatches", "*.aco;*.ase"},
        {"ArcSysPalette", "*.hpl;*pal.pac"},
        {"ArcSysImage", "*.hip;*img.pac;*vri.pac"},
        {"ArcSysDirectory", "*.pac;*.paccs;*.pacgz"},
        {"NativeImage", $"*.{string.Join(";*.", ImageTools.NativeImageExtensions)}"}
    };

    private static readonly IDictionary<string, Assembly> possibleAssemblyDict = new Dictionary<string, Assembly>();

    [STAThread]
    private static void Main(string[] args)
    {
        var codeBase = Assembly.GetExecutingAssembly().CodeBase;
        var uri = new UriBuilder(codeBase);
        AssemblyPath = Path.GetFullPath(Uri.UnescapeDataString(uri.Path));
        var possibleLibPath = Path.Combine(Path.GetDirectoryName(AssemblyPath), "Lib");
        if (Directory.Exists(possibleLibPath))
        {
            var optionalAssemblies =
                new DirectoryInfo(possibleLibPath).GetFilesRecursive();
            foreach (var fi in optionalAssemblies)
                try
                {
                    var assembly = Assembly.Load(File.ReadAllBytes(fi.FullName));
                    possibleAssemblyDict.Add(assembly.FullName, assembly);
                }
                catch
                {
                }

            if (optionalAssemblies.Length > 0)
            {
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += ResolvePossibleAssembly;
                AppDomain.CurrentDomain.AssemblyResolve += ResolvePossibleAssembly;
            }
        }

        SubtleMessage("\nGeo ArcSys AIO CLI Tool\nprogrammed by: Geo\n");

        try
        {
            if (ShouldGetUsage(args))
            {
                ShowUsage();
                Pause();
                return;
            }

            CLIArg = args[0];
            var subArgs = args.Skip(1).ToArray();
            var clim = CLIModes.Where(clim => clim.ID.Equals(CLIArg, StringComparison.OrdinalIgnoreCase) ||
                                              clim.Aliases.Any(
                                                  a => a.Equals(CLIArg, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault();

            if (clim == null)
            {
                ShowUsage();
                Pause();
                return;
            }

            CLIArg = clim.ID;

            clim.Func.Invoke(subArgs);
        }
        catch (Exception ex)
        {
            ErrorMessage(ex.ToString());
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine($"Usage: {Path.GetFileName(AssemblyPath)} <CLIMode> [args...]");
        var nameMaxLength =
            CLIModes.Select(clim => clim.ID).OrderByDescending(s => s.Length).First().Length;

        Console.WriteLine("CLI Modes:");
        Console.WriteLine(PadElementsInLines(
            CLIModes.Select(clim => new[] {clim.ID, $"({string.Join("|", clim.Aliases)})", clim.Description})
                .ToArray(),
            nameMaxLength, Console.WindowWidth));
    }

    public static void DefaultCLIMainBlock<T>(string[] args, T options, Action<string[]> code,
        string subtleMessage = "") where T : Enum
    {
        SubtleMessage(subtleMessage + "\n\n");

        try
        {
            code.Invoke(args);
        }
        catch (Exception ex)
        {
            var fileText = string.Empty;
            if (CurrentFile != null)
                fileText = $"Current File: {CurrentFile.Name}\n";
            ErrorMessage(fileText + ex);
        }
        finally
        {
            Pause(options.HasFlag((T) (object) GlobalOptions.Continue));
        }
    }

    public static void DefaultCLIProcessFileBlock(VirtualFileSystemInfo vfsi, Action<VirtualFileSystemInfo> code)
    {
        var fileName = vfsi.Name;
        DefaultCLIProcessFileBlock(vfsi, code, ref DefaultConsoleColor, ref fileName);
    }

    public static void DefaultCLIProcessFileBlock(VirtualFileSystemInfo vfsi, Action<VirtualFileSystemInfo> code,
        ref ConsoleColor fileColor)
    {
        var fileName = vfsi.Name;
        DefaultCLIProcessFileBlock(vfsi, code, ref fileColor, ref fileName);
    }

    public static void DefaultCLIProcessFileBlock(VirtualFileSystemInfo vfsi, Action<VirtualFileSystemInfo> code,
        ref string fileName)
    {
        DefaultCLIProcessFileBlock(vfsi, code, ref DefaultConsoleColor, ref fileName);
    }

    public static void DefaultCLIProcessFileBlock(VirtualFileSystemInfo vfsi, Action<VirtualFileSystemInfo> code,
        ref ConsoleColor fileColor, ref string fileName)
    {
        CurrentFile = vfsi;
        Console.Write("Processing ");
        var origColor = Console.ForegroundColor;
        Console.ForegroundColor = fileColor;
        Console.Write(vfsi.Name);
        Console.ForegroundColor = origColor;
        Console.WriteLine("...");

        code.Invoke(vfsi);

        Console.Write("Finished processing ");
        origColor = Console.ForegroundColor;
        Console.ForegroundColor = fileColor;
        Console.Write(fileName);
        Console.ForegroundColor = origColor;
        Console.WriteLine(".");
    }

    public static bool WriteFile<T>(string path, byte[] bytes, T options = default) where T : Enum
    {
        return WriteFile(path, delegate(string path) { File.WriteAllBytes(path, bytes); }, options);
    }

    public static bool WriteFile<T>(string path, Action<string> code, T options = default) where T : Enum
    {
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 0 &&
                !OverwritePrompt(path, ref OverwriteMode))
            {
                InfoMessage($"{Path.GetFileName(path)} already exists. Skipping...");
                return false;
            }

            if (options.HasFlag((T) (object) FileOptions.Backup))
            {
                var backupPath = path + ".bak";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Copy(path, backupPath);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        code.Invoke(path);

        return true;
    }

    public static string AdjustSavePathFromVFSI(VirtualFileSystemInfo vfsi, string savePath)
    {
        if (vfsi == vfsi.VirtualRoot)
            return Path.Combine(savePath, vfsi.Name);
        var rootExt = vfsi.VirtualRoot.Extension;
        var isDirectory = vfsi.Extension == rootExt && vfsi != vfsi.VirtualRoot;
        savePath = Path.Combine(
            savePath,
            Path.GetFileNameWithoutExtension(vfsi.VirtualRoot.Name) +
            (string.IsNullOrWhiteSpace(rootExt) && isDirectory ? "_unpacked" : string.Empty),
            isDirectory
                ? vfsi.ExtendedToSimplePath().Replace(vfsi.Extension, string.Empty)
                : vfsi.ExtendedToSimplePath()
        ).Replace("?", "%3F");

        return savePath;
    }

    private static Assembly ResolvePossibleAssembly(object sender, ResolveEventArgs e)
    {
        possibleAssemblyDict.TryGetValue(e.Name, out var res);
        return res;
    }
}