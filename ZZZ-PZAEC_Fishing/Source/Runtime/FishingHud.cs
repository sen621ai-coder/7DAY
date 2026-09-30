using UnityEngine;

namespace PZAEC.Fishing.Runtime
{
    public static class FishingHud
    {
        static GUIStyle title,body,note;
        public static void Draw(string heading,string stats,string help,string message)
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
                float width=Mathf.Min(550,Screen.width/scale-40),height=string.IsNullOrEmpty(message)||compact?225:305;
                var box=new Rect(24,Screen.height<600?24:110,width,height);GUI.color=new Color(1,1,1,.95f);GUI.Box(box,GUIContent.none);GUI.color=Color.white;
                bool bite=heading.StartsWith("鱼已咬实");
                GUI.color=bite?new Color(1,.85f,.25f):Color.white;
                GUI.Label(new Rect(box.x+18,box.y+12,width-36,40),heading,title);GUI.color=Color.white;
                if(bite) {
                    var cue=new Rect(Screen.width/scale/2-220,Screen.height/scale*.60f,440,48);
                    GUI.Box(cue,GUIContent.none);GUI.color=new Color(1,.85f,.25f);
                    GUI.Label(new Rect(cue.x+12,cue.y+6,416,38),"咬实！左键 + 鼠标向后拉",title);GUI.color=Color.white;
                }
                GUI.Label(new Rect(box.x+18,box.y+57,width-36,70),stats,body);
                GUI.Label(new Rect(box.x+18,box.y+130,width-36,85),compact&&!string.IsNullOrEmpty(message)?message:help,note);
                if(!compact&&!string.IsNullOrEmpty(message))GUI.Label(new Rect(box.x+18,box.y+218,width-36,80),message,body);
            } finally {GUI.matrix=matrix;GUI.color=color;}
        }
    }
}
