/*
 * Characterization harness for linuxdoom-1.10 m_fixed.c / m_random.c / tables.c.
 *
 * Compiles the ORIGINAL, UNMODIFIED C sources and dumps input/output pairs
 * as JSON for use as characterization vectors in the C# test suite.
 *
 * Build (from repo root, Linux or Docker):
 *   gcc -O0 -DNORMALUNIX -DLINUX -I Legacy/linuxdoom-1.10 -o charact \
 *       DoomSharp/tools/charact/charact.c \
 *       Legacy/linuxdoom-1.10/m_fixed.c \
 *       Legacy/linuxdoom-1.10/m_random.c \
 *       Legacy/linuxdoom-1.10/tables.c
 *
 * Run: ./charact > DoomSharp/tools/charact/vectors.json
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <setjmp.h>

/* Original headers; the .c files are compiled unmodified. */
#include "doomtype.h"
#include "m_fixed.h"
#include "m_random.h"
#include "tables.h"

/* Index variables are defined in m_random.c but not declared in m_random.h. */
extern int rndindex;
extern int prndindex;

/* ------------------------------------------------------------------ */
/* Minimal I_Error replacement so we can link the original sources.    */
/* The original m_fixed.c calls I_Error on FixedDiv2 overflow.         */
/* The harness recovers via longjmp so one fatal case does not abort   */
/* the whole vector dump; the fatal case is recorded in the output.    */
/* ------------------------------------------------------------------ */
jmp_buf g_fatal_jmp;
int g_fatal_active = 0;

void I_Error (char *error, ...)
{
    (void)error;
    if (g_fatal_active)
        longjmp(g_fatal_jmp, 1);
    /* Outside a guarded section: behave like the original (abort). */
    exit(42);
}

/* Guarded call: emits a "fatal" record instead of dying. */
#define GUARD2(opname, A, B)                                    \
    do {                                                        \
        g_fatal_active = 1;                                     \
        if (setjmp(g_fatal_jmp) == 0) {                         \
            emit_fixed_op(opname, (A), (B), opfn((A), (B)));    \
        } else {                                                \
            emit_fatal(opname, (A), (B));                       \
        }                                                       \
        g_fatal_active = 0;                                     \
    } while (0)

/* ------------------------------------------------------------------ */
/* Deterministic LCG for fuzz inputs (same inputs on any platform).    */
/* ------------------------------------------------------------------ */
static unsigned int lcg_state = 0x12345678u;

static int next_int(void)
{
    lcg_state = lcg_state * 1664525u + 1013904223u;
    return (int)lcg_state;
}

/* ------------------------------------------------------------------ */
/* Output helpers                                                      */
/* ------------------------------------------------------------------ */
static void emit_fixed_op(const char* op, int a, int b, int r)
{
    printf("  { \"op\": \"%s\", \"a\": %d, \"b\": %d, \"r\": %d },\n", op, a, b, r);
}

static void emit_fatal(const char* op, int a, int b)
{
    printf("  { \"op\": \"%s\", \"a\": %d, \"b\": %d, \"fatal\": true },\n", op, a, b);
}

/* Dispatch for the GUARD2 macro. */
static fixed_t (*opfn)(fixed_t, fixed_t) = 0;

int main(void)
{
    int i;

    printf("[\n");

    /* ---------------- FixedMul ---------------- */
    {
        static const int bounds[] = {
            0, 1, -1, 2, -2, 1 << FRACBITS, -(1 << FRACBITS),
            0x7fff, -0x8000, 0x10000, -0x10000,
            0x7fffffff, (int)0x80000000, 0x40000000, (int)0xc0000000,
            0x0000ffff, (int)0xffff0001
        };
        int n = (int)(sizeof(bounds) / sizeof(bounds[0]));
        opfn = FixedMul;
        for (i = 0; i < n; i++) {
            int j;
            for (j = 0; j < n; j++) {
                GUARD2("mul", bounds[i], bounds[j]);
            }
        }
    }

    /* Fuzz: 100k deterministic pairs. */
    opfn = FixedMul;
    for (i = 0; i < 100000; i++) {
        int a = next_int();
        int b = next_int();
        GUARD2("mul", a, b);
    }

    /* ---------------- FixedDiv ---------------- */
    {
        static const int bounds[] = {
            0, 1, -1, 1 << FRACBITS, -(1 << FRACBITS), 0x7fff, -0x8000,
            0x7fffffff, (int)0x80000000, 0x00010000, 0x00004000,
            (int)0xffffc001, 0x00020000, (int)0xfffe0001, 3 << 14, -3 << 14
        };
        int n = (int)(sizeof(bounds) / sizeof(bounds[0]));
        opfn = FixedDiv;
        for (i = 0; i < n; i++) {
            int j;
            for (j = 0; j < n; j++) {
                GUARD2("div", bounds[i], bounds[j]);
            }
        }
    }

    /* Fuzz: 100k deterministic pairs. */
    opfn = FixedDiv;
    for (i = 0; i < 100000; i++) {
        int a = next_int();
        int b = next_int();
        GUARD2("div", a, b);
    }

    /* ---------------- FixedDiv2 boundary cases ---------------- */
    /* FixedDiv2 calls I_Error when the double result overflows int.
       Probe the boundary: |a/b*65536| >= 2^31. */
    opfn = FixedDiv2;
    GUARD2("div2", (int)0x80000000, -1);   /* ->  2^31 -> fatal */
    GUARD2("div2", (int)0x80000000, 1);    /* -> -2^31 -> fatal */
    GUARD2("div2", 0x7fffffff, 1);         /*  2147483647.0 -> ok */
    GUARD2("div2", (int)0x80000000, 2);
    GUARD2("div2", (int)0x80000000, -2);
    GUARD2("div2", 0x7fffffff, -1);
    GUARD2("div2", 0, 0);                  /* 0/0 -> 0.0 -> ok, returns 0 */

    /* ---------------- Random ---------------- */
    {
        printf("  { \"op\": \"random_start\", \"p\": %d, \"m\": %d },\n", prndindex, rndindex);
        printf("  [");
        for (i = 0; i < 256; i++) {
            printf("%d%s", P_Random(), i < 255 ? ", " : "");
        }
        printf("],\n");
        printf("  { \"op\": \"prndindex_after_256\", \"v\": %d },\n", prndindex);

        printf("  [");
        for (i = 0; i < 300; i++) {
            printf("%d%s", M_Random(), i < 299 ? ", " : "");
        }
        printf("],\n");
        printf("  { \"op\": \"rndindex_after_300\", \"v\": %d },\n", rndindex);

        M_ClearRandom();
        printf("  { \"op\": \"after_clear\", \"p\": %d, \"m\": %d },\n", prndindex, rndindex);
        printf("  { \"op\": \"p_after_clear\", \"v\": %d },\n", P_Random());
    }

    /* ---------------- Tables ---------------- */
    {
        printf("  { \"op\": \"finesine\", \"len\": %d, \"data\": [", (int)(5*FINEANGLES/4));
        for (i = 0; i < 5*FINEANGLES/4; i++) {
            printf("%d%s", finesine[i], i < 5*FINEANGLES/4 - 1 ? "," : "");
        }
        printf("] },\n");

        printf("  { \"op\": \"finetangent\", \"len\": %d, \"data\": [", FINEANGLES/2);
        for (i = 0; i < FINEANGLES/2; i++) {
            printf("%d%s", finetangent[i], i < FINEANGLES/2 - 1 ? "," : "");
        }
        printf("] },\n");

        printf("  { \"op\": \"tantoangle\", \"len\": %d, \"data\": [", SLOPERANGE+1);
        for (i = 0; i <= SLOPERANGE; i++) {
            printf("%u%s", tantoangle[i], i < SLOPERANGE ? "," : "");
        }
        printf("] },\n");
    }

    printf("  { \"op\": \"end\" }\n");
    printf("]\n");

    return 0;
}
