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
//      Shutdown lifecycle: the I_Error and I_Quit sequences
//      (i_system.c:116-183) as a host-boundary service.
//      Library code never terminates the process (ADR-002).
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using Serilog;

namespace DoomSharp.Platform;

/// <summary>
/// Shutdown callback registry with LIFO teardown (atexit-style),
/// backing the I_Error and I_Quit sequences. Instantiable so parallel
/// tests can own isolated registries; <see cref="DoomShutdown"/> uses
/// one instance internally.
/// </summary>
public sealed class ShutdownRegistry
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(ShutdownRegistry));

    private readonly List<Action> _callbacks = [];

    /// <summary>Number of registered callbacks (diagnostics/tests).</summary>
    public int Count => _callbacks.Count;

    /// <summary>
    /// Registers a shutdown callback. Returns a token whose Dispose
    /// removes it — used by tests to avoid leaking callbacks.
    /// </summary>
    public IDisposable Register(Action callback)
    {
        Log.Verbose("Register({Callback})", callback.Method.Name);
        _callbacks.Add(callback);
        return new Registration(this, callback);
    }

    /// <summary>Removes all callbacks (test isolation helper).</summary>
    public void Clear()
    {
        Log.Verbose("Clear({Count} callbacks)", _callbacks.Count);
        _callbacks.Clear();
    }

    /// <summary>
    /// Runs all callbacks in LIFO order (reverse registration — the
    /// atexit-style teardown of I_Quit/I_Error). Exceptions in one
    /// callback do not prevent the remaining callbacks.
    /// </summary>
    public void RunAll()
    {
        Log.Verbose("RunAll({Count} callbacks)", _callbacks.Count);
        for (var i = _callbacks.Count - 1; i >= 0; i--)
        {
            try
            {
                _callbacks[i]();
            }
            catch
            {
                // Shutdown callbacks must not prevent the remaining
                // teardown steps (the original calls each shutdown
                // function unconditionally).
            }
        }
        _callbacks.Clear();
    }

    private sealed class Registration(ShutdownRegistry owner, Action callback) : IDisposable
    {
        public void Dispose()
        {
            owner._callbacks.Remove(callback);
        }
    }
}

/// <summary>
/// The I_Error / I_Quit lifecycle (i_system.c:116-183).
/// Fatal formats to stderr, runs shutdown callbacks, and throws
/// DoomFatalException — the HOST catches it and terminates with a
/// non-success exit code (ADR-002). Quit runs the callbacks and
/// returns; the host then exits with code 0.
/// </summary>
public static class DoomShutdown
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(DoomShutdown));
    private static readonly ShutdownRegistry Registry = new();

    /// <summary>The registry used by Fatal/Quit (for host wiring/tests).</summary>
    public static ShutdownRegistry Callbacks => Registry;

    /// <summary>
    /// Registers a shutdown callback (sound, music, config save,
    /// graphics, net — in init order; torn down LIFO).
    /// Returns a token whose Dispose removes the callback.
    /// </summary>
    public static IDisposable Register(Action callback)
    {
        return Registry.Register(callback);
    }

    /// <summary>
    /// I_Error (i_system.c:162-183): "Error: <message>\n" to stderr,
    /// flush, shutdown callbacks, then DoomFatalException. The original
    /// exit(-1) is the host's responsibility (ADR-002).
    /// </summary>
    public static void Fatal(string message)
    {
        Log.Verbose("Fatal({Message})", message);
        // i_system.c:168-173 — message first, then flush.
        Console.Error.Write("Error: ");
        Console.Error.WriteLine(message);
        Console.Error.Flush();

        // i_system.c:175-180 — shutdown (demo flush, net quit,
        // graphics shutdown register here during their migrations).
        Registry.RunAll();

        Log.Verbose("Fatal throwing DoomFatalException");
        throw new DoomFatalException(message);
    }

    /// <summary>
    /// I_Quit (i_system.c:116-124): orderly shutdown callbacks, then
    /// return — the host performs exit(0).
    /// </summary>
    public static void Quit()
    {
        Log.Verbose("Quit()");
        Registry.RunAll();
    }
}
