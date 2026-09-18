using System;
using System.Collections.Generic;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Recolor only this vehicle's neutral prefab surfaces. Shared bundle materials
    // are never edited, so other vehicles and transparent cockpit parts keep theirs.
    public static class ApacheAirframeAppearance
    {
        private static readonly Dictionary<long, Material> variants = new Dictionary<long, Material>();
        private static readonly Color Olive = new Color(.38f, .42f, .29f, 1f);
        private static readonly Color Hardware = new Color(.25f, .29f, .24f, 1f);
        private static readonly Color Rotor = new Color(.15f, .17f, .16f, 1f);

        private static bool Contains(string text, string term)
        { return text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0; }

        private static int Surface(string name)
        {
            if (Contains(name, "glass") || Contains(name, "canopy") || Contains(name, "window") ||
                Contains(name, "windscreen") || Contains(name, "light") || Contains(name, "lamp") ||
                Contains(name, "particle") || Contains(name, "emission")) return -1;
            if (Contains(name, "rotor") || Contains(name, "propeller") || Contains(name, "blade")) return 2;
            if (Contains(name, "wheel") || Contains(name, "tire") || Contains(name, "gear") ||
                Contains(name, "rocket") || Contains(name, "missile") || Contains(name, "weapon")) return 1;
            return 0;
        }

        private static Material Variant(Material source, int surface)
        {
            long key = ((long)(source != null ? source.GetInstanceID() : 0) << 3) + surface;
            if (variants.TryGetValue(key, out var cached) && cached != null) return cached;
            var shader = source != null && source.shader != null && source.shader.isSupported
                ? source.shader : Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null) return null;
            var material = source != null && source.shader == shader ? new Material(source) : new Material(shader);
            material.name = "PZAEC_Apache_" + surface + "_" + (source != null ? source.name : "Missing");
            if (source != null && source.shader != shader && source.mainTexture != null)
            {
                material.mainTexture = source.mainTexture;
                material.mainTextureScale = source.mainTextureScale;
                material.mainTextureOffset = source.mainTextureOffset;
            }
            var baseColor = source != null && source.HasProperty("_Color") ? source.color : Color.white;
            float brightness = Mathf.Clamp01((baseColor.r + baseColor.g + baseColor.b) / 3f);
            var paint = surface == 2 ? Rotor : surface == 1 ? Hardware : Olive;
            paint = new Color(paint.r * brightness, paint.g * brightness, paint.b * brightness, baseColor.a);
            if (material.HasProperty("_Color")) material.SetColor("_Color", paint);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", paint);
            variants[key] = material;
            return material;
        }

        public static void Apply(EntityVehicle vehicle)
        {
            if (GameManager.IsDedicatedServer || !ApacheWeapons.IsApache(vehicle)) return;
            var mesh = ApacheWeapons.GetState(vehicle).Mesh ?? vehicle.ModelTransform;
            if (mesh == null) return;
            int changed = 0;
            foreach (var renderer in mesh.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                if (Contains(renderer.name, "PZAEC_Apache_")) continue;
                var originals = renderer.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < originals.Length; i++)
                {
                    var source = originals[i];
                    if (source != null && source.name.StartsWith("PZAEC_Apache_", StringComparison.Ordinal)) continue;
                    string hint = renderer.name + " " + renderer.transform.parent?.name + " " + source?.name;
                    int surface = Surface(hint);
                    if (surface < 0 || source != null && source.renderQueue >= 3000) continue;
                    if (source != null && source.HasProperty("_Color"))
                    {
                        var c = source.color;
                        float low = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                        float high = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                        if (low < .58f || high - low > .25f || c.a < .9f) continue;
                    }
                    var replacement = Variant(source, surface);
                    if (replacement == null) continue;
                    originals[i] = replacement;
                    dirty = true;
                    changed++;
                }
                if (dirty) renderer.sharedMaterials = originals;
            }
            if (changed > 0) Log.Out("[Apache-Visual] Applied olive airframe palette to " + changed + " materials on vehicle " + vehicle.entityId);
        }

        public static void Clear()
        {
            foreach (var material in variants.Values) if (material != null) UnityEngine.Object.Destroy(material);
            variants.Clear();
        }
    }
}
