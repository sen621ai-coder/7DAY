using System;
using System.IO;
using System.Collections.Generic;

namespace AECT16RuntimeFix
{
    public static class ForestryPreflight
    {
        public static void Validate(string folder)
        {
            var errors=new List<string>();var names=new List<string>{"sawmill.meshbin","sawmill-lod.meshbin","timber.png"};
            for(int i=0;i<6;i++){names.Add("color"+i+".png");names.Add("metal"+i+".png");names.Add("normal"+i+".png");}
            for(int i=2;i<6;i++)names.Add("ao"+i+".png");
            foreach(var name in names)
            {
                try
                {
                    using(var stream=File.OpenRead(Path.Combine(folder,name)))
                    using(var reader=new BinaryReader(stream))
                    {
                        if(name.EndsWith(".png",StringComparison.Ordinal))
                        {if(stream.Length<24||reader.ReadUInt64()!=0x0a1a0a0d474e5089UL)errors.Add(name+" (invalid PNG)");}
                        else if(stream.Length<12||new string(reader.ReadChars(4))!="YFF1"||reader.ReadInt32()!=6)
                            errors.Add(name+" (invalid mesh header)");
                    }
                }
                catch(Exception ex){errors.Add(name+" ("+ex.GetType().Name+")");}
            }
            if(errors.Count>0)throw new InvalidDataException("Forestry resources incomplete: "+String.Join(", ",errors));
        }
    }
}
