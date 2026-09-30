using UnityEngine;

namespace PZAEC.Fishing.Runtime
{
    public static class FishingHud
    {
        static GUIStyle title,body,note;
        public static void Draw(string heading,string stats,string help,string message,FloatReadoutState? floatView=null)
        {
            if(title==null) {
                title=new GUIStyle(GUI.skin.label){fontSize=25,fontStyle=FontStyle.Bold,wordWrap=true};
                body=new GUIStyle(GUI.skin.label){fontSize=21,wordWrap=true};
                note=new GUIStyle(GUI.skin.label){fontSize=19,wordWrap=true};
                title.normal.textColor=new Color(.9f,.97f,1);body.normal.textColor=Color.white;note.normal.textColor=new Color(.85f,.9f,.93f);
            }
            float scale=Mathf.Clamp(Screen.height/1080f,.8f,1.6f);
            var matrix=GUI.matrix;var color=GUI.color;
            try {
                GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(scale,scale,1));
                bool compact=Screen.height<700;
                float width=Mathf.Min(550,Screen.width/scale-(floatView.HasValue?308:40)),height=string.IsNullOrEmpty(message)||compact?225:305;
                var box=new Rect(floatView.HasValue?Screen.width/scale-width-24:24,floatView.HasValue?24:Screen.height<600?24:110,width,height);GUI.color=new Color(1,1,1,.95f);GUI.Box(box,GUIContent.none);GUI.color=Color.white;
                bool bite=heading.StartsWith("鱼已咬实")||heading.StartsWith("鱼有口");
                GUI.color=bite?new Color(1,.85f,.25f):Color.white;
                GUI.Label(new Rect(box.x+18,box.y+12,width-36,40),heading,title);GUI.color=Color.white;
                if(bite&&!floatView.HasValue) {
                    var cue=new Rect(Screen.width/scale/2-220,Screen.height/scale*.60f,440,48);
                    GUI.Box(cue,GUIContent.none);GUI.color=new Color(1,.85f,.25f);
                    GUI.Label(new Rect(cue.x+12,cue.y+6,416,38),heading,title);GUI.color=Color.white;
                }
                GUI.Label(new Rect(box.x+18,box.y+57,width-36,70),stats,body);
                GUI.Label(new Rect(box.x+18,box.y+130,width-36,85),compact&&!string.IsNullOrEmpty(message)?message:help,note);
                if(!compact&&!string.IsNullOrEmpty(message))GUI.Label(new Rect(box.x+18,box.y+218,width-36,80),message,body);
                if(floatView.HasValue)DrawFloat(floatView.Value,scale);
            } finally {GUI.matrix=matrix;GUI.color=color;}
        }
        static void Fill(Rect rect,Color color)
        {GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=Color.white;}
        static void DrawFloat(FloatReadoutState state,float scale)
        {
            var box=new Rect(24,Screen.height/scale*.5f-175,220,350);
            Fill(box,new Color(.025f,.055f,.065f,.94f));
            Fill(new Rect(box.x,box.y,3,350),state.CanStrike?new Color(1,.68f,.16f):new Color(.25f,.65f,.72f));
            GUI.Label(new Rect(box.x+16,box.y+10,188,30),"漂目放大",body);
            GUI.Label(new Rect(box.x+16,box.y+40,188,27),"露 "+state.VisibleMarks.ToString("F1")+" 目",note);
            GUI.BeginGroup(new Rect(box.x+16,box.y+68,188,218));
            try {
                const float water=155,mark=24,x=78;
                Fill(new Rect(0,water,188,218-water),new Color(.10f,.32f,.39f,.7f));
                float top=water-state.VisibleMarks*mark;
                Fill(new Rect(x-3,top-2,20,148),new Color(.01f,.02f,.02f));
                for(int i=0;i<6;i++) {
                    float y=top+i*mark;
                    var band=i%2==0?new Color(1,.96f,.75f):new Color(1,.35f,.08f);
                    if(y>=water)band=new Color(band.r*.4f,band.g*.5f,band.b*.6f,.75f);
                    Fill(new Rect(x,y,14,mark-2),band);
                    if(y<water-8)GUI.Label(new Rect(x+25,y-1,36,25),(i+1).ToString(),note);
                }
                Fill(new Rect(0,water,188,2),new Color(.4f,.86f,.93f));
                GUI.Label(new Rect(0,water+5,66,26),"水面",note);
            } finally {GUI.EndGroup();}
            string signal;
            switch(state.Signal) {
                case FloatSignal.Casting:signal="抛竿中";break;
                case FloatSignal.Settling:signal="浮漂站立";break;
                case FloatSignal.Tapping:signal="试饵 · 轻点";break;
                case FloatSignal.Downstroke:signal="顿口";break;
                case FloatSignal.Submerged:signal="黑漂";break;
                case FloatSignal.Rising:signal="顶漂";break;
                case FloatSignal.Traveling:signal="走漂";break;
                case FloatSignal.Fighting:signal="中鱼 · 遛鱼";break;
                default:signal="等口";break;
            }
            GUI.color=state.CanStrike?new Color(1,.8f,.3f):Color.white;
            GUI.Label(new Rect(box.x+16,box.y+289,188,29),signal,title);
            GUI.color=Color.white;
            GUI.Label(new Rect(box.x+16,box.y+320,188,25),state.CanStrike?"左键提竿":state.Signal==FloatSignal.Fighting?"后拉鼠标遛鱼":"看漂等口",note);
        }
    }
}
