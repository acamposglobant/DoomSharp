// Emacs style mode select   -*- C# -*-
//-----------------------------------------------------------------------------
//
// Copyright (C) 1993-1996 by id Software, Inc.
//
// This source is available for distribution and/or modification
// only under the terms of the GNU General Public License 2.0 as
// published by the Free Software Foundation.
//
// DESCRIPTION:
//      WAD file structures and single-file reader.
//      Migration of Legacy/linuxdoom-1.10/w_wad.h (wadinfo_t,
//      filelump_t, lumpinfo_t) and the file-handling parts of w_wad.c.
//
//-----------------------------------------------------------------------------

using System.Buffers.Binary;
using System.Text;
using DoomSharp.Core;
using Serilog;

namespace DoomSharp.Data;

/// <summary>
/// A file that lump data can be read from (WAD file or single-lump file).
/// </summary>
public interface ILumpSource : IDisposable
{
    /// <summary>Reads lump data at the given absolute file offset.</summary>
    void ReadAt(long position, byte[] buffer, int count);
}

/// <summary>
/// One directory entry of a WAD file.
/// Migration of <c>lumpinfo_t</c> (w_wad.h:56-62) extended with the
/// owning file so a directory spanning multiple WADs can read lumps.
/// </summary>
public sealed class LumpInfo
{
    public const int NameLength = 8;
    public const int DirectoryEntrySize = 16;

    private readonly byte[] _name = new byte[NameLength];

    /// <summary>Raw 8-byte lump name (null-padded, as stored on disk).</summary>
    public byte[] NameBytes => _name;

    /// <summary>
    /// Lump name with trailing NUL padding removed, ASCII-uppercased
    /// (matches the strupr'd comparison name of W_CheckNumForName).
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Offset of the lump data within the WAD file (filepos).</summary>
    public long Position { get; internal set; }

    /// <summary>Size of the lump data in bytes (size).</summary>
    public int Size { get; internal set; }

    /// <summary>The WAD file this lump belongs to.</summary>
    public ILumpSource Source { get; internal set; } = null!;

    /// <summary>
    /// Fills the entry from a 16-byte directory record
    /// (filelump_t: int filepos, int size, char name[8] — little-endian).
    /// </summary>
    public void ReadFrom(ReadOnlySpan<byte> record, WadFile source)
    {
        Position = BinaryPrimitives.ReadInt32LittleEndian(record);
        Size = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(4));

        // Copy the raw 8 name bytes, stopping at the first NUL (if any)
        // so "E1M1\0\0\0\0" normalizes to "E1M1" while full 8-char names
        // without a terminator ("FLOOR4_8") are preserved.
        var nameEnd = 0;
        while (nameEnd < 8 && record[8 + nameEnd] != 0)
        {
            nameEnd++;
        }

        for (var i = 0; i < nameEnd; i++)
        {
            _name[i] = record[8 + i];
        }
        for (var i = nameEnd; i < 8; i++)
        {
            _name[i] = 0;
        }

        Name = AsciiUpper(_name.AsSpan(0, nameEnd));
        Source = source;
    }

    /// <summary>
    /// ASCII-only uppercase (a-z only), matching C toupper on the
    /// ASCII lump names used by the game; leaves other bytes unchanged.
    /// </summary>
    public static string AsciiUpper(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i] = b is >= (byte)'a' and <= (byte)'z'
                ? (char)(b - 32)
                : (char)b;
        }
        return new string(chars);
    }

    /// <summary>
    /// Sets the name for a single-lump file (ExtractFileBase result,
    /// already uppercased and ≤8 chars by w_wad.c:93-114 rules).
    /// </summary>
    internal void SetSingleLumpName(string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        Array.Clear(_name);
        Array.Copy(bytes, _name, Math.Min(bytes.Length, NameLength));
        Name = name;
    }
}

/// <summary>
/// One open WAD file. Parses the 12-byte header (wadinfo_t) and the
/// lump directory (filelump_t records) in little-endian byte order.
/// </summary>
public sealed class WadFile : ILumpSource
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(WadFile));

    public const int HeaderSize = 12;

    private readonly FileStream _stream;

    public string FilePath { get; }
    public long FileLength { get; }
    public IReadOnlyList<LumpInfo> Lumps { get; }

    public WadFile(string path)
    {
        Log.Verbose("WadFile({Path})", path);
        FilePath = path;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        try
        {
            FileLength = _stream.Length;

            Span<byte> header = stackalloc byte[HeaderSize];
            ReadExactly(header);

            var id = Encoding.ASCII.GetString(header.Slice(0, 4));
            if (id != "IWAD" && id != "PWAD")
            {
                Log.Verbose("WadFile fatal: bad id {Id}", id);
                throw new DoomFatalException(
                    $"Wad file {path} doesn't have IWAD or PWAD id");
            }

            var numLumps = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4));
            var infoTableOfs = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(8));

            ValidateDirectoryBounds(numLumps, infoTableOfs);

            var lumps = new LumpInfo[numLumps];
            if (numLumps > 0)
            {
                _stream.Seek(infoTableOfs, SeekOrigin.Begin);
                var directoryBytes = new byte[(long)numLumps * LumpInfo.DirectoryEntrySize];
                ReadExactly(directoryBytes);

                for (var i = 0; i < numLumps; i++)
                {
                    var lump = new LumpInfo();
                    lump.ReadFrom(
                        directoryBytes.AsSpan(i * LumpInfo.DirectoryEntrySize, LumpInfo.DirectoryEntrySize),
                        this);
                    ValidateLumpBounds(lump, i);
                    lumps[i] = lump;
                }
            }

            Lumps = lumps;
            Log.Verbose("WadFile({Path}) = {NumLumps} lumps, {FileLength} bytes", path, numLumps, FileLength);
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    private void ValidateDirectoryBounds(int numLumps, int infoTableOfs)
    {
        if (numLumps < 0)
        {
            Log.Verbose("WadFile fatal: negative lump count {NumLumps}", numLumps);
            throw new DoomFatalException(
                $"Wad file {FilePath}: negative lump count ({numLumps})");
        }

        var directoryEnd = infoTableOfs + (long)numLumps * LumpInfo.DirectoryEntrySize;
        if (infoTableOfs < 0 || directoryEnd > FileLength)
        {
            Log.Verbose("WadFile fatal: directory out of bounds (ofs={Ofs}, n={NumLumps})", infoTableOfs, numLumps);
            throw new DoomFatalException(
                $"Wad file {FilePath}: directory out of bounds " +
                $"(infotableofs={infoTableOfs}, numlumps={numLumps}, filelen={FileLength})");
        }
    }

    private void ValidateLumpBounds(LumpInfo lump, int index)
    {
        if (lump.Position < 0 || lump.Size < 0 || lump.Position + lump.Size > FileLength)
        {
            Log.Verbose("WadFile fatal: lump {Index} out of bounds", index);
            throw new DoomFatalException(
                $"Wad file {FilePath}: lump {index} ({lump.Name}) out of bounds " +
                $"(pos={lump.Position}, size={lump.Size}, filelen={FileLength})");
        }
    }

    /// <summary>Reads exactly the requested number of bytes from the current position.</summary>
    private void ReadExactly(Span<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = _stream.Read(destination.Slice(total));
            if (read <= 0)
            {
                throw new DoomFatalException($"Wad file {FilePath}: unexpected end of file");
            }
            total += read;
        }
    }

    /// <summary>Reads lump data at the given absolute file offset.</summary>
    public void ReadAt(long position, byte[] buffer, int count)
    {
        _stream.Seek(position, SeekOrigin.Begin);
        var total = 0;
        while (total < count)
        {
            var read = _stream.Read(buffer, total, count - total);
            if (read <= 0)
            {
                throw new DoomFatalException("W_ReadLump: only read partial lump");
            }
            total += read;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}

/// <summary>
/// A non-WAD file added as a single lump (w_wad.c:172-180): the whole
/// file is one lump named by its base filename. No WAD header parsing.
/// </summary>
public sealed class SingleLumpFile : ILumpSource
{
    private readonly FileStream _stream;

    public string FilePath { get; }
    public long FileLength { get; }

    public SingleLumpFile(string path)
    {
        FilePath = path;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        FileLength = _stream.Length;
    }

    public void ReadAt(long position, byte[] buffer, int count)
    {
        _stream.Seek(position, SeekOrigin.Begin);
        var total = 0;
        while (total < count)
        {
            var read = _stream.Read(buffer, total, count - total);
            if (read <= 0)
            {
                throw new DoomFatalException("W_ReadLump: only read partial lump");
            }
            total += read;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
