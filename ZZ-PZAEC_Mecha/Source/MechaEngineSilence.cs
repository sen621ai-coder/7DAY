using HarmonyLib;
using UnityEngine;
namespace PZAEC.Mecha
{
    // Gate the native engine only; RobotAudio's action/weapon sources bypass it.
    public static class EngineSilence
    {
        public static int Blocked {get;private set;}
        public static void Install(Harmony harmony)
        {
            foreach(var name in new[]{"playSound","playSoundLoop"})
                harmony.Patch(AccessTools.Method(typeof(VPEngine),name),prefix:new HarmonyMethod(typeof(EngineSilence),nameof(Allow)));
            harmony.Patch(AccessTools.Method(typeof(VPEngine),"playAccelDecelSound"),prefix:new HarmonyMethod(typeof(EngineSilence),nameof(AllowAccel)));
            harmony.Patch(AccessTools.Method(typeof(VPEngine),"updateEngineSounds"),prefix:new HarmonyMethod(typeof(EngineSilence),nameof(Update)));
        }
        static bool IsMecha(VPEngine engine){return engine!=null&&engine.vehicle!=null&&Weapons.IsMecha(engine.vehicle.entity);}
        static bool Allow(VPEngine __instance){if(!IsMecha(__instance))return true;Blocked++;return false;}
        // Null is a stop command inside stopEngineSounds, not a request to play.
        static bool AllowAccel(VPEngine __instance,string __0){return string.IsNullOrEmpty(__0)||Allow(__instance);}
        static bool Update(VPEngine __instance)
        {
            if(!IsMecha(__instance))return true;
            // Release any existing native loops as well, including on reload.
            __instance.stopEngineSounds();Blocked++;return false;
        }
        public static void SilenceInheritedSources(Transform root)
        {
            // Called on the cloned truck prefab BEFORE mechanical audio exists.
            foreach(var source in root.GetComponentsInChildren<AudioSource>(true))
            {source.Stop();source.playOnAwake=false;source.loop=false;source.clip=null;source.volume=0;source.mute=true;source.enabled=false;}
        }
    }
}
