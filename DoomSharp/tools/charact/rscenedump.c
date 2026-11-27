/*
 * Characterization harness for linuxdoom-1.10 r_bsp.c / r_segs.c /
 * r_plane.c / r_things.c (DOOM-030c).
 *
 * Compiles the ORIGINAL, UNMODIFIED C sources and dumps input/output
 * pairs as JSON for use as characterization vectors in the C# test
 * suite.
 *
 * Build (from repo root, Linux or Docker):
 *   gcc -O0 -DNORMALUNIX -DLINUX -I Legacy/linuxdoom-1.10 -o rscenedump \
 *       DoomSharp/tools/charact/rscenedump.c \
 *       Legacy/linuxdoom-1.10/r_bsp.c \
 *       Legacy/linuxdoom-1.10/r_segs.c \
 *       Legacy/linuxdoom-1.10/r_plane.c \
 *       Legacy/linuxdoom-1.10/r_things.c \
 *       Legacy/linuxdoom-1.10/r_main.c \
 *       Legacy/linuxdoom-1.10/r_draw.c \
 *       Legacy/linuxdoom-1.10/v_video.c \
 *       Legacy/linuxdoom-1.10/tables.c \
 *       Legacy/linuxdoom-1.10/m_fixed.c \
 *       Legacy/linuxdoom-1.10/m_bbox.c
 *
 * Run: ./rscenedump > DoomSharp/tools/charact/rscene_vectors.json
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <setjmp.h>
#include <stdarg.h>

/* Original headers; the .c files are compiled unmodified. */
#include "doomtype.h"
#include "m_fixed.h"
#include "tables.h"
#include "r_main.h"
#include "r_bsp.h"
#include "r_segs.h"
#include "r_plane.h"
#include "r_things.h"
#include "r_draw.h"
#include "v_video.h"
#include "r_defs.h"
#include "r_sky.h"
#include "d_player.h"

/* Module-private globals the harness inspects (defined in the .c
 * files; not exposed in the headers). */
struct cliprange { int first, last; };   /* r_bsp.c:80-85 */
extern struct cliprange solidsegs[32];
extern struct cliprange* newend;
extern visplane_t   visplanes[128];
extern visplane_t*  lastvisplane;
extern short        floorclip[SCREENWIDTH];
extern short        ceilingclip[SCREENWIDTH];

/* ------------------------------------------------------------------ */
/* I_Error replacement: longjmp recovery so one fatal case does not    */
/* abort the whole vector dump.                                        */
/* ------------------------------------------------------------------ */
jmp_buf g_fatal_jmp;
int g_fatal_active = 0;

void I_Error (char *error, ...)
{
    (void)error;
    printf("{\"fatal\": true},\n");
    fprintf(stderr, "I_Error called\n");
    if (g_fatal_active)
        longjmp(g_fatal_jmp, 1);
    exit(1);
}

/* ------------------------------------------------------------------ */
/* Stubs: only globals/functions NOT defined by the compiled modules.  */
/* r_bsp.c defines: drawsegs, ds_p, solidsegs, newend, curline,        */
/*   sidedef, linedef, frontsector, backsector.                        */
/* r_segs.c defines: rw_* (except rw_angle1 which it also defines),    */
/*   world*, pix*, top/bottom frac/step, walllights, maskedtexturecol, */
/*   floorclip/ceilingclip? NO - r_plane.c defines those.              */
/* r_plane.c defines: visplanes, lastvisplane, floorplane,             */
/*   ceilingplane, openings, lastopening, floorclip, ceilingclip,      */
/*   spanstart, planeheight, basexscale, baseyscale, cached*.          */
/* r_things.c defines: vissprites, vissprite_p, overflowsprite,        */
/*   sprites, numsprites, sprtemp, maxframe, spritename,               */
/*   negonearray, screenheightarray, pspriteiscale? NO (r_main.c),     */
/*   spryscale, sprtopscreen, mfloorclip, mceilingclip.                */
/* r_main.c defines: viewx/y/z, viewangle, viewcos/sin, centerx/frac,  */
/*   centeryfrac, projection, validcount, framecount, sscount,         */
/*   linecount, loopcount, detailshift, extralight, fixedcolormap,     */
/*   xtoviewangle, viewangletox, clipangle, yslope, distscale,         */
/*   scalelight, zlight, pspriteiscale, pspritescale,                  */
/*   R_PointToAngle*, R_PointToDist, R_ScaleFromGlobalAngle,           */
/*   R_PointInSubsector, R_RenderPlayerView, R_SetupFrame.             */
/* r_draw.c defines: viewwidth/height/window, ylookup, columnofs,      */
/*   dc_*, ds_*, fuzzoffset, fuzzpos, translationtables, colfunc etc.  */
/* v_video.c defines: screens, dirtybox, gammatable, usegamma.         */
/* ------------------------------------------------------------------ */
lighttable_t*   colormaps;
int             numflats;
int             firstflat;
int             lastflat;
int*            flattranslation;
int             numtextures;
short*          texturewidthmask;
fixed_t*        textureheight;
int*            texturetranslation;
int             firstspritelump;
int             lastspritelump;
int             numspritelumps;
int*            spritewidth;
int*            spriteoffset;
int*            spritetopoffset;
int             skyflatnum = 42;
int             skytexture;
int             skytexturemid;
int             numnodes;
subsector_t*    subsectors;
node_t*         nodes;
seg_t*          segs;
sector_t*       sectors;
vertex_t*       vertexes;
side_t*         sides;
line_t*         lines;
int             gamemode = 2;   /* retail */
int             modifiedgame = 0;
int             numsubsectors;
int             detailLevel;
int             screenblocks;

/* lumpinfo_t (w_wad.h:55-62) — the harness never loads a WAD, but
 * R_InitSpriteDefs scans lumpinfo[] names. */
typedef struct
{
    char        name[8];
    int         handle;
    int         position;
    int         size;
} fake_lumpinfo_t;
fake_lumpinfo_t* lumpinfo;
int             numlumps;

void R_InitData (void) {}
void R_InitSkyMap (void) {}

void NetUpdate (void) {}
void* Z_Malloc (int size, int purpose, unsigned int* user)
{
    (void)purpose; (void)user;
    return calloc(1, (size_t)size);
}
void Z_Free (void* p) { free(p); }
void Z_ChangeTag2 (void* p, int tag) { (void)p; (void)tag; }
byte* W_CacheLumpNum (int lump, int tag) { (void)lump; (void)tag; return 0; }
byte* W_CacheLumpName (char* name, int tag) { (void)name; (void)tag; return 0; }
int W_CheckNumForName (char* name) { (void)name; return -1; }
int W_GetNumForName (char* name) { (void)name; return 0; }
int W_LumpLength (int lump) { (void)lump; return 0; }
byte* I_AllocLow (int length) { return calloc(1, (size_t)length); }

/* R_GetColumn replacement: the harness hands out slices of a static  */
/* 128-byte column buffer so R_RenderSegLoop can rasterize.           */
static byte g_columnbuf[128];
byte* R_GetColumn (int tex, int col)
{
    (void)tex;
    return g_columnbuf + (col & 127);
}

/* r_draw.c externs the colfunc pointer table; nothing to stub. */

/* Fill ylookup/columnofs like R_InitBuffer does for the full screen. */
extern byte*    ylookup[200];
extern int      columnofs[320];

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
    centeryfrac = centery << FRACBITS;
    centerxfrac = centerx << FRACBITS;
    projection = centerxfrac;
    detailshift = 0;
}

/* ------------------------------------------------------------------ */
/* JSON helpers                                                        */
/* ------------------------------------------------------------------ */
static void dump_clipsegs(const char *name)
{
    int i;
    printf("    {\n      \"name\": \"%s\",\n      \"solidsegs\": [", name);
    for (i = 0; i < newend - solidsegs; i++)
    {
        printf("%s{\"first\": %d, \"last\": %d}",
               i ? "," : "", solidsegs[i].first, solidsegs[i].last);
    }
    printf("],\n      \"newend\": %d,\n      \"drawsegs\": [", (int)(newend - solidsegs));
    {
        drawseg_t* ds;
        int first = 1;
        for (ds = drawsegs; ds < ds_p; ds++)
        {
            printf("%s{\"x1\": %d, \"x2\": %d, \"scale1\": %d, \"scale2\": %d, "
                   "\"scalestep\": %d, \"silhouette\": %d, \"bsil\": %d, \"tsil\": %d}",
                   first ? "" : ",", ds->x1, ds->x2, ds->scale1, ds->scale2,
                   ds->scalestep, ds->silhouette, ds->bsilheight, ds->tsilheight);
            first = 0;
        }
    }
    printf("]\n    }");
}

/* ------------------------------------------------------------------ */
/* R_ClipSolidWallSegment / R_ClipPassWallSegment vectors              */
/* ------------------------------------------------------------------ */
static sector_t g_front, g_back;
static side_t g_side;
static line_t g_line;
static seg_t g_seg;
static vertex_t g_v1, g_v2;
static byte g_flat[64 * 64];

static void build_level(void);
static void dump_clips(void)
{
    static const struct { int solid; int first, last; } ops[] =
    {
        { 1, 10, 20 },     /* solid in the middle */
        { 1, 40, 60 },     /* second solid post */
        { 0, 25, 35 },     /* pass between them */
        { 1, 15, 45 },     /* solid overlapping both */
        { 0, 5, 12 },      /* pass partially clipped */
        { 1, 0, 319 },     /* solid covering everything */
        { 0, 100, 120 },   /* pass fully occluded */
        { 1, 200, 250 },   /* solid after full cover (invisible) */
        { 0, 300, 310 },   /* pass at the right edge */
    };
    int i;

    printf("  \"clipSolid\": [\n");
    build_level();
    R_ClearClipSegs();
    R_ClearDrawSegs();
    R_ClearPlanes();
    curline = &g_seg;
    frontsector = &g_front;
    backsector = &g_back;
    rw_angle1 = R_PointToAngle2(viewx, viewy, g_v1.x, g_v1.y);
    floorplane = R_FindPlane(0, 1, 200);
    ceilingplane = R_FindPlane((128 << FRACBITS) - viewz, 1, 200);
    for (i = 0; i < (int)(sizeof(ops)/sizeof(ops[0])); i++)
    {
        g_fatal_active = 1;
        if (setjmp(g_fatal_jmp) == 0)
        {
            if (ops[i].solid)
                R_ClipSolidWallSegment(ops[i].first, ops[i].last);
            else
                R_ClipPassWallSegment(ops[i].first, ops[i].last);
        }
        g_fatal_active = 0;
        dump_clipsegs("op");
        printf("%s\n", (i + 1 < (int)(sizeof(ops)/sizeof(ops[0]))) ? "," : "");
    }
    printf("  ],\n");
}

/* ------------------------------------------------------------------ */
/* R_FindPlane / R_CheckPlane vectors                                  */
/* ------------------------------------------------------------------ */
static void dump_planes(void)
{
    struct { int height, picnum, lightlevel; } finds[] =
    {
        { 64 << 16, 1, 160 },
        { 64 << 16, 1, 160 },      /* duplicate */
        { 128 << 16, 1, 160 },
        { 0, 42, 0 },              /* sky plane (picnum == skyflatnum) */
        { 96 << 16, 1, 160 },
    };
    int i;
    visplane_t* pl;
    visplane_t* p;

    printf("  \"planes\": [\n");
    R_ClearPlanes();
    for (i = 0; i < (int)(sizeof(finds)/sizeof(finds[0])); i++)
    {
        p = R_FindPlane(finds[i].height, finds[i].picnum,
                        finds[i].lightlevel);
        printf("    {\"find\": %d, \"index\": %ld, \"minx\": %d, \"maxx\": %d}%s\n",
               i, p - visplanes, p->minx, p->maxx,
               (i + 1 < (int)(sizeof(finds)/sizeof(finds[0]))) ? "," : "");
    }
    printf("  ],\n");

    /* CheckPlane: extend vs split on the first plane */
    pl = &visplanes[0];
    pl->minx = 10;
    pl->maxx = 20;
    memset(pl->top, 0xff, sizeof(pl->top));

    p = R_CheckPlane(pl, 15, 25);       /* overlaps used range -> split */
    printf("  \"checkPlaneSplit\": {\"index\": %ld, \"minx\": %d, \"maxx\": %d},\n",
           p - visplanes, p->minx, p->maxx);

    p = R_CheckPlane(pl, 30, 40);       /* free range: extends */
    printf("  \"checkPlaneExtend\": {\"index\": %ld, \"minx\": %d, \"maxx\": %d},\n",
           p - visplanes, p->minx, p->maxx);
}

/* ------------------------------------------------------------------ */
/* R_StoreWallRange geometry vectors                                   */
/* ------------------------------------------------------------------ */
/* Hand-build a one-seg "level": a vertical wall 256 units in front of
 * the view, two-sided with a lower step. Shared by all dump stages. */
static void build_level(void)
{
    viewx = 0;
    viewy = 0;
    viewz = 48 << FRACBITS;
    viewangle = ANG90;
    viewsin = finesine[ANG90 >> ANGLETOFINESHIFT];
    viewcos = finecosine[ANG90 >> ANGLETOFINESHIFT];
    validcount++;

    g_front.floorheight = 0;
    g_front.ceilingheight = 128 << FRACBITS;
    g_front.lightlevel = 200;
    g_front.floorpic = 1;
    g_front.ceilingpic = 1;
    g_back.floorheight = 32 << FRACBITS;
    g_back.ceilingheight = 128 << FRACBITS;
    g_back.lightlevel = 200;
    g_back.floorpic = 1;
    g_back.ceilingpic = 1;

    g_v1.x = -128 << FRACBITS;
    g_v1.y = 256 << FRACBITS;
    g_v2.x = 128 << FRACBITS;
    g_v2.y = 256 << FRACBITS;

    memset(&g_line, 0, sizeof(g_line));
    g_line.flags = 0;
    g_line.sidenum[0] = 0;
    g_line.sidenum[1] = 0;
    g_line.frontsector = &g_front;
    g_line.backsector = &g_back;
    g_line.v1 = &g_v1;
    g_line.v2 = &g_v2;

    memset(&g_seg, 0, sizeof(g_seg));
    g_seg.v1 = &g_v1;
    g_seg.v2 = &g_v2;
    g_seg.linedef = &g_line;
    g_seg.sidedef = &g_side;
    g_seg.frontsector = &g_front;
    g_seg.backsector = &g_back;
    g_seg.angle = 0;            /* seg runs along +x */

    memset(&g_side, 0, sizeof(g_side));
    g_side.toptexture = 0;
    g_side.bottomtexture = 1;
    g_side.midtexture = 0;
    g_side.sector = &g_front;

    texturetranslation = calloc(4, sizeof(int));
    textureheight = calloc(4, sizeof(fixed_t));
    textureheight[1] = 128 << FRACBITS;
    texturewidthmask = calloc(4, sizeof(short));
    texturewidthmask[1] = 63;

    extralight = 0;
    fixedcolormap = 0;

    /* scalelight: identity LUTs */
    {
        int l, s;
        static lighttable_t identity[256];
        int j;
        for (j = 0; j < 256; j++)
            identity[j] = (lighttable_t)j;
        for (l = 0; l < LIGHTLEVELS; l++)
            for (s = 0; s < MAXLIGHTSCALE; s++)
                scalelight[l][s] = identity;
    }
}

static void dump_storewallrange(void)
{
    static const struct { int start, stop; } ranges[] =
    {
        { 100, 140 },
        { 50, 90 },
        { 150, 151 },
    };
    int i;

    printf("  \"storeWallRange\": [\n");

    build_level();

    for (i = 0; i < (int)(sizeof(ranges)/sizeof(ranges[0])); i++)
    {
        R_ClearClipSegs();
        R_ClearDrawSegs();
        R_ClearPlanes();

        curline = &g_seg;
        frontsector = &g_front;
        backsector = &g_back;
        rw_angle1 = R_PointToAngle2(viewx, viewy, g_v1.x, g_v1.y);

        floorplane = R_FindPlane(0, 1, 200);
        ceilingplane = R_FindPlane((128 << FRACBITS) - viewz, 1, 200);

        R_StoreWallRange(ranges[i].start, ranges[i].stop);

        printf("    {\"start\": %d, \"stop\": %d, ", ranges[i].start, ranges[i].stop);
        printf("\"scale1\": %d, \"scale2\": %d, \"scalestep\": %d, ",
               drawsegs[0].scale1, drawsegs[0].scale2, drawsegs[0].scalestep);
        printf("\"silhouette\": %d, \"bsil\": %d, \"tsil\": %d, ",
               drawsegs[0].silhouette, drawsegs[0].bsilheight, drawsegs[0].tsilheight);
        printf("\"floorclip\": [");
        {
            int x;
            for (x = ranges[i].start; x <= ranges[i].stop; x++)
                printf("%s%d", x > ranges[i].start ? "," : "", floorclip[x]);
        }
        printf("], \"ceilingclip\": [");
        {
            int x;
            for (x = ranges[i].start; x <= ranges[i].stop; x++)
                printf("%s%d", x > ranges[i].start ? "," : "", ceilingclip[x]);
        }
        printf("]}%s\n", (i + 1 < (int)(sizeof(ranges)/sizeof(ranges[0]))) ? "," : "");
    }
    printf("  ],\n");
}

/* ------------------------------------------------------------------ */
/* R_ProjectSprite vectors                                             */
/* ------------------------------------------------------------------ */
static void dump_projectsprite(void)
{
    static mobj_t thing;
    static spritedef_t sdef;
    static spriteframe_t frame;
    int i;

    struct { int x, y, z; angle_t angle; } cases[] =
    {
        { 0, 256 << FRACBITS, 0, ANG90 },        /* dead ahead */
        { 64 << FRACBITS, 256 << FRACBITS, 0, ANG90 },  /* off to the right */
        { -200 << FRACBITS, 128 << FRACBITS, 0, ANG45 }, /* far left */
    };

    printf("  \"projectSprite\": [\n");

    build_level();
    numsprites = 1;
    sprites = &sdef;
    sdef.numframes = 1;
    sdef.spriteframes = &frame;
    frame.rotate = false;
    frame.lump[0] = 0;
    frame.flip[0] = 0;

    firstspritelump = 0;
    spritewidth = malloc(sizeof(int));
    spriteoffset = malloc(sizeof(int));
    spritetopoffset = malloc(sizeof(int));
    spritewidth[0] = 4 << FRACBITS;
    spriteoffset[0] = 2 << FRACBITS;
    spritetopoffset[0] = 1 << FRACBITS;

    viewx = 0;
    viewy = 0;
    viewz = 41 << FRACBITS;
    viewangle = ANG90;
    viewsin = finesine[ANG90 >> ANGLETOFINESHIFT];
    viewcos = finecosine[ANG90 >> ANGLETOFINESHIFT];
    detailshift = 0;

    memset(&thing, 0, sizeof(thing));
    thing.sprite = 0;
    thing.frame = 0;
    thing.flags = 0;

    for (i = 0; i < 3; i++)
    {
        vissprite_t* vis;
        R_ClearSprites();
        thing.x = cases[i].x;
        thing.y = cases[i].y;
        thing.z = cases[i].z;
        thing.angle = cases[i].angle;

        /* R_AddSprites walks sec->thinglist; link the thing. */
        g_front.thinglist = &thing;
        g_front.validcount = 0;
        validcount = 1;
        R_AddSprites(&g_front);
        vis = vissprites;
        printf("    {\"case\": %d, \"count\": %d, \"x1\": %d, \"x2\": %d, "
               "\"scale\": %d, \"startfrac\": %d, \"xiscale\": %d, "
               "\"texturemid\": %d, \"patch\": %d}%s\n",
               i, vissprite_p - vissprites,
               vis->x1, vis->x2, vis->scale, vis->startfrac,
               vis->xiscale, vis->texturemid, vis->patch,
               (i + 1 < 3) ? "," : "");
    }
    printf("  ]\n");
}

int main (void)
{
    static byte identity_colormaps[34 * 256];
    int ci;

    V_Init();
    viewwidth = SCREENWIDTH;
    viewheight = SCREENHEIGHT;
    centerx = SCREENWIDTH / 2;
    centery = SCREENHEIGHT / 2;
    centerxfrac = centerx << FRACBITS;
    centeryfrac = centery << FRACBITS;
    projection = centerxfrac;
    for (ci = 0; ci < 34 * 256; ci++)
        identity_colormaps[ci] = (byte)ci;
    colormaps = identity_colormaps;
    R_InitBuffer(SCREENWIDTH, SCREENHEIGHT);
    R_InitTextureMapping();
    R_InitLightTables();

    printf("{\n");
    dump_clips();
    dump_planes();
    dump_storewallrange();
    dump_projectsprite();
    printf("}\n");
    return 0;
}
