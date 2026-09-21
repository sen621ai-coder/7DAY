using System;
using System.Collections.Generic;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Session-owned resources. Native bundle assets are borrowed, never destroyed.
    // Backups are never assigned to a Renderer, so native pool cleanup cannot see them.
    public static class ForestryResources
    {
        sealed class TextureEntry { public Texture2D live; public Func<Texture2D> reload; public string name;public int id; }
        sealed class MaterialEntry { public Material live,seed; public string[] slots; public int[] textures; }
        sealed class MeshEntry { public Mesh live,seed; }
        static readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        static readonly List<TextureEntry> textures=new List<TextureEntry>();
        static readonly List<MaterialEntry> materials=new List<MaterialEntry>();
        static readonly List<MeshEntry> meshes=new List<MeshEntry>();
        static readonly Dictionary<int,int> materialIds=new Dictionary<int,int>(),meshIds=new Dictionary<int,int>();
        static float nextCheck;
        static int failures;
        static int activated,retired;
        public static void InstanceActivated(){activated++;if(activated==1||activated%25==0)Log.Out("[AutoForestry] Lifecycle: activations="+activated+", retired="+retired+", generation="+Generation);}
        public static void InstanceRetired(){retired++;if(retired==1||retired%25==0)Log.Out("[AutoForestry] Lifecycle: activations="+activated+", retired="+retired+", generation="+Generation);}
        public static int Generation { get; private set; }
        public struct Checkpoint { internal int objects,textures,materials,meshes; }
        public static Checkpoint Begin() { return new Checkpoint{objects=owned.Count,textures=textures.Count,materials=materials.Count,meshes=meshes.Count}; }
        public static T Own<T>(T value) where T:UnityEngine.Object
        {
            value.hideFlags|=HideFlags.DontUnloadUnusedAsset;
            owned.Add(value);return value;
        }
        public static void Release(UnityEngine.Object value)
        { if(owned.Remove(value)&&value!=null)UnityEngine.Object.Destroy(value); }
        public static Texture2D Texture(Func<Texture2D> factory)
        {
            var t=factory();textures.Add(new TextureEntry{live=t,reload=factory,name=t.name,id=t.GetInstanceID()});return t;
        }
        static void Trim<T>(List<T> list,int count){if(list.Count>count)list.RemoveRange(count,list.Count-count);}
        public static void Rollback(Checkpoint mark)
        {
            for(int i=owned.Count-1;i>=mark.objects;i--)if(owned[i]!=null)UnityEngine.Object.Destroy(owned[i]);
            Trim(owned,mark.objects);Trim(textures,mark.textures);Trim(materials,mark.materials);Trim(meshes,mark.meshes);
            materialIds.Clear();meshIds.Clear();
            for(int i=0;i<materials.Count;i++)if(materials[i].live!=null)materialIds[materials[i].live.GetInstanceID()]=i;
            for(int i=0;i<meshes.Count;i++)if(meshes[i].live!=null)meshIds[meshes[i].live.GetInstanceID()]=i;
        }
        public static int MaterialId(Material m)
        {
            if(m==null)throw new InvalidOperationException("Forestry renderer has no material during capture");
            int id;if(materialIds.TryGetValue(m.GetInstanceID(),out id))return id;
            var e=new MaterialEntry{live=m,seed=Own(new Material(m)),slots=m.GetTexturePropertyNames()};
            e.seed.name=m.name+"_RecoverySeed";e.textures=new int[e.slots.Length];
            for(int s=0;s<e.slots.Length;s++)
            {
                e.textures[s]=-1;var texture=m.GetTexture(e.slots[s]);
                for(int t=0;t<textures.Count;t++)if(texture!=null&&texture==textures[t].live){e.textures[s]=t;break;}
            }
            id=materials.Count;materials.Add(e);materialIds[m.GetInstanceID()]=id;
            Log.Out("[AutoForestry] Material resource="+id+", name="+m.name+", instance="+m.GetInstanceID()
                +", shader="+(m.shader==null?"null":m.shader.name)+", textures="+e.slots.Length);
            return id;
        }
        public static int MeshId(Mesh mesh)
        {
            if(mesh==null)return -1;
            int id;if(meshIds.TryGetValue(mesh.GetInstanceID(),out id))return id;
            // Only generated meshes belong to this model. Primitive cube meshes are borrowed.
            if(!owned.Contains(mesh))return -1;
            var e=new MeshEntry{live=mesh,seed=Own(UnityEngine.Object.Instantiate(mesh))};
            id=meshes.Count;meshes.Add(e);meshIds[mesh.GetInstanceID()]=id;return id;
        }
        public static Material MaterialAt(int id){return materials[id].live;}
        public static Mesh MeshAt(int id){return id<0?null:meshes[id].live;}
        public static Material NamedMaterial(string name)
        { foreach(var e in materials)if(e.seed!=null&&e.seed.name==name+"_RecoverySeed")return e.live;return null; }

        public static bool Check(bool force=false)
        {
            if(!force&&Time.unscaledTime<nextCheck)return failures==0;
            nextCheck=Time.unscaledTime+5;
            try
            {
                int changes=0;
                Shader replacementShader=null;
                foreach(var e in textures)if(e.live==null)
                {Log.Out("[AutoForestry] Texture lost: "+e.name+", instance="+e.id);e.live=e.reload();e.id=e.live.GetInstanceID();changes++;}
                foreach(var e in materials)
                {
                    if(e.seed==null)throw new InvalidOperationException("Material recovery seed missing");
                    if(e.seed.shader==null||!e.seed.shader.isSupported)
                    {
                        if(replacementShader==null)replacementShader=AutoForestryModel.ReloadNativeShader();
                        e.seed.shader=replacementShader;changes++;
                    }
                    if(e.live==null)
                    {Log.Out("[AutoForestry] Material lost: "+e.seed.name);e.live=Own(new Material(e.seed));e.live.name=e.seed.name.Replace("_RecoverySeed","");materialIds[e.live.GetInstanceID()]=materials.IndexOf(e);changes++;}
                    if(e.live.shader!=e.seed.shader){Log.Out("[AutoForestry] Shader changed: "+e.live.name);e.live.shader=e.seed.shader;changes++;}
                    if(String.Join(",",e.live.shaderKeywords)!=String.Join(",",e.seed.shaderKeywords))
                    {Log.Out("[AutoForestry] Shader keywords changed: "+e.live.name);e.live.shaderKeywords=e.seed.shaderKeywords;changes++;}
                    for(int s=0;s<e.slots.Length;s++)
                    {
                        var expected=e.textures[s]<0?e.seed.GetTexture(e.slots[s]):textures[e.textures[s]].live;
                        if(e.live.GetTexture(e.slots[s])!=expected){e.live.SetTexture(e.slots[s],expected);changes++;}
                    }
                }
                foreach(var e in meshes)if(e.live==null)
                {
                    if(e.seed==null)throw new InvalidOperationException("Mesh recovery seed lost");
                    e.live=Own(UnityEngine.Object.Instantiate(e.seed));meshIds[e.live.GetInstanceID()]=meshes.IndexOf(e);changes++;
                }
                if(changes>0){Generation++;Log.Out("[AutoForestry] Resource recovery generation="+Generation+", repaired="+changes);}
                if(failures>0)Log.Out("[AutoForestry] Resource health restored");
                failures=0;return true;
            }
            catch(Exception ex)
            {
                failures++;nextCheck=Time.unscaledTime+60;
                if(failures==1)Log.Error("[AutoForestry] Resource recovery blocked; retry in 60s: "+ex.Message);
                return false;
            }
        }
    }

    public sealed class ForestrySharedMaterialOwner : MonoBehaviour
    {
        // Unity remaps renderer/filter references when the prefab is cloned.
        public Renderer[] renderers;
        public int[] counts,materialSlots;
        public MeshFilter[] filters;public int[] filterSlots;
        public MeshCollider[] colliders;public int[] colliderSlots;
        [NonSerialized] public bool retiring;
        float nextCheck;
        public void Capture()
        {
            renderers=GetComponentsInChildren<Renderer>(true);counts=new int[renderers.Length];
            var slots=new List<int>();
            for(int i=0;i<renderers.Length;i++)
            {
                var mats=renderers[i].sharedMaterials;counts[i]=mats.Length;
                foreach(var m in mats)slots.Add(ForestryResources.MaterialId(m));
            }
            materialSlots=slots.ToArray();filters=GetComponentsInChildren<MeshFilter>(true);filterSlots=new int[filters.Length];
            for(int i=0;i<filters.Length;i++)filterSlots[i]=ForestryResources.MeshId(filters[i].sharedMesh);
            colliders=GetComponentsInChildren<MeshCollider>(true);colliderSlots=new int[colliders.Length];
            for(int i=0;i<colliders.Length;i++)colliderSlots[i]=ForestryResources.MeshId(colliders[i].sharedMesh);
            Log.Out("[AutoForestry] Captured recovery bindings: renderers="+renderers.Length+", materials="+slots.Count);
        }
        public void Restore()
        {
            if(retiring||renderers==null||!ForestryResources.Check())return;
            int slot=0,changed=0;
            for(int i=0;i<renderers.Length;i++)
            {
                var r=renderers[i];var mats=r==null?null:r.sharedMaterials;bool dirty=mats!=null&&mats.Length!=counts[i];
                if(r!=null&&dirty)mats=new Material[counts[i]];
                for(int m=0;m<counts[i];m++,slot++)if(r!=null)
                {var expected=ForestryResources.MaterialAt(materialSlots[slot]);if(mats[m]!=expected){mats[m]=expected;dirty=true;}}
                if(r!=null&&dirty){r.sharedMaterials=mats;changed++;}
            }
            for(int i=0;i<filters.Length;i++)if(filters[i]!=null&&filterSlots[i]>=0)
            {var mesh=ForestryResources.MeshAt(filterSlots[i]);if(filters[i].sharedMesh!=mesh){filters[i].sharedMesh=mesh;changed++;}}
            for(int i=0;i<colliders.Length;i++)if(colliders[i]!=null&&colliderSlots[i]>=0)
            {var mesh=ForestryResources.MeshAt(colliderSlots[i]);if(colliders[i].sharedMesh!=mesh){colliders[i].sharedMesh=mesh;changed++;}}
            if(changed>0)Log.Out("[AutoForestry] Rebound instance="+GetInstanceID()+", generation="+ForestryResources.Generation+", components="+changed);
        }
        void OnEnable(){nextCheck=0;ForestryResources.InstanceActivated();}
        void Update()
        {
            if(retiring||Time.unscaledTime<nextCheck)return;nextCheck=Time.unscaledTime+5;
            Restore();
        }
    }
}
