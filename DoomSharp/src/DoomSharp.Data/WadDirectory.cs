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
//      Lump directory spanning one or more WAD files.
//      Migration of Legacy/linuxdoom-1.10/w_wad.c (W_AddFile,
//      W_InitMultipleFiles, W_CheckNumForName, W_GetNumForName,
//      W_LumpLength, W_ReadLump, W_CacheLumpNum/Name, ExtractFileBase).
//
//-----------------------------------------------------------------------------

using System.Buffers.Binary;
using System.Text;
using DoomSharp.Core;

namespace DoomSharp.Data;

/// <summary>
/// Lump directory over one or more WAD files.
/// Migration of the global <c>lumpinfo</c>/<c>numlumps</c> state of
/// w_wad.c, with deterministic disposal of opened files.
/// </summary>
public sealed class WadDirectory : IDisposable
{
    private readonly List<LumpInfo> _lumps = [];
    private readonly List<IDisposable> _files = [];
    private readonly Dictionary<string, byte[]> _lumpCache = new(StringComparer.Ordinal);

    /// <summary>Total number of lumps (numlumps, w_wad.c:66).</summary>
    public int NumLumps => _lumps.Count;

    /// <summary>All lump entries in load order.</summary>
    public IReadOnlyList<LumpInfo> Lumps => _lumps;

    /// <summary>
    /// W_InitMultipleFiles (w_wad.c:296-311): adds each file, then
    /// aborts if the resulting directory is empty. Files that cannot
    /// be opened are skipped with a message (original behavior).
    /// Disposes everything on failure.
    /// </summary>
    public void InitMultipleFiles(IEnumerable<string> filenames)
    {
        try
        {
            foreach (var name in filenames)
            {
                AddFile(name);
            }

            if (NumLumps == 0)
            {
                throw new DoomFatalException("W_InitFiles: no files found");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// W_AddFile (w_wad.c:141-226). Files with a .wad extension are
    /// multi-lump WADs; all other files become a single lump named by
    /// their base filename. Files that cannot be opened are skipped
    /// with a console message (w_wad.c:163-167).
    /// </summary>
    public void AddFile(string filename)
    {
        // Single-lump path: any file not ending in ".wad" (case-insensitive,
        // as the original uses strcasecmp via strcmpi, w_wad.c:158).
        // The original does NOT parse a WAD header here — it opens the
        // file, uses filelength() as the lump size, and reads it whole
        // (w_wad.c:172-180).
        if (!filename.EndsWith(".wad", StringComparison.OrdinalIgnoreCase))
        {
            var name = ExtractFileBase(filename);

            SingleLumpFile? file;
            try
            {
                file = new SingleLumpFile(filename);
            }
            catch (Exception e) when (e is not DoomFatalException)
            {
                // w_wad.c:163-167 — printf + return; optional file missing.
                Console.WriteLine($" couldn't open {filename}");
                return;
            }

            var lump = new LumpInfo
            {
                Position = 0,
                Size = (int)file.FileLength,
                Source = file,
            };
            lump.SetSingleLumpName(name);

            _files.Add(file);
            _lumps.Add(lump);
            return;
        }

        // Multi-lump WAD path.
        WadFile wad;
        try
        {
            wad = new WadFile(filename);
        }
        catch (Exception e) when (e is not DoomFatalException)
        {
            Console.WriteLine($" couldn't open {filename}");
            return;
        }

        _files.Add(wad);
        _lumps.AddRange(wad.Lumps);
    }

    /// <summary>
    /// ExtractFileBase (w_wad.c:93-114): base filename without directory
    /// or extension, uppercased, max 8 chars — fatal if longer.
    /// Handles both '/' and '\' separators.
    /// </summary>
    public static string ExtractFileBase(string path)
    {
        var src = path.Length - 1;

        // Back up until a \ or / or the start (w_wad.c:96-101).
        while (src != 0 && path[src - 1] != '\\' && path[src - 1] != '/')
        {
            src--;
        }

        // Copy up to eight characters, stopping at '.' (w_wad.c:107-113).
        var length = 0;
        Span<char> dest = stackalloc char[8];
        while (src < path.Length && path[src] != '.')
        {
            if (++length == 9)
            {
                throw new DoomFatalException($"Filename base of {path} >8 chars");
            }
            dest[length - 1] = AsciiUpperChar(path[src]);
            src++;
        }

        return new string(dest.Slice(0, length));
    }

    private static char AsciiUpperChar(char c)
    {
        return c is >= 'a' and <= 'z' ? (char)(c - 32) : c;
    }

    /// <summary>
    /// W_CheckNumForName (w_wad.c:351-390): case-insensitive lookup,
    /// scanning BACKWARDS so the last-loaded lump wins (PWAD overrides
    /// IWAD). Returns -1 if not found (non-fatal).
    /// </summary>
    public int CheckNumForName(string name)
    {
        var name8 = NormalizeName(name);

        // Backwards scan — precedence to later files (w_wad.c:376-386).
        for (var i = _lumps.Count - 1; i >= 0; i--)
        {
            if (NameMatches(_lumps[i], name8))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// W_GetNumForName (w_wad.c:397-407): fatal if not found.
    /// </summary>
    public int GetNumForName(string name)
    {
        var lump = CheckNumForName(name);
        if (lump < 0)
        {
            throw new DoomFatalException($"W_GetNumForName: {name} not found!");
        }
        return lump;
    }

    /// <summary>W_LumpLength (w_wad.c:412-421): fatal on out-of-range index.</summary>
    public int LumpLength(int lump)
    {
        ValidateLumpIndex(lump);
        return _lumps[lump].Size;
    }

    /// <summary>
    /// W_ReadLump (w_wad.c:411-470): reads lump data into a new array.
    /// Fatal on out-of-range index or short read.
    /// </summary>
    public byte[] ReadLump(int lump)
    {
        ValidateLumpIndex(lump);

        var info = _lumps[lump];
        var data = new byte[info.Size];
        if (info.Size > 0)
        {
            info.Source.ReadAt(info.Position, data, info.Size);
        }
        return data;
    }

    /// <summary>
    /// W_CacheLumpNum (w_wad.c:472-510) with a managed cache in place of
    /// z_zone purge tags (documented deviation, revisited in DOOM-005).
    /// </summary>
    public byte[] CacheLumpNum(int lump)
    {
        ValidateLumpIndex(lump);

        var name = _lumps[lump].Name;
        if (_lumpCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var data = ReadLump(lump);
        _lumpCache[name] = data;
        return data;
    }

    /// <summary>W_CacheLumpName (w_wad.c:512-519).</summary>
    public byte[] CacheLumpName(string name)
    {
        return CacheLumpNum(GetNumForName(name));
    }

    /// <summary>
    /// Normalizes a lookup name: truncate to 8 chars, ASCII-uppercase
    /// (strupr of the strncpy'd 8 bytes, w_wad.c:361-371).
    /// </summary>
    private static string NormalizeName(string name)
    {
        var chars = new char[Math.Min(name.Length, LumpInfo.NameLength)];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = AsciiUpperChar(name[i]);
        }
        return new string(chars);
    }

    private static bool NameMatches(LumpInfo lump, string name8)
    {
        // Compare against the stored (NUL-stripped, uppercased) name.
        // The C original compares the full 8 bytes including NUL padding;
        // since both sides are NUL-padded and the comparison name is
        // NUL-terminated at the same position, string equality of the
        // NUL-stripped forms is equivalent for all practical names.
        return string.Equals(lump.Name, name8, StringComparison.Ordinal);
        // Note: lump.Name is already ASCII-uppercased at parse time.
    }

    private void ValidateLumpIndex(int lump)
    {
        if (lump < 0 || lump >= _lumps.Count)
        {
            // Uniform mapping of the original I_Error paths (ADR-002).
            throw new DoomFatalException($"W_GetNumForName: lump {lump} out of range");
        }
    }

    public void Dispose()
    {
        foreach (var file in _files)
        {
            file.Dispose();
        }
        _files.Clear();
        _lumps.Clear();
        _lumpCache.Clear();
    }
}
