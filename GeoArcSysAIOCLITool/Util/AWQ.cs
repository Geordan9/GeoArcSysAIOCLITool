using System.IO;

namespace GeoArcSysAIOCLITool.Util;

public static class AWQ
{
    private static readonly int NO_OF_CHARS = 256;

    //A utility function to get maximum of two integers
    private static int Max(int a, int b)
    {
        return a > b ? a : b;
    }

    //The preprocessing function for Boyer Moore's
    //bad character heuristic
    private static void BadCharHeuristic(char[] str, int size, int[] badchar)
    {
        int i;

        // Initialize all occurrences as -1
        for (i = 0; i < NO_OF_CHARS; i++)
            badchar[i] = -1;

        // Fill the actual value of last occurrence
        // of a character
        for (i = 0; i < size; i++)
            badchar[str[i]] = i;
    }

    /* A pattern searching function that uses Bad
    Character Heuristic of Boyer Moore Algorithm */
    public static bool Search(BinaryReader reader, char[] pat)
    {
        var m = pat.Length;

        var badchar = new int[NO_OF_CHARS];

        /* Fill the bad character array by calling
        the preprocessing function badCharHeuristic()
        for given pattern */
        BadCharHeuristic(pat, m, badchar);

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var j = m - 1;

            /* Keep reducing index j of pattern while
            characters of pattern and text are
            matching at this shift s */
            reader.BaseStream.Seek(j, SeekOrigin.Current);
            var b = reader.ReadByte();
            reader.BaseStream.Seek(-(1 + j), SeekOrigin.Current);
            while (j >= 0 && pat[j] == b)
            {
                j--;
                reader.BaseStream.Seek(j, SeekOrigin.Current);
                b = reader.ReadByte();
                reader.BaseStream.Seek(-(1 + j), SeekOrigin.Current);
            }

            if (j < 0) return true;

            reader.BaseStream.Seek(j, SeekOrigin.Current);
            b = reader.ReadByte();
            reader.BaseStream.Seek(-(1 + j), SeekOrigin.Current);
            reader.BaseStream.Seek(Max(1, j - badchar[b]), SeekOrigin.Current);
        }

        return false;
    }
}