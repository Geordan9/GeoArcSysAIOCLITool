using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GCLILib.Core;
using GCLILib.Util;
using GeoArcSysAIOCLITool.Util.Extensions;
using GHLib.Core.Hack;
using GHLib.Util;
using Steamless.API.Model;
using static GCLILib.Util.ConsoleTools;
using static GeoArcSysAIOCLITool.AIO;
using static GeoArcSysAIOCLITool.Util.ConsoleArgumentTools;
using static GeoArcSysAIOCLITool.Util.SteamlessTools;

namespace GeoArcSysAIOCLITool.Core.CLI;

public static class Patcher
{
    public static ConsoleOption[] ConsoleOptions =
    {
        new()
        {
            Name = "Game",
            ShortOp = "-g",
            LongOp = "--game",
            Description =
                $"Specifies the targeted game {{{string.Join("|", Enum.GetNames(typeof(Games)))}}} to patch.",
            HasArg = true,
            Flag = Options.Game,
            Func = delegate(string[] subArgs)
            {
                foreach (var arg in subArgs)
                    if (Enum.TryParse(arg, true, out Games game))
                    {
                        if (subArgs.Length > 1)
                            WarningMessage(
                                $"Too many arguments for game. Defaulting to \"{arg}\"...");
                        games |= game;
                        break;
                    }

                if (games == 0)
                {
                    if (subArgs.Length > 1)
                        WarningMessage(
                            "None of the given games are compatible. Ignoring...");
                    else if (subArgs.Length == 1)
                        WarningMessage(
                            "Given game was not compatible. Ignoring...");
                    else if (subArgs.Length == 0)
                        WarningMessage(
                            "No game was given. Ignoring...");
                }
            }
        },
        new()
        {
            Name = "Patch",
            ShortOp = "-p",
            LongOp = "--patch",
            Description =
                "Specifies which patches to apply using one or many of the patch suboptions or a path to a patch file or folder.",
            HasArg = true,
            Flag = Options.Patch,
            Func = delegate(string[] subArgs)
            {
                DefaultPatchPath = Path.Combine(Path.GetDirectoryName(AssemblyPath), "Patches");
                var defaultPatchPathExists = Directory.Exists(DefaultPatchPath);
                if (!defaultPatchPathExists)
                {
                    DefaultPatchPath += ".ghm";
                    defaultPatchPathExists = File.Exists(DefaultPatchPath);
                    if (!defaultPatchPathExists)
                    {
                        WarningMessage(
                            "Default patch patch doesn't exist. Ignoring suboptions...");
                        return;
                    }
                }

                var patchPathAttr = new FileAttributes();
                try
                {
                    patchPathAttr = File.GetAttributes(DefaultPatchPath);
                }
                catch (Exception ex)
                {
                    ErrorMessage(ex.Message);
                    return;
                }

                var hackGroups = (patchPathAttr.HasFlag(FileAttributes.Directory)
                        ? new DirectoryInfo(DefaultPatchPath).GetFilesRecursive()
                        : new[] {new FileInfo(DefaultPatchPath)})
                    .SelectManyNullCheck(f =>
                        GHBinaryTools.ReadBinaryHackModule(f.FullName).Where(hc => hc.Name == "Patches")
                            .SelectMany(hc => hc.HackGroups)).ToArray();

                var patchConsoleOptionList = new List<ConsoleOption>();

                foreach (var hackGroup in hackGroups)
                foreach (var hack in hackGroup.Hacks)
                    if (!string.IsNullOrWhiteSpace(hack.ID) && !string.IsNullOrWhiteSpace(hack.Name))
                        patchConsoleOptionList.Add(new ConsoleOption
                        {
                            Name = hack.Name,
                            ShortOp = "~" + hack.ID.Replace(" ", string.Empty).ToLower(),
                            LongOp = "~~" + hack.Name.Replace(" ", string.Empty).ToLower(),
                            Description = hack.Description,
                            Flag = hackGroup.Name,
                            SpecialObject = hack
                        });

                if (patchConsoleOptionList.Count > 0)
                    patchConsoleOptionList.Insert(0, new ConsoleOption
                    {
                        Name = "All",
                        ShortOp = "~a",
                        LongOp = "~~all",
                        Description = "Apply all available patches."
                    });

                PatchConsoleOptions = patchConsoleOptionList.ToArray();

                var chosenPatchOptionList = new List<ConsoleOption>();

                foreach (var arg in subArgs)
                {
                    var found = false;
                    if (defaultPatchPathExists && PatchConsoleOptions.Length > 0)
                        foreach (var co in PatchConsoleOptions.Where(pco =>
                                     pco.ShortOp.Replace("~", string.Empty).Equals(arg.Replace("~", string.Empty),
                                         StringComparison.OrdinalIgnoreCase) ||
                                     pco.LongOp.Replace("~", string.Empty).Equals(arg.Replace("~", string.Empty),
                                         StringComparison.OrdinalIgnoreCase)))
                        {
                            chosenPatchOptionList.Add(co);
                            found = true;
                        }

                    if (!found)
                    {
                        var path = Path.GetFullPath(arg);
                        if (File.Exists(arg) || Directory.Exists(path)) PatchPathList.Add(path);
                    }
                }

                ChosenPatchOptions = chosenPatchOptionList.Any(cpo => cpo.Flag == null && cpo.SpecialObject == null)
                    ? PatchConsoleOptions
                    : chosenPatchOptionList.ToArray();

                if (ChosenPatchOptions.Length == 0)
                {
                    if (subArgs.Length > 1)
                        WarningMessage(
                            "None of the given patches exist. Ignoring...");
                    else if (subArgs.Length == 1)
                        WarningMessage(
                            "Given patch doesn't exist. Ignoring...");
                    else if (subArgs.Length == 0)
                        InfoMessage(
                            "No game was given. Ignoring...");
                }
            }
        },
        new()
        {
            Name = "Process",
            ShortOp = "-prc",
            LongOp = "--process",
            Description =
                "Reads the file path as the process ID or name and patches the process/es.",
            Flag = Options.Process
        },
        new()
        {
            Name = "Unpack",
            ShortOp = "-u",
            LongOp = "--unpack",
            Description =
                "Will attempt to unpack the portable executable using Steamless.",
            Flag = Options.Unpack
        }
    };

    public static ConsoleOption[] PatchConsoleOptions = new ConsoleOption[0];

    private static Options options;
    private static Games games;
    private static string DefaultPatchPath = string.Empty;
    private static readonly List<string> PatchPathList = new();
    private static readonly byte[] MSDOS16BitMagicBytes = {0x4D, 0x5A, 0x90, 0x00};
    private static readonly byte[] PortableExecutableMagicBytes = {0x50, 0x45, 0x00, 0x00};
    private static ConsoleOption[] ChosenPatchOptions = new ConsoleOption[0];

    [STAThread]
    public static void Main(string[] args)
    {
        DefaultCLIMainBlock(args, options, delegate(string[] args)
        {
            if (ShouldGetUsage(args) || !SetFirstArgumentAsPath(ref args))
            {
                ShowUsage();
                return;
            }

            options = (Options) ((int) ProcessOptions<Options>(args, ConsoleOptions) |
                                 (int) ProcessOptions<AIO.FileOptions>(args, FileConsoleOptions) |
                                 (int) ProcessOptions<GlobalOptions>(args, GlobalConsoleOptions));

            var path = Path.GetFullPath(args[0]);

            if (!options.HasFlag(Options.Process))
            {
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

                if (attr.HasFlag(FileAttributes.Directory))
                {
                    ErrorMessage("Input path was not a file.");
                    return;
                }
            }

            var hacks = new List<Hack>();

            if (games != 0)
            {
                var game = Enum.GetName(typeof(Games), games);
                PatchConsoleOptions = PatchConsoleOptions
                    .Where(pco =>
                        pco.Flag == null || ((string) pco.Flag).Equals(game, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            if (ChosenPatchOptions.Length > 0)
            {
                if (games != 0)
                {
                    var game = Enum.GetName(typeof(Games), games);
                    hacks.AddRange(ChosenPatchOptions
                        .Where(cpo =>
                            cpo.Flag != null && ((string) cpo.Flag).Equals(game, StringComparison.OrdinalIgnoreCase))
                        .Select(cpo => (Hack) cpo.SpecialObject));
                }
                else
                {
                    hacks.AddRange(ChosenPatchOptions.Where(cpo => cpo.SpecialObject != null)
                        .Select(pco => (Hack) pco.SpecialObject));
                }
            }

            if (ChosenPatchOptions.Length == 0 && hacks.Count == 0 && PatchPathList.Count == 0)
            {
                ShowUsage();
                return;
            }

            hacks.AddRange(PatchPathList.SelectManyNullCheck(pp =>
                GHBinaryTools.ReadBinaryHackModule(pp)
                    .SelectManyNullCheck(hc => hc.HackGroups.SelectManyNullCheck(hg => hg.Hacks))));

            if (hacks.Count == 0)
            {
                WarningMessage("No patches available or chosen. Stopping...");
                return;
            }

            var hackArray = hacks.ToArray();
            var is64Bit = false;
            HackScanner hackScanner;

            if (options.HasFlag(Options.Process))
            {
                var ID = -1;
                var isID = int.TryParse(args[0], out ID) || args[0].LastIndexOf("0x") == 0 &&
                    int.TryParse(args[0].Replace("0x", string.Empty), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out ID);
                var processes = Process.GetProcesses().Where(prc =>
                {
                    var b = false;
                    b = prc.Id == ID || args[0].Equals(prc.ProcessName, StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        var starttime = prc.StartTime;
                        return b;
                    }
                    catch
                    {
                    }

                    return false;
                }).OrderByDescending(p => p.StartTime).ToArray();

                if (processes.Length == 0)
                {
                    InfoMessage("No processes could be found. Stopping...");
                    return;
                }

                if (processes.Length > 1)
                {
                    if (ConfirmPrompt(
                            $"Multiple processes were found. Would you like to use the first one? Process ID: {processes[0].Id}"))
                        processes = new[] {processes[0]};
                    else
                        InfoMessage("Patching multiple processes...");
                }

                foreach (var process in processes)
                {
                    Console.WriteLine($"Patching Process ID: {process.Id}");
                    hackScanner = new HackScanner(process);
                    ProcessHacks(hackScanner, hackArray);
                }
            }
            else
            {
                if (options.HasFlag(Options.Unpack))
                {
                    var unpackedPath = path + ".unpacked.exe";
                    if (UnpackFile(path, new SteamlessOptions()) && File.Exists(unpackedPath))
                    {
                        File.Move(path, path + ".packed.exe");
                        File.Move(unpackedPath, path);
                    }
                }

                using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                using var reader = new BinaryReader(fs, Encoding.Default, true);
                var magicBytes = reader.ReadBytes(0x4);
                if (magicBytes.SequenceEqual(MSDOS16BitMagicBytes))
                {
                    reader.BaseStream.Seek(0x38, SeekOrigin.Current);
                    var off = reader.ReadInt32();
                    reader.BaseStream.Seek(off, SeekOrigin.Begin);
                    magicBytes = reader.ReadBytes(0x4);
                }

                if (!magicBytes.SequenceEqual(PortableExecutableMagicBytes))
                {
                    WarningMessage("File is not an executable. Stopping...");
                    return;
                }

                var machineType = reader.ReadUInt16();
                is64Bit = machineType == 0x8664;
                reader.BaseStream.Seek(0x12, SeekOrigin.Current);

                var peFormat = reader.ReadInt16();

                reader.BaseStream.Seek(peFormat == 0x10B ? 0x1A : 0x16, SeekOrigin.Current);

                var imageBase = peFormat == 0x10B ? reader.ReadInt32() : reader.ReadInt64();
                var sectionAlignment = reader.ReadInt32();

                reader.BaseStream.Seek(0x18, SeekOrigin.Current);

                var sizeOfHeaders = reader.ReadInt32();

                reader.Close();

                if (machineType != 0x8664 && machineType != 0x14c)
                {
                    WarningMessage("Executable machine type is not supported. Stopping...");
                    return;
                }

                fs.Position = 0;
                hackScanner = new HackScanner(fs, imageBase, sectionAlignment - sizeOfHeaders);
                fs.Close();
                fs.Dispose();
                if (string.IsNullOrWhiteSpace(OutputPath)) OutputPath = Path.GetDirectoryName(path);
                if (ProcessHacks(hackScanner, hackArray, is64Bit))
                    WriteFile(Path.Combine(OutputPath, Path.GetFileName(path)), hackScanner.Memory.Stream.ReadToEnd(),
                        options);
                else
                    InfoMessage("Nothing was patched.");
            }

            CompleteMessage();
        }, "Mode: Geo ArcSys Patcher");
    }

    private static bool ProcessHacks(HackScanner scanner, Hack[] hacks, bool is64Bit = false)
    {
        var changed = false;
        foreach (var hack in hacks)
        {
            HackTools.InitializeHack(scanner, hack, is64Bit);
            if (!hack.Enabled)
            {
                WarningMessage($"The hack, {hack.Name}, could not be enabled. Skipping...");
                continue;
            }

            hack.Activated = true;

            HackTools.ToggleHack(scanner.Memory, hack);
            Console.WriteLine($"{hack.Name} was successfully applied.");
            changed = true;
        }

        return changed;
    }

    private static void ShowUsage()
    {
        var patchConsoleOption = ConsoleOptions.Where(co => ((Options) co.Flag).HasFlag(Options.Patch)).First();
        ConsoleTools.ShowUsage(
            $"Usage: {Path.GetFileName(AssemblyPath)} {CLIArg} <executable path> <{patchConsoleOption.ShortOp}/{patchConsoleOption.LongOp}> [options...]",
            ConsoleOptions
                .Concat(FileConsoleOptions.Where(fco => (AIO.FileOptions) fco.Flag != AIO.FileOptions.Endianness))
                .Concat(GlobalConsoleOptions).ToArray());

        if (PatchConsoleOptions.Length > 0)
            ShowSpecialOptions("Patch Suboptions", PatchConsoleOptions);
    }

    [Flags]
    private enum Games
    {
        BBCT = 0x1,
        BBCSEX = 0x2,
        BBCPEX = 0x4,
        BBTAG = 0x8,
        BBCF = 0x10
    }

    [Flags]
    private enum PatchOptions
    {
        EnableDLC = 0x1
    }

    [Flags]
    private enum Options
    {
        Patch = 0x1,
        Game = 0x2,
        Process = 0x10,
        Unpack = 0x100
    }
}