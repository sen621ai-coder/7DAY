using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Read-only presentation policy, independent of Unity and collector accounting.
    public static class ForestryVisualState
    {
        public static int LogCount(int stored,int capacity)
        { return stored<=0||capacity<=0?0:Math.Min(5,(int)Math.Ceiling(stored*5d/capacity)); }
        // 0 waiting/stopped, 1 working, 2 full, 3 no fuel, 4 obstructed/underwater.
        public static int Status(bool known,bool obstructed,bool space,bool fuel,bool nativeDisabled)
        {
            if(!known)return 0;
            if(obstructed)return 4;
            if(!space)return 2;
            if(!fuel)return 3;
            return nativeDisabled?0:1;
        }
    }

    public sealed class AutoForestryActivity : MonoBehaviour
    {
        WorldBase world;Vector3i position;bool bound,initialized,running,animate;
        float nextPoll,travel,speed=1,velocity,lightWeight,dustWeight;
        int desiredLogs;
        Transform blade,sawPulley,drivePulley;
        Quaternion sawPulleyStart,drivePulleyStart;
        readonly Transform[,] boards=new Transform[2,3];
        readonly List<Transform> rollers=new List<Transform>();
        readonly List<Quaternion> originalRotations=new List<Quaternion>();
        Quaternion bladeStart;
        readonly List<GameObject> timber=new List<GameObject>();
        readonly List<Renderer> logRenderers=new List<Renderer>();
        readonly List<Collider> logColliders=new List<Collider>();
        readonly float[] logAlpha=new float[5];
        Material solidLog,fadeLog;MaterialPropertyBlock logProperties;
        Renderer screen,bladeRenderer;MaterialPropertyBlock screenProperties;Light workLight;ParticleSystem dust;
        static readonly int ColorId=Shader.PropertyToID("_Color"),EmissionId=Shader.PropertyToID("_EmissionColor");

        public static void Install(Harmony h)
        {
            var method=AccessTools.Method(typeof(BlockCollector),"OnBlockEntityTransformBeforeActivated");
            if(method==null)throw new MissingMethodException("BlockCollector.OnBlockEntityTransformBeforeActivated");
            h.Patch(method,postfix:new HarmonyMethod(typeof(AutoForestryActivity),nameof(Activated)));
        }
        static void Activated(WorldBase __0,Vector3i __1,BlockValue __2,BlockEntityData __3)
        {
            if(GameManager.IsDedicatedServer||__2.Block.GetBlockName()!="yfAutoForestry"||__3==null||__3.transform==null)return;
            foreach(var lod in __3.transform.GetComponentsInChildren<LODGroup>(true))
            {
                // The block pool may rename an instance root; our child marker is stable.
                if(lod.transform.Find("MachineryNear")==null)continue;
                var activity=lod.GetComponent<AutoForestryActivity>()??lod.gameObject.AddComponent<AutoForestryActivity>();
                activity.Bind(__0,__1);break;
            }
        }
        void Initialize()
        {
            if(initialized)return;initialized=true;screenProperties=new MaterialPropertyBlock();logProperties=new MaterialPropertyBlock();
            foreach(var t in GetComponentsInChildren<Transform>(true))
            {
                if(t.name=="SawBlade"){blade=t;bladeStart=t.localRotation;bladeRenderer=t.GetComponent<Renderer>();}
                for(int i=0;i<2;i++)
                {
                    if(t.name=="FeedTimber"+i)boards[i,0]=t;
                    if(t.name=="CutTimberL"+i)boards[i,1]=t;
                    if(t.name=="CutTimberR"+i)boards[i,2]=t;
                }
                if(t.name=="SawPulley"){sawPulley=t;sawPulleyStart=t.localRotation;}
                if(t.name=="DrivePulley"){drivePulley=t;drivePulleyStart=t.localRotation;}
                if(t.name.StartsWith("FeedRoller",StringComparison.Ordinal))
                {rollers.Add(t);originalRotations.Add(t.localRotation);}
                if(t.name.StartsWith("Timber",StringComparison.Ordinal))timber.Add(t.gameObject);
                if(t.name=="ControlScreen")screen=t.GetComponent<Renderer>();
                if(t.name=="WorkLamp")workLight=t.GetComponent<Light>();
                if(t.name=="Sawdust")dust=t.GetComponent<ParticleSystem>();
            }
            timber.Sort((a,b)=>String.CompareOrdinal(a.name,b.name));
            foreach(var log in timber){logRenderers.Add(log.GetComponent<Renderer>());logColliders.Add(log.GetComponent<Collider>());}
            if(logRenderers.Count>0)
            {
                solidLog=logRenderers[0].sharedMaterial;
                fadeLog=new Material(solidLog){name="ForestryLogTransition"};
                fadeLog.SetFloat("_Mode",2);fadeLog.SetOverrideTag("RenderType","Transparent");
                fadeLog.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                fadeLog.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                fadeLog.SetInt("_ZWrite",0);fadeLog.DisableKeyword("_ALPHATEST_ON");fadeLog.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                fadeLog.EnableKeyword("_ALPHABLEND_ON");fadeLog.renderQueue=3000;
            }
        }
        void Bind(WorldBase source,Vector3i at)
        {
            Initialize();ResetPresentation();world=source;position=at;bound=true;nextPoll=0;
        }
        void ResetPresentation()
        {
            running=false;animate=false;travel=0;velocity=0;desiredLogs=0;lightWeight=0;dustWeight=0;
            if(blade!=null)blade.localRotation=bladeStart;
            if(sawPulley!=null)sawPulley.localRotation=sawPulleyStart;
            if(drivePulley!=null)drivePulley.localRotation=drivePulleyStart;
            PoseBoards();
            for(int i=0;i<rollers.Count;i++)rollers[i].localRotation=originalRotations[i];
            for(int i=0;i<timber.Count;i++)
            {
                logAlpha[i]=0;timber[i].SetActive(false);logRenderers[i].sharedMaterial=solidLog;
                logRenderers[i].SetPropertyBlock(null);if(logColliders[i]!=null)logColliders[i].enabled=false;
            }
            if(workLight!=null){workLight.intensity=0;workLight.enabled=false;}
            if(dust!=null)dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            SetScreen(0);
        }
        void OnDisable(){bound=false;world=null;if(initialized)ResetPresentation();}
        void OnDestroy(){if(fadeLog!=null)Destroy(fadeLog);}
        bool PoseBoards()
        {
            bool cutting=false;
            for(int i=0;i<2;i++)for(int part=0;part<3;part++)
            {
                var t=boards[i,part];if(t==null)continue;
                var slice=ForestryMotion.Slice(travel,i,part);bool show=slice.length>.0001f;
                if(t.gameObject.activeSelf!=show)t.gameObject.SetActive(show);
                if(show)
                {
                    t.localPosition=new Vector3(slice.x,.935f,slice.z);
                    t.localScale=new Vector3(slice.length,.12f,part==0?.23f:.095f);
                }
                if(part==0&&show&&ForestryMotion.Slice(travel,i,1).length>.0001f)cutting=true;
            }
            return cutting;
        }
        void UpdateLogs(float dt)
        {
            for(int i=0;i<timber.Count;i++)
            {
                float alpha=Mathf.MoveTowards(logAlpha[i],i<desiredLogs?1:0,dt*2.5f);
                if(alpha==logAlpha[i])continue;
                logAlpha[i]=alpha;timber[i].SetActive(alpha>0);
                if(logColliders[i]!=null)logColliders[i].enabled=alpha>=.99f;
                if(alpha>=1||alpha<=0){logRenderers[i].sharedMaterial=solidLog;logRenderers[i].SetPropertyBlock(null);}
                else
                {
                    logRenderers[i].sharedMaterial=fadeLog;
                    logProperties.SetColor(ColorId,new Color(1,1,1,alpha));logRenderers[i].SetPropertyBlock(logProperties);
                }
            }
        }
        void SetScreen(int status)
        {
            if(screen==null)return;
            Color c=status==1?new Color(.14f,.65f,.25f):status==2?new Color(.90f,.53f,.09f):
                status==3||status==4?new Color(.70f,.09f,.035f):new Color(.10f,.15f,.13f);
            screenProperties.SetColor(ColorId,c);screenProperties.SetColor(EmissionId,c*(status==0?.04f:.24f));
            screen.SetPropertyBlock(screenProperties);
        }
        void Update()
        {
            if(!bound||GameManager.IsDedicatedServer)return;
            if(Time.unscaledTime>=nextPoll)
            {
                nextPoll=Time.unscaledTime+.5f;
                Poll();
            }
            float dt=Mathf.Min(Time.deltaTime,.1f);
            velocity=ForestryMotion.Approach(velocity,running?speed:0,dt);
            UpdateLogs(dt);
            float metres=dt*velocity*ForestryMotion.FeedSpeed;
            travel=Mathf.Repeat(travel+metres,ForestryMotion.Cycle);
            bool cutting=false;
            if(animate)
            {
                float angle=dt*velocity*ForestryMotion.SawDegrees;
                if(blade!=null)blade.Rotate(Vector3.forward,angle,Space.Self);
                if(sawPulley!=null)sawPulley.Rotate(Vector3.up,angle,Space.Self);
                if(drivePulley!=null)drivePulley.Rotate(Vector3.up,ForestryMotion.DriveDegrees(angle),Space.Self);
                foreach(var roller in rollers)roller.Rotate(Vector3.up,ForestryMotion.RollerDegrees(metres),Space.Self);
                cutting=PoseBoards();
            }
            if(workLight!=null)
            {
                workLight.intensity=Mathf.MoveTowards(workLight.intensity,(running?.8f:0)*lightWeight,dt*1.6f);
                workLight.enabled=workLight.intensity>.005f;
            }
            if(dust!=null)
            {
                float rate=cutting?12*dustWeight*velocity:0;
                var emission=dust.emission;emission.rateOverTime=rate;
                if(rate>.01f&&!dust.isPlaying)dust.Play();
                else if(rate<=.01f&&dust.isPlaying)dust.Stop(true,ParticleSystemStopBehavior.StopEmitting);
            }
        }
        void Poll()
        {
            var te=world==null?null:world.GetTileEntity(position) as TileEntityCollector;
            if(te==null||te.blockValue.Block.GetBlockName()!="yfAutoForestry")
            {ResetPresentation();return;}
            int stored=0,capacity=0;bool space=false;int perSlot=CollectorBatchStorage.Capacity(te.HasModCount);
            var items=te.Items;
            if(items==null||items.Length==0){ResetPresentation();return;}
            for(int i=0;i<items.Length;i++)
            {
                var stack=items[i];bool empty=stack==null||stack.IsEmpty();
                if(!empty&&stack.itemValue.ItemClass.GetItemName()=="yfForestryWoodBundle")stored+=stack.count;
                if(te.IsSlotDisabled(i))continue;
                capacity+=perSlot;
                if(empty||(stack.itemValue.ItemClass.GetItemName()=="yfForestryWoodBundle"&&stack.count<perSlot))space=true;
            }
            desiredLogs=ForestryVisualState.LogCount(stored,capacity);
            var type=te.GetSlotOutputType(0);
            bool known=te.outOfFuel.ContainsKey(type.Name)&&te.isFull.ContainsKey(type.Name);
            bool fuel=space&&te.getMaxProductionCount(type,te.collector.GetFuelType(type.Fuel))>0;
            int status=ForestryVisualState.Status(known,te.isBlocked||te.isUnderwater,space,fuel,te.isDisabled(type));
            running=status==1;speed=te.HasModSpeed?1.35f:1f;SetScreen(status);
            var player=GameManager.Instance==null||GameManager.Instance.World==null?null:GameManager.Instance.World.GetPrimaryPlayer();
            var camera=player!=null&&player.playerCamera!=null?player.playerCamera:Camera.main;
            float distance=camera==null?float.PositiveInfinity:Vector3.Distance(camera.transform.position,transform.position);
            // Hysteresis prevents repeated animation enable/disable near the boundary.
            animate=distance<(animate?44:40)||(bladeRenderer!=null&&bladeRenderer.isVisible);
            lightWeight=1-Mathf.InverseLerp(16,22,distance);
            dustWeight=1-Mathf.InverseLerp(12,17,distance);
        }
    }
}
