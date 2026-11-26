/*
 * Characterization harness for linuxdoom-1.10 w_wad.c.
 *
 * Builds synthetic WAD fixtures (same bytes as the C# test fixtures),
 * runs the ORIGINAL w_wad.c code over them, and dumps the resulting
 * directory state and lookup results as JSON.
 *
 * Build (from repo root, Linux or Docker):
 *   gcc -O0 -DNORMALUNIX -DLINUX -I Legacy/linuxdoom-1.10 -o waddump \
 *       DoomSharp/tools/charact/waddump.c \
 *       Legacy/linuxdoom-1.10/w_wad.c
 *
 * Run: ./waddump <fixture-dir> > vectors.json
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <setjmp.h>

#include "w_wad.h"

/* ---- I_Error / z_zone stubs so the original w_wad.c links ---- */

jmp_buf g_fatal_jmp;
int g_fatal_active = 0;

void I_Error (char *error, ...)
{
    if (g_fatal_active)
        longjmp(g_fatal_jmp, 1);
    exit(42);
}

/* z_zone stubs: W_CacheLumpNum uses Z_Malloc with a user pointer. */
void *Z_Malloc (int size, int tag, void **user)
{
    void *p = malloc(size);
    if (user) *user = p;
    return p;
}

void Z_Free (void *p) { free(p); }
void Z_ChangeTag2 (void *p, int tag) { (void)p; (void)tag; }

/* i_system stub */
int I_GetTime (void) { return 0; }

/* m_swap no-ops on little-endian x86 */
/* w_wad.c includes m_swap.h which defines macros; nothing to stub. */

static jmp_buf g_check_jmp;
static int g_check_active = 0;

static void emit_lump(int i)
{
    lumpinfo_t *l = &lumpinfo[i];
    char name[9];
    int k;
    for (k = 0; k < 8; k++) {
        char c = l->name[k];
        if (c >= 'a' && c <= 'z') c = c - 32;
        name[k] = c;
    }
    name[8] = 0;
    /* strip trailing NULs for display */
    for (k = 7; k >= 0 && name[k] == 0; k--) name[k] = 0;
    printf("  { \"i\": %d, \"name\": \"%s\", \"pos\": %d, \"size\": %d },\n",
           i, name, l->position, l->size);
}

static void emit_lookup(const char* op, const char* name, int r)
{
    printf("  { \"op\": \"%s\", \"name\": \"%s\", \"r\": %d },\n", op, name, r);
}

int main(int argc, char** argv)
{
    int i;
    if (argc < 2) {
        fprintf(stderr, "usage: waddump <fixture-dir>\n");
        return 1;
    }
    const char* dir = argv[1];

    /* Fixture file list — mirrors the C# test scenario. */
    char* files[4];
    files[0] = malloc(strlen(dir) + 32);
    sprintf(files[0], "%s/test_iwad.wad", dir);
    files[1] = malloc(strlen(dir) + 32);
    sprintf(files[1], "%s/test_pwad.wad", dir);
    files[2] = malloc(strlen(dir) + 32);
    sprintf(files[2], "%s/DEHACKED", dir);      /* non-wad single lump */
    files[3] = malloc(strlen(dir) + 32);
    sprintf(files[3], "%s/missing.wad", dir);   /* skipped silently */
    files[4] = NULL;

    printf("[\n");

    W_InitMultipleFiles(files);

    printf("  { \"op\": \"numlumps\", \"v\": %d },\n", numlumps);
    for (i = 0; i < numlumps; i++) {
        emit_lump(i);
    }

    /* Lookup semantics */
    emit_lookup("check", "E1M1",   W_CheckNumForName("E1M1"));
    emit_lookup("check", "MAP01",  W_CheckNumForName("MAP01"));
    emit_lookup("check", "map01",  W_CheckNumForName("map01"));   /* case */
    emit_lookup("check", "DEHACK", W_CheckNumForName("DEHACK"));  /* 8-char */
    emit_lookup("check", "MISSING", W_CheckNumForName("MISSING"));
    emit_lookup("get",   "E1M1",   W_GetNumForName("E1M1"));

    /* Lengths and data */
    {
        int e1m1 = W_GetNumForName("E1M1");
        printf("  { \"op\": \"length\", \"name\": \"E1M1\", \"v\": %d },\n",
               W_LumpLength(e1m1));
        {
            unsigned char buf[64];
            memset(buf, 0xEE, sizeof(buf));
            W_ReadLump(e1m1, buf);
            printf("  { \"op\": \"read\", \"name\": \"E1M1\", \"data\": [");
            for (i = 0; i < W_LumpLength(e1m1); i++)
                printf("%d%s", buf[i], i < W_LumpLength(e1m1)-1 ? "," : "");
            printf("] },\n");
        }
    }

    /* Duplicate precedence: MAP01 exists in both IWAD and PWAD */
    {
        int map01 = W_GetNumForName("MAP01");
        unsigned char buf[16];
        memset(buf, 0, sizeof(buf));
        W_ReadLump(map01, buf);
        printf("  { \"op\": \"dup_precedence\", \"index\": %d, \"data\": [", map01);
        for (i = 0; i < 4; i++)
            printf("%d%s", buf[i], i < 3 ? "," : "");
        printf("] },\n");
    }

    /* Out-of-range lump index -> I_Error (fatal) */
    {
        g_check_active = 1;
        g_fatal_active = 1;
        if (setjmp(g_fatal_jmp) == 0) {
            W_ReadLump(numlumps + 5, malloc(16));
            printf("  { \"op\": \"oor\", \"fatal\": false },\n");
        } else {
            printf("  { \"op\": \"oor\", \"fatal\": true },\n");
        }
        g_check_active = 0;
        g_fatal_active = 0;
    }

    /* Missing lump name -> I_Error (fatal) */
    {
        g_fatal_active = 1;
        if (setjmp(g_fatal_jmp) == 0) {
            W_GetNumForName("NOEXIST");
            printf("  { \"op\": \"get_missing\", \"fatal\": false },\n");
        } else {
            printf("  { \"op\": \"get\", \"name\": \"NOEXIST\", \"fatal\": true },\n");
        }
        g_fatal_active = 0;
    }

    printf("  { \"op\": \"end\" }\n");
    printf("]\n");
    return 0;
}
