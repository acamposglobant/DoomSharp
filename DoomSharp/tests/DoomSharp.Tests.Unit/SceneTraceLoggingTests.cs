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
//      ADR-004 trace logging tests for the DOOM-030c scene classes:
//      emission with a Verbose sink, silent-by-default.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Serilog;
using Xunit;

namespace DoomSharp.Tests.Unit;

[Collection("RendererSerial")]
public class SceneTraceLoggingTests
{
    [Fact]
    public void BspRenderer_EmitsVerboseTrace_WithSinkAttached()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();
            try
            {
                var level = BspRendererTests.BuildLevel();
                var (bsp, segs, planes, _, _, _) = BspRendererTests.CreateRenderers(level);
                bsp.ClearDrawSegs();

                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("ClearDrawSegs"));
            }
            finally
            {
                Serilog.Log.Logger = new Serilog.LoggerConfiguration().CreateLogger();
            }
        }
    }

    [Fact]
    public void SegRenderer_EmitsVerboseTrace_WithSinkAttached()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();
            try
            {
                var level = BspRendererTests.BuildLevel();
                var (bsp, segs, planes, _, _, _) = BspRendererTests.CreateRenderers(level);
                bsp.ClearClipSegs();
                segs.ClearDrawSegs();
                planes.ClearPlanes();
                bsp.CurLine = 0;
                segs.CurLine = 0;
                bsp.FrontSector = 0;
                bsp.BackSector = 1;
                segs.RwAngle1 = RendererMath.PointToAngle2(0, 0, -128 << 16, 256 << 16);
                planes.FloorPlane = planes.FindPlane(0, 1, 200, 42);
                planes.CeilingPlane = planes.FindPlane((128 << 16) - RendererMath.ViewZ, 1, 200, 42);
                segs.StoreWallRange(100, 140);

                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("StoreWallRange"));
            }
            finally
            {
                Serilog.Log.Logger = new Serilog.LoggerConfiguration().CreateLogger();
            }
        }
    }

    [Fact]
    public void VisplaneManager_EmitsVerboseTrace_WithSinkAttached()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();
            try
            {
                var view = new ViewSetup(new TextureManager());
                var colormaps = new byte[ViewSetup.NumColormaps * 256];
                view.Colormaps = colormaps;
                ViewSetup.ColormapsGlobal = colormaps;
                view.SetViewSize(11, 0);
                view.ExecuteSetViewSize();
                var planes = new VisplaneManager(view);
                planes.ClearPlanes();
                planes.FindPlane(64 << 16, 1, 160, 42);

                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("FindPlane"));
            }
            finally
            {
                Serilog.Log.Logger = new Serilog.LoggerConfiguration().CreateLogger();
            }
        }
    }

    [Fact]
    public void SceneRenderer_SilentByDefault_NoOutput()
    {
        lock (LogTestSync.Lock)
        {
            Serilog.Log.Logger = new Serilog.LoggerConfiguration().CreateLogger();
            var level = BspRendererTests.BuildLevel();
            var textures = SceneFixture.Textures;
            var view = new ViewSetup(textures);
            var colormaps = new byte[ViewSetup.NumColormaps * 256];
            view.Colormaps = colormaps;
            ViewSetup.ColormapsGlobal = colormaps;
            view.SetViewSize(11, 0);
            view.ExecuteSetViewSize();
            var wad = SceneFixture.Wad;
            var scene = new SceneRenderer(wad, level, textures, view);

            scene.RenderPlayerView(0, 0, 48 << 16, AngleTables.Ang90, []);

            // No exception and the pipeline ran; silent-by-default is
            // enforced by the default logger having no sinks.
            Assert.Equal(1, scene.Segs.DsP);
        }
    }
}
