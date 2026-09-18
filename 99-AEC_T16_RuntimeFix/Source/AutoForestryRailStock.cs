using UnityEngine;

namespace AECT16RuntimeFix
{
    // Source-model outdoor rail: doorway near x=1.6, terminus x=4.75,
    // centre z=.69. Assign inward (-X) flow; stock is staged beside the rail.
    // These are scenery logs, deliberately separate from inventory Timber0..4.
    public static class AutoForestryRailStock
    {
        public static void Build(Transform root,Material bark,Material wood,Material steel,Material paint)
        {
            var yard=new GameObject("RailRawStock");yard.transform.SetParent(root,false);
            var p=yard.transform;
            foreach(float x in new[]{2.78f,4.22f})
            {
                AutoForestryMachinery.Box(p,"RawStockCrib",new Vector3(x,.14f,1.53f),new Vector3(.16f,.10f,1.12f),wood,true);
                foreach(float z in new[]{1.00f,2.06f})
                    AutoForestryMachinery.Box(p,"RawStockStop",new Vector3(x,.35f,z),new Vector3(.085f,.43f,.085f),steel);
            }
            // Three logs on the bottom, two nested above, one on top.
            var positions=new[]{
                new Vector3(3.51f,.33f,1.20f),new Vector3(3.55f,.338f,1.53f),new Vector3(3.50f,.346f,1.86f),
                new Vector3(3.54f,.595f,1.365f),new Vector3(3.50f,.603f,1.695f),new Vector3(3.53f,.860f,1.53f)};
            for(int i=0;i<positions.Length;i++)
            {
                var log=AutoForestryModel.Timber(p,bark,i);
                log.name="RawStockLog"+i;
                log.transform.localPosition=positions[i];
                log.transform.localRotation=Quaternion.Euler(0,(i%3-1)*.65f,0);
            }
            // Side-loading skids stop short of the original rails (z=.568.. .823).
            foreach(float x in new[]{2.95f,4.05f})
                AutoForestryMachinery.Box(p,"RawStockLoadingSkid",new Vector3(x,.125f,.985f),new Vector3(.13f,.07f,.24f),wood);
            // A small plate beside the terminus shows inward flow without obscuring it.
            AutoForestryMachinery.Box(p,"RailFlowPlate",new Vector3(4.12f,.115f,.28f),new Vector3(.70f,.025f,.24f),steel);
            var arrow=new GameObject("RailInfeedArrow");arrow.transform.SetParent(p,false);
            var mesh=new Mesh{name="ForestryInfeedArrow"};
            mesh.vertices=new[]{new Vector3(3.86f,.129f,.28f),new Vector3(4.05f,.129f,.37f),new Vector3(4.05f,.129f,.19f)};
            mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.right};mesh.triangles=new[]{0,1,2};
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            arrow.AddComponent<MeshFilter>().sharedMesh=mesh;arrow.AddComponent<MeshRenderer>().sharedMaterial=paint;
            AutoForestryMachinery.Box(p,"RailArrowStem",new Vector3(4.19f,.13f,.28f),new Vector3(.30f,.003f,.045f),paint);
        }
    }
}
