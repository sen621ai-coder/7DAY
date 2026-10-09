using System;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json;

namespace PZAEC.FlyingSword
{
    public sealed class SwordMod : IModApi
    {
        public static string Root;
        public static SwordSettings Settings=new SwordSettings();
        public void InitMod(Mod mod)
        {
            Root=mod.Path;
            var path=Path.Combine(Root,"settings.json");
            if(File.Exists(path))Settings=JsonConvert.DeserializeObject<SwordSettings>(File.ReadAllText(path))??new SwordSettings();
            Settings.Effects=UnityEngine.Mathf.Clamp(Settings.Effects,0,2);
            var h=new Harmony("pzaec.juque.v1");
            try {SwordModel.Install(h);SwordRuntime.Install(h);SwordControls.Install(h);SwordInventory.Install(h);SwordSafety.Install(h);SwordProgression.Install(h);SwordPresentation.Install(h);SwordAttackVisuals.Install(h);Log.Out("[Juque] Installed 0.1.9: reserved sword, flight, native firearms, waves and return.");}
            catch(Exception e){h.UnpatchSelf();Log.Error("[Juque] Installation failed; all Juque hooks rolled back: "+e);throw;}
        }
    }
}
