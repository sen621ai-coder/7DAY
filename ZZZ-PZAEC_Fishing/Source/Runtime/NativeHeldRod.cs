using System;
using System.Linq;
using PZAEC.Fishing.Contracts;
using PZAEC.Fishing.Presentation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PZAEC.Fishing.Runtime
{
    // Use the item's native hand mount in both idle and fishing, never a body-position guess.
    public sealed class NativeHeldRod : IDisposable
    {
        Transform mount,rod,grip;
        Renderer[] originals=new Renderer[0];bool[] visibility=new bool[0];
        PresentationAssets assets;
        CameraMatrixOverride projection;
        Transform diagnosed;
        public Vector3? HandScene => mount!=null&&mount.gameObject.activeInHierarchy?
            (Vector3?)(projection!=null&&projection.enabled?NativeHandProjection.ToWorld(projection.referenceCamera,projection.fov,mount.position):mount.position):null;
        public Vector3? VisibleGripScene => grip!=null&&rod.gameObject.activeInHierarchy?(Vector3?)grip.position:null;
        public void Update(EntityPlayerLocal player,FishingConfig config,bool fishing)
        {
            var next=player?.inventory?.GetHoldingItemTransform();
            Vector3 aim=player!=null&&player.playerCamera!=null?player.playerCamera.transform.forward:next!=null?next.forward:Vector3.forward;
            Update(next,aim,config,fishing);
            projection=next!=null&&player.emodel!=null&&player.emodel.IsFPV?
                (next.GetComponentInParent<CameraMatrixOverride>()??player.m_vp_FPWeapon?.CameraMatrixOverride):null;
            if(next!=null&&next!=diagnosed) {
                diagnosed=next;
                Log.Out("[PZAEC.Fishing] Grip mount="+next.name+" parent="+next.parent?.name+
                    " fpProjection="+(projection!=null)+" handFov="+(projection!=null?projection.fov:0)+
                    " worldFov="+(projection!=null&&projection.referenceCamera!=null?projection.referenceCamera.fieldOfView:0)+
                    " holdType="+player.inventory.holdingItem.HoldType.Value);
            }
            if(!fishing&&grip!=null&&HandScene.HasValue)rod.position+=HandScene.Value-grip.position;
        }
        public void Update(Transform next,Vector3 aim,FishingConfig config,bool fishing)
        {
            if(next!=mount){Dispose();mount=next;}
            if(mount==null)return;
            if(rod==null) {
                // Native held prefabs can include skinned hands. Hide only the rigid placeholder weapon.
                originals=mount.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is SkinnedMeshRenderer)).ToArray();visibility=originals.Select(r=>r.enabled).ToArray();
                try {
                    assets=new PresentationAssets(ModDirectory);
                    string name=assets.Bundle.GetAllAssetNames().First(n=>n.EndsWith("/fishingrod.prefab",StringComparison.OrdinalIgnoreCase));
                    rod=Object.Instantiate(assets.Bundle.LoadAsset<GameObject>(name)).transform;
                    rod.name="FishingHeldRod";
                    grip=rod.GetComponentsInChildren<Transform>(true).First(t=>t.name=="GripRight");
                    foreach(var collider in rod.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                } catch {Dispose();throw;}
            }
            foreach(var renderer in originals)if(renderer!=null)renderer.enabled=false;
            rod.gameObject.SetActive(!fishing&&mount.gameObject.activeInHierarchy);
            if(fishing)return;
            if(aim.sqrMagnitude<.01f)aim=mount.forward;
            rod.rotation=Quaternion.LookRotation(aim,Vector3.up);
            rod.localScale=Vector3.one*(config.Rod.LengthMeters/2.7f);
            // Align the actual prefab grip, including its scale, to the native held-item origin.
            rod.position+=mount.position-grip.position;
        }
        public string ModDirectory {get;set;}
        public Vec3 Root(RodPose pose,float length)
        {
            var hand=mount!=null&&mount.gameObject.activeInHierarchy?(Vector3?)mount.position:null;
            if(!hand.HasValue)throw new InvalidOperationException("Fishing hand mount unavailable");
            return NativeCoordinates.ToAbsolute(hand.Value)-PresentationMath.RodAim(pose)*(.23f*length/2.7f);
        }
        public void Dispose()
        {
            for(int i=0;i<originals.Length;i++)if(originals[i]!=null)originals[i].enabled=visibility[i];
            originals=new Renderer[0];visibility=new bool[0];
            if(rod!=null)Object.Destroy(rod.gameObject);rod=null;grip=null;mount=null;projection=null;diagnosed=null;
            assets?.Dispose();assets=null;
        }
    }
}
