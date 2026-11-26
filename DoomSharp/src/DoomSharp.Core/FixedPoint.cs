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
//      Fixed point arithmetic, 32bit as 16.16.
//      Migration of Legacy/linuxdoom-1.10/m_fixed.h and m_fixed.c.
//
//-----------------------------------------------------------------------------

using Serilog;
using Serilog.Events;

namespace DoomSharp.Core;

/// <summary>
/// 16.16 fixed-point arithmetic.
/// Direct migration of <c>m_fixed.h</c> / <c>m_fixed.c</c> (FRACBITS 16,
/// FRACUNIT 1&lt;&lt;16, fixed_t = int).
/// All arithmetic preserves the original C semantics, including
/// wraparound on overflow and truncation toward zero.
/// </summary>
public static class FixedPoint
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(FixedPoint));

    /// <summary>FRACBITS — number of fractional bits (m_fixed.h:35).</summary>
    public const int FracBits = 16;

    /// <summary>FRACUNIT — 1.0 in fixed point (m_fixed.h:36).</summary>
    public const int FracUnit = 1 << FracBits;

    /// <summary>MININT (doomtype.h:54).</summary>
    public const int MinInt = int.MinValue;

    /// <summary>MAXINT (doomtype.h:48).</summary>
    public const int MaxInt = int.MaxValue;

    /// <summary>
    /// FixedMul — ((long long)a * b) >> FRACBITS  (m_fixed.c:44-49).
    /// Arithmetic shift of the signed 64-bit product; the shift of a
    /// negative value rounds toward negative infinity, matching C.
    /// </summary>
    public static int FixedMul(int a, int b)
    {
        unchecked
        {
            var result = (int)(((long)a * (long)b) >> FracBits);
            // Hot path: guarded per ADR-004 (zero cost when disabled).
            if (Log.IsEnabled(LogEventLevel.Verbose))
            {
                Log.Verbose("FixedMul({A}, {B}) = {Result}", a, b, result);
            }
            return result;
        }
    }

    /// <summary>
    /// FixedDiv — overflow-guarded division (m_fixed.c:58-65).
    /// If (abs(a) >> 14) >= abs(b), returns MININT or MAXINT by sign;
    /// otherwise delegates to FixedDiv2.
    /// Note: in C, abs(int.MinValue) is int.MinValue (negative), so the
    /// guard comparison is signed and can pass through to FixedDiv2 —
    /// reproduced here by using the same signed arithmetic.
    /// </summary>
    public static int FixedDiv(int a, int b)
    {
        unchecked
        {
            int absA = a < 0 ? -a : a;          // C abs(): -MININT == MININT
            int absB = b < 0 ? -b : b;
            if ((absA >> 14) >= absB)
            {
                var sentinel = (a ^ b) < 0 ? MinInt : MaxInt;
                if (Log.IsEnabled(LogEventLevel.Verbose))
                {
                    Log.Verbose("FixedDiv({A}, {B}) overflow sentinel = {Result}", a, b, sentinel);
                }
                return sentinel;
            }
            return FixedDiv2(a, b);
        }
    }

    /// <summary>
    /// FixedDiv2 — double-precision division, truncated to int
    /// (m_fixed.c:70-87). The original computes
    /// c = ((double)a / (double)b) * FRACUNIT and casts to int
    /// (truncation toward zero). If the result is outside
    /// [-2^31, 2^31), the original calls I_Error (process abort);
    /// here that condition throws <see cref="DoomFatalException"/>.
    /// </summary>
    public static int FixedDiv2(int a, int b)
    {
        double c = ((double)a / (double)b) * FracUnit;

        if (c >= 2147483648.0 || c < -2147483648.0)
        {
            Log.Verbose("FixedDiv2({A}, {B}) fatal: result {C} out of range", a, b, c);
            throw new DoomFatalException("FixedDiv: divide by zero");
        }
        var result = (int)c;
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("FixedDiv2({A}, {B}) = {Result}", a, b, result);
        }
        return result;
    }
}
