using System;
using System.IO;
using System.Linq;
using ArcSysLib.Core.ArcSys;
using ArcSysLib.Core.IO.File.ArcSys;
using ArcSysLib.Util;
using GCLILib.Core;
using GCLILib.Util;
using GeoArcSysAIOCLITool.Common.Enum;
using GeoArcSysAIOCLITool.Util.Extensions;
using VFSILib.Common.Enum;
using static GCLILib.Util.ConsoleTools;
using static GeoArcSysAIOCLITool.AIO;
using static GeoArcSysAIOCLITool.Util.ConsoleArgumentTools;
using static GeoArcSysAIOCLITool.Util.Dialogs;

namespace GeoArcSysAIOCLITool.Core.CLI;

public static class PACker
{
    private static readonly ConsoleOption[] ConsoleOptions =
    {
        new()
        {
            Name = "Recursive",
            ShortOp = "-r",
            LongOp = "--recursive",
            Description =
                "Specifies, if the tool is unpacking, to look through every folder, from the parent, recursively.",
            Flag = Options.Recursive
        },
        new()
        {
            Name = "FileHeaderEndPadding",
            ShortOp = "-fhep",
            LongOp = "--fileheaderendpadding",
            Description =
                "Adds padding at the end of the header and files. (Padding is 0x80 chunks)",
            Flag = Options.FileHeaderEndPadding
        },
        new()
        {
            Name = "NoByteAlignment",
            ShortOp = "-nba",
            LongOp = "--nobytealignment",
            Description =
                "Prevent byte alignment between packed files.",
            Flag = Options.NoByteAlignment
        },
        new()
        {
            Name = "NameID",
            ShortOp = "-ni",
            LongOp = "--nameid",
            Description =
                "Applies a unique ID based the file's name. (32 character limit)",
            Flag = Options.NameID
        },
        new()
        {
            Name = "NameIDExt",
            ShortOp = "-nie",
            LongOp = "--nameidext",
            Description =
                "Applies a unique ID based the file's name. (64 character limit)",
            Flag = Options.NameIDExt
        },
        new()
        {
            Name = "ExtractFileOrder",
            ShortOp = "-efo",
            LongOp = "--extractfileorder",
            Description =
                "Specifies, if the tool is unpacking, to extract the original file order.",
            HasArg = true,
            Flag = Options.ExtractFileOrder,
            Func = delegate(string[] subArgs)
            {
                if (subArgs.Length == 0)
                {
                    subArgs = new string[1];
                    subArgs[0] = SaveFileDialog("Save File Order as...", "PFO File|*.pfo");
                }

                foreach (var arg in subArgs)
                {
                    var subArg = Path.GetFullPath(arg.Replace("\"", "\\"));
                    if (subArgs.Length > 1)
                        WarningMessage(
                            $"Too many arguments for file order extraction path. Defaulting to \"{subArg}\"...");
                    FileOrderPath = Path.GetFullPath(subArg);
                    break;
                }

                if (string.IsNullOrWhiteSpace(FileOrderPath) && subArgs.Length == 0)
                    InfoMessage(
                        "No file path was given. Ignoring...");
            }
        },
        new()
        {
            Name = "FileOrder",
            ShortOp = "-fo",
            LongOp = "--fileorder",
            Description =
                "Specifies, if the tool is packing, to use the given file order file.",
            HasArg = true,
            Flag = Options.FileOrder,
            Func = delegate(string[] subArgs)
            {
                if (subArgs.Length == 0)
                {
                    subArgs = new string[1];
                    subArgs[0] = OpenFileDialog("Select File Order File...", "PFO File|*.pfo");
                }

                foreach (var arg in subArgs)
                {
                    var subArg = Path.GetFullPath(arg.Replace("\"", "\\"));
                    if (subArgs.Length > 1)
                        WarningMessage(
                            $"Too many arguments for File Order file path. Defaulting to \"{subArg}\"...");

                    FileOrderPath = Path.GetFullPath(subArg);
                    break;
                }

                if (string.IsNullOrWhiteSpace(FileOrderPath))
                {
                    if (subArgs.Length > 1)
                        WarningMessage(
                            "None of the given file paths exist. Ignoring...");
                    else if (subArgs.Length == 1)
                        WarningMessage(
                            "Given file path does not exist. Ignoring...");
                    else if (subArgs.Length == 0)
                        InfoMessage(
                            "No file path was given. Ignoring...");
                }
                else
                {
                    if (!File.Exists(FileOrderPath) && !Directory.Exists(FileOrderPath))
                    {
                        WarningMessage(
                            "Given file path does not exist. Ignoring...");
                        FileOrderPath = string.Empty;
                    }
                }
            }
        }
    };

    private static Options options;
    private static string FileOrderPath = string.Empty;

    [STAThread]
    public static void Main(string[] args)
    {
        DefaultCLIMainBlock(args, options, delegate(string[] args)
        {
            if (ShouldGetUsage(args) || !SetFirstArgumentAsPath(ref args, Enum.GetNames(typeof(PACProcedure)),
                    $"ArcSys Directories|{FileFilterDict["ArcSysDirectory"]}|" +
                    "All Files|*.*"))
            {
                ShowUsage();
                return;
            }

            options = (Options) ((int) ProcessOptions<Options>(args, ConsoleOptions) |
                                 (int) ProcessOptions<AIO.FileOptions>(args, FileConsoleOptions) |
                                 (int) ProcessOptions<GlobalOptions>(args, GlobalConsoleOptions));

            var procedureType = PACProcedure.Pack;

            if (args.Length > 1)
            {
                if (byte.TryParse(args[1], out var b))
                {
                    if (b <= 1)
                        procedureType = (PACProcedure) b;
                }
                else if (Enum.TryParse(args[1], true, out PACProcedure p))
                {
                    procedureType = p;
                }
            }

            var path = Path.GetFullPath(args[0]);

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


            if (!attr.HasFlag(FileAttributes.Directory)) procedureType = PACProcedure.Unpack;

            if (procedureType == PACProcedure.Pack)
            {
                var savePath = path + ".pac";
                if (!string.IsNullOrWhiteSpace(OutputPath))
                    savePath = Path.Combine(OutputPath, Path.GetFileName(savePath));

                PAC.Parameters pacParams = new();

                if(options.HasFlag(Options.FileHeaderEndPadding))
                    pacParams |= PAC.Parameters.FileHeaderEndPadding;
                if (options.HasFlag(Options.NoByteAlignment))
                    pacParams |= PAC.Parameters.NoByteAlignment;
                if (options.HasFlag(Options.NameID))
                    pacParams |= PAC.Parameters.GenerateNameID;
                if (options.HasFlag(Options.NameIDExt)) 
                    pacParams |= PAC.Parameters.GenerateExtendedNameID;

                Console.WriteLine(
                    $"Packing {Path.GetFileNameWithoutExtension(savePath)} in {Enum.GetName(typeof(ByteOrder), Endianness ?? ByteOrder.LittleEndian)}...");

                if (WriteFile(savePath, new PACFileInfo(path, pacParams,
                        !string.IsNullOrEmpty(FileOrderPath)
                            ? PACFileOrderTools.ReadFileOrder(FileOrderPath)
                            : null, Endianness ?? ByteOrder.LittleEndian).GetBytes(), options))
                    Console.WriteLine("Successfully packed.");
            }
            else
            {
                string[] paths;

                var saveFolder = string.Empty;

                var isDirectory = attr.HasFlag(FileAttributes.Directory);

                var isRecursive = options.HasFlag(Options.Recursive);

                if (isRecursive)
                {
                    saveFolder = path + "_unpack";
                    if (!string.IsNullOrWhiteSpace(OutputPath))
                        saveFolder = Path.Combine(OutputPath, Path.GetFileName(saveFolder));
                }

                var directoryInfo = new DirectoryInfo(path);

                if (isRecursive && isDirectory)
                    paths = directoryInfo.GetFilesRecursive().Select(fi => fi.FullName).ToArray();
                else if (isDirectory)
                    paths = directoryInfo.GetFiles().Select(fi => fi.FullName).ToArray();
                else
                    paths = new[] {path};

                foreach (var filePath in paths)
                {
                    if (!File.Exists(filePath))
                    {
                        WarningMessage($"The \"{filePath}\" file does not exist. Skipping...");
                        continue;
                    }

                    if (!isRecursive) saveFolder = Directory.GetParent(filePath).FullName;

                    var mainPACFile = new PACFileInfo(filePath) {Active = true};

                    if (!mainPACFile.IsValidPAC)
                    {
                        WarningMessage($"{mainPACFile.Name} is not a valid PAC file. Skipping...");
                        continue;
                    }

                    ProcessFile(mainPACFile, saveFolder);

                    Console.WriteLine($"Searching {mainPACFile.Name}...");
                    var vfiles = mainPACFile.GetFilesRecursive();

                    if (!string.IsNullOrEmpty(FileOrderPath))
                    {
                        Console.WriteLine($"Saving {mainPACFile.Name}'s File Order...");
                        if (WriteFile(FileOrderPath,
                                delegate(string path)
                                {
                                    PACFileOrderTools.WriteFileOrder(path, mainPACFile.GetPACFileOrder());
                                }, options))
                            Console.WriteLine("Successfully saved.");
                    }

                    foreach (var vfile in vfiles) ProcessFile(vfile, saveFolder);

                    mainPACFile.Active = false;
                }
            }

            CompleteMessage();
        }, "Mode: Geo ArcSys PACker");
    }

    public static void ProcessFile(ArcSysFileSystemInfo vfsi, string savePath)
    {
        var isDirectory = vfsi.Extension == vfsi.VirtualRoot.Extension && vfsi != vfsi.VirtualRoot;
        if (!isDirectory && vfsi is ArcSysDirectoryInfo)
        {
            isDirectory = true;
            savePath = Path.Combine(Path.GetDirectoryName(savePath), Path.GetFileNameWithoutExtension(savePath));
        }
        else
        {
            savePath = AdjustSavePathFromVFSI(vfsi, savePath);
        }

        if (isDirectory)
        {
            Directory.CreateDirectory(savePath);
            return;
        }

        Console.WriteLine($"Saving {vfsi.Name}...");
        if (WriteFile(savePath, vfsi.GetBytes(), options))
            Console.WriteLine("Successfully saved.");
        else
            WarningMessage("Failed to save the file.");
    }

    private static void ShowUsage()
    {
        ConsoleTools.ShowUsage(
            $"Usage: {Path.GetFileName(AssemblyPath)} {CLIArg} <file/folder path> [{string.Join("/", Enum.GetNames(typeof(PACProcedure)))}] [options...]",
            ConsoleOptions.Concat(FileConsoleOptions).Concat(GlobalConsoleOptions).ToArray());
    }

    [Flags]
    private enum Options
    {
        Recursive = 0x1,
        FileHeaderEndPadding = 0x10,
        NoByteAlignment = 0x20,
        NameID = 0x40,
        NameIDExt = 0x80,
        ExtractFileOrder = 0x10000,
        FileOrder = 0x20000
    }
}