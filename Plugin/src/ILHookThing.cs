using System;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace CodeRebirth.src;

class ILHookThing
{
    internal static void Init()
    {
        IL.RoundManager.SpawnOutsideHazards += ILHook_RoundManager_SpawnOutsideHazards;
    }

    private static void ILHook_RoundManager_SpawnOutsideHazards(ILContext il)
    {
        Plugin.Logger.LogDebug("Original IL: " + il.ToString());


        ILCursor c = new(il);
        
        // Make sure we are at the second for loop which uses `spawnDenialPoints`

        // IL_02e8: ldfld class [UnityEngine.CoreModule]UnityEngine.GameObject[] RoundManager::spawnDenialPoints /* 04000AEB */
        // IL_02ed: ldloc.s 18      // Also known as int n, used in for loop

        if (!c.TryGotoNext(MoveType.After,
            x => x.MatchLdfld<RoundManager>(nameof(RoundManager.spawnDenialPoints)),
            x => x.MatchLdloc(18)
        ))
        {
            Plugin.Logger.LogError($"[{nameof(ILHook_RoundManager_SpawnOutsideHazards)}] Could not match first predicates!");
            return;
        }

        // Now we can find the logic for the float argument for the Vector3.Distance: 

        // IL_0300: ldarg.0
        // IL_0301: ldfld class SelectableLevel RoundManager::currentLevel /* 04000B04 */
        // IL_0306: ldfld class SpawnableOutsideObjectWithRarity[] SelectableLevel::spawnableOutsideObjects /* 040010E1 */
        // IL_030b: ldloc.s 9
        // IL_030d: ldelem.ref
        // IL_030e: ldfld class SpawnableOutsideObject SpawnableOutsideObjectWithRarity::spawnableObject /* 040010FB */
        // IL_0313: ldfld int32 SpawnableOutsideObject::objectWidth /* 04001106 */
        // IL_0318: conv.r4

        if (!c.TryGotoNext(MoveType.Before,
            x => x.MatchLdarg(0),
            x => x.MatchLdfld<RoundManager>(nameof(RoundManager.currentLevel)),
            x => x.MatchLdfld<SelectableLevel>(nameof(SelectableLevel.spawnableOutsideObjects)),
            x => x.MatchLdloc(9),
            x => x.MatchLdelemRef(),
            x => x.MatchLdfld<SpawnableOutsideObjectWithRarity>(nameof(SpawnableOutsideObjectWithRarity.spawnableObject)),
            x => x.MatchLdfld<SpawnableOutsideObject>(nameof(SpawnableOutsideObject.objectWidth)),
            x => x.MatchConvR4(),
            x => x.MatchLdcR4(6),
            x => x.MatchAdd()
        ))
        {
            Plugin.Logger.LogError($"[{nameof(ILHook_RoundManager_SpawnOutsideHazards)}] Could not match second predicates!");
            return;
        }

        // Find the end of the previous match
        ILCursor cAtEnd = new ILCursor(c).GotoNext(MoveType.After,
            x => x.MatchLdcR4(6),
            x => x.MatchAdd());

        ILLabel label_original_logic = il.DefineLabel(c.Next);
        ILLabel label_past_original_logic = il.DefineLabel(cAtEnd.Next);

        //      if !thing
        //          goto original_logic;
        //
        //      custom thing;
        //      goto past_original_logic;
        //
        //  original_logic:
        //      original thing;
        //
        //  past_original_logic:

        c.Emit(OpCodes.Ldarg_0);
        c.Emit(OpCodes.Ldloc_S, (byte)18); // int n, used in for loop
        c.EmitDelegate<Func<RoundManager, int, bool>>((self, n) =>
        {
            return self.spawnDenialPoints[n].gameObject.name.Contains("_XuPatch");
        });

        // If the previous boolean is false, jump over our custom logic
        c.Emit(OpCodes.Brfalse_S, label_original_logic);

        // We emit our custom logic here
        c.Emit(OpCodes.Ldarg_0);
        c.Emit(OpCodes.Ldloc_S, (byte)18); // int n, used in for loop
        c.EmitDelegate<Func<RoundManager, int, float>>((self, n) =>
        {
            Plugin.Logger.LogInfo("Ran custom logic!");
            return self.spawnDenialPoints[n].transform.localScale.x;
        });

        c.Emit(OpCodes.Br_S, label_past_original_logic);

        Plugin.Logger.LogDebug("Modified IL: " + il.ToString());
    }
}