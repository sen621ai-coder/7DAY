using System;

namespace YFPhoenix
{
    public static class PhoenixEntityLookup
    {
        public static int Resolve(string name)
        {
            // FromString hashes the name; negative values are valid registry keys.
            int id = EntityClass.FromString(name);
            EntityClass definition;
            if (!EntityClass.list.TryGetValue(id, out definition) || definition == null)
                throw new InvalidOperationException("Missing phoenix entity class: name=" + name + " id=" + id);
            if (!string.Equals(definition.entityClassName, name, StringComparison.Ordinal))
                throw new InvalidOperationException("Phoenix entity class mismatch: name=" + name + " id=" + id
                    + " registered=" + definition.entityClassName);
            return id;
        }
    }
}
