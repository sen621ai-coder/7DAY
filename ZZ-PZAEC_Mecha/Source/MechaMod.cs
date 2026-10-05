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
                EngineSilence.Install(harmony);
                Weapons.Install(harmony);
                Optics.Install(harmony);
                Locomotion.Install(harmony);
                GroundSupport.Install(harmony);
                MechaArmor.Install(harmony);
                Boarding.Install(harmony);
                Log.Out("[Mecha] Combat Robot biped installed (runtime rig + IK gait, FPV optics, beam + blades + missiles).");
            }
            catch (Exception ex)
            {
                Log.Error("[Mecha] Initialization failed: " + ex.GetBaseException().Message);
            }
        }
    }
}
