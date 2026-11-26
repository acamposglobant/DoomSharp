/*
 * Characterization harness for linuxdoom-1.10 z_zone.c.
 *
 * Compiles the UNMODIFIED z_zone.c with a small stubbed zone and runs a
 * scripted allocation sequence, dumping block states and fatal flags as
 * JSON. The C# ZoneMemory tests assert identical outcomes.
 *
 * Build (Docker):
 *   gcc -O0 -DNORMALUNIX -DLINUX -I Legacy/linuxdoom-1.10 -o zonedump \
 *       DoomSharp/tools/charact/zonedump.c Legacy/linuxdoom-1.10/z_zone.c
 *
 * Run: ./zonedump > zone_vectors.json
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <setjmp.h>
#include "z_zone.h"

typedef unsigned char byte;

/* memzone_t is private to z_zone.c; mirror its layout (z_zone.c:46-56). */
typedef struct
{
    int size;
    memblock_t blocklist;
    memblock_t* rover;
} memzone_t;

extern void* mainzone;  /* defined in z_zone.c, real type memzone_t* */

/* ---- I_Error / I_ZoneBase stubs ---- */

jmp_buf g_fatal_jmp;
int g_fatal_active = 0;

void I_Error (char *error, ...)
{
    if (g_fatal_active)
        longjmp(g_fatal_jmp, 1);
    exit(42);
}

byte *I_ZoneBase (int *size)
{
    /* Small zone so exhaustion is reachable in the script. */
    *size = 4096;
    return malloc(*size);
}

/* doomdef.h pull-ins avoided: provide the two macros z_zone.c needs. */
/* z_zone.c includes doomdef.h only for nothing critical on NORMALUNIX. */

static void dump_zone(const char* label)
{
    memblock_t* block;
    memzone_t* zone = (memzone_t*)mainzone;
    printf("  { \"op\": \"dump\", \"label\": \"%s\", \"blocks\": [", label);
    int first = 1;
    for (block = zone->blocklist.next;
         block != &zone->blocklist;
         block = block->next)
    {
        if (!first) printf(", ");
        first = 0;
        printf("{\"size\":%d,\"tag\":%d,\"inuse\":%s}",
               block->size, block->tag, block->user ? "true" : "false");
    }
    printf("] },\n");
}

static int run_fatal(void (*fn)(void))
{
    g_fatal_active = 1;
    if (setjmp(g_fatal_jmp) == 0)
    {
        fn();
        g_fatal_active = 0;
        return 0;
    }
    g_fatal_active = 0;
    return 1;
}

static int g_last_alloc_size;

static void fatal_alloc(void)
{
    Z_Malloc(g_last_alloc_size, PU_STATIC, 0);
}

static void fatal_purgable_no_owner(void)
{
    Z_Malloc(64, PU_CACHE, 0);
}

static void fatal_double_free(void)
{
    void* p = Z_Malloc(64, PU_STATIC, 0);
    Z_Free(p);
    Z_Free(p);
}

static void fatal_change_tag_freed(void)
{
    void* p = Z_Malloc(64, PU_STATIC, 0);
    Z_Free(p);
    Z_ChangeTag2(p, PU_CACHE);
}

static void fatal_change_tag_no_owner(void)
{
    void* p = Z_Malloc(64, PU_STATIC, 0);
    Z_ChangeTag2(p, PU_CACHE);
}

int main(void)
{
    void *a, *b, *c, *d;

    printf("[\n");

    Z_Init();  /* the engine calls this from d_main; harness must too */

    /* --- basic allocation and tags --- */
    b = Z_Malloc(200, PU_LEVEL, 0);
    {
        void** holder = malloc(sizeof(void*));
        *holder = NULL;
        void* c = Z_Malloc(300, PU_CACHE, holder);
        (void)c;
    }
    /* Owned STATIC block so the PU_CACHE retag below is legal
     * (z_zone.c:440 fatals on unowned purgable retags). */
    {
        void** aholder = malloc(sizeof(void*));
        *aholder = NULL;
        a = Z_Malloc(100, PU_STATIC, aholder);
    }
    printf("  { \"op\": \"note\", \"v\": \"3 blocks: 100/STATIC, 200/LEVEL, 300/CACHE\" },\n");
    dump_zone("after_alloc");

    /* --- Z_FreeTags(PU_LEVEL, PU_PURGELEVEL-1): frees only LEVEL --- */
    Z_FreeTags(PU_LEVEL, PU_PURGELEVEL-1);
    dump_zone("after_freetags_level");

    /* --- Z_FreeTags(PU_LEVEL, MAXINT): frees CACHE too --- */
    Z_FreeTags(PU_LEVEL, 0x7FFFFFFF);
    dump_zone("after_freetags_maxint");

    /* --- Z_ChangeTag2: demote STATIC to CACHE --- */
    Z_ChangeTag2(a, PU_CACHE);
    dump_zone("after_changetag");

    /* --- Z_FreeMemory accounting --- */
    printf("  { \"op\": \"freemem\", \"v\": %d },\n", Z_FreeMemory());

    /* --- exhaustion: a single allocation larger than the zone is fatal --- */
    g_last_alloc_size = 1024 * 1024; /* zone is only 4096 bytes */
    printf("  { \"op\": \"exhaust\", \"fatal\": %s },\n",
           run_fatal(fatal_alloc) ? "true" : "false");

    /* --- fatal: purgable without owner --- */
    printf("  { \"op\": \"purgable_no_owner\", \"fatal\": %s },\n",
           run_fatal(fatal_purgable_no_owner) ? "true" : "false");

    /* --- fatal: double free --- */
    printf("  { \"op\": \"double_free\", \"fatal\": %s },\n",
           run_fatal(fatal_double_free) ? "true" : "false");

    /* --- fatal: ChangeTag on freed block --- */
    printf("  { \"op\": \"changetag_freed\", \"fatal\": %s },\n",
           run_fatal(fatal_change_tag_freed) ? "true" : "false");

    /* --- fatal: ChangeTag to purgable without owner --- */
    printf("  { \"op\": \"changetag_no_owner\", \"fatal\": %s },\n",
           run_fatal(fatal_change_tag_no_owner) ? "true" : "false");

    printf("  { \"op\": \"end\" }\n");
    printf("]\n");
    return 0;
}
