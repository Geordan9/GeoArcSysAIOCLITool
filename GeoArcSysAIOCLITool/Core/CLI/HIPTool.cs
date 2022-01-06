using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using ArcSysLib.Core.ArcSys;
using ArcSysLib.Core.IO.File.ArcSys;
using ArcSysLib.Util;
using GCLILib.Core;
using GCLILib.Util;
using GeoArcSysAIOCLITool.Util.Extensions;
using VFSILib.Common.Enum;
using VFSILib.Core.IO;
using static GCLILib.Util.ConsoleTools;
using static GeoArcSysAIOCLITool.AIO;
using static GeoArcSysAIOCLITool.Util.ConsoleArgumentTools;
using static GeoArcSysAIOCLITool.Util.Dialogs;

namespace GeoArcSysAIOCLITool.Core.CLI;

public static class HIPTool
{
    private static readonly ConsoleOption[] ConsoleOptions =
    {
        new()
        {
            Name = "Encoding",
            ShortOp = "-e",
            LongOp = "--encoding",
            Description =
                $"Specifies the HIP encoding to use. {{{string.Join("|", Enum.GetNames(typeof(HIP.Encoding)).Where(n => n != "Unknown"))}}}",
            HasArg = true,
            Flag = Options.Encoding,
            Func = delegate(string[] subArgs)
            {
                Encoding = subArgs.Length > 0 &&
                           (Enum.TryParse(subArgs[0], true, out HIP.Encoding encoding) ||
                            Enum.TryParse(string.Join("", subArgs), true, out encoding))
                    ? encoding
                    : HIP.Encoding.Raw;

                if (Encoding != HIP.Encoding.Raw && Encoding != HIP.Encoding.RawRepeat)
                {
                    WarningMessage("Chosen encoding is not supported yet. Defaulting to Raw...");
                    Encoding = HIP.Encoding.Raw;
                }
            }
        },
        new()
        {
            Name = "Layered",
            ShortOp = "-l",
            LongOp = "--layered",
            Description =
                "Specifies whether or not the Image is layered on a canvas.",
            HasArg = true,
            Flag = Options.Layered,
            Func = delegate(string[] subArgs)
            {
                Layered = subArgs.Length > 0 && bool.TryParse(subArgs[0], out var layered) && layered;
            }
        },
        new()
        {
            Name = "Offsets",
            ShortOp = "-off",
            LongOp = "--offsets",
            Description =
                "Specifies the X and Y offsets of a layered image.",
            HasArg = true,
            Flag = Options.Offsets,
            Func = delegate(string[] subArgs)
            {
                Offsets = new Tuple<int, int>(
                    subArgs.Length > 0 && int.TryParse(subArgs[0], out var offsetX) ? offsetX : 0,
                    subArgs.Length > 1 && int.TryParse(subArgs[1], out var offsetY) ? offsetY : 0);
            }
        },
        new()
        {
            Name = "CanvasDimensions",
            ShortOp = "-cd",
            LongOp = "--canvasdimensions",
            Description = "Specifies the width and height of a layered image's canvas.",
            HasArg = true,
            Flag = Options.CanvasDimensions,
            Func = delegate(string[] subArgs)
            {
                CanvasDimensions = new Tuple<int, int>(
                    subArgs.Length > 0 && int.TryParse(subArgs[0], out var canvasWidth) ? canvasWidth : 0,
                    subArgs.Length > 1 && int.TryParse(subArgs[1], out var canvasHeight) ? canvasHeight : 0);
            }
        },
        new()
        {
            Name = "ReferencedHIP",
            ShortOp = "-rhip",
            LongOp = "--referencedhip",
            Description =
                "Provides an existing HIP file to reference it's layered info. If no path is provided or is \"Auto\", it'll automatically find one with similar name.",
            HasArg = true,
            Flag = Options.ReferencedHIP,
            Func = delegate(string[] subArgs)
            {
                if (subArgs.Length == 0)
                {
                    subArgs = new string[1];
                    subArgs[0] = OpenFileDialog("Select Referenced HIP File...");
                    if (string.IsNullOrWhiteSpace(subArgs[0]))
                        subArgs[0] = OpenFolderDialog("Select Referenced HIP folder...");
                }

                foreach (var arg in subArgs)
                {
                    var subArg = Path.GetFullPath(arg.Replace("\"", "\\"));
                    if (subArgs.Length > 1)
                        WarningMessage(
                            $"Too many arguments for referenced HIP file or folder path. Defaulting to \"{subArg}\"...");

                    ReferencedHIPPath = subArg.ToLower() == "auto" ? "auto" : Path.GetFullPath(subArg);
                    break;
                }

                if (string.IsNullOrWhiteSpace(ReferencedHIPPath))
                {
                    if (subArgs.Length > 1)
                        WarningMessage(
                            "None of the given file or folder paths exist. Ignoring...");
                    else if (subArgs.Length == 1)
                        WarningMessage(
                            "Given file or folder path does not exist. Ignoring...");
                    else if (subArgs.Length == 0)
                        InfoMessage(
                            "No file or folder path was given. Ignoring...");
                }
                else
                {
                    if (!File.Exists(ReferencedHIPPath) && !Directory.Exists(ReferencedHIPPath))
                    {
                        WarningMessage(
                            "Given file or folder path does not exist. Ignoring...");
                        ReferencedHIPPath = string.Empty;
                    }
                }
            }
        },
        new()
        {
            Name = "KeepCanvas",
            ShortOp = "-kc",
            LongOp = "--keepcanvas",
            Description =
                "If decoding, will keep the dimensions of the canvas and offset the image inside.",
            Flag = Options.KeepCanvas
        },
        new()
        {
            Name = "Transparent",
            ShortOp = "-t",
            LongOp = "--transparent",
            Description = "If decoding, will make the first color, in the image palette, transparent.",
            Flag = Options.Transparent
        }
    };

    private static Options options;
    private static HIP.Encoding Encoding = HIP.Encoding.Raw;
    private static bool Layered;
    private static Tuple<int, int> Offsets = new(0, 0);
    private static Tuple<int, int> CanvasDimensions = new(0, 0);
    private static string ReferencedHIPPath = string.Empty;

    [STAThread]
    public static void Main(string[] args)
    {
        DefaultCLIMainBlock(args, options, delegate(string[] args)
        {
            if (ShouldGetUsage(args) || !SetFirstArgumentAsPath(ref args, null,
                    $"Supported Files|{FileFilterDict["ArcSysImage"]};{FileFilterDict["NativeImage"]};*.dds;{FileFilterDict["ArcSysDirectory"]}|" +
                    $"ArcSys Images|{FileFilterDict["ArcSysImage"]}|" +
                    $"ArcSys Directories|{FileFilterDict["ArcSysDirectory"]}|" +
                    $"Native Images|{FileFilterDict["NativeImage"]}|" +
                    "All Files|*.*"))
            {
                ShowUsage();
                return;
            }

            var path = Path.GetFullPath(args[0]);

            options = (Options) ((int) ProcessOptions<Options>(args, ConsoleOptions) |
                                 (int) ProcessOptions<AIO.FileOptions>(args, FileConsoleOptions) |
                                 (int) ProcessOptions<GlobalOptions>(args, GlobalConsoleOptions));

            var attr = new FileAttributes();
            try
            {
                attr = File.GetAttributes(path);
            }
            catch (Exception ex)
            {
                ErrorMessage(ex.Message);
                return;
            }

            var pacFileInfo = new PACFileInfo(path);
            if (attr.HasFlag(FileAttributes.Directory))
            {
                var files = new DirectoryInfo(path).GetFilesRecursive();
                foreach (var file in files) ProcessFile(new VirtualFileSystemInfo(file.FullName));
            }
            else if (pacFileInfo.IsValidPAC)
            {
                var files = pacFileInfo.GetFilesRecursive();
                foreach (var file in files) ProcessFile(file);
            }
            else
            {
                ProcessFile(new VirtualFileSystemInfo(path));
            }

            CompleteMessage();
        }, "Mode: Geo ArcSys HIP Tool");
    }

    public static void ProcessFile(VirtualFileSystemInfo vfsi)
    {
        var fileName = vfsi.Name;
        DefaultCLIProcessFileBlock(vfsi, delegate(VirtualFileSystemInfo vfsi)
        {
            var completed = false;
            var ext = vfsi.Extension;
            var path = vfsi.FullName;
            var savePath = AdjustSavePathFromVFSI(vfsi,
                string.IsNullOrWhiteSpace(OutputPath) ? Path.GetDirectoryName(path) : OutputPath);
            var saveDir = Path.GetDirectoryName(savePath);
            HIPFileInfo hipFileInfo;
            if (ImageTools.NativeImageExtensions.Contains(ext) || ext == ".dds")
            {
                fileName = Path.GetFileNameWithoutExtension(path) + ".hip";
                savePath = Path.Combine(saveDir, fileName);

                if (options.HasFlag(Options.ReferencedHIP))
                {
                    if (string.IsNullOrWhiteSpace(ReferencedHIPPath) || ReferencedHIPPath == "auto")
                        ReferencedHIPPath =
                            Path.Combine(saveDir, Path.GetFileNameWithoutExtension(path) + ".hip");

                    if (File.Exists(ReferencedHIPPath) || Directory.Exists(ReferencedHIPPath))
                    {
                        var referencedHIPFilePath = ReferencedHIPPath;
                        if (File.GetAttributes(ReferencedHIPPath).HasFlag(FileAttributes.Directory))
                            referencedHIPFilePath = Path.Combine(ReferencedHIPPath,
                                Path.GetFileNameWithoutExtension(path) + ".hip");

                        var referencedHIPFileInfo = new HIPFileInfo(referencedHIPFilePath);
                        var referencedHIP = referencedHIPFileInfo.HIPFile;
                        hipFileInfo = new HIPFileInfo(path, Encoding, ref referencedHIP,
                            Endianness ?? ByteOrder.LittleEndian);
                    }
                    else
                    {
                        WarningMessage($"\"{ReferencedHIPPath}\" does not exist. Ignoring...");
                        hipFileInfo = new HIPFileInfo(path, Encoding, Layered, Offsets.Item1, Offsets.Item2,
                            CanvasDimensions.Item1, CanvasDimensions.Item2, Endianness ?? ByteOrder.LittleEndian);
                    }
                }
                else
                {
                    hipFileInfo = new HIPFileInfo(path, Encoding, Layered, Offsets.Item1, Offsets.Item2,
                        CanvasDimensions.Item1, CanvasDimensions.Item2, Endianness ?? ByteOrder.LittleEndian);
                }

                completed = WriteFile(savePath, hipFileInfo.GetBytes(), options);
            }
            else
            {
                hipFileInfo = new HIPFileInfo(path);
                if (hipFileInfo.IsValidHIP)
                {
                    fileName = Path.GetFileNameWithoutExtension(path) + ".png";
                    savePath = Path.Combine(saveDir, fileName);

                    completed = WriteFile(savePath,
                        delegate(string path)
                        {
                            using var bitmap = hipFileInfo.GetImage(options.HasFlag(Options.KeepCanvas));
                            if (options.HasFlag(Options.Transparent))
                            {
                                var palette = bitmap.Palette;
                                if (palette.Entries.Length > 0)
                                {
                                    palette.Entries[0] = Color.Transparent;
                                    bitmap.Palette = palette;
                                }
                            }

                            bitmap.Save(path, ImageFormat.Png);
                        }, options);
                }
                else
                {
                    WarningMessage($"{hipFileInfo.Name} is not a valid HIP file. Skipping...");
                }

                if (completed)
                    fileName = Path.GetFileName(path);
            }
        }, ref fileName);
    }

    private static void ShowUsage()
    {
        ConsoleTools.ShowUsage(
            $"Usage: {Path.GetFileName(AssemblyPath)} {CLIArg} <file/folder path> [options...]",
            ConsoleOptions.Concat(FileConsoleOptions).Concat(GlobalConsoleOptions).ToArray());
    }

    [Flags]
    private enum Options
    {
        Encoding = 0x1,
        Layered = 0x2,
        Offsets = 0x4,
        CanvasDimensions = 0x8,
        ReferencedHIP = 0x10,
        KeepCanvas = 0x20,
        Transparent = 0x40
    }
}