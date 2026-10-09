using System;
using UnityEngine;

namespace PZAEC.FlyingSword
{
    public static class SwordRules
    {
        public const string Prefix="meleePZAECJuqueT", VehicleName="vehiclePZAECJuque", Crystal="resourcePZAECSpiritStone";
        public const string IdKey="PZAECJuqueId", DeployedKey="PZAECJuqueEntity", EnergyKey="PZAECJuqueEnergy", FallKey="$PZAECJuqueFall";
        public static readonly int[] Melee={1000000,2500000,5500000,12000000};
        public static readonly int[] Wave={750000,1800000,4000000,9000000}, Charged={4500000,11000000,24000000,55000000};
        public static readonly float[] Capacity={1000,1250,1500,1800};
        public static int Tier(ItemValue item) {return item==null||item.IsEmpty()?-1:Tier(item.ItemClass.GetItemName());}
        public static int Tier(string name) {int n;return name!=null&&name.StartsWith(Prefix,StringComparison.Ordinal)&&int.TryParse(name.Substring(Prefix.Length),out n)&&n>=16&&n<=19?n-16:-1;}
        public static bool IsSword(ItemValue v){return Tier(v)>=0;}
        public static bool IsVehicle(Entity e){return e is EntityJuque;}
        public static bool Finite(float n){return !float.IsNaN(n)&&!float.IsInfinity(n);}
        public static bool Finite(Vector3 v){return Finite(v.x)&&Finite(v.y)&&Finite(v.z);}
        public static int Saturate(double n){return double.IsNaN(n)||n<=0?0:n>=int.MaxValue?int.MaxValue:(int)n;}
        public static float Charge(float seconds){return seconds<.3f?0:Mathf.Clamp01((seconds-.3f)/1.2f);}
        public static int Damage(int tier,float charge){return Saturate(Wave[tier]+(double)(Charged[tier]-Wave[tier])*charge);}
        public static float Cost(float charge){return 15+45*charge;}
        public static float Energy(ItemValue v){float f;int t=Tier(v);return t<0?0:v.TryGetMetadata(EnergyKey,out f)&&Finite(f)?Mathf.Clamp(f,0,Capacity[t]):Capacity[t];}
        public static int Deployed(ItemValue v){int n;return IsSword(v)&&v.TryGetMetadata(DeployedKey,out n)?n:0;}
        public static string Id(ItemValue v){string id;return v!=null&&v.TryGetMetadata(IdKey,out id)?id:null;}
        public static string EnsureId(ItemValue v){var id=Id(v);if(string.IsNullOrEmpty(id)){id=Guid.NewGuid().ToString("N");v.SetMetadata(IdKey,id);}return id;}
        public static bool InArc(Vector3 forward,Vector3 aim){if(!Finite(aim)||aim.sqrMagnitude<.9f)return false;var local=Quaternion.Inverse(Quaternion.LookRotation(forward))*aim.normalized;float yaw=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,pitch=Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg;return Mathf.Abs(yaw)<=135.1f&&pitch>=-80.1f&&pitch<=45.1f;}
    }
    [Serializable] public sealed class SwordSettings
    {
        public string DeployKey="G", RiseKey="Space", DescendKey="C", ViewKey="BackQuote";
        public float Effects=1, Cruise=26, Boost=40, Reverse=8, Rise=8, Descend=6;
        public KeyCode Key(string name,KeyCode fallback){KeyCode k;return Enum.TryParse(name,true,out k)?k:fallback;}
    }
}
