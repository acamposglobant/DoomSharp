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
//      Characterization tests for FixedPoint, generated from the
//      UNMODIFIED original C sources (m_fixed.c) via the harness in
//      DoomSharp/tools/charact/charact.c (gcc, -DNORMALUNIX -DLINUX).
//      Vectors: CharacterizationVectors.json (subset; full dump in
//      DoomSharp/tools/charact/vectors.json).
//
//-----------------------------------------------------------------------------

using System.Text.Json;
using DoomSharp.Core;
using Xunit;

namespace DoomSharp.Tests.Unit;

public class FixedPointCharacterizationTests
{
    private sealed record VectorSet(string GeneratedBy, VectorOp[] Ops, VectorOp[] FatalCases);

    private sealed record VectorOp(string Op, int A, int B, int R = 0, bool Fatal = false);

    private static VectorSet LoadVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CharacterizationVectors.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<VectorSet>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize characterization vectors");
    }

    public static IEnumerable<object[]> MulVectors()
    {
        var set = LoadVectors();
        foreach (var v in set.Ops.Where(v => v.Op == "mul"))
            yield return new object[] { v.A, v.B, v.R };
    }

    public static IEnumerable<object[]> DivVectors()
    {
        var set = LoadVectors();
        foreach (var v in set.Ops.Where(v => v.Op == "div" || v.Op == "div2"))
            yield return new object[] { v.Op, v.A, v.B, v.R };
    }

    public static IEnumerable<object[]> FatalVectors()
    {
        var set = LoadVectors();
        foreach (var v in set.FatalCases)
            yield return new object[] { v.Op, v.A, v.B };
    }

    [Theory]
    [MemberData(nameof(MulVectors))]
    public void FixedMul_MatchesOriginalC(int a, int b, int expected)
    {
        Assert.Equal(expected, FixedPoint.FixedMul(a, b));
    }

    [Theory]
    [MemberData(nameof(DivVectors))]
    public void FixedDiv_MatchesOriginalC(string op, int a, int b, int expected)
    {
        // Both "div" (FixedDiv) and "div2" (FixedDiv2) vectors must match.
        int actual = op == "div"
            ? FixedPoint.FixedDiv(a, b)
            : FixedPoint.FixedDiv2(a, b);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(FatalVectors))]
    public void FatalCases_ThrowDoomFatalException(string op, int a, int b)
    {
        // The original C calls I_Error (process abort) for these inputs.
        // The C# port throws DoomFatalException (ADR-002).
        var ex = Assert.Throws<DoomFatalException>(() =>
        {
            if (op == "div")
                FixedPoint.FixedDiv(a, b);
            else
                FixedPoint.FixedDiv2(a, b);
        });
        Assert.Equal("FixedDiv: divide by zero", ex.Message);
    }

    [Fact]
    public void FixedDiv_OverflowSentinels_MatchOriginalC()
    {
        // m_fixed.c:62-64 — guard path returns MININT/MAXINT by sign.
        Assert.Equal(int.MaxValue, FixedPoint.FixedDiv(1 << 16, 1));       // positive
        Assert.Equal(int.MinValue, FixedPoint.FixedDiv(-(1 << 16), 1));    // negative
        Assert.Equal(int.MinValue, FixedPoint.FixedDiv(1 << 16, -1));      // mixed sign
        Assert.Equal(int.MaxValue, FixedPoint.FixedDiv(-(1 << 16), -1));   // both negative
        Assert.Equal(int.MaxValue, FixedPoint.FixedDiv(0x7fffffff, 0));    // div by zero -> MAXINT
        Assert.Equal(int.MinValue, FixedPoint.FixedDiv(-1, 0));            // div by zero, negative -> MININT
        // Just below the guard threshold: (abs(a)>>14) < abs(b) -> FixedDiv2 path.
        // a = 3<<14 = 49152: abs(a)>>14 = 3, abs(b) = 65536 -> 3 < 65536 -> FixedDiv2.
        // 49152/65536 = 0.75 -> *65536 = 49152 exactly.
        Assert.Equal(49152, FixedPoint.FixedDiv(3 << 14, 1 << 16));
        // a = 1<<14: abs(a)>>14 = 0 < 65536 -> FixedDiv2: 0.25 -> 16384.
        Assert.Equal(16384, FixedPoint.FixedDiv(1 << 14, 1 << 16));
        // Exact guard boundary: abs(a)>>14 == abs(b) -> sentinel.
        // a = 3<<14 -> abs(a)>>14 = 3; b = 3 -> 3 >= 3 -> MAXINT (positive).
        Assert.Equal(int.MaxValue, FixedPoint.FixedDiv(3 << 14, 3));
        // a = -(3<<14), b = 3 -> signs differ -> MININT.
        Assert.Equal(int.MinValue, FixedPoint.FixedDiv(-(3 << 14), 3));
    }

    [Fact]
    public void FixedDiv_MinIntMinusOne_IsFatalLikeOriginalC()
    {
        // abs(MININT) is MININT in C (negative), so the guard
        // (abs(a)>>14) >= abs(b) is signed and passes through to
        // FixedDiv2, where the double result 2^31 overflows -> I_Error.
        // Verified against the C harness (fatal vector).
        Assert.Throws<DoomFatalException>(() => FixedPoint.FixedDiv(int.MinValue, -1));
    }

    [Fact]
    public void FixedMul_NegativeShift_RoundsTowardNegativeInfinity()
    {
        // C: ((long long)a * b) >> 16 is an arithmetic shift.
        // -1 * 65536 = -65536; >> 16 = -1 (not 0, truncation toward -inf).
        Assert.Equal(-1, FixedPoint.FixedMul(-1, 1 << 16));
        Assert.Equal(-1, FixedPoint.FixedMul(1 << 16, -1));
        // -0.5 in fixed: -32768 * 65536 = -2147483648; >> 16 = -32768.
        // (Arithmetic shift: -2147483648 / 65536 = -32768 exactly.)
        Assert.Equal(-32768, FixedPoint.FixedMul(-32768, 1 << 16));
        // -1.5 in fixed: -98304 * 65536 >> 16 = -98304 (exact, no rounding).
        Assert.Equal(-98304, FixedPoint.FixedMul(-98304, 1 << 16));
        // Fractional: -3 * 32768 = -98304 (=-1.5); * 65536 >> 16 = -98304.
        // Non-exact case: -1 * 32768 = -32768 (=-0.5); >> 16 = -1 (floor).
        Assert.Equal(-1, FixedPoint.FixedMul(-32768, 2));
    }
}
