using UnityEngine;
namespace PZAEC.Mecha
{
    public sealed class MechaRenderPart:MonoBehaviour
    {
        public string Role;
        public Material OwnedMaterial;
        void OnDestroy(){if(OwnedMaterial!=null)Destroy(OwnedMaterial);}
        public bool FirstPersonVisible {get{return Role!=null&&(Role.StartsWith("Arm")||Role.StartsWith("Leg")||Role.StartsWith("Sword")||Role.StartsWith("Weapon")||Role=="Shield");}}
    }
}
