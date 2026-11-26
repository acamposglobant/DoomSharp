/*
 * Characterization harness for linuxdoom-1.10 r_main.c / r_draw.c / v_video.c.
 *
 * Compiles the ORIGINAL, UNMODIFIED C sources and dumps input/output pairs
 * as JSON for use as characterization vectors in the C# test suite.
 *
 * Build (from repo root, Linux or Docker):
 *   gcc -O0 -DNORMALUNIX -DLINUX -I Legacy/linuxdoom-1.10 -o rmaindump \
 *       DoomSharp/tools/charact/rmaindump.c \
 *       Legacy/linuxdoom-1.10/r_main.c \
 *       Legacy/linuxdoom-1.10/r_draw.c \
 *       Legacy/linuxdoom-1.10/v_video.c \
 *       Legacy/linuxdoom-1.10/tables.c \
 *       Legacy/linuxdoom-1.10/m_fixed.c \
 *       Legacy/linuxdoom-1.10/m_bbox.c
 *
 * Run: ./rmaindump > DoomSharp/tools/charact/rmain_vectors.json
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <setjmp.h>

/* Original headers; the .c files are compiled unmodified. */
#include "doomtype.h"
#include "m_fixed.h"
#include "tables.h"
#include "r_main.h"
#include "r_draw.h"
#include "v_video.h"
#include "r_data.h"
#include "d_player.h"
#include "r_defs.h"

/* ------------------------------------------------------------------ */
/* I_Error replacement: longjmp recovery so one fatal case does not    */
/* abort the whole vector dump.                                        */
/* ------------------------------------------------------------------ */
jmp_buf g_fatal_jmp;
int g_fatal_active = 0;

void I_Error (char *error, ...)
{
    printf("{\"fatal\": true},\n");
    fprintf(stderr, "I_Error called\n");
    if (g_fatal_active)
        longjmp(g_fatal_jmp, 1);
    exit(1);
}

/* ------------------------------------------------------------------ */
/* Stubs for globals the renderer modules reference.                   */
/* ------------------------------------------------------------------ */
/* Stubs: only globals/functions NOT defined by the compiled modules.  */
/* r_draw.c defines: viewwidth/height/window, ylookup, columnofs,      */
/* dc_*, ds_*, fuzzoffset, fuzzpos, translationtables, viewimage.      */
/* v_video.c defines: screens, dirtybox, gammatable, usegamma.         */
/* r_main.c defines: viewx/y/z, viewangle, viewcos/sin, centerx/frac,  */
/* projection, validcount, framecount, sscount, linecount, loopcount,  */
/* detailshift, fixedcolormap, scalelight, zlight, extralight,         */
/* colfunc/basecolfunc/fuzzcolfunc/transcolfunc/spanfunc.              */
lighttable_t*   walllights;
byte*           colormaps;
int             numflats;
int             firstflat;
int             lastflat;
int*            flattranslation;
int             numtextures;
short*          texturewidthmask;
fixed_t*        textureheight;
int             firstspritelump;
int             lastspritelump;
int             numspritelumps;
int*            spritewidth;
int*            spriteoffset;
int*            spritetopoffset;
seg_t*          curline;
side_t*         sidedef;
line_t*         linedef;
int             rw_x;
int             rw_stopx;
angle_t         rw_normalangle;
int             rw_distance;
fixed_t         rw_scale;
fixed_t         rw_scalestep;
int             rw_midtexturemid;
int             rw_toptexturemid;
int             rw_bottomtexturemid;
int             worldtop;
int             worldbottom;
int             worldhigh;
int             worldlow;
int             worldtopstep;
int             worldbottomstep;
fixed_t         rw_light;
fixed_t         rw_lightstep;
int             rw_lightleft;
int             skyflatnum;
int             pspritescale;
int             pspriteiscale;
int*            screenheightarray;
fixed_t*        yslope;
int*            distscale;
int             detailLevel;
int             screenblocks;
int             numnodes;
subsector_t*    subsectors;
node_t*         nodes;
void R_InitData (void) {}
void R_InitPlanes (void) {}
void R_InitSkyMap (void) {}
void R_ClearClipSegs (void) {}
void R_ClearDrawSegs (void) {}
void R_ClearPlanes (void) {}
void R_ClearSprites (void) {}
void NetUpdate (void) {}
void R_RenderBSPNode (int bspnum) { (void)bspnum; }
void R_DrawPlanes (void) {}
void R_DrawMasked (void) {}
void* Z_Malloc (int size, int purpose, unsigned int* user) { (void)purpose; (void)user; return malloc((size_t)size); }
int             gamemode = 2;   /* retail */
byte* W_CacheLumpName (char* name, int tag) { (void)name; (void)tag; return 0; }
byte* I_AllocLow (int length) { return calloc(1, (size_t)length); }

/* r_draw.c globals (fuzzpos is a global in r_draw.c) */
extern int	fuzzpos;
extern byte*	ylookup[200];
extern int	columnofs[320];

/* Fill ylookup/columnofs like R_InitBuffer does for the full screen. */
static void init_buffer_luts(void)
{
    int i;
    for (i = 0; i < 200; i++)
        ylookup[i] = screens[0] + i * SCREENWIDTH;
    for (i = 0; i < 320; i++)
        columnofs[i] = i;
    viewwidth = SCREENWIDTH;
    viewheight = SCREENHEIGHT;
    centery = SCREENHEIGHT / 2;
    centerx = SCREENWIDTH / 2;
}

/* ------------------------------------------------------------------ */
/* JSON helpers                                                        */
/* ------------------------------------------------------------------ */
static void json_escape(const char *s)
{
    putchar('"');
    for (; *s; s++)
    {
        if (*s == '"' || *s == '\\')
            printf("\\%c", *s);
        else if ((unsigned char)*s < 32)
            printf("\\u%04x", *s);
        else
            putchar(*s);
    }
    putchar('"');
}

static void dump_screen_region(const char *name, int y, int x, int h, int w)
{
    int row, col;
    printf("    {\n      \"name\": ");
    json_escape(name);
    printf(",\n      \"y\": %d, \"x\": %d, \"h\": %d, \"w\": %d,\n      \"pixels\": [", y, x, h, w);
    for (row = 0; row < h; row++)
    {
        for (col = 0; col < w; col++)
        {
            printf("%s%d", (row || col) ? "," : "", screens[0][(y + row) * SCREENWIDTH + x + col]);
        }
    }
    printf("]\n    }");
}

/* ------------------------------------------------------------------ */
/* R_PointOnSide / R_PointOnSegSide vectors                            */
/* ------------------------------------------------------------------ */
static void dump_pointonside(void)
{
    /* node partition lines through the origin with various slopes */
    static const struct { fixed_t x, y, dx, dy; } nodes[] =
    {
        { 0, 0, 0, 1 },        /* vertical partition, dy > 0 */
        { 0, 0, 0, -1 },       /* vertical, dy < 0 */
        { 0, 0, 1, 0 },        /* horizontal, dx > 0 */
        { 0, 0, -1, 0 },       /* horizontal, dx < 0 */
        { 100 << 16, 50 << 16, 300 << 16, 200 << 16 },
        { -50 << 16, -25 << 16, 64 << 16, -128 << 16 },
        { 0, 0, -200 << 16, 200 << 16 },
    };
    static const struct { fixed_t x, y; } points[] =
    {
        { 10 << 16, 10 << 16 }, { -10 << 16, 10 << 16 },
        { -10 << 16, -10 << 16 }, { 10 << 16, -10 << 16 },
        { 0, 0 }, { 100 << 16, 0 }, { 0, 100 << 16 },
        { 100 << 16, 50 << 16 }, { 99 << 16, 50 << 16 },
        { 101 << 16, 50 << 16 }, { 0x7fff0000, -0x7fff0000 },
    };
    size_t i, j;
    node_t node;
    int side;

    printf("  \"pointOnSide\": [\n");
    for (i = 0; i < sizeof(nodes) / sizeof(nodes[0]); i++)
    {
        node.x = nodes[i].x;
        node.y = nodes[i].y;
        node.dx = nodes[i].dx;
        node.dy = nodes[i].dy;
        for (j = 0; j < sizeof(points) / sizeof(points[0]); j++)
        {
            side = R_PointOnSide(points[j].x, points[j].y, &node);
            printf("    {\"nx\": %d, \"ny\": %d, \"ndx\": %d, \"ndy\": %d, \"x\": %d, \"y\": %d, \"side\": %d},\n",
                   node.x, node.y, node.dx, node.dy, points[j].x, points[j].y, side);
        }
    }
    printf("  ],\n");
}

/* ------------------------------------------------------------------ */
/* R_PointToAngle2 / R_PointToDist vectors across all octants          */
/* ------------------------------------------------------------------ */
static void dump_pointtoangle(void)
{
    static const struct { fixed_t x1, y1, x2, y2; } cases[] =
    {
        { 0, 0, 100 << 16, 0 },          /* octant 0: due east */
        { 0, 0, 100 << 16, 50 << 16 },   /* octant 0 */
        { 0, 0, 50 << 16, 100 << 16 },   /* octant 1 */
        { 0, 0, 0, 100 << 16 },          /* octant 1 boundary: due north */
        { 0, 0, -50 << 16, 100 << 16 },  /* octant 2 */
        { 0, 0, -100 << 16, 50 << 16 },  /* octant 3 */
        { 0, 0, -100 << 16, 0 },         /* octant 3/4 boundary: due west */
        { 0, 0, -100 << 16, -50 << 16 }, /* octant 4 */
        { 0, 0, -50 << 16, -100 << 16 }, /* octant 5 */
        { 0, 0, 0, -100 << 16 },         /* octant 7 boundary: due south */
        { 0, 0, 50 << 16, -100 << 16 },  /* octant 7 */
        { 0, 0, 100 << 16, -50 << 16 },  /* octant 8 */
        { 0, 0, 100 << 16, 100 << 16 },  /* x == y diagonal */
        { 0, 0, -100 << 16, 100 << 16 },
        { 0, 0, 100 << 16, 1 },          /* near-axis: SlopeDiv den < 512 */
        { 0, 0, 1, 100 << 16 },
        { 0, 0, 1, 1 },                  /* tiny: den < 512 clamp */
        { 0, 0, 0, 0 },                  /* degenerate -> 0 */
        { 5 << 16, 5 << 16, 5 << 16, 5 << 16 },
        { 0, 0, 0x7fff0000, 0x7fff0000 },
        { 0, 0, -0x7fff0000, 0x7fff0000 },
        { 100 << 16, 0, -100 << 16, 0 }, /* 180 degrees */
        { 0, 0, 1 << 16, 0x7fff0000 },   /* steep slope */
    };
    size_t i;

    printf("  \"pointToAngle2\": [\n");
    for (i = 0; i < sizeof(cases) / sizeof(cases[0]); i++)
    {
        angle_t a = R_PointToAngle2(cases[i].x1, cases[i].y1, cases[i].x2, cases[i].y2);
        /* R_PointToDist with dx==dy==0 hits FixedDiv overflow sentinel
           and indexes tantoangle out of bounds (original UB) — skip. */
        fixed_t d = -1;
        if (cases[i].x1 != cases[i].x2 || cases[i].y1 != cases[i].y2)
            d = R_PointToDist(cases[i].x2, cases[i].y2);
        printf("    {\"x1\": %d, \"y1\": %d, \"x2\": %d, \"y2\": %d, \"angle\": %u, \"dist\": %d},\n",
               cases[i].x1, cases[i].y1, cases[i].x2, cases[i].y2, a, d);
    }
    printf("  ],\n");
}

/* ------------------------------------------------------------------ */
/* R_ScaleFromGlobalAngle vectors                                      */
/* ------------------------------------------------------------------ */
static void dump_scalefromglobalangle(void)
{
    static const angle_t visangles[] =
    {
        0, ANG45, ANG90, ANG45 + ANG45 / 2, ANG180, ANG270,
        0x7fffffff, 0xffffffffu, 0x12345678, 0x7edcba98,
    };
    size_t i;
    int k;

    printf("  \"scaleFromGlobalAngle\": [\n");
    for (k = 0; k < 3; k++)
    {
        /* scenario 0: normal; 1: far wall; 2: den > num>>16 guard */
        viewangle = ANG90;
        rw_normalangle = ANG90;
        detailshift = 0;
        centerxfrac = 160 << 16;
        projection = centerxfrac;
        if (k == 0)
            rw_distance = 100 << 16;
        else if (k == 1)
            rw_distance = 800 << 16;
        else
            rw_distance = 1 << 16;

        for (i = 0; i < 8; i++)
        {
            /* visangle stays within the view frustum (±ANG45/2 of the
               view angle) as in real rendering; larger deltas index
               finesine out of bounds (original UB). */
            angle_t vis = ANG90 - (ANG45 / 2) + i * (ANG45 / 8);
            int scale;
            g_fatal_active = 1;
            if (setjmp(g_fatal_jmp))
            {
                g_fatal_active = 0;
                printf("    {\"visangle\": %u, \"viewangle\": %u, \"rw_normalangle\": %u, \"rw_distance\": %d, \"detailshift\": %d, \"fatal\": true},\n",
                       vis, viewangle, rw_normalangle, rw_distance, detailshift);
                continue;
            }
            scale = R_ScaleFromGlobalAngle(vis);
            g_fatal_active = 0;
            printf("    {\"visangle\": %u, \"viewangle\": %u, \"rw_normalangle\": %u, \"rw_distance\": %d, \"detailshift\": %d, \"scale\": %d},\n",
                   vis, viewangle, rw_normalangle, rw_distance, detailshift, scale);
        }
    }
    printf("  ],\n");
}

/* ------------------------------------------------------------------ */
/* R_DrawColumn / R_DrawFuzzColumn / R_DrawTranslatedColumn vectors    */
/* ------------------------------------------------------------------ */
static byte test_column_source[128];

static void init_test_column(void)
{
    int i;
    for (i = 0; i < 128; i++)
        test_column_source[i] = (byte)(i * 7 + 3);
}

static void dump_columns(void)
{
    int i;

    /* identity colormap: colormap[i] = i (so we see raw texel flow) */
    static byte identity[34*256];
    for (i = 0; i < 34*256; i++)
        identity[i] = (byte)i;

    dc_colormap = identity;
    dc_source = test_column_source;

    printf("  \"drawColumn\": [\n");
    {
        static const struct { int x, yl, yh, iscale, mid; } cases[] =
        {
            { 10, 50, 149, 1 << 16, 0 },
            { 0, 0, 199, 1 << 16, 0 },
            { 319, 0, 199, 1 << 16, 0 },
            { 160, 50, 149, 0x18000, 100 << 16 },
            { 160, 50, 149, 0x8000, 50 << 16 },
            { 200, 0, 199, 0x20000, 0 },
        };
        size_t c;

        for (c = 0; c < sizeof(cases) / sizeof(cases[0]); c++)
        {
            memset(screens[0], 0, SCREENWIDTH * SCREENHEIGHT);
            dc_x = cases[c].x;
            dc_yl = cases[c].yl;
            dc_yh = cases[c].yh;
            dc_iscale = cases[c].iscale;
            dc_texturemid = cases[c].mid;
            dc_source = test_column_source;
            R_DrawColumn();
            printf("    {\"x\": %d, \"yl\": %d, \"yh\": %d, \"iscale\": %d, \"mid\": %d, \"screen\": [",
                   dc_x, dc_yl, dc_yh, dc_iscale, dc_texturemid);
            for (i = 0; i < SCREENWIDTH * SCREENHEIGHT; i++)
                printf("%s%d", i ? "," : "", screens[0][i]);
            printf("]},\n");
        }
    }
    printf("  ],\n");
}

/* ------------------------------------------------------------------ */
/* R_DrawFuzzColumn vectors (fuzzpos continuity across calls)          */
/* ------------------------------------------------------------------ */
static void dump_fuzz(void)
{
    int i;

    /* identity colormap */
    static byte identity[34*256];
    for (i = 0; i < 34*256; i++)
        identity[i] = (byte)i;

    colormaps = identity;

    printf("  \"drawFuzzColumn\": [\n");
    {
        /* seed the screen with a gradient so fuzz reads real pixels */
        int p;
        for (p = 0; p < SCREENWIDTH * SCREENHEIGHT; p++)
            screens[0][p] = (byte)(p * 13 + 5);

        dc_x = 100;
        dc_yl = 10;
        dc_yh = 60;
        R_DrawFuzzColumn();
        printf("    {\"call\": 1, \"fuzzpos_after\": %d, \"screen\": [", fuzzpos);
        for (i = 0; i < SCREENWIDTH * SCREENHEIGHT; i++)
            printf("%s%d", i ? "," : "", screens[0][i]);
        printf("]},\n");

        /* second call continues fuzzpos */
        dc_x = 11;
        dc_yl = 40;
        dc_yh = 80;
        R_DrawFuzzColumn();
        printf("    {\"call\": 2, \"fuzzpos_after\": %d, \"screen\": [", fuzzpos);
        for (i = 0; i < SCREENWIDTH * SCREENHEIGHT; i++)
            printf("%s%d", i ? "," : "", screens[0][i]);
        printf("]},\n");
    }
    printf("  ],\n");
}

/* ------------------------------------------------------------------ */
/* R_DrawSpan vectors                                                  */
/* ------------------------------------------------------------------ */
static byte test_flat_source[64 * 64];

static void dump_spans(void)
{
    int i;

    static byte identity[34*256];
    for (i = 0; i < 34*256; i++)
        identity[i] = (byte)i;

    for (i = 0; i < 64 * 64; i++)
        test_flat_source[i] = (byte)(i * 5 + 11);

    ds_colormap = identity;
    ds_source = test_flat_source;

    printf("  \"drawSpan\": [\n");
    {
        static const struct { int y, x1, x2, xf, yf, xs, ys; } cases[] =
        {
            { 100, 10, 309, 0, 0, 1 << 16, 0 },
            { 100, 10, 309, 0, 0, 1 << 16, 1 << 16 },
            { 0, 0, 319, 0x8000, 0x4000, 0x12345, 0x6789a },
            { 50, 0, 319, 0, 0, 0x18000, 0x8000 },
            { 199, 5, 314, 0xabcdef, 0x0f0f0f, 0x22222, 0x33333 },
        };
        size_t c;

        for (c = 0; c < sizeof(cases) / sizeof(cases[0]); c++)
        {
            memset(screens[0], 0, SCREENWIDTH * SCREENHEIGHT);
            ds_y = cases[c].y;
            ds_x1 = cases[c].x1;
            ds_x2 = cases[c].x2;
            ds_xfrac = cases[c].xf;
            ds_yfrac = cases[c].yf;
            ds_xstep = cases[c].xs;
            ds_ystep = cases[c].ys;
            ds_source = test_flat_source;
            ds_colormap = identity;
            R_DrawSpan();
            printf("    {\"y\": %d, \"x1\": %d, \"x2\": %d, \"xfrac\": %d, \"yfrac\": %d, \"xstep\": %d, \"ystep\": %d, \"screen\": [",
                   ds_y, ds_x1, ds_x2, ds_xfrac, ds_yfrac, ds_xstep, ds_ystep);
            for (i = 0; i < SCREENWIDTH * SCREENHEIGHT; i++)
                printf("%s%d", i ? "," : "", screens[0][i]);
            printf("]},\n");
        }
    }
    printf("  ],\n");
}

/* ------------------------------------------------------------------ */
/* V_DrawPatch vectors                                                 */
/* ------------------------------------------------------------------ */
static byte test_patch[256];

static void dump_drawpatch(void)
{
    /* Build a 4x4 patch with two posts in column 0 and transparency. */
    static byte identity[34*256];
    int i;
    int ofs[4];
    int pos;
    patch_t *patch;

    for (i = 0; i < 34*256; i++)
        identity[i] = (byte)i;

    memset(screens[0], 0, SCREENWIDTH * SCREENHEIGHT);

    pos = 8 + 4 * 4;   /* header + 4 column offsets */

    /* column 0: post at topdelta 0 len 2, post at topdelta 3 len 1 */
    ofs[0] = pos;
    test_patch[pos++] = 0;
    test_patch[pos++] = 2;
    test_patch[pos++] = 0;
    test_patch[pos++] = 0x11;
    test_patch[pos++] = 0x22;
    test_patch[pos++] = 0;
    test_patch[pos++] = 3;
    test_patch[pos++] = 1;
    test_patch[pos++] = 0;
    test_patch[pos++] = 0x33;
    test_patch[pos++] = 0;
    test_patch[pos++] = 0xff;

    /* column 1: empty */
    ofs[1] = pos;
    test_patch[pos++] = 0xff;

    /* column 2: post at topdelta 1, 4 pixels */
    ofs[2] = pos;
    test_patch[pos++] = 1;
    test_patch[pos++] = 4;
    test_patch[pos++] = 0;
    test_patch[pos++] = 0xAA;
    test_patch[pos++] = 0x22;
    test_patch[pos++] = 0x33;
    test_patch[pos++] = 0x44;
    test_patch[pos++] = 0;
    test_patch[pos++] = 0xff;

    /* column 3: empty */
    ofs[3] = pos;
    test_patch[pos++] = 0xff;

    patch = (patch_t *)test_patch;
    patch->width = 4;          /* little-endian SHORT(4) */
    ((byte *)&patch->width)[1] = 0;
    patch->height = 4;
    ((byte *)&patch->height)[1] = 0;
    patch->leftoffset = 2;
    ((byte *)&patch->leftoffset)[1] = 0;
    patch->topoffset = 1;
    ((byte *)&patch->topoffset)[1] = 0;
    for (i = 0; i < 4; i++)
        patch->columnofs[i] = ofs[i];

    V_DrawPatch(10, 10, 0, patch);

    printf("  \"drawPatch\": [\n");
    printf("    ");
    dump_screen_region("patch_10_10", 5, 5, 12, 12);
    printf("\n  ],\n");
}

/* ------------------------------------------------------------------ */
/* main                                                                */
/* ------------------------------------------------------------------ */
int main(void)
{
    printf("{\n");

    V_Init();
    init_buffer_luts();
    init_test_column();

    dump_pointonside();
    dump_pointtoangle();
    dump_scalefromglobalangle();
    dump_columns();
    dump_fuzz();
    dump_spans();
    dump_drawpatch();

    printf("  \"end\": true\n}\n");
    return 0;
}



