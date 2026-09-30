using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PZAEC.Fishing.Presentation
{
    // Unity bundles cannot be loaded a second time under the same internal identity.
    // Main-thread refcount lets simultaneous observers share assets without destroying each other.
    internal sealed class PresentationAssets : IDisposable
    {
        sealed class Entry { public AssetBundle Bundle;public int Count; }
        static readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        readonly string path;
        Entry entry;
        public AssetBundle Bundle => entry==null?null:entry.Bundle;
        public PresentationAssets(string modDirectory)
        {
            path=Path.GetFullPath(Path.Combine(modDirectory,"Resources","fishing-presentation.unity3d"));
            if(!entries.TryGetValue(path,out entry))
            {
                var loaded=AssetBundle.LoadFromFile(path);
                if(loaded==null)throw new FileNotFoundException("Fishing presentation bundle could not load",path);
                entry=new Entry{Bundle=loaded};entries.Add(path,entry);
            }
            entry.Count++;
        }
        public void Dispose()
        {
            if(entry==null)return;
            if(--entry.Count==0){entry.Bundle.Unload(true);entries.Remove(path);}
            entry=null;
        }
    }
}
