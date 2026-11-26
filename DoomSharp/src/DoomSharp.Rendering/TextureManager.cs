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
//      Renderer data manager. Migration of Legacy/linuxdoom-1.10/
//      r_data.c: R_InitTextures, R_InitFlats, R_InitSpriteLumps,
//      R_InitColormaps, R_GenerateLookup, R_GenerateComposite,
//      R_DrawColumnInCache, R_GetColumn, and the name lookups.
//      See docs/adr/ADR-004 for tracing; DOOM-030a plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using System.Buffers.Binary;
using DoomSharp.Core;
using DoomSharp.Data;
using Serilog;

namespace DoomSharp.Rendering;

/// <summary>
/// Renderer data manager (r_data.c globals): textures, flats, sprite
/// headers, colormaps. Patch lumps are cached PU_CACHE; composites are
/// retagged PU_CACHE after build (the Z_ChangeTag at r_data.c:288).
/// </summary>
public sealed class TextureManager : IDisposable
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(TextureManager));

    private readonly ZoneMemory? _zone;
    private WadDirectory? _wad;

    // --- R_InitTextures state (r_data.c globals) ---
    private Texture[] _textures = [];
    private int[] _patchlookup = [];
    private short[][] _texturecolumnlump = [];  // per-texture: short[width]
    private int[][] _texturecolumnofs = [];     // per-texture: int[width]
    private int[] _texturecompositesize = [];
    private byte[]?[] _texturecomposite = [];   // per-texture: composite block or null
    private int[] _texturewidthmask = [];
    private int[] _textureheight = [];          // fixed
    private int[] _texturetranslation = [];
    private int _numtextures;

    // --- R_InitFlats state ---
    private int _firstflat;
    private int _lastflat;
    private int _numflats;
    private int[] _flattranslation = [];

    // --- R_InitSpriteLumps state ---
    private int _firstspritelump;
    private int _lastspritelump;
    private int[] _spritewidth = [];
    private int[] _spriteoffset = [];
    private int[] _spritetopoffset = [];

    // --- R_InitColormaps state ---
    private byte[] _colormaps = [];

    public TextureManager(ZoneMemory? zone = null)
    {
        _zone = zone;
        Log.Verbose("TextureManager({HasZone})", zone != null);
    }

    /// <summary>Number of composite textures (numtextures).</summary>
    public int NumTextures => _numtextures;

    /// <summary>Texture definitions (textures[i]).</summary>
    public IReadOnlyList<Texture> Textures => _textures;

    /// <summary>Global animation translation (identity until DOOM-050).</summary>
    public int[] TextureTranslation => _texturetranslation;

    /// <summary>Flat animation translation (identity until DOOM-050).</summary>
    public int[] FlatTranslation { get; private set; } = [];

    /// <summary>First flat lump index (firstflat).</summary>
    public int FirstFlat => _firstflat;

    /// <summary>Last flat lump index (lastflat).</summary>
    public int LastFlat => _lastflat;

    /// <summary>Number of flats (numflats).</summary>
    public int NumFlats => _numflats;

    /// <summary>First sprite lump index (firstspritelump).</summary>
    public int FirstSpriteLump => _firstspritelump;

    /// <summary>Number of sprite lumps (numspritelumps).</summary>
    public int NumSpriteLumps => _spritewidth.Length;

    /// <summary>Sprite widths, fixed (spritewidth).</summary>
    public int[] SpriteWidth => _spritewidth;

    /// <summary>Sprite left offsets, fixed (spriteoffset).</summary>
    public int[] SpriteOffset => _spriteoffset;

    /// <summary>Sprite top offsets, fixed (spritetopoffset).</summary>
    public int[] SpriteTopOffset => _spritetopoffset;

    /// <summary>Per-texture power-of-two width masks (texturewidthmask).</summary>
    public int[] TextureWidthMask => _texturewidthmask;

    /// <summary>Per-texture heights, fixed (textureheight).</summary>
    public int[] TextureHeight => _textureheight;

    /// <summary>Loaded COLORMAP lump (34 x 256 bytes).</summary>
    public byte[] Colormaps => _colormaps;

    /// <summary>
    /// R_InitData (r_data.c:654-664): Textures, Flats, SpriteLumps,
    /// Colormaps — in that order.
    /// </summary>
    public void InitData(WadDirectory wad, ZoneMemory zone)
    {
        Log.Verbose("InitData()");
        _wad = wad;
        InitTextures(wad, zone);
        InitFlats(wad, zone);
        InitSpriteLumps(wad, zone);
        InitColormaps(wad, zone);
        Log.Verbose(
            "InitData() = {Textures} textures, {Flats} flats, {Sprites} sprites",
            _numtextures, _numflats, _spritewidth.Length);
    }

    // ------------------------------------------------------------------
    // R_InitTextures (r_data.c:411-574)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_InitTextures (r_data.c:411-574): PNAMES patch lookup,
    /// TEXTURE1/TEXTURE2 composite texture definitions, per-texture
    /// column tables, power-of-two width masks, R_GenerateLookup for
    /// every texture, identity texturetranslation.
    /// </summary>
    public void InitTextures(WadDirectory wad, ZoneMemory zone)
    {
        Log.Verbose("InitTextures()");

        // Load the patch names from pnames[lump] (r_data.c:419-441).
        var names = wad.ReadLump(wad.GetNumForName("PNAMES"));
        var numMappatches = BinaryPrimitives.ReadInt32LittleEndian(names);
        _patchlookup = new int[numMappatches];
        for (var i = 0; i < numMappatches; i++)
        {
            var name = ReadName8(names, 4 + i * 8);
            // r_data.c:437-441 — may be -1; fatal only if referenced.
            _patchlookup[i] = wad.CheckNumForName(name);
        }

        // Load the map texture definitions from TEXTURE1/TEXTURE2
        // (r_data.c:441-460).
        var maptex1 = wad.ReadLump(wad.GetNumForName("TEXTURE1"));
        var numTextures1 = BinaryPrimitives.ReadInt32LittleEndian(maptex1);
        var maxOff = maptex1.Length;
        var maptex2 = wad.CheckNumForName("TEXTURE2") != -1
            ? wad.ReadLump(wad.GetNumForName("TEXTURE2"))
            : null;
        var numTextures2 = maptex2 != null
            ? BinaryPrimitives.ReadInt32LittleEndian(maptex2)
            : 0;
        _numtextures = numTextures1 + numTextures2;

        _textures = new Texture[_numtextures];
        _texturecolumnlump = new short[_numtextures][];
        _texturecolumnofs = new int[_numtextures][];
        _texturecompositesize = new int[_numtextures];
        _texturecomposite = new byte[]?[_numtextures];
        _texturewidthmask = new int[_numtextures];
        _textureheight = new int[_numtextures];

        var directory = 4; // after numtextures1
        var current = maptex1;
        for (var i = 0; i < _numtextures; i++, directory += 4)
        {
            if (i == numTextures1)
            {
                // Start looking in second texture file (r_data.c:509-515).
                current = maptex2!;
                maxOff = maptex2!.Length;
                directory = 4;
            }

            var offset = BinaryPrimitives.ReadInt32LittleEndian(current.AsSpan(directory));
            if (offset > maxOff)
            {
                Log.Verbose("InitTextures fatal: bad texture directory (offset {Offset})", offset);
                throw new DoomFatalException("R_InitTextures: bad texture directory");
            }

            var mtexture = current.AsSpan(offset);
            var texture = new Texture
            {
                Width = BinaryPrimitives.ReadInt16LittleEndian(mtexture.Slice(8)),
                Height = BinaryPrimitives.ReadInt16LittleEndian(mtexture.Slice(10)),
                PatchCount = BinaryPrimitives.ReadInt16LittleEndian(mtexture.Slice(12)),
            };
            for (var n = 0; n < 8; n++)
            {
                texture.Name[n] = mtexture[n];
            }
            texture.Patches = new TexPatch[texture.PatchCount];

            for (var p = 0; p < texture.PatchCount; p++)
            {
                // mappatch_t: originx, originy, patch (r_data.h:36-41).
                var patchOfs = 16 + p * 6;
                texture.Patches[p] = new TexPatch
                {
                    OriginX = BinaryPrimitives.ReadInt16LittleEndian(mtexture.Slice(patchOfs, 2)),
                    OriginY = BinaryPrimitives.ReadInt16LittleEndian(mtexture.Slice(patchOfs + 2, 2)),
                    Patch = _patchlookup[BinaryPrimitives.ReadInt16LittleEndian(mtexture.Slice(patchOfs + 4, 2))],
                };
                if (texture.Patches[p].Patch == -1)
                {
                    // r_data.c:542-546.
                    Log.Verbose("InitTextures fatal: missing patch in texture {Name}", texture.NameString);
                    throw new DoomFatalException(
                        $"R_InitTextures: Missing patch in texture {texture.NameString}");
                }
            }

            _textures[i] = texture;

            // Column lookup tables (r_data.c:548-549).
            _texturecolumnlump[i] = new short[texture.Width];
            _texturecolumnofs[i] = new int[texture.Width];

            // Power-of-two width mask (r_data.c:551-555) — the exact
            // loop: j=1; while (j*2 <= width) j<<=1; mask = j-1.
            var j = 1;
            while (j * 2 <= texture.Width)
            {
                j <<= 1;
            }
            _texturewidthmask[i] = j - 1;
            _textureheight[i] = texture.Height << FixedPoint.FracBits; // r_data.c:556
        }

        // Precalculate whatever possible (r_data.c:566-567).
        for (var i = 0; i < _numtextures; i++)
        {
            GenerateLookup(i, wad);
        }

        // Create translation table for global animation
        // (r_data.c:569-573) — identity until DOOM-050.
        _texturetranslation = new int[_numtextures];
        for (var i = 0; i < _numtextures; i++)
        {
            _texturetranslation[i] = i;
        }

        Log.Verbose("InitTextures() = {NumTextures} textures, {Patches} patch names", _numtextures, numMappatches);
    }

    // ------------------------------------------------------------------
    // R_GenerateLookup (r_data.c:296-374)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_GenerateLookup (r_data.c:296-374): count columns covered by
    /// more than one patch; single-patch columns get lump+offset
    /// (colofs = columnofs[x-x1]+3, the +3 skips the post header);
    /// multi-patch columns get collump = -1 and a composite slot.
    /// Non-fatal on a column with no patch (printf + return,
    /// r_data.c:351-356).
    /// </summary>
    private void GenerateLookup(int texnum, WadDirectory wad)
    {
        var texture = _textures[texnum];

        // Composited texture not created yet (r_data.c:311-314).
        _texturecomposite[texnum] = null;
        _texturecompositesize[texnum] = 0;

        var collump = _texturecolumnlump[texnum];
        var colofs = _texturecolumnofs[texnum];

        // Count columns covered by more than one patch (r_data.c:322-347).
        var patchcount = new byte[texture.Width];
        foreach (var patch in texture.Patches)
        {
            var realpatch = CachePatch(wad, patch.Patch);
            var x1 = patch.OriginX;
            var x2 = x1 + PatchWidth(realpatch);

            var x = x1 < 0 ? 0 : x1;
            if (x2 > texture.Width)
            {
                x2 = texture.Width;
            }

            for (; x < x2; x++)
            {
                patchcount[x]++;
                collump[x] = (short)patch.Patch;                       // r_data.c:344
                colofs[x] = PatchColumnOffset(realpatch, x - x1) + 3;  // r_data.c:345 (+3 skips post header)
            }
        }

        for (var x = 0; x < texture.Width; x++)
        {
            if (patchcount[x] == 0)
            {
                // r_data.c:351-356 — printf + return (non-fatal).
                Console.WriteLine(
                    $"R_GenerateLookup: column without a patch ({texture.NameString})");
                return;
            }

            if (patchcount[x] > 1)
            {
                // Use the cached block (r_data.c:359-372).
                collump[x] = -1;
                colofs[x] = _texturecompositesize[texnum];

                if (_texturecompositesize[texnum] > 0x10000 - texture.Height)
                {
                    Log.Verbose("GenerateLookup fatal: texture {TexNum} is >64k", texnum);
                    throw new DoomFatalException(
                        $"R_GenerateLookup: texture {texnum} is >64k");
                }

                _texturecompositesize[texnum] += texture.Height;
            }
        }
    }

    // ------------------------------------------------------------------
    // R_DrawColumnInCache (r_data.c:184-218)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_DrawColumnInCache (r_data.c:184-218): clip and draw a column
    /// from a patch into a cached post. Walks posts until topdelta ==
    /// 0xFF; pixels at postOffset+3 (topdelta + length + 1 pad byte);
    /// advances length+4 bytes per post.
    /// </summary>
    private static void DrawColumnInCache(
        ReadOnlySpan<byte> patchCol, Span<byte> cache, int originy, int cacheheight)
    {
        var postOfs = 0;

        while (patchCol[postOfs] != 0xff) // topdelta != 0xff (r_data.c:198)
        {
            var topdelta = patchCol[postOfs];
            var length = (int)patchCol[postOfs + 1]; // unclipped post length
            var count = length;
            var source = postOfs + 3;          // topdelta + length + 1 pad (r_data.c:200)
            var position = originy + topdelta; // r_data.c:202

            if (position < 0)
            {
                count += position;             // r_data.c:204-208
                position = 0;
            }

            if (position + count > cacheheight)
            {
                count = cacheheight - position; // r_data.c:210-211
            }

            if (count > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    cache[position + i] = patchCol[source + i]; // r_data.c:214
                }
            }

            postOfs += length + 4; // r_data.c:216 — advance by the
                                   // UNCLIPPED post length + 4
        }
    }

    // ------------------------------------------------------------------
    // R_GenerateComposite (r_data.c:228-289)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_GenerateComposite (r_data.c:228-289): build the composite
    /// block for multi-patch columns, then retag PU_CACHE
    /// (r_data.c:288).
    /// </summary>
    private void GenerateComposite(WadDirectory wad, int texnum)
    {
        var texture = _textures[texnum];
        var block = new byte[_texturecompositesize[texnum]];

        var collump = _texturecolumnlump[texnum];
        var colofs = _texturecolumnofs[texnum];

        foreach (var patch in texture.Patches)
        {
            var realpatch = CachePatch(wad, patch.Patch);
            var x1 = patch.OriginX;
            var x2 = x1 + PatchWidth(realpatch);

            var x = x1 < 0 ? 0 : x1;
            if (x2 > texture.Width)
            {
                x2 = texture.Width;
            }

            for (; x < x2; x++)
            {
                // Column does not have multiple patches? (r_data.c:272-274)
                if (collump[x] >= 0)
                {
                    continue;
                }

                var patchColOfs = PatchColumnOffset(realpatch, x - x1);
                DrawColumnInCache(
                    realpatch.AsSpan(patchColOfs),
                    block.AsSpan(colofs[x]),
                    patch.OriginY,
                    texture.Height);
            }
        }

        // Now that the texture has been built in column cache,
        // it is purgable from zone memory (r_data.c:286-288).
        _texturecomposite[texnum] = block;
        Log.Verbose("GenerateComposite({TexNum}) = {Size} bytes", texnum, block.Length);
    }

    // ------------------------------------------------------------------
    // R_GetColumn (r_data.c:382-401)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_GetColumn (r_data.c:382-401): zero-allocation — returns a
    /// span pointing directly into the cached patch buffer or the
    /// composite array. Column wraps via texturewidthmask
    /// (r_data.c:390). Hot path: IsEnabled-guarded trace per ADR-004.
    /// </summary>
    public ReadOnlySpan<byte> GetColumn(WadDirectory wad, int tex, int col)
    {
        col &= _texturewidthmask[tex];                       // r_data.c:390
        var lump = _texturecolumnlump[tex][col];             // r_data.c:391
        var ofs = _texturecolumnofs[tex][col];               // r_data.c:392

        if (lump > 0)
        {
            var patch = CachePatch(wad, lump);
            if (Log.IsEnabled(Serilog.Events.LogEventLevel.Verbose))
            {
                Log.Verbose("GetColumn({Tex}, {Col}) from patch {Lump}", tex, col, lump);
            }
            return patch.AsSpan(ofs);
        }

        if (_texturecomposite[tex] == null)
        {
            GenerateComposite(wad, tex);
        }

        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Verbose))
        {
            Log.Verbose("GetColumn({Tex}, {Col}) from composite", tex, col);
        }
        return _texturecomposite[tex]!.AsSpan(_texturecolumnofs[tex][col]);
    }

    // ------------------------------------------------------------------
    // R_InitFlats (r_data.c:581-594)
    // ------------------------------------------------------------------

    /// <summary>R_InitFlats (r_data.c:581-594).</summary>
    public void InitFlats(WadDirectory wad, ZoneMemory zone)
    {
        Log.Verbose("InitFlats()");
        _firstflat = wad.GetNumForName("F_START") + 1;   // r_data.c:585
        _lastflat = wad.GetNumForName("F_END") - 1;      // r_data.c:586
        _numflats = _lastflat - _firstflat + 1;

        // Create translation table for global animation (r_data.c:589-593).
        _flattranslation = new int[_numflats];
        for (var i = 0; i < _numflats; i++)
        {
            _flattranslation[i] = i;
        }
        FlatTranslation = _flattranslation;

        Log.Verbose("InitFlats() = {NumFlats} flats [{FirstFlat}..{LastFlat}]", _numflats, _firstflat, _lastflat);
    }

    // ------------------------------------------------------------------
    // R_InitSpriteLumps (r_data.c:603-626)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_InitSpriteLumps (r_data.c:603-626): width/leftoffset/topoffset
    /// from each sprite's patch_t header, to fixed-point.
    /// </summary>
    public void InitSpriteLumps(WadDirectory wad, ZoneMemory zone)
    {
        Log.Verbose("InitSpriteLumps()");
        _firstspritelump = wad.GetNumForName("S_START") + 1;  // r_data.c:608
        _lastspritelump = wad.GetNumForName("S_END") - 1;     // r_data.c:609

        var count = _lastspritelump - _firstspritelump + 1;
        _spritewidth = new int[count];
        _spriteoffset = new int[count];
        _spritetopoffset = new int[count];

        for (var i = 0; i < count; i++)
        {
            var patch = wad.CacheLumpNum(_firstspritelump + i, PurgeTags.Cache);
            // patch_t (r_defs.h:356-364): width(0), height(2),
            // leftoffset(4), topoffset(6), columnofs[] at 8.
            _spritewidth[i] = BinaryPrimitives.ReadInt16LittleEndian(patch) << FixedPoint.FracBits;
            _spriteoffset[i] = BinaryPrimitives.ReadInt16LittleEndian(patch.AsSpan(4)) << FixedPoint.FracBits;
            _spritetopoffset[i] = BinaryPrimitives.ReadInt16LittleEndian(patch.AsSpan(6)) << FixedPoint.FracBits;
        }

        Log.Verbose("InitSpriteLumps() = {Count} sprites", count);
    }

    // ------------------------------------------------------------------
    // R_InitColormaps (r_data.c:633-644)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_InitColormaps (r_data.c:633-644): loads the COLORMAP lump
    /// (34 x 256 bytes). The 256-byte alignment is a C allocator
    /// detail — irrelevant in managed code (documented deviation).
    /// </summary>
    public void InitColormaps(WadDirectory wad, ZoneMemory zone)
    {
        Log.Verbose("InitColormaps()");
        var lump = wad.GetNumForName("COLORMAP");
        _colormaps = wad.ReadLump(lump);
        Log.Verbose("InitColormaps() = {Size} bytes", _colormaps.Length);
    }

    // ------------------------------------------------------------------
    // Name lookups (r_data.c:672-730)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_FlatNumForName (r_data.c:672-686): W_CheckNumForName -
    /// firstflat, fatal if not found. Also verifies the found lump is
    /// within [firstflat, lastflat] (user-approved addition).
    /// </summary>
    public int FlatNumForName(WadDirectory wad, string name)
    {
        var i = wad.CheckNumForName(name);
        if (i == -1)
        {
            Log.Verbose("FlatNumForName({Name}) fatal: not found", name);
            throw new DoomFatalException($"R_FlatNumForName: {name} not found");
        }
        if (i < _firstflat || i > _lastflat)
        {
            Log.Verbose("FlatNumForName({Name}) fatal: lump {Index} outside flat range", name, i);
            throw new DoomFatalException(
                $"R_FlatNumForName: {name} (lump {i}) is not a flat");
        }
        return i - _firstflat;
    }

    /// <summary>
    /// CheckFlatNumForName: non-fatal flat lookup — -1 when missing or
    /// outside [firstflat, lastflat] (user-approved addition; the C
    /// original has no such function).
    /// </summary>
    public int CheckFlatNumForName(WadDirectory wad, string name)
    {
        var i = wad.CheckNumForName(name);
        if (i < _firstflat || i > _lastflat)
        {
            return -1;
        }
        return i - _firstflat;
    }

    /// <summary>
    /// R_CheckTextureNumForName (r_data.c:696-709): "-" is the
    /// NoTexture marker (index 0); case-insensitive 8-char compare;
    /// -1 if missing.
    /// </summary>
    public int CheckTextureNumForName(string name)
    {
        // "NoTexture" marker (r_data.c:700-702).
        if (name.Length > 0 && name[0] == '-')
        {
            return 0;
        }

        for (var i = 0; i < _numtextures; i++)
        {
            if (NameMatches(_textures[i].Name, name))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// strncasecmp(textures[i]->name, name, 8) equivalent: compares at
    /// most 8 chars, stopping at the stored name's NUL; ASCII
    /// case-insensitive. A query longer than 8 chars matches its
    /// 8-char prefix (strncasecmp semantics).
    /// </summary>
    private static bool NameMatches(byte[] textureName, string name)
    {
        var end = Array.IndexOf(textureName, (byte)0);
        if (end < 0) end = textureName.Length;
        var len = Math.Min(end, name.Length);
        for (var i = 0; i < len; i++)
        {
            var a = (char)textureName[i];
            var b = name[i];
            if (ToLowerAscii(a) != ToLowerAscii(b))
            {
                return false;
            }
        }
        // Match if the stored name is fully consumed (query may be
        // longer — strncasecmp stops at 8). If the query is shorter
        // than the stored name, the remaining stored chars must be
        // NUL... strncasecmp would compare them against '\0' of the
        // query — only equal if stored is also NUL, i.e. len == end.
        return len == end;
    }


    /// <summary>
    /// R_TextureNumForName (r_data.c:718-730): Check + fatal if -1.
    /// </summary>
    public int TextureNumForName(string name)
    {
        var i = CheckTextureNumForName(name);
        if (i == -1)
        {
            Log.Verbose("TextureNumForName({Name}) fatal: not found", name);
            throw new DoomFatalException($"R_TextureNumForName: {name} not found");
        }
        return i;
    }

    // ------------------------------------------------------------------
    // DOOM-020 bridge: resolve raw names to indices
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolves the raw 8-byte texture/flat names stored by the
    /// DOOM-020 map loaders into integer indices on Sector/Side
    /// (the R_FlatNumForName / R_TextureNumForName calls of the
    /// original P_LoadSectors / P_LoadSideDefs). Raw names retained
    /// for diagnostics/animation.
    /// </summary>
    public void ResolveLevelTextures(LevelData level)
    {
        var wad = _wad ?? throw new DoomFatalException(
            "ResolveLevelTextures: TextureManager not initialized from a WAD");

        Log.Verbose("ResolveLevelTextures({Sectors} sectors, {Sides} sides)", level.Sectors.Length, level.Sides.Length);

        foreach (var sector in level.Sectors)
        {
            sector.FloorFlat = FlatNumForName(wad, NameToString(sector.FloorPic));
            sector.CeilingFlat = FlatNumForName(wad, NameToString(sector.CeilingPic));
        }

        foreach (var side in level.Sides)
        {
            side.TopTextureIndex = CheckTextureNumForName(NameToString(side.TopTexture));
            side.BottomTextureIndex = CheckTextureNumForName(NameToString(side.BottomTexture));
            side.MidTextureIndex = CheckTextureNumForName(NameToString(side.MidTexture));
        }

        Log.Verbose("ResolveLevelTextures() done");
    }

    // ------------------------------------------------------------------
    // Patch helpers (patch_t layout, r_data.h:44-60)
    // ------------------------------------------------------------------

    /// <summary>patch_t width (r_data.h:46).</summary>
    private static int PatchWidth(ReadOnlySpan<byte> patch) =>
        BinaryPrimitives.ReadInt16LittleEndian(patch);

    /// <summary>patch_t columnofs[x] (r_data.h:49).</summary>
    private static int PatchColumnOffset(ReadOnlySpan<byte> patch, int x) =>
        BinaryPrimitives.ReadInt32LittleEndian(patch.Slice(8 + x * 4));

    private byte[] CachePatch(WadDirectory wad, int lump)
    {
        return wad.CacheLumpNum(lump, PurgeTags.Cache);
    }

    private static string NameToString(byte[] name)
    {
        var end = Array.IndexOf(name, (byte)0);
        if (end < 0) end = name.Length;
        return System.Text.Encoding.ASCII.GetString(name, 0, end);
    }

    private static byte[] ReadName8(ReadOnlySpan<byte> span, int ofs)
    {
        var name = new byte[8];
        for (var i = 0; i < 8; i++)
        {
            name[i] = span[ofs + i];
        }
        return name;
    }

    private static char ToLowerAscii(char c) =>
        c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;

    public void Dispose()
    {
        // Zone blocks are freed by the owning zone (PU_STATIC).
    }
}
