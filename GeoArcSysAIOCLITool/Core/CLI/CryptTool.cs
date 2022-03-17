using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ArcSysLib.Common.Enum;
using ArcSysLib.Core.ArcSys;
using ArcSysLib.Core.IO.File.ArcSys;
using ArcSysLib.Util;
using ArcSysLib.Util.Extension;
using GCLILib.Core;
using GCLILib.Util;
using GeoArcSysAIOCLITool.Properties;
using GeoArcSysAIOCLITool.Util.Extensions;
using VFSILib.Core.IO;
using static ArcSysLib.Util.ArcSysMD5CryptTools;
using static GCLILib.Util.ConsoleTools;
using static GeoArcSysAIOCLITool.AIO;
using static GeoArcSysAIOCLITool.Util.ConsoleArgumentTools;
using static GeoArcSysAIOCLITool.Util.Dialogs;

namespace GeoArcSysAIOCLITool.Core.CLI;

public static class CryptTool
{
    public static ConsoleOption[] ConsoleOptions =
    {
        new()
        {
            Name = "Mode",
            ShortOp = "-m",
            LongOp = "--mode",
            Description =
                $"Specifies to {{{string.Join("|", Enum.GetNames(typeof(Modes)))}}} the file. Without this option, it'll automatically decide.",
            HasArg = true,
            Flag = Options.Mode,
            Func = delegate(string[] subArgs)
            {
                if (subArgs.Length > 0)
                    foreach (var arg in subArgs)
                    {
                        if (Enum.TryParse(arg, true, out Modes mode)) modes |= mode;
                    }
                else
                    modes |= Modes.Auto;
            }
        },
        new()
        {
            Name = "Game",
            ShortOp = "-g",
            LongOp = "--game",
            Description =
                $"Specifies the targeted game {{{string.Join("|", Enum.GetNames(typeof(Games)))}}} to assist in the automatic mode.",
            HasArg = true,
            Flag = Options.Game,
            Func = delegate(string[] subArgs)
            {
                for (var i = 0; i < subArgs.Length; i++)
                    subArgs[i] = subArgs[i].ToUpper();
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
                        InfoMessage(
                            "No game was given. Ignoring...");
                }
            }
        },
        new()
        {
            Name = "MD5CryptKey",
            ShortOp = "-md5ck",
            LongOp = "--md5cryptkey",
            Description =
                $"Sets the key used with the MD5 encryption mode. Allows preset keys or a custom one as a byte array or file. Presets: {{{string.Join("|", Enum.GetNames(typeof(MD5CryptKeyPresets)))}}}",
            HasArg = true,
            Flag = Options.MD5CryptKey,
            Func = delegate(string[] subArgs)
            {
                if (subArgs.Length > 0)
                {
                    var fileOrPreset = false;
                    foreach (var arg in subArgs)
                        if (Enum.TryParse(arg, true, out MD5CryptKeyPresets md5CryptKeyPreset))
                        {
                            if (subArgs.Length > 1) InfoMessage($"Found multiple arguments. Defaulting to: \"{arg}\"");
                            switch (md5CryptKeyPreset)
                            {
                                case MD5CryptKeyPresets.BBTAG:
                                    EncryptionKey = Resources.ArcSysMD5Crypt_BBTAG;
                                    break;
                                case MD5CryptKeyPresets.P4U2:
                                    EncryptionKey = Resources.ArcSysMD5Crypt_P4U2;
                                    break;
                            }

                            fileOrPreset = true;
                            break;
                        }
                        else if (File.Exists(Path.GetFullPath(arg)))
                        {
                            if (subArgs.Length > 1) InfoMessage($"Found multiple arguments. Defaulting to: \"{arg}\"");
                            EncryptionKey = File.ReadAllBytes(Path.GetFullPath(arg));
                            fileOrPreset = true;
                            break;
                        }

                    if (!fileOrPreset)
                    {
                        var byteString = string.Join(string.Empty, subArgs);
                        if (byteString.OnlyHex())
                            EncryptionKey = byteString.BulkRemove(new[] {" ", ",", "0x"}).ToByteArray();
                        else
                            WarningMessage("No arguments were used. Skipping...");
                    }
                }
                else
                {
                    EncryptionKey = Resources.ArcSysMD5Crypt_BBTAG;
                }
            }
        },
        new()
        {
            Name = "Paths",
            ShortOp = "-p",
            LongOp = "--paths",
            Description =
                "Provides a path to a file containing a list of file paths which will be used when dealing with the MD5Decrypt mode. Otherwise it will default to \"paths.txt\" in the same directory as executable.",
            HasArg = true,
            Flag = Options.Paths,
            Func = delegate(string[] subArgs)
            {
                var defaultPath = Path.Combine(Path.GetDirectoryName(AssemblyPath), "paths.txt");
                if (subArgs.Length == 0)
                {
                    subArgs = new string[1];
                    if (File.Exists(defaultPath))
                    {
                        InfoMessage(
                            "Using default paths text file...");

                        subArgs[0] = defaultPath;
                    }
                    else
                    {
                        subArgs[0] = OpenFileDialog("Select paths text file...");
                    }
                }

                foreach (var arg in subArgs)
                    if (File.Exists(arg))
                    {
                        if (subArgs.Length > 1)
                            WarningMessage(
                                $"Too many arguments for paths text file. Defaulting to \"{arg}\"...");
                        PathsFile = arg;
                        UpdatePaths();
                        break;
                    }

                if (string.IsNullOrWhiteSpace(PathsFile))
                {
                    if (subArgs.Length > 1)
                        WarningMessage(
                            "None of the given paths text files exist. Ignoring...");
                    else if (subArgs.Length == 1)
                        WarningMessage(
                            "Given paths text file does not exist. Ignoring...");

                    if (File.Exists(defaultPath))
                    {
                        InfoMessage(
                            "Using default paths text file...");

                        PathsFile = defaultPath;
                        UpdatePaths();
                    }
                    else if (subArgs.Length == 0)
                    {
                        InfoMessage(
                            "No paths text file was given. Ignoring...");
                    }
                }
            }
        }
    };

    private static Options options;
    private static Modes modes;
    private static Games games;

    private static string InitPath = string.Empty;
    private static string PathsFile = string.Empty;
    private static FilePaths[] PathsArray;

    private static readonly string[] MD5ObfuscatedFiles =
        {string.Empty, ".pac", ".pacgz", ".hip", ".abc", ".txt", ".pat", ".ha6", ".fod"};

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

            InitPath = Path.GetFullPath(args[0]);

            options = (Options) ((int) ProcessOptions<Options>(args, ConsoleOptions) |
                                 (int) ProcessOptions<AIO.FileOptions>(args, FileConsoleOptions) |
                                 (int) ProcessOptions<GlobalOptions>(args, GlobalConsoleOptions));

            if (!options.HasFlag(Options.MD5CryptKey))
            {
                EncryptionKey = Resources.ArcSysMD5Crypt_BBTAG;

                if (games.HasFlag(Games.P4U2)) EncryptionKey = Resources.ArcSysMD5Crypt_P4U2;
            }

            var attr = new FileAttributes();
            try
            {
                attr = File.GetAttributes(InitPath);
            }
            catch (Exception ex)
            {
                ErrorMessage(ex.Message);
                return;
            }

            ProcessGamesAndModes();

            if (attr.HasFlag(FileAttributes.Directory))
            {
                if (string.IsNullOrWhiteSpace(OutputPath)) OutputPath = InitPath;
                var files = new DirectoryInfo(InitPath).GetFilesRecursive();
                var origModes = modes;
                foreach (var file in files)
                {
                    ProcessFile(new VirtualFileSystemInfo(file.FullName));
                    modes = origModes;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(OutputPath)) OutputPath = Path.GetDirectoryName(InitPath);
                ProcessFile(new VirtualFileSystemInfo(InitPath));
            }

            CompleteMessage();
        }, "Mode: Geo ArcSys Crypt Tool");
    }

    public static void ProcessGamesAndModes()
    {
        if (modes.HasFlag(Modes.Auto) && modes != Modes.Auto)
        {
            InfoMessage("Auto mode supersedes all other modes. Continuing to use Auto mode...");
            modes = Modes.Auto;
        }
        else if (modes.HasFlag(Modes.Encrypt) && modes.HasFlag(Modes.Decrypt) ||
                 modes.HasFlag(Modes.MD5Encrypt) && modes.HasFlag(Modes.MD5Decrypt) ||
                 modes.HasFlag(Modes.ArcSysDeflate) && modes.HasFlag(Modes.ArcSysInflate) ||
                 modes.HasFlag(Modes.SwitchDeflate) && modes.HasFlag(Modes.SwitchInflate))
        {
            WarningMessage("Can't use opposing modes. Defaulting to Auto mode...");
            modes = Modes.Auto;
        }
        else if (!modes.HasFlag(Modes.Auto))
        {
            if (games.HasFlag(Games.BBCT) || games.HasFlag(Games.BBCSEX) || games.HasFlag(Games.BBCPEX) ||
                games.HasFlag(Games.BBCF))
            {
                if (modes.HasFlag(Modes.MD5Encrypt) || modes.HasFlag(Modes.MD5Decrypt))
                {
                    WarningMessage("Specified game does not use MD5 cryptography. Defaulting to Auto mode...");
                    modes = Modes.Auto;
                }
                else if (modes.HasFlag(Modes.SwitchDeflate) || modes.HasFlag(Modes.SwitchInflate))
                {
                    WarningMessage("Specified game does not use Switch compression. Defaulting to Auto mode...");
                    modes = Modes.Auto;
                }
            }
            else if (games.HasFlag(Games.BBTAG))
            {
                if (modes.HasFlag(Modes.Encrypt) ||
                    modes.HasFlag(Modes.Decrypt) ||
                    modes.HasFlag(Modes.ArcSysInflate) ||
                    modes.HasFlag(Modes.ArcSysDeflate))
                {
                    WarningMessage("Specified game only uses MD5 cryptography. Defaulting to Auto mode...");
                    modes = Modes.Auto;
                }
                else if ((modes.HasFlag(Modes.MD5Decrypt) ||
                          modes.HasFlag(Modes.MD5Encrypt)) &&
                         (modes.HasFlag(Modes.SwitchDeflate) ||
                          modes.HasFlag(Modes.SwitchInflate)))
                {
                    WarningMessage(
                        "Specified game does not use both MD5 Encryption and Switch Compression combined. Defaulting to Auto mode...");
                    modes = Modes.Auto;
                }
            }

            if (games.HasFlag(Games.BBCT) && (modes.HasFlag(Modes.ArcSysInflate) ||
                                              modes.HasFlag(Modes.ArcSysDeflate)))
            {
                WarningMessage("Specified game does not use compression. Defaulting to Auto mode...");
                modes = Modes.Auto;
            }
            else if (games.HasFlag(Games.BBCF) && (modes.HasFlag(Modes.Encrypt) ||
                                                   modes.HasFlag(Modes.Decrypt)))
            {
                WarningMessage("Specified game does not use encryption. Defaulting to Auto mode...");
                modes = Modes.Auto;
            }
        }
    }

    public static void ProcessFile(VirtualFileSystemInfo vfsi)
    {
        var fileName = vfsi.Name;
        var origColor = Console.ForegroundColor;
        var finishedFileColor = vfsi is ArcSysFileSystemInfo afsi ? afsi.GetTextColor() ?? origColor : origColor;
        DefaultCLIProcessFileBlock(vfsi, delegate(VirtualFileSystemInfo vfsi)
        {
            if (!vfsi.Exists)
            {
                WarningMessage("File does not exist. Skipping...");
                return;
            }

            var ext = vfsi.Extension;
            if ((games.HasFlag(Games.BBCT) ||
                 games.HasFlag(Games.BBCSEX) ||
                 games.HasFlag(Games.BBCPEX) ||
                 games.HasFlag(Games.BBCF)) && ext != ".pac")
            {
                InfoMessage("Specified game only obfuscates .pac files. Skipping...");
                return;
            }

            if (games.HasFlag(Games.BBTAG) && !MD5ObfuscatedFiles.Contains(ext))
            {
                InfoMessage($"Specified game does not obfuscate {ext} files. Skipping...");
                return;
            }

            if (ext == ".pacgz" && !modes.HasFlag(Modes.SwitchDeflate) && !modes.HasFlag(Modes.SwitchInflate) &&
                !modes.HasFlag(Modes.Auto))
            {
                InfoMessage($"Specified game and mode does not obfuscate {ext} files. Skipping...");
                return;
            }

            if (string.IsNullOrWhiteSpace(ext) &&
                (modes.HasFlag(Modes.SwitchDeflate) || modes.HasFlag(Modes.SwitchInflate)))
            {
                InfoMessage("Specified game and mode does not obfuscate empty exetension files. Skipping...");
                return;
            }

            byte[] fileBytes = null;
            var fileDirectory = OutputPath;
            var filePath = vfsi.FullName;

            var magicBytes = new byte[4];
            var memStream = new MemoryStream(vfsi.GetBytes());
            memStream.Read(magicBytes, 0, 4);

            var fileIsKnown = magicBytes.SequenceEqual(PAC.MagicBytes) ||
                              magicBytes.SequenceEqual(HIP.MagicBytes) ||
                              magicBytes.SequenceEqual(HPL.MagicBytes);

            if (modes == Modes.Auto || games == Games.BBCSEX || games == Games.BBCPEX || games == Games.BBCF)
                if (magicBytes.SequenceEqual(BBObfuscatorTools.DeflateArcSystemMagicBytes))
                    modes = Modes.ArcSysInflate;

            if (modes == Modes.Auto || games == Games.BBTAG)
            {
                if (magicBytes.Take(3).SequenceEqual(MagicBytes.GZIP))
                    modes = Modes.SwitchInflate;
                else if (MD5Tools.IsMD5(fileName)) modes = Modes.MD5Decrypt;
                else if (fileName.Length > 32 && MD5Tools.IsMD5(fileName.Substring(0, 32))) modes = Modes.MD5Encrypt;
            }

            if (modes == Modes.Auto)
                modes = games switch
                {
                    Games.BBCT => fileIsKnown ? Modes.Encrypt : Modes.Decrypt,
                    Games.BBTAG => fileIsKnown ? Modes.MD5Encrypt : Modes.MD5Decrypt,
                    Games.BBCF => fileIsKnown ? Modes.ArcSysDeflate : Modes.ArcSysInflate,
                    var g when g == Games.BBCSEX || g == Games.BBCPEX => fileIsKnown
                        ? Modes.ArcSysDeflate | Modes.Encrypt
                        : Modes.ArcSysInflate | Modes.Decrypt,
                    _ => Modes.Auto
                };

            if (fileBytes == null && modes != Modes.Auto)
            {
                var changed = false;

                if (modes.HasFlag(Modes.ArcSysDeflate))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"ArcSys Deflating {fileName}...");
                    var ms = BBObfuscatorTools.DFASFPACDeflateStream(memStream);
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    changed = true;
                }
                else if (modes.HasFlag(Modes.Decrypt))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Decrypting {fileName}...");
                    var ms = BBObfuscatorTools.FPACCryptStream(memStream, filePath, CryptMode.Decrypt);
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    changed = true;
                }

                if (modes.HasFlag(Modes.Encrypt))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Encrypting {fileName}...");
                    var ms = BBObfuscatorTools.FPACCryptStream(memStream, filePath, CryptMode.Encrypt);
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    changed = true;
                }
                else if (modes.HasFlag(Modes.ArcSysInflate))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"ArcSys Inflating {fileName}...");
                    var ms = BBObfuscatorTools.DFASFPACInflateStream(memStream);
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    changed = true;
                }
                else if (modes.HasFlag(Modes.MD5Encrypt))
                {
                    memStream.Position = 0;
                    if (fileName.Length > 32 && MD5Tools.IsMD5(fileName.Substring(0, 32)) ||
                        filePath.LastIndexOf("data") >= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"MD5 Encrypting {fileName}...");
                        var ms = ArcSysMD5CryptStream(memStream, filePath, CryptMode.Encrypt);
                        memStream.Close();
                        memStream.Dispose();
                        memStream = ms;
                        if (fileName.Length > 32 && MD5Tools.IsMD5(fileName.Substring(0, 32)))
                        {
                            fileName = fileName.Substring(0, 32);
                        }
                        else if (!MD5Tools.IsMD5(fileName))
                        {
                            var lastIndex = filePath.LastIndexOf("data");
                            var datapath = filePath.Substring(lastIndex, filePath.Length - lastIndex);
                            fileName = MD5Tools.CreateMD5(datapath.Replace("\\", "/"));
                            filePath = fileName;
                        }

                        changed = true;
                    }
                    else
                    {
                        WarningMessage(
                            "File's name and/or directory does not follow the rules for MD5 Encryption. Ignoring...");
                    }
                }
                else if (modes.HasFlag(Modes.MD5Decrypt))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"MD5 Decrypting {fileName}...");
                    var ms = ArcSysMD5CryptStream(memStream, filePath, CryptMode.Decrypt);
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    if (MD5Tools.IsMD5(fileName))
                    {
                        if (!string.IsNullOrWhiteSpace(PathsFile))
                        {
                            var length = PathsArray.Length;
                            for (var i = 0; i < length; i++)
                                if (PathsArray[i].filepathMD5 == fileName)
                                {
                                    var filepath = PathsArray[i].filepath;
                                    fileName = Path.GetFileName(filepath);
                                    fileDirectory = Path.Combine(OutputPath, Path.GetDirectoryName(filepath));
                                }
                        }

                        if (MD5Tools.IsMD5(fileName)) fileName = fileName + "_" + fileName.ToByteArray()[7] % 43;
                    }

                    changed = true;
                }
                else if (modes.HasFlag(Modes.SwitchDeflate))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Switch Deflating {fileName}...");
                    var ms = memStream.GZipCompressStream();
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    changed = true;
                    if (ext == ".pac")
                        fileName = Path.GetFileNameWithoutExtension(fileName) + ".pacgz";
                }
                else if (modes.HasFlag(Modes.SwitchInflate))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Switch Inflating {fileName}...");
                    var ms = memStream.GZipDecompressStream();
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    changed = true;
                    if (ext == ".pacgz")
                        fileName = Path.GetFileNameWithoutExtension(fileName) + ".pac";
                }

                Console.ForegroundColor = DefaultConsoleColor;
                if (changed)
                    fileBytes = memStream.ToArray();
            }

            if (fileBytes == null)
            {
                if (!fileIsKnown && (modes == Modes.Auto || modes.HasFlag(Modes.Decrypt) ||
                                     modes.HasFlag(Modes.MD5Decrypt) ||
                                     modes.HasFlag(Modes.ArcSysInflate) ||
                                     modes.HasFlag(Modes.SwitchInflate)))
                {
                    InfoMessage("Regular deobfuscation methods failed. Trying alternative...");
                    var pacFile = new PACFileInfo(vfsi.FullName);
                    if (pacFile.IsValidPAC)
                    {
                        fileBytes = pacFile.GetBytes();
                    }
                    else
                    {
                        var hipFile = new HIPFileInfo(vfsi.FullName);
                        if (hipFile.IsValidHIP)
                        {
                            fileBytes = hipFile.GetBytes();
                        }
                        else
                        {
                            var hplFile = new HPLFileInfo(vfsi.FullName);
                            if (hplFile.IsValidHPL) fileBytes = hplFile.GetBytes();
                        }
                    }
                }

                if (fileBytes == null)
                {
                    var automaticString = modes == Modes.Auto ? " automatically" : string.Empty;
                    WarningMessage($"Could not{automaticString} process {fileName}.");
                    return;
                }
            }

            memStream.Close();
            memStream.Dispose();

            var directory = InitPath == filePath || filePath == fileName
                ? fileDirectory
                : Path.Combine(fileDirectory, Path.GetDirectoryName(filePath).Replace(InitPath, string.Empty));
            Directory.CreateDirectory(directory);
            filePath = Path.GetFullPath(Path.Combine(directory, fileName));

            WriteFile(filePath, fileBytes, options);

            if ((modes.HasFlag(Modes.Encrypt) || modes.HasFlag(Modes.MD5Encrypt)) && modes.HasFlag(Modes.ArcSysDeflate))
                finishedFileColor = ConsoleColor.Magenta;
            else if (modes.HasFlag(Modes.ArcSysDeflate))
                finishedFileColor = ConsoleColor.Cyan;
            else if (modes.HasFlag(Modes.Encrypt) || modes.HasFlag(Modes.MD5Encrypt))
                finishedFileColor = ConsoleColor.Green;
        }, ref finishedFileColor, ref fileName);
    }

    private static void UpdatePaths()
    {
        var pathList = new List<FilePaths>();
        using (TextReader reader = File.OpenText(PathsFile))
        {
            var pattern = new Regex("[/\"]|[/]{2}");
            while (reader.Peek() >= 0)
            {
                var line = reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    line = pattern.Replace(line, "/").ToLower();
                    try
                    {
                        Path.GetFullPath(line);
                        var lineMD5 = MD5Tools.CreateMD5(line);
                        line = line.Replace("/", "\\");
                        pathList.Add(new FilePaths(line, lineMD5));
                    }
                    catch
                    {
                    }
                }
            }
        }

        PathsArray = pathList.ToArray();
    }

    private static void ShowUsage()
    {
        ConsoleTools.ShowUsage(
            $"Usage: {Path.GetFileName(AssemblyPath)} {CLIArg} <file/folder path> [options...]",
            ConsoleOptions
                .Concat(FileConsoleOptions.Where(fco => (AIO.FileOptions) fco.Flag != AIO.FileOptions.Endianness))
                .Concat(GlobalConsoleOptions).ToArray());
    }

    [Flags]
    private enum Games
    {
        BBCT = 0x1,
        BBCSEX = 0x2,
        BBCPEX = 0x4,
        BBTAG = 0x8,
        BBCF = 0x10,

        // Other Games
        P4U2 = 0x8,
        AH3LMSSSX = 0x1
    }

    [Flags]
    private enum Modes
    {
        Auto = 0x0,
        Encrypt = 0x1,
        Decrypt = 0x2,
        MD5Encrypt = 0x4,
        MD5Decrypt = 0x8,
        ArcSysDeflate = 0x10,
        ArcSysInflate = 0x20,
        SwitchDeflate = 0x40,
        SwitchInflate = 0x80
    }

    private enum MD5CryptKeyPresets
    {
        BBTAG = 0x0,
        P4U2 = 0x1
    }

    [Flags]
    private enum Options
    {
        Mode = 0x1,
        Game = 0x2,
        MD5CryptKey = 0x10,
        Paths = 0x100000
    }

    public struct FilePaths
    {
        public string filepath, filepathMD5;

        public FilePaths(string p1, string p2)
        {
            filepath = p1;
            filepathMD5 = p2;
        }
    }
}