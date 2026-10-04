using UnityEngine;
namespace PZAEC.Mecha
{
    public sealed class MechaRenderPart:MonoBehaviour
    {
        public string Role;
        public bool FirstPersonVisible {get{return Role!=null&&(Role.StartsWith("Arm")||Role.StartsWith("Leg")||Role.StartsWith("Sword")||Role=="Shield");}}
    }
}
