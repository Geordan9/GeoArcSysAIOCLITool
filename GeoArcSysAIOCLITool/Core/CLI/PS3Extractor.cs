using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ArcSysLib.Core.ArcSys;
using ArcSysLib.Core.IO.File;
using ArcSysLib.Core.IO.File.ArcSys;
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

public static class PS3Extractor
{
    public static ConsoleOption[] ConsoleOptions =
    {
        new()
        {
            Name = "Trim",
            ShortOp = "-t",
            LongOp = "--trim",
            Description =
                "Remove padded null bytes from the files. Optional argument for byte alignment on unknown files. Default: 16",
            HasArg = true,
            Flag = Options.Trim,
            Func = delegate(string[] subArgs)
            {
                if (subArgs.Length > 0)
                {
                    if (int.TryParse(subArgs[0], out var alignment)) TrimAlignment = alignment;
                    else if (int.TryParse(subArgs[0].Replace("0x", string.Empty), NumberStyles.HexNumber,
                                 CultureInfo.InvariantCulture, out var alignmentHex)) TrimAlignment = alignmentHex;
                }
                else
                {
                    TrimAlignment = 0x10;
                }
            }
        },
        new()
        {
            Name = "Depcompress",
            ShortOp = "-d",
            LongOp = "--decompress",
            Description =
                "Decompress the SEGS compression on files.",
            Flag = Options.Decompress
        },
        new()
        {
            Name = "Extract TOC",
            ShortOp = "-etoc",
            LongOp = "--extracttoc",
            Description =
                "Saves the table of contents if provided a decrpted EBOOT.",
            HasArg = true,
            Flag = Options.ExtractTOC,
            Func = delegate(string[] subArgs)
            {
                if (subArgs.Length == 0)
                {
                    subArgs = new string[1];
                    var path = SaveFileDialog("Save TOC file as...", "Text Documents|*.txt", "toc.txt");
                    if (!string.IsNullOrWhiteSpace(path))
                        subArgs[0] = path;
                    else
                        subArgs = new string[0];
                }

                foreach (var arg in subArgs)
                {
                    var subArg = Path.GetFullPath(arg.Replace("\"", "\\"));
                    if (subArgs.Length > 1)
                        WarningMessage(
                            $"Too many arguments for TOC path. Defaulting to \"{subArg}\"...");
                    TOCSavePath = Path.GetFullPath(subArg);
                    break;
                }

                if (string.IsNullOrWhiteSpace(TOCSavePath) && subArgs.Length == 0)
                    InfoMessage(
                        "No TOC path was given. Ignoring...");
            }
        }
    };

    private static Options options;
    private static string TOCSavePath = string.Empty;
    private static int TrimAlignment = 0x10;

    [STAThread]
    public static void Main(string[] args)
    {
        DefaultCLIMainBlock(args, options, delegate(string[] args)
        {
            if (ShouldGetUsage(args) || !SetFirstArgumentAsPath(ref args, null,
                    "Supported Files|*.bin|" +
                    "All Files|*.*"))
            {
                ShowUsage();
                return;
            }

            options = (Options) ((int) ProcessOptions<Options>(args, ConsoleOptions) |
                                 (int) ProcessOptions<AIO.FileOptions>(args, FileConsoleOptions) |
                                 (int) ProcessOptions<GlobalOptions>(args, GlobalConsoleOptions));

            var ebootPath = string.Empty;

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

            var tableOfContentsList = new List<string>();

            if (args.Length > 1 && args[1][0] != '-')
            {
                if (File.Exists(args[1]))
                {
                    if (Path.GetExtension(args[1]).ToLower() == ".txt")
                    {
                        Console.WriteLine("Reading TOC in text file...");
                        tableOfContentsList = File.ReadAllLines(args[1]).ToList();
                        Console.WriteLine("TOC is now applied.");
                    }
                    else
                    {
                        ebootPath = args[1];
                    }
                }
                else
                {
                    WarningMessage(
                        "The optional EBOOT or TOC file path provided doesn't exist. Ignoring...");
                }
            }

            if (attr.HasFlag(FileAttributes.Directory))
            {
                var bddataPath = Path.Combine(path, "bddata.bin");
                if (File.Exists(bddataPath))
                {
                    if (tableOfContentsList.Count == 0)
                    {
                        var tempEbootPath = Path.Combine(path, "eboot.elf");
                        if (File.Exists(tempEbootPath))
                        {
                            if (!string.IsNullOrWhiteSpace(ebootPath))
                                InfoMessage(
                                    "The EBOOT.ELF file found in folder will be used.");

                            if (tableOfContentsList.Count > 0)
                                InfoMessage(
                                    "The EBOOT.ELF file found will overwrite the table of contents.");

                            ebootPath = tempEbootPath;
                        }
                        else if (string.IsNullOrWhiteSpace(ebootPath))
                        {
                            InfoMessage(
                                "No EBOOT.ELF file was found. Skipping...");
                        }
                    }

                    path = bddataPath;
                }
                else
                {
                    WarningMessage(
                        "No bddata.bin file was found. Stopping...");
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(OutputPath)) OutputPath = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(ebootPath))
                using (var reader =
                       new BinaryReader(new FileStream(ebootPath, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite)))
                {
                    var magicBytes = reader.ReadBytes(4);
                    if (magicBytes.SequenceEqual(new byte[] {0x7F, 0x45, 0x4C, 0x46}))
                    {
                        Console.WriteLine("Scanning for TOC in EBOOT...");
                        if (reader.GoToString("AAWin_FileReadThread") &&
                            reader.GoToString("bddata.bin") &&
                            reader.GoToString("%s/%08x/%08x"))
                        {
                            reader.BaseStream.Seek(0x10, SeekOrigin.Current);
                            var curTOCStr = string.Empty;
                            do
                            {
                                curTOCStr = reader.ReadZeroTerminatedString();
                                var seekLength = curTOCStr.Length % 8;
                                seekLength = (seekLength == 0 ? 8 : 8 - seekLength) - 1;
                                reader.BaseStream.Seek(seekLength, SeekOrigin.Current);
                                tableOfContentsList.Add(curTOCStr);
                            } while (curTOCStr.Length > 4);

                            tableOfContentsList.RemoveAt(tableOfContentsList.Count - 1);
                            Console.WriteLine("Found and applied TOC data.");
                        }
                        else
                        {
                            WarningMessage(
                                "Cannot find the TOC inside the provided EBOOT. Ignoring...");
                        }
                    }
                    else
                    {
                        WarningMessage(
                            "The EBOOT file provided doesn't have the correct magic bytes or is encrypted. Ignoring...");
                    }

                    reader.Close();
                }

            tableOfContentsList.Sort(StringComparer.OrdinalIgnoreCase);
            if (options.HasFlag(Options.ExtractTOC) && tableOfContentsList.Count > 0 &&
                !string.IsNullOrWhiteSpace(TOCSavePath))
                if (WriteFile(TOCSavePath,
                        delegate(string path) { File.WriteAllLines(path, tableOfContentsList.ToArray()); }, options))
                    Console.WriteLine("Saved TOC data.");

            using (var reader =
                   new EndiannessAwareBinaryReader(
                       new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), ByteOrder.BigEndian))
            {
                var magicString = string.Empty;
                var magicBytes = new byte[4];
                var fileHeaderPosList = new List<Tuple<string, long>>();
                Console.WriteLine("Scanning for potential files...");
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    magicBytes = reader.ReadBytes(6, ByteOrder.LittleEndian);
                    magicString = Encoding.ASCII.GetString(magicBytes);
                    if (magicString != "//log:")
                        magicString = Encoding.ASCII.GetString(magicBytes.Take(4).ToArray());
                    reader.BaseStream.Seek(-6, SeekOrigin.Current);
                    if (magicString == "segs")
                    {
                        fileHeaderPosList.Add(new Tuple<string, long>(magicString, reader.BaseStream.Position));
                        reader.BaseStream.Seek(0xC, SeekOrigin.Current);
                        var headerFileSize =
                            BitConverter.ToInt32(reader.ReadBytes(4), 0);
                        reader.BaseStream.Seek(-0x10, SeekOrigin.Current);
                        var seekAmount = headerFileSize + (0x10000 - headerFileSize % 0x10000);
                        reader.BaseStream.Seek(seekAmount, SeekOrigin.Current);
                        continue;
                    }

                    if (magicString == "FPAC")
                    {
                        fileHeaderPosList.Add(new Tuple<string, long>(magicString, reader.BaseStream.Position));
                        var pacFileInfo =
                            new PACFileInfo(new MemoryStream(reader.ReadBytes(0x40, ByteOrder.LittleEndian)));
                        reader.BaseStream.Seek(-0x40, SeekOrigin.Current);
                        var headerFileSize =
                            pacFileInfo.FileLength;
                        var seekAmount = headerFileSize + (0x10000 - headerFileSize % 0x10000);
                        reader.BaseStream.Seek((long) seekAmount, SeekOrigin.Current);
                        continue;
                    }

                    if (magicString == "BCSM")
                    {
                        fileHeaderPosList.Add(new Tuple<string, long>(magicString, reader.BaseStream.Position));
                        var pacFileInfo =
                            new PACFileInfo(new MemoryStream(reader.ReadBytes(0x40, ByteOrder.LittleEndian)));
                        reader.BaseStream.Seek(-0x40, SeekOrigin.Current);
                        var headerFileSize =
                            pacFileInfo.FileLength + 0x10;
                        var seekAmount = headerFileSize + (0x10000 - headerFileSize % 0x10000);
                        reader.BaseStream.Seek((long) seekAmount, SeekOrigin.Current);
                        continue;
                    }

                    if (BitConverter.ToUInt16(magicBytes.Reverse().ToArray(), 4) == 0xFEFF)
                        fileHeaderPosList.Add(new Tuple<string, long>("idlist", reader.BaseStream.Position));

                    reader.BaseStream.Seek(0x10000, SeekOrigin.Current);
                }

                Console.WriteLine($"Found {fileHeaderPosList.Count} files.");

                reader.BaseStream.Position = 0;

                for (var i = 0;
                     i < fileHeaderPosList.Count &&
                     reader.BaseStream.Position < reader.BaseStream.Length;
                     i++)
                {
                    magicString = fileHeaderPosList[i].Item1;
                    var defaultExt = magicString == "FPAC" ? "pac" : magicString;
                    var filePath = Path.Combine(OutputPath,
                        i < tableOfContentsList.Count
                            ? tableOfContentsList[i]
                            : $"{Path.GetFileNameWithoutExtension(path)}+{i.ToString().PadLeft(5, '0')}.{defaultExt}");

                    var fileName = Path.GetFileName(filePath);

                    Console.WriteLine(
                        $"Processing {(i < tableOfContentsList.Count ? tableOfContentsList[i] : fileName)}...");

                    var fileLength = (int) ((i < fileHeaderPosList.Count - 1
                        ? fileHeaderPosList[i + 1].Item2
                        : reader.BaseStream.Length) - fileHeaderPosList[i].Item2);

                    var fileBytes = reader.ReadBytes(fileLength, ByteOrder.LittleEndian);

                    if (options.HasFlag(Options.Decompress) && magicString == "segs")
                    {
                        Console.WriteLine("Decompressing SEGS data...");
                        var origSEGSSize = fileBytes.Length;
                        var segsFileInfo = new SEGSFileInfo(new MemoryStream(fileBytes), fileName);

                        if (segsFileInfo.IsValidSEGS)
                        {
                            fileBytes = segsFileInfo.Decompress().ToArray();

                            if (fileBytes.Length < origSEGSSize)
                                Array.Resize(ref fileBytes, origSEGSSize);

                            magicString = Encoding.ASCII.GetString(fileBytes.Take(4).ToArray());

                            if (tableOfContentsList.Count == 0)
                            {
                                if (fileBytes.Take(4).SequenceEqual(PAC.MagicBytes))
                                    defaultExt = "pac";
                                else if (fileBytes.Take(4).SequenceEqual(HIP.MagicBytes))
                                    defaultExt = "hip";
                                else if (Encoding.ASCII.GetString(fileBytes.Take(6).ToArray()) == "//log:")
                                    defaultExt = "info";
                                else
                                    defaultExt = string.Empty;

                                filePath = Path.Combine(Path.GetDirectoryName(filePath),
                                    $"{Path.GetFileNameWithoutExtension(filePath)}.{defaultExt}");
                                fileName = Path.GetFileName(filePath);

                                Console.WriteLine($"Successfully decompressed as {fileName}");
                            }

                            Console.WriteLine("Successfully decompressed.");
                        }
                        else
                        {
                            WarningMessage("SEGS data was not valid. Skipping decompression...");
                        }
                    }

                    if (options.HasFlag(Options.Trim))
                    {
                        var trimmed = false;
                        if (magicString == "segs")
                        {
                            var segsFileInfo = new SEGSFileInfo(new MemoryStream(fileBytes), fileName);

                            if (segsFileInfo.IsValidSEGS)
                            {
                                if (segsFileInfo.SEGSFile.CompressedSize <= fileBytes.Length)
                                {
                                    Console.WriteLine("Trimming based on SEGS header...");
                                    Array.Resize(ref fileBytes, (int) segsFileInfo.SEGSFile.CompressedSize);
                                    trimmed = true;
                                }
                            }
                            else
                            {
                                WarningMessage("SEGS data was not valid. Skipping trimming...");
                            }
                        }
                        else if (magicString == "FPAC")
                        {
                            Console.WriteLine("Trimming based on FPAC header...");
                            var pacFileInfo = new PACFileInfo(new MemoryStream(fileBytes), fileName);
                            if (pacFileInfo.IsValidPAC)
                            {
                                Array.Resize(ref fileBytes, (int) pacFileInfo.FileLength);
                                trimmed = true;
                            }
                            else
                            {
                                WarningMessage("FPAC data was not valid. Using default trimming method...");
                            }
                        }
                        else if (magicString == "BCSM")
                        {
                            Console.WriteLine("Trimming based on BCSM header...");
                            var pacFileInfo = new PACFileInfo(new MemoryStream(fileBytes), fileName);
                            if (pacFileInfo.IsValidPAC)
                            {
                                Array.Resize(ref fileBytes, (int) pacFileInfo.FileLength + 0x10);
                                trimmed = true;
                            }
                            else
                            {
                                WarningMessage("BCSM data was not valid. Using default trimming method...");
                            }
                        }

                        if (!trimmed)
                        {
                            Console.WriteLine("Trimming...");
                            var j = fileBytes.Length - 1;
                            while (fileBytes[j] == 0)
                                --j;
                            j++;
                            if (magicString != "//log:" && magicString != "idlist")
                            {
                                var remainder = j % TrimAlignment;
                                j += remainder == 0 ? 0 : TrimAlignment - remainder;
                            }

                            Array.Resize(ref fileBytes, j);
                            trimmed = true;
                        }

                        Console.WriteLine("Successfully trimmed.");
                    }

                    WriteFile(filePath, fileBytes, options);

                    Console.WriteLine(
                        $"Successfully processed {(i < tableOfContentsList.Count ? tableOfContentsList[i] : fileName)}");
                }

                reader.Close();
            }

            CompleteMessage();
        }, "Mode: Geo ArcSys PS3 Extractor");
    }

    private static void ShowUsage()
    {
        ConsoleTools.ShowUsage(
            $"Usage: {Path.GetFileName(AssemblyPath)} {CLIArg} <folder/bddata path> [EBOOT/TOC path] [options...]",
            ConsoleOptions
                .Concat(FileConsoleOptions.Where(fco => (AIO.FileOptions) fco.Flag != AIO.FileOptions.Endianness))
                .Concat(GlobalConsoleOptions).ToArray());
    }

    [Flags]
    private enum Options
    {
        Trim = 0x1,
        Decompress = 0x2,

        ExtractTOC = 0x10 /*,
        Rebuild = 0x8000000*/
    }
}