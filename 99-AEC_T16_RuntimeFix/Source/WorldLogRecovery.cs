using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    internal static class WorldLogRecovery
    {
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(EntityLootContainer), "OnUpdateEntity"),
                prefix: new HarmonyMethod(typeof(WorldLogRecovery), nameof(BeforeLootUpdate)));
            foreach (var method in typeof(SignDataManager).GetMethods())
            {
                var args = method.GetParameters();
                if (method.Name == "TryApplyRenderingData" && args.Length == 5 && args[2].ParameterType == typeof(List<SignRenderer>))
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(WorldLogRecovery), nameof(BeforeSignRendering)));
            }
            T16RuntimeFixMod.SafeLog("[AEC-World-Fix] Fallen loot recovery and stale sign reference filtering active.");
        }

        public static void BeforeSignRendering(ref List<SignRenderer> __2)
        {
            if (__2 == null) { __2 = new List<SignRenderer>(); return; }
            // Filter the call's view, not the owner's list: preserve its indices
            // and allow renderers to be rebound after a chunk is reloaded.
            if (!__2.Exists(r => r == null || r.Renderer == null)) return;
            __2 = __2.FindAll(r => r != null && r.Renderer != null);
        }

        public static bool BeforeLootUpdate(EntityLootContainer __instance)
        {
            if (__instance == null || __instance.world == null || __instance.isEntityRemote ||
                !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer || __instance.position.y >= -32f)
                return true;
            var old = __instance.position;
            if (float.IsNaN(old.x) || float.IsNaN(old.y) || float.IsNaN(old.z) ||
                float.IsInfinity(old.x) || float.IsInfinity(old.y) || float.IsInfinity(old.z)) return false;
            var world = __instance.world;
            if (world.GetChunkFromWorldPos(Mathf.FloorToInt(old.x), Mathf.FloorToInt(old.z)) == null)
                return false; // Wait for real terrain; do not teleport to an unloaded column.
            return RecoverAtHeight(__instance, world.GetHeightAt(old.x, old.z));
        }

        internal static bool RecoverAtHeight(EntityLootContainer entity, float height)
        {
            var old = entity.position;
            if (old.y >= -32f) return true;
            if (float.IsNaN(height) || height < 0 || height > 255) return false;
            entity.motion = entity.physicsVel = entity.physicsAngVel = Vector3.zero;
            ResetVelocity(entity.itemRB);
            if (entity.physicsRB != entity.itemRB) ResetVelocity(entity.physicsRB);
            var target = new Vector3(old.x, height + 2f, old.z);
            entity.SetPosition(target, true);
            // EntityItem.updateTransform reads the root Transform back into
            // Entity.position while it is physics master. Entity.SetPosition
            // alone does not move that root, so the next frame undoes recovery.
            var scenePosition = target - Origin.position;
            entity.transform.position = scenePosition;
            if (entity.itemRB != null) entity.itemRB.position = scenePosition;
            T16RuntimeFixMod.SafeLog("[AEC-World-Fix] Recovered fallen loot entity=" + entity.entityId +
                " from " + old + " to " + entity.position + "; bag and ownership preserved.");
            return true;
        }

        static void ResetVelocity(Rigidbody body)
        {
            if (body == null || body.isKinematic) return;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
