using System;
namespace PZAEC.M1
{
    public static class EffectRules
    {
        // Snapshot ordering is independent of transient shots; snapshots cannot
        // acknowledge an effect the renderer has never received.
        public sealed class Events
        {
            readonly int[] seen=new int[64];int count,next;
            public bool Accept(int serial)
            {for(int i=0;i<count;i++)if(seen[i]==serial)return false;seen[next]=serial;next=(next+1)%seen.Length;count=Math.Min(seen.Length,count+1);return true;}
        }
        public static float FlashLife(float frame,bool machineGun)
        {return Math.Min(machineGun?.085f:.16f,Math.Max(machineGun?.055f:.09f,frame*2));}
        public static bool Fresh(float age)=>age>=0&&age<.35f;
    }
}
