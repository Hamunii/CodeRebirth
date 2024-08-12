using System;
using System.Collections.Generic;
using CodeRebirth.src.Util;
using GameNetcodeStuff;
using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Unity.Netcode;
using UnityEngine;

namespace CodeRebirth.src.Patches;
[HarmonyPatch(typeof(GameNetcodeStuff.PlayerControllerB))]
static class PlayerControllerBPatch {
    [HarmonyPatch(nameof(GameNetcodeStuff.PlayerControllerB.PlayerHitGroundEffects)), HarmonyPrefix]
    public static bool PlayerHitGroundEffects(PlayerControllerB __instance) {
        if (CodeRebirthPlayerManager.dataForPlayer.ContainsKey(__instance) && __instance.GetCRPlayerData().flung) {
            __instance.GetCRPlayerData().flung = false;
            __instance.playerRigidbody.isKinematic = true;
        }
        return true;
    }

	[HarmonyPatch(nameof(GameNetcodeStuff.PlayerControllerB.PlayFootstepSound)), HarmonyPrefix]
	public static bool PlayFootstepSound(PlayerControllerB __instance) {
        if (CodeRebirthPlayerManager.dataForPlayer.ContainsKey(__instance) && __instance.GetCRPlayerData().ridingHoverboard) {
            return false;
        } else {
            return true;
        }
	}

    [HarmonyPatch(nameof(GameNetcodeStuff.PlayerControllerB.Awake)), HarmonyPostfix]
    public static void Awake(PlayerControllerB __instance) {
        if (!CodeRebirthPlayerManager.dataForPlayer.ContainsKey(__instance)) {
            CodeRebirthPlayerManager.dataForPlayer.Add(__instance, new CRPlayerData());
            List<Collider> colliders = new List<Collider>(__instance.GetComponentsInChildren<Collider>());
            __instance.GetCRPlayerData().playerColliders = colliders;
        }
    }

    [HarmonyPatch(nameof(GameNetcodeStuff.PlayerControllerB.Update)), HarmonyPrefix]
    public static void Update(PlayerControllerB __instance) {
        if (GameNetworkManager.Instance.localPlayerController == null) return;
        if (__instance.GetCRPlayerData().playerOverrideController != null) return;
        Plugin.ExtendedLogging($"[ILHook:PlayerControllerB.Update] Setting playerOverrideController to {__instance.playerBodyAnimator.runtimeAnimatorController}");
        __instance.GetCRPlayerData().playerOverrideController = new AnimatorOverrideController(__instance.playerBodyAnimator.runtimeAnimatorController);
        __instance.playerBodyAnimator.runtimeAnimatorController = __instance.GetCRPlayerData().playerOverrideController; 
    }

    public static void Init() {
        IL.GameNetcodeStuff.PlayerControllerB.CheckConditionsForSinkingInQuicksand += PlayerControllerB_CheckConditionsForSinkingInQuicksand;
        IL.GameNetcodeStuff.PlayerControllerB.DiscardHeldObject += ILHookAllowParentingOnEnemy_PlayerControllerB_DiscardHeldObject;
    }

    private static void PlayerControllerB_CheckConditionsForSinkingInQuicksand(ILContext il)
    {
        ILCursor c = new(il);

        if (!c.TryGotoNext(MoveType.After,
            x => x.MatchLdarg(0),
            x => x.MatchLdfld<PlayerControllerB>(nameof(PlayerControllerB.thisController)),
            x => x.MatchCallvirt<CharacterController>("get_" + nameof(CharacterController.isGrounded)),
            x => x.MatchBrtrue(out _)
        ))
        {
            Plugin.Logger.LogError("[ILHook:PlayerControllerB.CheckConditionsForSinkingInQuicksand] Couldn't find thisController.isGrounded check!");
            return;
        }

        c.Index -= 1;
        c.Emit(OpCodes.Ldarg_0);
        c.EmitDelegate((bool isGrounded, PlayerControllerB self) =>
        {
            // Pretend we are grounded when in a water tornado so we can drown.
            return isGrounded || self.GetCRPlayerData().Water;
        });
    }

    /// <summary>
    /// Modifies item dropping code to attempt to find a NetworkObject from collided object's parents
    /// in case the collided object itself doesn't have a NetworkObject, if the following is true:<br/>
    /// - The collided GameObject's name ends with <c>"_RedirectToRootNetworkObject"</c><br/>
    /// <br/>
    /// This is necessary for parenting items to enemies, because the raycast that collides with an object
    /// ignores the enemies layer.
    /// </summary>
    private static void ILHookAllowParentingOnEnemy_PlayerControllerB_DiscardHeldObject(ILContext il)
    {
        ILCursor c = new(il);

        if (!c.TryGotoNext(MoveType.Before,
            x => x.MatchLdloc(0),                                   // load transform to stack
            x => x.MatchCallvirt<Component>(nameof(Component.GetComponent)), // var component = transform.GetComponent<NetworkObject>();
            x => x.MatchStloc(3)
            // Context:
            // x => x.MatchLdloc(3),                                // if (component != null)
            // x => x.MatchLdnull(),
            // x => x.MatchCall<UnityEngine.Object>("op_Inequality"),
            // x => x.MatchBrfalse(out _)
        ))
        {
            // Couldn't match, let's figure out if we should worry
            if (c.TryGotoNext(MoveType.Before,
                x => x.MatchLdloc(0),
                x => x.MatchLdcI4(out _),   // Matching against EmitDelegate
                x => x.MatchCall("MonoMod.Cil.RuntimeILReferenceBag/InnerBag`1<System.Func`2<UnityEngine.Transform,UnityEngine.Transform>>", "Get"), // There exists some bug probably that typeof doesn't work here because of generics?
                x => x.MatchCall(typeof(RuntimeILReferenceBag.FastDelegateInvokers), "Invoke"),
                x => x.MatchCallvirt<Component>(nameof(Component.GetComponent)),
                x => x.MatchStloc(3)
            ))
                Plugin.Logger.LogInfo($"[{nameof(ILHookAllowParentingOnEnemy_PlayerControllerB_DiscardHeldObject)}] This ILHook has most likely already been applied by another mod.");
            else
                Plugin.Logger.LogError($"[{nameof(ILHookAllowParentingOnEnemy_PlayerControllerB_DiscardHeldObject)}] Could not match IL!!");
            return;
        }

        c.Index += 1;
        c.EmitDelegate<Func<Transform, Transform>>(transform =>
        {
            if (!transform.name.EndsWith("_RedirectToRootNetworkObject"))
                return transform;

            return TryFindRoot(transform) ?? transform;
        });

    }
    public static Transform? TryFindRoot(Transform child)
    {
        // iterate upwards until we find a NetworkObject
        Transform current = child;
        while (current != null)
        {
            if (current.GetComponent<NetworkObject>() != null)
            {
                return current;
            }
            current = current.transform.parent;
        }
        return null;
    }
}