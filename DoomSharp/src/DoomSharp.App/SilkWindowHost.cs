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
//      Silk.NET window adapter: IFramePresenter + IInputSource over a
//      native OS window. Presents the 320x200 RGBA frame integer-scaled
//      with aspect-corrected 4:3 letterbox (GL texture blit) and
//      translates keyboard events into DoomEvents (I_GetEvent,
//      i_video.c:194-260).
//
//-----------------------------------------------------------------------------

using DoomSharp.Platform;
using Serilog;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace DoomSharp.App;

/// <summary>
/// Silk.NET window host: presents frames and feeds the EventQueue.
/// Keyboard events are translated with <see cref="KeyTranslator"/>
/// (xlatekey port). Presentation uploads the RGBA frame as a GL texture
/// and draws it letterboxed with a fullscreen quad (nearest filtering
/// keeps the pixels crisp at integer scales).
/// </summary>
public sealed class SilkWindowHost : IFramePresenter, IInputSource
{
    private IWindow? _window;
    private IInputContext? _input;
    private GL? _gl;
    private uint _texture;
    private uint _program;
    private uint _vao;
    private uint _vbo;
    private int _uScale;
    private readonly int _scale;

    // Key events buffered by the Silk callbacks, drained by Poll
    // (the C polled X events synchronously in I_GetEvent; the Silk
    // callback model requires buffering between frames).
    private readonly List<(int Key, bool Down)> _pendingKeys = [];

    /// <summary>Creates the host. Call <see cref="Initialize"/> before use.</summary>
    public SilkWindowHost(int scale = 2)
    {
        _scale = Math.Clamp(scale, 1, 4);
    }

    /// <inheritdoc />
    public int PresentWidth => PaletteConverter.ScreenWidth * _scale;

    /// <inheritdoc />
    public int PresentHeight => PaletteConverter.ScreenHeight * _scale;

    /// <summary>Creates the window (I_InitGraphics analogue).</summary>
    public void Initialize()
    {
        var opts = WindowOptions.Default with
        {
            Size = new Silk.NET.Maths.Vector2D<int>(PresentWidth, PresentHeight),
            Title = "DoomSharp",
            VSync = false,
            WindowBorder = WindowBorder.Fixed,
        };

        _window = Window.Create(opts);
        _window.Load += OnWindowLoad;
        _window.Initialize();
        Log.Verbose("SilkWindowHost initialized ({Width}x{Height})", PresentWidth, PresentHeight);
    }

    private void OnWindowLoad()
    {
        _input = _window!.CreateInput();
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => _pendingKeys.Add((KeyTranslator.XlateKey(Translate(key)), true));
            keyboard.KeyUp += (_, key, _) => _pendingKeys.Add((KeyTranslator.XlateKey(Translate(key)), false));
        }

        _gl = GL.GetApi(_window);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Viewport(0, 0, (uint)PresentWidth, (uint)PresentHeight);

        _texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _texture);
        var nearest = (int)GLEnum.Nearest;
        var clampToEdge = (int)GLEnum.ClampToEdge;
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in nearest);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in nearest);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, in clampToEdge);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, in clampToEdge);

        _program = BuildProgram();
        _uScale = _gl.GetUniformLocation(_program, "uScale");
    }

    private uint BuildProgram()
    {
        const string vertexSrc = """
            #version 330 core
            layout(location = 0) in vec2 aPos;
            layout(location = 1) in vec2 aUv;
            out vec2 vUv;
            uniform vec2 uScale;
            void main()
            {
                vUv = aUv;
                gl_Position = vec4(aPos.xy * uScale, 0.0, 1.0);
            }
            """;
        const string fragmentSrc = """
            #version 330 core
            in vec2 vUv;
            out vec4 fragColor;
            uniform sampler2D uTex;
            void main()
            {
                fragColor = texture(uTex, vUv);
            }
            """;

        var vs = _gl!.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vs, vertexSrc);
        _gl.CompileShader(vs);
        var fs = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fs, fragmentSrc);
        _gl.CompileShader(fs);
        var prog = _gl.CreateProgram();
        _gl.AttachShader(prog, vs);
        _gl.AttachShader(prog, fs);
        _gl.LinkProgram(prog);
        _gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out var ok);
        if (ok == 0)
        {
            throw new InvalidOperationException("GL program link failed: " + _gl.GetProgramInfoLog(prog));
        }

        // Fullscreen quad: positions + uvs, drawn scaled by uScale.
        float[] quad =
        [
            // pos        uv
            -1f, -1f,      0f, 1f,
             1f, -1f,      1f, 1f,
             1f,  1f,      1f, 0f,
            -1f, -1f,      0f, 1f,
             1f,  1f,      1f, 0f,
            -1f,  1f,      0f, 0f,
        ];

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);
        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* p = quad)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            }
        }

        unsafe
        {
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        }

        return prog;
    }

    /// <inheritdoc />
    public void Present(ReadOnlySpan<byte> rgba)
    {
        if (_gl is null)
        {
            throw new InvalidOperationException("SilkWindowHost not initialized");
        }

        _gl.Clear(ClearBufferMask.ColorBufferBit);

        // Aspect-corrected 4:3 letterbox: the 320x200 frame is 8:5, so
        // with a 4:3 window the quad occupies (8/5)/(4/3) = 1.2 of the
        // width — clamped to 1.0 (the window is 8:5 at integer scale,
        // so the quad fills it; the letterbox math matters only if the
        // user resizes).
        var quadWidth = Math.Min(1f, ((float)PaletteConverter.ScreenWidth / PaletteConverter.ScreenHeight) / (4.0f / 3.0f));

        _gl.UseProgram(_program);
        _gl.Uniform2(_uScale, quadWidth, 1f);

        _gl.BindTexture(TextureTarget.Texture2D, _texture);
        unsafe
        {
            fixed (byte* p = rgba)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)GLEnum.Rgba,
                    (uint)PaletteConverter.ScreenWidth, (uint)PaletteConverter.ScreenHeight,
                    0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
            }
        }

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _window!.SwapBuffers();
    }

    /// <inheritdoc />
    public void PumpEvents()
    {
        _window?.DoEvents();
    }

    /// <inheritdoc />
    public void Poll(EventQueue queue)
    {
        foreach (var (key, down) in _pendingKeys)
        {
            queue.PostEvent(new DoomEvent(down ? EventType.KeyDown : EventType.KeyUp, key, 0, 0));
        }

        _pendingKeys.Clear();
    }

    /// <summary>True once the window has been closed by the user.</summary>
    public bool ShouldClose => _window?.IsClosing ?? false;

    private static PlatformKey Translate(Key key) => key switch
    {
        Key.Left => PlatformKey.Left,
        Key.Right => PlatformKey.Right,
        Key.Down => PlatformKey.Down,
        Key.Up => PlatformKey.Up,
        Key.Escape => PlatformKey.Escape,
        Key.Enter => PlatformKey.Return,
        Key.Tab => PlatformKey.Tab,
        Key.F1 => PlatformKey.F1,
        Key.F2 => PlatformKey.F2,
        Key.F3 => PlatformKey.F3,
        Key.F4 => PlatformKey.F4,
        Key.F5 => PlatformKey.F5,
        Key.F6 => PlatformKey.F6,
        Key.F7 => PlatformKey.F7,
        Key.F8 => PlatformKey.F8,
        Key.F9 => PlatformKey.F9,
        Key.F10 => PlatformKey.F10,
        Key.F11 => PlatformKey.F11,
        Key.F12 => PlatformKey.F12,
        Key.Backspace => PlatformKey.Backspace,
        Key.Delete => PlatformKey.Delete,
        Key.Pause => PlatformKey.Pause,
        Key.Equal => PlatformKey.Equal,
        Key.Minus => PlatformKey.Minus,
        Key.ShiftLeft => PlatformKey.ShiftL,
        Key.ShiftRight => PlatformKey.ShiftR,
        Key.ControlLeft => PlatformKey.ControlL,
        Key.ControlRight => PlatformKey.ControlR,
        Key.AltLeft => PlatformKey.AltL,
        Key.AltRight => PlatformKey.AltR,
        _ => TranslatePrintable(key),
    };

    private static PlatformKey TranslatePrintable(Key key)
    {
        // Silk.NET Key values for A-Z and 0-9 are contiguous; map them
        // to their ASCII codes so the printable-range fold in
        // KeyTranslator applies.
        var k = (int)key;
        if (k >= (int)Key.A && k <= (int)Key.Z)
        {
            return (PlatformKey)('A' + (k - (int)Key.A));
        }

        if (k >= (int)Key.Number0 && k <= (int)Key.Number9)
        {
            return (PlatformKey)('0' + (k - (int)Key.Number0));
        }

        return key switch
        {
            Key.Space => PlatformKey.Space,
            Key.Comma => (PlatformKey)',',
            Key.Period => (PlatformKey)'.',
            Key.Slash => (PlatformKey)'/',
            Key.Semicolon => (PlatformKey)';',
            Key.Apostrophe => (PlatformKey)'\'',
            _ => PlatformKey.None,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _input?.Dispose();
        _gl?.Dispose();
        _window?.Dispose();
    }
}
