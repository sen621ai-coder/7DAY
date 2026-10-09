using UnityEngine;
namespace PZAEC.FlyingSword
{
    // Soft particle volumes. World-space flames remain behind the moving sword.
    public sealed class SwordFlightAura:MonoBehaviour
    {
        ParticleSystem core,halo,flame;Material coreMaterial,flameMaterial;Texture2D glowTexture,flameTexture;
        float lastTime=-1,motionSpeed,turnAmount;Vector3 lastPosition,lastDirection;
        public float MotionSpeed=>motionSpeed;
        public float TurnAmount=>turnAmount;
        Mesh[] coatMeshes;MeshRenderer[] coats;Material coatMaterial;Vector3[] coatVertices,coatNormals;Color[] coatColors;float coatStrength;Transform coatSource;
        public static SwordFlightAura For(EntityJuque v)
        {
            var t=v.vehicleRB.transform.Find("Juque flight aura");if(t!=null)return t.GetComponent<SwordFlightAura>();
            var go=new GameObject("Juque flight aura");go.transform.SetParent(v.vehicleRB.transform,false);return go.AddComponent<SwordFlightAura>();
        }
        static Texture2D Texture(bool fire)
        {
            var tex=new Texture2D(64,128,TextureFormat.RGBA32,false){name=fire?"Juque wispy flame":"Juque soft radiance",wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<128;y++)for(int x=0;x<64;x++){
                float u=(x+.5f)/32-1,v=(y+.5f)/64-1,bend=fire?.18f*Mathf.Sin(v*5):0;
                float width=fire?Mathf.Lerp(.5f,.15f,(v+1)*.5f):.65f;
                float r=(u-bend)*(u-bend)/(width*width)+v*v/(fire?.7f:.45f);
                float a=Mathf.Exp(-r*3)*Mathf.Clamp01((1-Mathf.Abs(u))*6)*Mathf.Clamp01((1-Mathf.Abs(v))*6);
                if(fire)a*=.75f+.25f*Mathf.Sin(v*14+u*9);
                tex.SetPixel(x,y,new Color(1,1,1,a));
            }tex.Apply(false,true);return tex;
        }
        ParticleSystem Make(string name,Material material,bool world,float size,float life,float speed,float rate)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);go.transform.localPosition=new Vector3(0,.04f,-1.05f);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.playOnAwake=false;main.loop=true;main.duration=1;main.simulationSpace=world?ParticleSystemSimulationSpace.World:ParticleSystemSimulationSpace.Local;main.maxParticles=160;
            main.startLifetime=new ParticleSystem.MinMaxCurve(life*.6f,life);main.startSize=new ParticleSystem.MinMaxCurve(size*.6f,size);main.startSpeed=speed;main.startRotation=new ParticleSystem.MinMaxCurve(-.7f,.7f);
            var emission=ps.emission;emission.rateOverTime=rate;
            var shape=ps.shape;shape.enabled=true;shape.shapeType=world?ParticleSystemShapeType.Cone:ParticleSystemShapeType.Sphere;shape.radius=world?.10f:.06f;shape.angle=14;shape.rotation=new Vector3(0,180,0);
            var sizeModule=ps.sizeOverLifetime;sizeModule.enabled=true;sizeModule.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.35f),new Keyframe(.18f,1),new Keyframe(.7f,.7f),new Keyframe(1,0)));
            var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(.85f,1,1),0),new GradientColorKey(new Color(.22f,.8f,1),.4f),new GradientColorKey(new Color(.1f,.4f,.7f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(.45f,.55f),new GradientAlphaKey(0,1)});color.color=gradient;
            if(world){var noise=ps.noise;noise.enabled=true;noise.strength=.28f;noise.frequency=1.3f;noise.scrollSpeed=.7f;noise.damping=true;}
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.sortMode=ParticleSystemSortMode.Distance;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            return ps;
        }
        void Init()
        {
            if(core!=null)return;glowTexture=Texture(false);flameTexture=Texture(true);
            var shader=Shader.Find("Legacy Shaders/Particles/Additive")??Shader.Find("Particles/Additive")??Shader.Find("Sprites/Default");
            coreMaterial=new Material(shader){name="Juque soft cyan emission",mainTexture=glowTexture};flameMaterial=new Material(shader){name="Juque dissipating flame",mainTexture=flameTexture};
            core=Make("Condensed white core",coreMaterial,false,.65f,.22f,.18f,65);
            var coreColor=core.colorOverLifetime;var white=new Gradient();white.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.85f,1,1),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(.6f,.6f),new GradientAlphaKey(0,1)});coreColor.color=white;
            halo=Make("Diffuse cyan halo",coreMaterial,false,1.35f,.35f,.25f,25);
            flame=Make("Dissipating world flame",flameMaterial,true,1.15f,.65f,4,110);
            InitCoat(shader);
        }
        void InitCoat(Shader shader)
        {
            var visual=transform.parent.Find("JuqueVisual");if(visual==null)return;
            coatSource=visual;
            var original=visual.GetComponent<MeshFilter>().sharedMesh;coatVertices=original.vertices;coatNormals=original.normals;coatColors=new Color[coatVertices.Length];
            for(int i=0;i<coatVertices.Length;i++)coatVertices[i]=Vector3.Scale(coatVertices[i],visual.localScale);
            coatMaterial=new Material(shader){name="Juque thin cyan mantle",mainTexture=Texture2D.whiteTexture};coatMeshes=new Mesh[3];coats=new MeshRenderer[3];
            for(int layer=0;layer<coatMeshes.Length;layer++){
                var vertices=new Vector3[coatVertices.Length];for(int i=0;i<vertices.Length;i++)vertices[i]=coatVertices[i]+coatNormals[i]*(layer==0?.006f:layer==1?.018f:.032f);
                var mesh=new Mesh{name="Juque fitted glow shell "+layer};mesh.vertices=vertices;mesh.normals=coatNormals;mesh.triangles=original.triangles;mesh.colors=coatColors;mesh.RecalculateBounds();coatMeshes[layer]=mesh;
                var go=new GameObject(mesh.name);go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=coatMaterial;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;coats[layer]=renderer;
            }
            Camera.onPreCull+=ShadeCoat;
        }
        void ShadeCoat(Camera camera)
        {
            if(coats==null||!coats[0].enabled||(camera.cullingMask&(1<<coats[0].gameObject.layer))==0)return;
            var eye=coats[0].transform.InverseTransformPoint(camera.transform.position);
            for(int layer=0;layer<coatMeshes.Length;layer++){
                for(int i=0;i<coatVertices.Length;i++){float rim=1-Mathf.Abs(Vector3.Dot(coatNormals[i],(eye-coatVertices[i]).normalized));float alpha=(.018f+rim*rim*.34f)*coatStrength*(layer==0?1:layer==1?.45f:.12f);coatColors[i]=new Color(.55f,.93f,1,alpha);}
                coatMeshes[layer].colors=coatColors;
            }
        }
        void OnDestroy(){Camera.onPreCull-=ShadeCoat;if(coatMeshes!=null)foreach(var mesh in coatMeshes)Destroy(mesh);if(coatMaterial!=null)Destroy(coatMaterial);if(coreMaterial!=null)Destroy(coreMaterial);if(flameMaterial!=null)Destroy(flameMaterial);if(glowTexture!=null)Destroy(glowTexture);if(flameTexture!=null)Destroy(flameTexture);}
        public void Step(bool occupied,bool boost,bool firstPerson,float strength,float now,Quaternion deck,Vector3? velocity=null)
        {
            Init();transform.localRotation=deck;strength=Mathf.Clamp(strength,0,2);bool active=occupied&&strength>0;float brightness=strength*(firstPerson?.22f:1);
            float elapsed=lastTime<0?0:now-lastTime,dt=Mathf.Clamp(elapsed,0,.1f);var position=transform.position;var delta=position-lastPosition;
            bool reset=lastTime<0||elapsed<0||elapsed>1||delta.magnitude>20;
            var movement=velocity??(!reset&&elapsed>.0001f?delta/elapsed:Vector3.zero);
            if(reset){motionSpeed=turnAmount=0;lastDirection=Vector3.zero;foreach(var ps in new[]{core,halo,flame})ps.Clear();}
            float speed=Mathf.Clamp(movement.magnitude,0,60);float turn=speed>1&&lastDirection.sqrMagnitude>.1f&&dt>.0001f?Mathf.Clamp01(Vector3.Angle(lastDirection,movement.normalized)/dt/100):0;
            if(speed>1)lastDirection=movement.normalized;
            motionSpeed=Mathf.Lerp(motionSpeed,active?speed:0,1-Mathf.Exp(-dt*7));turnAmount=Mathf.Lerp(turnAmount,turn,1-Mathf.Exp(-dt*8));lastTime=now;lastPosition=position;
            float wake=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.35f,5,motionSpeed)),fast=Mathf.InverseLerp(26,40,motionSpeed);
            coatStrength=brightness;if(coats!=null)foreach(var coat in coats){coat.enabled=active;coat.transform.position=coatSource.position;}
            var cm=core.main;cm.startColor=new Color(1,1,1,.95f*brightness);cm.startSize=new ParticleSystem.MinMaxCurve(.25f,Mathf.Lerp(.45f,.85f,Mathf.Clamp01(motionSpeed/40)));
            var hm=halo.main;hm.startColor=new Color(.2f,.8f,1,.12f*brightness);
            var fm=flame.main;fm.startColor=new Color(.6f,.95f,1,.45f*brightness);fm.startSpeed=Mathf.Lerp(1,6,Mathf.Clamp01(motionSpeed/40));fm.startLifetime=new ParticleSystem.MinMaxCurve(.18f,Mathf.Lerp(.22f,.55f,wake)+fast*.3f);fm.startSize=new ParticleSystem.MinMaxCurve(.3f,Mathf.Lerp(.65f,1.05f,wake)+fast*.35f);
            var shape=flame.shape;shape.angle=14+turnAmount*10;shape.rotation=Quaternion.LookRotation(transform.InverseTransformDirection(speed>.35f?-movement.normalized:-transform.forward)).eulerAngles;
            var noise=flame.noise;noise.strength=.28f+turnAmount*.18f;
            var emission=flame.emission;emission.rateOverTime=(100+fast*45)*strength*wake;var ce=core.emission;ce.rateOverTime=65*wake;var he=halo.emission;he.rateOverTime=25*wake;
            foreach(var ps in new[]{core,halo,flame}){ps.GetComponent<ParticleSystemRenderer>().enabled=active;if(!active){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);motionSpeed=turnAmount=0;}else if(wake>.001f){if(!ps.isPlaying)ps.Play();}else ps.Stop(true,ParticleSystemStopBehavior.StopEmitting);}
        }
    }
}
