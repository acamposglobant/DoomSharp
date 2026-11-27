/*
 * Characterization harness for linuxdoom-1.10 info.c.
 *
 * Includes the ORIGINAL info.c (states[], sprnames[], mobjinfo[]) and
 * dumps the tables as JSON for the managed InfoTables.g.cs generator.
 * Action function pointers are resolved to their symbolic names by
 * comparing against the addresses of stubbed functions.
 *
 * Build (from repo root, Linux or Docker):
 *   gcc -O0 -DNORMALUNIX -DLINUX -I Legacy/linuxdoom-1.10 -o infodump \
 *       DoomSharp/tools/charact/infodump.c \
 *       Legacy/linuxdoom-1.10/info.c
 *
 * Run: ./infodump > info_vectors.json
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "info.h"

/* ---- stubs so info.c links without the rest of the engine ---- */

void I_Error (char *error, ...) { exit(42); }

/*
 * One no-op body per action function referenced by info.c, declared
 * through a macro so the name list is single-sourced: the same macro
 * builds (a) the stub definitions and (b) the address->name lookup
 * table used to serialize st->action.acp1 as a string.
 */
#define ACTION_LIST \
    X(A_Light0) X(A_WeaponReady) X(A_Lower) X(A_Raise) \
    X(A_Punch) X(A_ReFire) X(A_FirePistol) X(A_Light1) \
    X(A_FireShotgun) X(A_FireShotgun2) X(A_Light2) X(A_LoadShotgun2) \
    X(A_CockGun) X(A_FireCGun) X(A_GunFlash) X(A_FireMissile) \
    X(A_Saw) X(A_FirePlasma) X(A_Playsound) X(A_Tic) \
    X(A_FireBFG) X(A_BFGSpray) X(A_Explode) X(A_Pain) \
    X(A_PlayerScream) X(A_Fall) X(A_XScream) X(A_Look) \
    X(A_Chase) X(A_FaceTarget) X(A_PosAttack) X(A_Scream) \
    X(A_SPosAttack) X(A_VileChase) X(A_VileStart) X(A_VileTarget) \
    X(A_VileAttack) X(A_StartFire) X(A_Fire) X(A_FireCrackle) \
    X(A_Tracer) X(A_SkelWhoosh) X(A_SkelFist) X(A_SkelMissile) \
    X(A_FatRaise) X(A_FatAttack1) X(A_FatAttack2) X(A_FatAttack3) \
    X(A_BossDeath) X(A_CPosAttack) X(A_CPosRefire) X(A_TroopAttack) \
    X(A_SargAttack) X(A_HeadAttack) X(A_BruisAttack) X(A_SkullAttack) \
    X(A_Metal) X(A_SpidRefire) X(A_BabyMetal) X(A_BspiAttack) \
    X(A_Hoof) X(A_CyberAttack) X(A_PainAttack) X(A_PainDie) \
    X(A_KeenDie) X(A_BrainPain) X(A_BrainAwake) X(A_BrainDie) \
    X(A_BrainSpit) X(A_SpawnSound) X(A_BrainExplode) X(A_Detonate) \
    X(A_Mushroom) X(A_CompleteAttack) X(A_FireOldBfg) X(A_NoBlocking) \
    X(A_Die) X(A_MissileAttack) X(A_PainShootSkull) \
    X(A_CheckReload) X(A_OpenShotgun2) X(A_CloseShotgun2) \
    X(A_BFGsound) X(A_BrainScream) X(A_SpawnFly)

#define X(fn) void fn(void) {}
ACTION_LIST
#undef X

static const char *action_name (actionf_p1 fn)
{
#define X(fn) if (st->action.acp1 == fn) return #fn;
    /* expanded inline below with st in scope */
#undef X
    return NULL;
}

int main (void)
{
    int i;

    printf("{\n");
    printf("  \"numSprites\": %d,\n", NUMSPRITES);
    printf("  \"numStates\": %d,\n", NUMSTATES);
    printf("  \"numMobjTypes\": %d,\n", NUMMOBJTYPES);

    printf("  \"sprnames\": [");
    for (i = 0; i < NUMSPRITES; i++)
        printf("%s\"%s\"", i ? ", " : "", sprnames[i]);
    printf("],\n");

    printf("  \"states\": [\n");
    for (i = 0; i < NUMSTATES; i++)
    {
        const state_t *st = &states[i];
        const char *action = "null";

        /* resolve the action pointer to its name */
#define X(fn) if (st->action.acp1 == (actionf_p1)fn) { action = #fn; goto done; }
        ACTION_LIST
#undef X
        done:
        printf("    {\"sprite\":%d,\"frame\":%d,\"tics\":%d,\"action\":\"%s\",\"next\":%d}%s\n",
               st->sprite, st->frame, st->tics, action, st->nextstate,
               i + 1 < NUMSTATES ? "," : "");
    }
    printf("  ],\n");

    printf("  \"mobjinfo\": [\n");
    for (i = 0; i < NUMMOBJTYPES; i++)
    {
        const mobjinfo_t *mi = &mobjinfo[i];
        printf("    {\"doomednum\":%d,\"spawnstate\":%d,\"spawnhealth\":%d,"
               "\"seestate\":%d,\"seesound\":%d,\"reactiontime\":%d,"
               "\"attacksound\":%d,\"painstate\":%d,\"painchance\":%d,"
               "\"painsound\":%d,\"meleestate\":%d,\"missilestate\":%d,"
               "\"deathstate\":%d,\"xdeathstate\":%d,\"deathsound\":%d,"
               "\"speed\":%d,\"radius\":%d,\"height\":%d,\"mass\":%d,"
               "\"damage\":%d,\"activesound\":%d,\"flags\":%d,"
               "\"raisestate\":%d}%s\n",
               mi->doomednum, mi->spawnstate, mi->spawnhealth,
               mi->seestate, mi->seesound, mi->reactiontime,
               mi->attacksound, mi->painstate, mi->painchance,
               mi->painsound, mi->meleestate, mi->missilestate,
               mi->deathstate, mi->xdeathstate, mi->deathsound,
               mi->speed, mi->radius, mi->height, mi->mass,
               mi->damage, mi->activesound, mi->flags, mi->raisestate,
               i + 1 < NUMMOBJTYPES ? "," : "");
    }
    printf("  ]\n");
    printf("}\n");
    return 0;
}
