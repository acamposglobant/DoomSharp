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
//      Tests for WadDirectory / WadFile (DOOM-004, w_wad.c migration).
//      Characterization values come from the C harness
//      (tools/charact/waddump.c over the UNMODIFIED w_wad.c), dumped to
//      tools/charact/wad_vectors.json; expected values below cite it.
//      Synthetic fixtures are rebuilt identically by WadTestFixtures.
//
//-----------------------------------------------------------------------------

using System.Text;
using DoomSharp.Core;
using DoomSharp.Data;
using Xunit;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// Builds the same synthetic fixtures as tools/gen/wad_fixtures.js so the
/// C# tests exercise byte-identical inputs to the C harness.
/// </summary>
public static class WadTestFixtures
{
    public static byte[] BuildWad(string id, (string Name, byte[] Data)[] lumps)
    {
        var directorySize = lumps.Length * 16;
        var dataLength = lumps.Sum(l => l.Data.Length);
        var infoTableOfs = 12 + dataLength;

        using var ms = new MemoryStream();
        var header = new byte[12];
        Encoding.ASCII.GetBytes(id).CopyTo(header, 0);
        BitConverter.GetBytes(lumps.Length).CopyTo(header, 4);
        BitConverter.GetBytes(infoTableOfs).CopyTo(header, 8);
        ms.Write(header);

        var directory = new List<byte[]>();
        var pos = 12;
        foreach (var (name, data) in lumps)
        {
            ms.Write(data);
            var entry = new byte[16];
            BitConverter.GetBytes(pos).CopyTo(entry, 0);
            BitConverter.GetBytes(data.Length).CopyTo(entry, 4);
            var nameBytes = new byte[8];
            Encoding.ASCII.GetBytes(name).CopyTo(nameBytes, 0);
            nameBytes.CopyTo(entry, 8);
            directory.Add(entry);
            pos += data.Length;
        }

        foreach (var entry in directory)
        {
            ms.Write(entry);
        }
        return ms.ToArray();
    }

    public static readonly byte[] E1M1Data = [1, 2, 3, 4, 5, 6, 7, 8];
    public static readonly byte[] Map01IwadData = [0xAA, 0xBB, 0xCC, 0xDD];
    public static readonly byte[] Map01PwadData = [0x11, 0x22, 0x33, 0x44];
    public static readonly byte[] DehackedData = [0xDE, 0xAD, 0xBE];

    /// <summary>Writes the fixture set matching wad_fixtures.js.</summary>
    public static string WriteFixtures(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "test_iwad.wad"),
            BuildWad("IWAD", [("E1M1", E1M1Data), ("MAP01", Map01IwadData)]));
        File.WriteAllBytes(Path.Combine(dir, "test_pwad.wad"),
            BuildWad("PWAD", [("MAP01", Map01PwadData)]));
        File.WriteAllBytes(Path.Combine(dir, "DEHACKED"), DehackedData);
        return dir;
    }
}

public class WadDirectoryTests : IDisposable
{
    private readonly string _tempDir;

    public WadDirectoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "doomsharp_wadtests", Guid.NewGuid().ToString("N"));
        WadTestFixtures.WriteFixtures(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ---------- characterization: matches C dump (wad_vectors.json) ----------

    [Fact]
    public void InitMultipleFiles_MatchesOriginalCDirectory()
    {
        // C dump: numlumps=4; E1M1(12,8) MAP01(20,4) MAP01(12,4) DEHACKED(0,3)
        using var dir = new WadDirectory();
        dir.InitMultipleFiles(
        [
            Path.Combine(_tempDir, "test_iwad.wad"),
            Path.Combine(_tempDir, "test_pwad.wad"),
            Path.Combine(_tempDir, "DEHACKED"),
            Path.Combine(_tempDir, "missing.wad"),   // skipped, like C
        ]);

        Assert.Equal(4, dir.NumLumps);

        Assert.Equal("E1M1", dir.Lumps[0].Name);
        Assert.Equal(12, dir.Lumps[0].Position);
        Assert.Equal(8, dir.Lumps[0].Size);

        Assert.Equal("MAP01", dir.Lumps[1].Name);
        Assert.Equal(20, dir.Lumps[1].Position);

        Assert.Equal("MAP01", dir.Lumps[2].Name);
        Assert.Equal(12, dir.Lumps[2].Position);

        Assert.Equal("DEHACKED", dir.Lumps[3].Name);
        Assert.Equal(0, dir.Lumps[3].Position);
        Assert.Equal(3, dir.Lumps[3].Size);
    }

    [Fact]
    public void Lookups_MatchOriginalCResults()
    {
        using var dir = new WadDirectory();
        dir.InitMultipleFiles(
        [
            Path.Combine(_tempDir, "test_iwad.wad"),
            Path.Combine(_tempDir, "test_pwad.wad"),
            Path.Combine(_tempDir, "DEHACKED"),
        ]);

        // C dump: check E1M1=0, MAP01=2, map01=2, DEHACK=-1, MISSING=-1
        Assert.Equal(0, dir.CheckNumForName("E1M1"));
        Assert.Equal(2, dir.CheckNumForName("MAP01"));    // PWAD copy wins
        Assert.Equal(2, dir.CheckNumForName("map01"));    // case-insensitive
        Assert.Equal(-1, dir.CheckNumForName("DEHACK"));  // 8-char name: no prefix match
        Assert.Equal(-1, dir.CheckNumForName("MISSING"));
        Assert.Equal(0, dir.GetNumForName("E1M1"));
    }

    [Fact]
    public void DuplicatePrecedence_LastLoadedWins_MatchesC()
    {
        using var dir = new WadDirectory();
        dir.InitMultipleFiles(
        [
            Path.Combine(_tempDir, "test_iwad.wad"),
            Path.Combine(_tempDir, "test_pwad.wad"),
        ]);

        // C dump: dup_precedence index=2, data=[17,34,51,68] (PWAD copy)
        var map01 = dir.GetNumForName("MAP01");
        Assert.Equal(2, map01);
        Assert.Equal(WadTestFixtures.Map01PwadData, dir.ReadLump(map01));
    }

    [Fact]
    public void ReadLump_MatchesOriginalCData()
    {
        using var dir = new WadDirectory();
        dir.InitMultipleFiles([Path.Combine(_tempDir, "test_iwad.wad")]);

        // C dump: read E1M1 -> [1,2,3,4,5,6,7,8]
        Assert.Equal(WadTestFixtures.E1M1Data, dir.ReadLump(dir.GetNumForName("E1M1")));
        Assert.Equal(8, dir.LumpLength(dir.GetNumForName("E1M1")));
    }

    // ---------- fatal error paths (match C I_Error behavior) ----------

    [Fact]
    public void OutOfRangeLumpIndex_IsFatal_LikeOriginalC()
    {
        using var dir = new WadDirectory();
        dir.InitMultipleFiles([Path.Combine(_tempDir, "test_iwad.wad")]);

        // C dump: oor fatal=true
        Assert.Throws<DoomFatalException>(() => dir.ReadLump(dir.NumLumps + 5));
        Assert.Throws<DoomFatalException>(() => dir.LumpLength(-1));
        Assert.Throws<DoomFatalException>(() => dir.CacheLumpNum(999));
    }

    [Fact]
    public void GetNumForName_Missing_IsFatal_LikeOriginalC()
    {
        using var dir = new WadDirectory();
        dir.InitMultipleFiles([Path.Combine(_tempDir, "test_iwad.wad")]);

        // C dump: get NOEXIST fatal=true
        var ex = Assert.Throws<DoomFatalException>(() => dir.GetNumForName("NOEXIST"));
        Assert.Contains("NOEXIST", ex.Message);
        // CheckNumForName stays non-fatal
        Assert.Equal(-1, dir.CheckNumForName("NOEXIST"));
    }

    // ---------- malformed WADs ----------

    [Fact]
    public void BadMagic_ThrowsFatal_WithOriginalMessage()
    {
        var path = Path.Combine(_tempDir, "bad_id.wad");
        File.WriteAllBytes(path, BuildBrokenWad("XXXX", 1, 28));

        // w_wad.c:190 — "Wad file %s doesn't have IWAD or PWAD id"
        var ex = Assert.Throws<DoomFatalException>(() => new WadFile(path));
        Assert.Contains("doesn't have IWAD or PWAD id", ex.Message);
    }

    [Fact]
    public void TruncatedDirectory_ThrowsFatal()
    {
        var path = Path.Combine(_tempDir, "truncated_dir.wad");
        // numlumps=2 but only 1 directory entry present
        var data = new byte[12 + 8 + 16];
        Encoding.ASCII.GetBytes("IWAD").CopyTo(data, 0);
        BitConverter.GetBytes(2).CopyTo(data, 4);
        BitConverter.GetBytes(20).CopyTo(data, 8);
        File.WriteAllBytes(path, data);

        Assert.Throws<DoomFatalException>(() => new WadFile(path));
    }

    [Fact]
    public void LumpOutOfBounds_ThrowsFatal()
    {
        var path = Path.Combine(_tempDir, "oob_lump.wad");
        var wad = WadTestFixtures.BuildWad("IWAD", [("BIG", [1, 2, 3, 4])]);
        // Directory entry for lump 0 starts at infotableofs = 12 + 4 = 16;
        // its filepos field is the first 4 bytes of the entry.
        BitConverter.GetBytes(0x7FFFFFFF).CopyTo(wad, 16);
        File.WriteAllBytes(path, wad);

        Assert.Throws<DoomFatalException>(() => new WadFile(path));
    }

    [Fact]
    public void NegativeLumpCount_ThrowsFatal()
    {
        var path = Path.Combine(_tempDir, "neg_count.wad");
        var data = new byte[12];
        Encoding.ASCII.GetBytes("IWAD").CopyTo(data, 0);
        BitConverter.GetBytes(-1).CopyTo(data, 4);
        BitConverter.GetBytes(12).CopyTo(data, 8);
        File.WriteAllBytes(path, data);

        Assert.Throws<DoomFatalException>(() => new WadFile(path));
    }

    [Fact]
    public void InitMultipleFiles_NoLumps_IsFatal()
    {
        // w_wad.c:305-307 — "W_InitFiles: no files found"
        using var dir = new WadDirectory();
        var ex = Assert.Throws<DoomFatalException>(
            () => dir.InitMultipleFiles([Path.Combine(_tempDir, "missing.wad")]));
        Assert.Contains("no files found", ex.Message);
    }

    // ---------- name normalization ----------

    [Fact]
    public void LumpNames_NullPadded_AndFull8Char_Preserve()
    {
        // Null-padded name normalizes; full 8-char name without NUL preserved.
        var wad = WadTestFixtures.BuildWad("IWAD",
        [
            ("E1M1", new byte[4]),        // stored as "E1M1\0\0\0\0"
            ("FLOOR4_8", new byte[4]),    // full 8 chars, no terminator
        ]);
        var path = Path.Combine(_tempDir, "names.wad");
        File.WriteAllBytes(path, wad);

        using var dir = new WadDirectory();
        dir.InitMultipleFiles([path]);

        Assert.Equal("E1M1", dir.Lumps[0].Name);
        Assert.Equal("FLOOR4_8", dir.Lumps[1].Name);
        Assert.Equal(1, dir.CheckNumForName("floor4_8"));
        Assert.Equal(0, dir.CheckNumForName("e1m1"));
    }

    [Fact]
    public void Lookup_LongerThan8Chars_DoesNotMatchShortLumpName()
    {
        using var dir = new WadDirectory();
        dir.InitMultipleFiles([Path.Combine(_tempDir, "test_iwad.wad")]);

        // The C original compares 8 packed bytes: a lookup name longer
        // than the lump name fills the remaining bytes with real
        // characters, which do NOT match the lump's NUL padding
        // ("E1M1_AND" != "E1M1\0\0\0\0" in the C int comparison).
        // Verified against C semantics of w_wad.c:351-390.
        Assert.Equal(-1, dir.CheckNumForName("E1M1_and_more_suffix"));
    }

    // ---------- ExtractFileBase parity (w_wad.c:93-114) ----------

    [Fact]
    public void ExtractFileBase_HandlesBothSeparators()
    {
        Assert.Equal("MAP01", WadDirectory.ExtractFileBase("/path/to/map01.wad"));
        Assert.Equal("MAP01", WadDirectory.ExtractFileBase(@"C:\path\to\MAP01.wad"));
        Assert.Equal("MAP01", WadDirectory.ExtractFileBase("map01"));          // no dir
        Assert.Equal("MAP01", WadDirectory.ExtractFileBase("dir/map01"));      // no ext
    }

    [Fact]
    public void ExtractFileBase_FatalOnLongBase_LikeOriginalC()
    {
        // w_wad.c:109-110 — I_Error("Filename base of %s >8 chars")
        Assert.Throws<DoomFatalException>(
            () => WadDirectory.ExtractFileBase("verylongname.deh"));
        // Exactly 8 is fine.
        Assert.Equal("E1M1_123", WadDirectory.ExtractFileBase("e1m1_8char".Replace("8char", "123")[..8] + ".lmp"));
    }

    // ---------- caching ----------

    [Fact]
    public void CacheLumpName_ReturnsSameInstance()
    {
        using var dir = new WadDirectory();
        dir.InitMultipleFiles([Path.Combine(_tempDir, "test_iwad.wad")]);

        var a = dir.CacheLumpName("E1M1");
        var b = dir.CacheLumpName("E1M1");
        Assert.Same(a, b);
        Assert.Equal(WadTestFixtures.E1M1Data, a);
    }

    // ---------- disposal ----------

    [Fact]
    public void InitMultipleFiles_DisposesOnFailure()
    {
        var dir = new WadDirectory();
        Assert.Throws<DoomFatalException>(() =>
            dir.InitMultipleFiles([Path.Combine(_tempDir, "missing.wad")]));
        dir.Dispose(); // must not throw even though init failed
    }

    // ---------- optional shareware IWAD test (gated) ----------

    [Fact]
    public void RealSharewareIwad_Parses()
    {
        var doomWadDir = Environment.GetEnvironmentVariable("DOOMWADDIR");
        if (string.IsNullOrEmpty(doomWadDir) || !File.Exists(Path.Combine(doomWadDir, "doom1.wad")))
        {
            return; // gated: shareware IWAD not available in this environment
        }
        var path = Path.Combine(doomWadDir, "doom1.wad");

        using var dir = new WadDirectory();
        dir.InitMultipleFiles([path]);

        // Shareware doom1.wad has 1265 lumps and E1M1 exists.
        Assert.True(dir.NumLumps > 1000, $"unexpected lump count {dir.NumLumps}");
        Assert.True(dir.CheckNumForName("E1M1") >= 0, "E1M1 not found in shareware IWAD");
        Assert.True(dir.CheckNumForName("PLAYPAL") >= 0, "PLAYPAL not found");
    }

    private static byte[] BuildBrokenWad(string id, int numLumps, int infoTableOfs)
    {
        var data = new byte[32];
        Encoding.ASCII.GetBytes(id).CopyTo(data, 0);
        BitConverter.GetBytes(numLumps).CopyTo(data, 4);
        BitConverter.GetBytes(infoTableOfs).CopyTo(data, 8);
        return data;
    }
}
