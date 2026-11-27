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
//      Headless tests for the DOOM-040a platform slice: PLAYPAL +
//      gamma RGBA conversion, key translation (xlatekey), the
//      MAXEVENTS=64 event ring, and the headless frame presenter.
//
//-----------------------------------------------------------------------------

using DoomSharp.Platform;
using Xunit;

namespace DoomSharp.Tests.Unit;

/// <summary>PaletteConverter tests (I_SetPalette / UploadNewPalette port).</summary>
public class PaletteConverterTests
{
    [Fact]
    public void Convert_AppliesGammaThenExpandsToRgba()
    {
        // Palette entry 0 = (10, 20, 30); identity gamma.
        var palette = new byte[PaletteConverter.PaletteSize];
        palette[0] = 10;
        palette[1] = 20;
        palette[2] = 30;
        var gamma = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            gamma[i] = (byte)i;
        }

        var indexed = new byte[PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight];
        indexed[0] = 0;                 // palette entry 0
        indexed[1] = 200;               // palette entry 200 -> bytes 600..602
        palette[600] = 200;
        palette[601] = 100;
        palette[602] = 0;

        var rgba = new byte[PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight * 4];
        PaletteConverter.Convert(indexed, palette, gamma, rgba);

        Assert.Equal((byte)10, rgba[0]);
        Assert.Equal((byte)20, rgba[1]);
        Assert.Equal((byte)30, rgba[2]);
        Assert.Equal((byte)0xff, rgba[3]);
        Assert.Equal((byte)200, rgba[4]);
        Assert.Equal((byte)100, rgba[5]);
        Assert.Equal((byte)0, rgba[6]);
        Assert.Equal((byte)0xff, rgba[7]);
    }

    [Fact]
    public void Convert_GammaDarkensChannels()
    {
        // Gamma row that halves every value (like the C gammatable rows).
        var palette = new byte[PaletteConverter.PaletteSize];
        palette[0] = 200;
        palette[1] = 100;
        palette[2] = 50;
        var gamma = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            gamma[i] = (byte)(i / 2);
        }

        var indexed = new byte[PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight];
        var rgba = new byte[PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight * 4];
        PaletteConverter.Convert(indexed, palette, gamma, rgba);

        Assert.Equal(100, rgba[0]);     // gamma[200] = 100
        Assert.Equal(50, rgba[1]);      // gamma[100] = 50
        Assert.Equal(25, rgba[2]);      // gamma[50] = 25
    }

    [Fact]
    public void GetPalette_SelectsSlice()
    {
        var playpal = new byte[PaletteConverter.PaletteCount * PaletteConverter.PaletteSize];
        // Palette 2 starts at byte 2*768; mark its first byte.
        playpal[2 * PaletteConverter.PaletteSize] = 0xAB;
        playpal[(2 * PaletteConverter.PaletteSize) + 767] = 0xCD;

        var slice = PaletteConverter.GetPalette(playpal, 2);
        Assert.Equal(PaletteConverter.PaletteSize, slice.Length);
        Assert.Equal(0xAB, slice[0]);
        Assert.Equal(0xCD, slice[^1]);
    }

    [Fact]
    public void GetPalette_RejectsOutOfRangeIndex()
    {
        var playpal = new byte[PaletteConverter.PaletteCount * PaletteConverter.PaletteSize];
        Assert.Throws<ArgumentOutOfRangeException>(() => PaletteConverter.GetPalette(playpal, 14));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaletteConverter.GetPalette(playpal, -1));
    }

    [Fact]
    public void Convert_RejectsUndersizedBuffers()
    {
        var tiny = new byte[10];
        var palette = new byte[PaletteConverter.PaletteSize];
        var gamma = new byte[256];
        var rgba = new byte[PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight * 4];

        Assert.Throws<ArgumentException>(() => PaletteConverter.Convert(tiny, palette, gamma, rgba));
        Assert.Throws<ArgumentException>(() => PaletteConverter.Convert(new byte[64000], tiny, gamma, rgba));
        Assert.Throws<ArgumentException>(() => PaletteConverter.Convert(new byte[64000], palette, tiny, rgba));
        Assert.Throws<ArgumentException>(() => PaletteConverter.Convert(new byte[64000], palette, gamma, tiny));
    }
}

/// <summary>KeyTranslator tests (xlatekey, i_video.c:97-162).</summary>
public class KeyTranslatorTests
{
    [Theory]
    [InlineData(PlatformKey.Left, DoomKeys.LeftArrow)]
    [InlineData(PlatformKey.Right, DoomKeys.RightArrow)]
    [InlineData(PlatformKey.Up, DoomKeys.UpArrow)]
    [InlineData(PlatformKey.Down, DoomKeys.DownArrow)]
    [InlineData(PlatformKey.Escape, DoomKeys.Escape)]
    [InlineData(PlatformKey.Return, DoomKeys.Enter)]
    [InlineData(PlatformKey.Tab, DoomKeys.Tab)]
    [InlineData(PlatformKey.F1, DoomKeys.F1)]
    [InlineData(PlatformKey.F12, DoomKeys.F12)]
    [InlineData(PlatformKey.Backspace, DoomKeys.Backspace)]
    [InlineData(PlatformKey.Delete, DoomKeys.Backspace)]   // XK_Delete -> KEY_BACKSPACE
    [InlineData(PlatformKey.Pause, DoomKeys.Pause)]
    [InlineData(PlatformKey.KpEqual, DoomKeys.Equals_)]
    [InlineData(PlatformKey.Equal, DoomKeys.Equals_)]
    [InlineData(PlatformKey.KpSubtract, DoomKeys.Minus)]
    [InlineData(PlatformKey.Minus, DoomKeys.Minus)]
    [InlineData(PlatformKey.ShiftL, DoomKeys.RShift)]
    [InlineData(PlatformKey.ShiftR, DoomKeys.RShift)]
    [InlineData(PlatformKey.ControlL, DoomKeys.RCtrl)]
    [InlineData(PlatformKey.ControlR, DoomKeys.RCtrl)]
    [InlineData(PlatformKey.AltL, DoomKeys.RAlt)]
    [InlineData(PlatformKey.AltR, DoomKeys.RAlt)]
    [InlineData(PlatformKey.MetaL, DoomKeys.RAlt)]
    [InlineData(PlatformKey.MetaR, DoomKeys.RAlt)]
    public void XlateKey_NamedKeys(PlatformKey key, int expected)
    {
        Assert.Equal(expected, KeyTranslator.XlateKey(key));
    }

    [Fact]
    public void XlateKey_PrintableRange_MapsToAscii()
    {
        // space..tilde map to themselves (i_video.c:153-154).
        Assert.Equal(' ', KeyTranslator.XlateKey(PlatformKey.Space));
        Assert.Equal('~', KeyTranslator.XlateKey((PlatformKey)'~'));
        Assert.Equal('5', KeyTranslator.XlateKey((PlatformKey)'5'));
    }

    [Fact]
    public void XlateKey_UppercaseFoldsToLowercase()
    {
        // i_video.c:155-156.
        Assert.Equal('a', KeyTranslator.XlateKey((PlatformKey)'A'));
        Assert.Equal('z', KeyTranslator.XlateKey((PlatformKey)'Z'));
    }

    [Fact]
    public void XlateKey_UnknownKeyPassesThrough()
    {
        // Values outside the printable range pass through unchanged
        // (the C default case leaves rc as the keysym).
        Assert.Equal(0x0500, KeyTranslator.XlateKey((PlatformKey)0x0500));
    }

    [Fact]
    public void DoomKeys_MatchOriginalConstants()
    {
        // doomdef.h:250-280 exact values.
        Assert.Equal(0xae, DoomKeys.RightArrow);
        Assert.Equal(0xac, DoomKeys.LeftArrow);
        Assert.Equal(0xad, DoomKeys.UpArrow);
        Assert.Equal(0xaf, DoomKeys.DownArrow);
        Assert.Equal(27, DoomKeys.Escape);
        Assert.Equal(13, DoomKeys.Enter);
        Assert.Equal(9, DoomKeys.Tab);
        Assert.Equal(0x80 + 0x3b, DoomKeys.F1);
        Assert.Equal(0x80 + 0x58, DoomKeys.F12);
        Assert.Equal(127, DoomKeys.Backspace);
        Assert.Equal(0xff, DoomKeys.Pause);
        Assert.Equal(0x3d, DoomKeys.Equals_);
        Assert.Equal(0x2d, DoomKeys.Minus);
        Assert.Equal(0x80 + 0x36, DoomKeys.RShift);
        Assert.Equal(0x80 + 0x1d, DoomKeys.RCtrl);
        Assert.Equal(0x80 + 0x38, DoomKeys.RAlt);
        Assert.Equal(DoomKeys.RAlt, DoomKeys.LAlt);
    }
}

/// <summary>EventQueue tests (d_main.c:141-176 ring semantics).</summary>
public class EventQueueTests
{
    [Fact]
    public void PostThenPop_IsFifo()
    {
        var queue = new EventQueue();
        queue.PostEvent(new DoomEvent(EventType.KeyDown, DoomKeys.Escape, 0, 0));
        queue.PostEvent(new DoomEvent(EventType.KeyUp, DoomKeys.Enter, 0, 0));

        Assert.Equal(2, queue.Count);
        Assert.True(queue.TryPop(out var first));
        Assert.Equal(EventType.KeyDown, first.Type);
        Assert.Equal(DoomKeys.Escape, first.Data1);
        Assert.True(queue.TryPop(out var second));
        Assert.Equal(EventType.KeyUp, second.Type);
        Assert.Equal(DoomKeys.Enter, second.Data1);
        Assert.False(queue.TryPop(out _));
    }

    [Fact]
    public void Ring_OverwritesUnreadEvents_WhenHeadLapsTail()
    {
        var queue = new EventQueue();
        // Post MAXEVENTS+1 events without draining: the C ring has no
        // overflow check (d_main.c:150-154) — head laps tail and the
        // oldest unread event is lost. After 65 posts, head=1, tail=0:
        // exactly one event remains, and it is the newest (i=64).
        for (var i = 0; i < EventQueue.MaxEvents + 1; i++)
        {
            queue.PostEvent(new DoomEvent(EventType.KeyDown, i, 0, 0));
        }

        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryPop(out var only));
        Assert.Equal(EventQueue.MaxEvents, only.Data1);
    }

    [Fact]
    public void Ring_FullLap_ReadsAsEmpty()
    {
        var queue = new EventQueue();
        // Posting exactly MAXEVENTS without draining leaves head==tail,
        // which the C cannot distinguish from empty (d_main.c:170).
        for (var i = 0; i < EventQueue.MaxEvents; i++)
        {
            queue.PostEvent(new DoomEvent(EventType.KeyDown, i, 0, 0));
        }

        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryPop(out _));
    }

    [Fact]
    public void Clear_EmptiesQueue()
    {
        var queue = new EventQueue();
        queue.PostEvent(new DoomEvent(EventType.Mouse, 1, 2, 3));
        queue.Clear();
        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryPop(out _));
    }

    [Fact]
    public void MaxEvents_Is64()
    {
        // d_event.h:108.
        Assert.Equal(64, EventQueue.MaxEvents);
    }
}

/// <summary>HeadlessFramePresenter tests.</summary>
public class HeadlessFramePresenterTests
{
    [Fact]
    public void Present_RecordsFrameCopy()
    {
        using var presenter = new HeadlessFramePresenter();
        var rgba = new byte[PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight * 4];
        rgba[0] = 0x12;
        rgba[1] = 0x34;
        rgba[^1] = 0xff;

        presenter.Present(rgba);
        rgba[0] = 0xff;                 // mutate the source after presenting

        var frame = Assert.Single(presenter.Frames);
        Assert.Equal(0x12, frame[0]);   // recorded copy is unaffected
        Assert.Equal(0xff, frame[^1]);
        Assert.Equal(PaletteConverter.ScreenWidth, presenter.PresentWidth);
        Assert.Equal(PaletteConverter.ScreenHeight, presenter.PresentHeight);
    }

    [Fact]
    public void Present_RejectsUndersizedBuffer()
    {
        using var presenter = new HeadlessFramePresenter();
        Assert.Throws<ArgumentException>(() => presenter.Present(new byte[10]));
    }
}
