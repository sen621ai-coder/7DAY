using System;

namespace PZAEC.Surveillance
{
    public struct MarkerPoint
    {
        public float X,Y,Z;
        public MarkerPoint(float x,float y,float z){X=x;Y=y;Z=z;}
    }
    public struct MarkerRect
    {
        public float Left,Bottom,Right,Top;
    }
    public static class TargetMarkerRules
    {
        public const float Range=48;
        public const int MaxBoxes=16,MaxCandidates=24,MaxRays=32;
        public static bool IsTarget(bool player,bool dead,bool zombie,bool animal)=>!player&&!dead&&(zombie||animal);
        public static bool TryRect(MarkerPoint[] corners,float near,float far,int width,int height,out MarkerRect rect)
        {
            rect=new MarkerRect();float left=float.MaxValue,bottom=float.MaxValue,right=float.MinValue,top=float.MinValue,minZ=float.MaxValue;
            foreach(var p in corners)
            {
                // Conservative near-plane rejection avoids inverted or full-screen boxes.
                if(float.IsNaN(p.X)||float.IsInfinity(p.X)||float.IsNaN(p.Y)||float.IsInfinity(p.Y)||float.IsNaN(p.Z)||float.IsInfinity(p.Z)||p.Z<=near)return false;
                left=Math.Min(left,p.X);bottom=Math.Min(bottom,p.Y);right=Math.Max(right,p.X);top=Math.Max(top,p.Y);minZ=Math.Min(minZ,p.Z);
            }
            if(minZ>far||right<=0||left>=1||top<=0||bottom>=1)return false;
            rect=new MarkerRect{Left=Math.Max(0,left),Bottom=Math.Max(0,bottom),Right=Math.Min(1,right),Top=Math.Min(1,top)};
            return (rect.Right-rect.Left)*width>=4&&(rect.Top-rect.Bottom)*height>=4;
        }
    }
}
