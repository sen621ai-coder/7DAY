using System;
using System.IO;
using HarmonyLib;

namespace PZAEC.Mecha
{
    public sealed class MechaMod : IModApi
    {
        public void InitMod(Mod modInstance)
        {
            try
            {
                Model.Path = Path.Combine(modInstance.Path, "Resources");
                var harmony = new Harmony("pzaec.mecha.buster");
                Model.Install(harmony);
                Weapons.Install(harmony);
                Optics.Install(harmony);
                Locomotion.Install(harmony);
                MechaArmor.Install(harmony);
                Log.Out("[Mecha] Buster drone walker installed (GLB runtime model, FPV optics, beam + guided missiles).");
            }
            catch (Exception ex)
            {
                Log.Error("[Mecha] Initialization failed: " + ex.GetBaseException().Message);
            }
        }
    }
}
