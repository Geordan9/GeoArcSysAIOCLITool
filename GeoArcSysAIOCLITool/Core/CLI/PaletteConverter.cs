using System;
using System.Drawing;
using System.IO;
using System.Linq;
using ArcSysLib.Core.IO.File;
using ArcSysLib.Core.IO.File.ArcSys;
using ArcSysLib.Util;
using GCLILib.Core;
using GCLILib.Util;
using GeoArcSysAIOCLITool.Util;
using GeoArcSysAIOCLITool.Util.Extensions;
using PaletteLib.Core.IO.Files;
using PaletteLib.Core.IO.Files.Adobe;
using VFSILib.Common.Enum;
using VFSILib.Core.IO;
using static GCLILib.Util.ConsoleTools;
using static GeoArcSysAIOCLITool.AIO;
using static GeoArcSysAIOCLITool.Util.ConsoleArgumentTools;

namespace GeoArcSysAIOCLITool.Core.CLI;

public static class PaletteConverter
{
    private static readonly ConsoleOption[] ConsoleOptions =
    {
        new()
        {
            Name = "Flip",
            ShortOp = "-f",
            LongOp = "--flip",
            Description =
                "Flips the converted palette.",
            Flag = Options.Flip
        }
    };

    private static Options options;
    private static PaletteFormat paletteFormat;

    private static readonly string[] supportedImageExtensions =
        ImageTools.NativeImageExtensions.Concat(new[] {".dds", ".hip"}).ToArray();

    [STAThread]
    public static void Main(string[] args)
    {
        DefaultCLIMainBlock(args, options, delegate(string[] args)
        {
            if (ShouldGetUsage(args) || !SetFirstArgumentAsPath(ref args, Enum.GetNames(typeof(PaletteFormat)),
                    $"Supported Files|{FileFilterDict["Palette"]};{FileFilterDict["Swatches"]};{FileFilterDict["ArcSysPalette"]};{FileFilterDict["ArcSysImage"]};{FileFilterDict["NativeImage"]};*.dds;{FileFilterDict["ArcSysDirectory"]}|" +
                    $"Palette Files|{FileFilterDict["Palette"]}|" +
                    $"Swatches|{FileFilterDict["Swatches"]}|" +
                    $"ArcSys Palettes|{FileFilterDict["ArcSysPalette"]}|" +
                    $"ArcSys Images|{FileFilterDict["ArcSysImage"]}|" +
                    $"ArcSys Directories|{FileFilterDict["ArcSysDirectory"]}|" +
                    $"Native Images|{FileFilterDict["NativeImage"]}|" +
                    "All files|*.*"))
            {
                ShowUsage();
                return;
            }

            if (args.Length > 0)
                if (Enum.TryParse(args[0], true, out PaletteFormat p) ||
                    args.Length > 1 && Enum.TryParse(args[1], true, out p))
                    paletteFormat = p;

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

            var noOutputPath = string.IsNullOrWhiteSpace(OutputPath);

            var pacFileInfo = new PACFileInfo(path);
            if (attr.HasFlag(FileAttributes.Directory))
            {
                if (noOutputPath) OutputPath = $"{path}_Converted";
                var files = new DirectoryInfo(path).GetFilesRecursive()
                    .Select(fi => new VirtualFileSystemInfo(fi.FullName));
                foreach (var file in files)
                {
                    pacFileInfo = new PACFileInfo(file.FullName);
                    if (pacFileInfo.IsValidPAC)
                    {
                        var cfiles = pacFileInfo.GetFilesRecursive();
                        foreach (var cfile in cfiles) ProcessFile(cfile);
                    }
                    else
                    {
                        ProcessFile(file);
                    }
                }
            }
            else if (pacFileInfo.IsValidPAC)
            {
                if (noOutputPath)
                    OutputPath = Path.Combine(Path.GetDirectoryName(path),
                        Path.GetFileNameWithoutExtension(path));
                else
                    OutputPath = Path.Combine(OutputPath,
                        Path.GetFileNameWithoutExtension(path));
                var files = pacFileInfo.GetFilesRecursive();
                foreach (var file in files) ProcessFile(file);
            }
            else
            {
                if (noOutputPath) OutputPath = Path.GetDirectoryName(path);
                ProcessFile(new VirtualFileSystemInfo(path));
            }

            CompleteMessage();
        }, "Mode: Geo ArcSys Palette Converter");
    }

    public static void ProcessFile(VirtualFileSystemInfo vfsi)
    {
        var fileName = vfsi.Name;
        DefaultCLIProcessFileBlock(vfsi, delegate(VirtualFileSystemInfo vfsi)
        {
            var ext = vfsi.Extension;
            var path = vfsi.FullName;
            var savePath = AdjustSavePathFromVFSI(vfsi, OutputPath);
            savePath = Path.Combine(Path.GetDirectoryName(savePath),
                $"{Path.GetFileNameWithoutExtension(savePath)}.{Enum.GetName(typeof(PaletteFormat), paletteFormat).ToLower()}");

            Color[] palette = null;
            var virtualFile = vfsi.VirtualRoot != vfsi ? vfsi : null;
            try
            {
                switch (ext)
                {
                    case ".hpl":
                        virtualFile ??= new HPLFileInfo(path);
                        palette = ((HPLFileInfo) virtualFile).Palette;
                        break;
                    case ".hip":
                        virtualFile ??= new HIPFileInfo(path);
                        palette = ((HIPFileInfo) virtualFile).Palette;
                        break;
                    case ".act":
                        virtualFile ??= new ACTFileInfo(path);
                        palette = ((ACTFileInfo) virtualFile).Palette;
                        break;
                    case ".aco":
                        virtualFile ??= new ACOFileInfo(path);
                        palette = ((ACOFileInfo) virtualFile).Colors;
                        break;
                    case ".ase":
                        virtualFile ??= new ASEFileInfo(path);
                        palette = ((ASEFileInfo) virtualFile).Colors;
                        break;
                    case ".pal":
                        if (virtualFile == null)
                        {
                            virtualFile = new RIFFPALFileInfo(path);
                            if (!((RIFFPALFileInfo) virtualFile).IsValidRIFFPAL)
                            {
                                virtualFile = new JSACPALFileInfo(path);
                                if (!((JSACPALFileInfo) virtualFile).IsValidJSACPAL)
                                    virtualFile = new VirtualFileSystemInfo(path);
                            }
                        }

                        switch (virtualFile.GetType())
                        {
                            case Type riffpalType when riffpalType == typeof(RIFFPALFileInfo):
                                palette = ((RIFFPALFileInfo) virtualFile).Palette;
                                break;
                            case Type jsacpalType when jsacpalType == typeof(JSACPALFileInfo):
                                palette = ((JSACPALFileInfo) virtualFile).Palette;
                                break;
                        }

                        break;
                    case ".dds":
                        virtualFile ??= new DDSFileInfo(path);
                        palette = ((DDSFileInfo) virtualFile).GetImage().Palette.Entries;
                        break;
                    case string e when supportedImageExtensions.Contains(e):
                        using (var bmp = virtualFile == null
                                   ? BitmapLoader.LoadBitmap(path)
                                   : BitmapLoader.LoadBitmap(virtualFile.GetBytes()))
                        {
                            palette = bmp.Palette.Entries;
                        }

                        break;
                }
            }
            catch
            {
                WarningMessage($"Retrieving palette failed. Skipping {Path.GetFileName(savePath)}...");
                return;
            }

            if (palette == null || palette.Length == 0)
            {
                InfoMessage($"No colors found. Skipping {Path.GetFileName(savePath)}...");
                return;
            }

            if (options.HasFlag(Options.Flip)) palette = palette.Reverse().ToArray();

            var fileBytes = paletteFormat switch
            {
                PaletteFormat.HPL => new HPLFileInfo(palette,
                    Endianness ?? ByteOrder.LittleEndian).GetBytes(),
                PaletteFormat.ACT => new ACTFileInfo(palette,
                    Endianness ?? ByteOrder.BigEndian).GetBytes(),
                _ => new byte[0]
            };

            if (WriteFile(savePath, fileBytes, options)) fileName = Path.GetFileName(savePath);
        }, ref fileName);
    }

    private static void ShowUsage()
    {
        ConsoleTools.ShowUsage(
            $"Usage: {Path.GetFileName(AssemblyPath)} {CLIArg} <file/folder path> [{string.Join("/", Enum.GetNames(typeof(PaletteFormat)))}] [options...]",
            ConsoleOptions.Concat(FileConsoleOptions).Concat(GlobalConsoleOptions).ToArray());
    }

    private enum PaletteFormat
    {
        HPL = 0x0,
        ACT = 0x1
    }

    [Flags]
    private enum Options
    {
        Flip = 0x1
    }
}