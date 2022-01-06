using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace GeoArcSysAIOCLITool.Util;

/// <summary>
///     Image loading toolset class which corrects the bug that prevents paletted PNG images with transparency from being
///     loaded as paletted.
/// </summary>
public class BitmapLoader
{
    private static readonly byte[] PNG_IDENTIFIER = {0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A};

    /// <summary>
    ///     Loads an image, checks if it is a PNG containing palette transparency, and if so, ensures it loads correctly.
    ///     The theory can be found at http://www.libpng.org/pub/png/book/chapter08.html
    /// </summary>
    /// <param name="filename">Filename to load</param>
    /// <returns>The loaded image</returns>
    public static Bitmap LoadBitmap(string filename)
    {
        var data = File.ReadAllBytes(filename);
        return LoadBitmap(data);
    }

    /// <summary>
    ///     Loads an image, checks if it is a PNG containing palette transparency, and if so, ensures it loads correctly.
    ///     The theory can be found at http://www.libpng.org/pub/png/book/chapter08.html
    /// </summary>
    /// <param name="data">File data to load</param>
    /// <returns>The loaded image</returns>
    public static Bitmap LoadBitmap(byte[] data)
    {
        byte[] transparencyData = null;
        if (data.Length > PNG_IDENTIFIER.Length)
        {
            // Check if the image is a PNG.
            var compareData = new byte[PNG_IDENTIFIER.Length];
            Array.Copy(data, compareData, PNG_IDENTIFIER.Length);
            if (PNG_IDENTIFIER.SequenceEqual(compareData))
            {
                // Check if it contains a palette.
                // I'm sure it can be looked up in the header somehow, but meh.
                var plteOffset = FindChunk(data, "PLTE");
                if (plteOffset != -1)
                {
                    // Check if it contains a palette transparency chunk.
                    var trnsOffset = FindChunk(data, "tRNS");
                    if (trnsOffset != -1)
                    {
                        // Get chunk
                        var trnsLength = GetChunkDataLength(data, trnsOffset);
                        transparencyData = new byte[trnsLength];
                        Array.Copy(data, trnsOffset + 8, transparencyData, 0, trnsLength);
                        // filter out the palette alpha chunk, make new data array
                        var data2 = new byte[data.Length - (trnsLength + 12)];
                        Array.Copy(data, 0, data2, 0, trnsOffset);
                        var trnsEnd = trnsOffset + trnsLength + 12;
                        Array.Copy(data, trnsEnd, data2, trnsOffset, data.Length - trnsEnd);
                        data = data2;
                    }
                }
            }
        }

        Bitmap loadedImage;
        using (var ms = new MemoryStream(data))
        using (var tmp = new Bitmap(ms))
        {
            loadedImage = CloneImage(tmp);
        }

        var pal = loadedImage.Palette;
        if (pal.Entries.Length == 0 || transparencyData == null)
            return loadedImage;
        for (var i = 0; i < pal.Entries.Length; i++)
        {
            if (i >= transparencyData.Length)
                break;
            var col = pal.Entries[i];
            pal.Entries[i] = Color.FromArgb(transparencyData[i], col.R, col.G, col.B);
        }

        loadedImage.Palette = pal;
        return loadedImage;
    }

    /// <summary>
    ///     Finds the start of a png chunk. This assumes the image is already identified as PNG.
    ///     It does not go over the first 8 bytes, but starts at the start of the header chunk.
    /// </summary>
    /// <param name="data">The bytes of the png image</param>
    /// <param name="chunkName">The name of the chunk to find.</param>
    /// <returns>The index of the start of the png chunk, or -1 if the chunk was not found.</returns>
    private static int FindChunk(byte[] data, string chunkName)
    {
        if (chunkName.Length != 4)
            throw new ArgumentException("Chunk must be 4 characters!", "chunkName");
        var chunkNamebytes = Encoding.ASCII.GetBytes(chunkName);
        if (chunkNamebytes.Length != 4)
            throw new ArgumentException("Chunk must be 4 characters!", "chunkName");
        var offset = PNG_IDENTIFIER.Length;
        var end = data.Length;
        var testBytes = new byte[4];
        // continue until either the end is reached, or there is not enough space behind it for reading a new chunk
        while (offset + 12 <= end)
        {
            Array.Copy(data, offset + 4, testBytes, 0, 4);
            // Alternative for more visual debugging:
            //String currentChunk = Encoding.ASCII.GetString(testBytes);
            //if (chunkName.Equals(currentChunk))
            //    return offset;
            if (chunkNamebytes.SequenceEqual(testBytes))
                return offset;
            var chunkLength = GetChunkDataLength(data, offset);
            // chunk size + chunk header + chunk checksum = 12 bytes.
            offset += 12 + chunkLength;
        }

        return -1;
    }

    private static int GetChunkDataLength(byte[] data, int offset)
    {
        if (offset + 4 > data.Length)
            throw new IndexOutOfRangeException("Bad chunk size in png image.");
        // Don't want to use BitConverter; then you have to check platform endianness and all that mess.
        var length = data[offset + 3] + (data[offset + 2] << 8) + (data[offset + 1] << 16) + (data[offset] << 24);
        if (length < 0)
            throw new IndexOutOfRangeException("Bad chunk size in png image.");
        return length;
    }

    /// <summary>
    ///     Clones an image object to free it from any backing resources.
    ///     Code taken from http://stackoverflow.com/a/3661892/ with some extra fixes.
    /// </summary>
    /// <param name="sourceImage">The image to clone</param>
    /// <returns>The cloned image</returns>
    private static Bitmap CloneImage(Bitmap sourceImage)
    {
        var rect = new Rectangle(0, 0, sourceImage.Width, sourceImage.Height);
        var targetImage = new Bitmap(rect.Width, rect.Height, sourceImage.PixelFormat);
        targetImage.SetResolution(sourceImage.HorizontalResolution, sourceImage.VerticalResolution);
        var sourceData = sourceImage.LockBits(rect, ImageLockMode.ReadOnly, sourceImage.PixelFormat);
        var targetData = targetImage.LockBits(rect, ImageLockMode.WriteOnly, targetImage.PixelFormat);
        var actualDataWidth = (Image.GetPixelFormatSize(sourceImage.PixelFormat) * rect.Width + 7) / 8;
        var h = sourceImage.Height;
        var origStride = sourceData.Stride;
        var isFlipped = origStride < 0;
        origStride = Math.Abs(origStride); // Fix for negative stride in BMP format.
        var targetStride = targetData.Stride;
        var imageData = new byte[actualDataWidth];
        var sourcePos = sourceData.Scan0;
        var destPos = targetData.Scan0;
        // Copy line by line, skipping by stride but copying actual data width
        for (var y = 0; y < h; y++)
        {
            Marshal.Copy(sourcePos, imageData, 0, actualDataWidth);
            Marshal.Copy(imageData, 0, destPos, actualDataWidth);
            sourcePos = new IntPtr(sourcePos.ToInt64() + origStride);
            destPos = new IntPtr(destPos.ToInt64() + targetStride);
        }

        targetImage.UnlockBits(targetData);
        sourceImage.UnlockBits(sourceData);
        // Fix for negative stride on BMP format.
        if (isFlipped)
            targetImage.RotateFlip(RotateFlipType.Rotate180FlipX);
        // For indexed images, restore the palette. This is not linking to a referenced
        // object in the original image; the getter of Palette creates a new object when called.
        if ((sourceImage.PixelFormat & PixelFormat.Indexed) != 0)
            targetImage.Palette = sourceImage.Palette;
        // Restore DPI settings
        targetImage.SetResolution(sourceImage.HorizontalResolution, sourceImage.VerticalResolution);
        return targetImage;
    }
}