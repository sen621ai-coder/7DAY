using System;
using System.IO;
using Newtonsoft.Json;

namespace PZAEC.Mecha
{
    // Minimal glTF 2.0 binary reader covering the Sketchfab export subset this
    // mod ships: float accessors, ushort/uint indices, embedded PNG images and
    // one TRS animation. Skins, morph targets and sparse accessors throw.
    public sealed class GlbFile
    {
        public sealed class GlbNode
        {
            public string name; public int[] children;
            public float[] translation, scale, rotation;
            public int? mesh;
        }
        public sealed class GlbMesh
        {
            public sealed class Primitive { public GlbAttributes attributes; public int? indices, material; }
            public sealed class GlbAttributes { public int POSITION, NORMAL, TANGENT, TEXCOORD_0; }
            public string name; public Primitive[] primitives;
        }
        public sealed class GlbPbr { public GlbTextureRef baseColorTexture, metallicRoughnessTexture; }
        public sealed class GlbTextureRef { public int index; }
        public sealed class GlbTex { public int source; }
        public sealed class GlbImage { public string mimeType; public int? bufferView; public string uri; }
        public sealed class GlbMaterial
        {
            public string name; public string alphaMode;
            public float[] emissiveFactor;
            public GlbPbr pbrMetallicRoughness;
            public GlbTextureRef normalTexture, emissiveTexture, occlusionTexture;
        }
        public sealed class GlbAccessor
        {
            public int? bufferView; public int byteOffset; public int count;
            public string type; public int componentType; public float[] min, max;
            public object sparse;
        }
        public sealed class GlbView { public int buffer; public int byteOffset, byteLength; public int? byteStride; }
        public sealed class GlbChannel { public GlbTarget target; public int sampler; }
        public sealed class GlbTarget { public int node; public string path; }
        public sealed class GlbSampler { public int input, output; }
        public sealed class GlbAnimation { public string name; public GlbChannel[] channels; public GlbSampler[] samplers; }
        public sealed class GlbScene { public int[] nodes; }

        public GlbNode[] nodes; public GlbMesh[] meshes; public GlbMaterial[] materials;
        public GlbTex[] textures; public GlbImage[] images; public GlbAccessor[] accessors;
        public GlbView[] bufferViews; public GlbAnimation[] animations;
        public GlbScene[] scenes; public int? scene;

        [JsonIgnore] public byte[] bin;

        public const int TypeFloat = 5126, TypeUShort = 5123, TypeUInt = 5125, TypeUByte = 5121;

        public static GlbFile Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 20 || System.Text.Encoding.ASCII.GetString(bytes, 0, 4) != "glTF")
                throw new InvalidDataException("Not a GLB file: " + path);
            int jsonLen = BitConverter.ToInt32(bytes, 12);
            var doc = JsonConvert.DeserializeObject<GlbFile>(System.Text.Encoding.UTF8.GetString(bytes, 20, jsonLen));
            int binHeader = 20 + jsonLen;
            while (binHeader % 4 != 0) binHeader++;
            // Chunk types are little-endian uint32: JSON=0x4E4F534A ("JSON"),
            // BIN=0x004E4942, which on disk reads 'B' 'I' 'N' 0x00.
            if (binHeader + 8 > bytes.Length ||
                bytes[binHeader + 4] != (byte)'B' || bytes[binHeader + 5] != (byte)'I' ||
                bytes[binHeader + 6] != (byte)'N' || bytes[binHeader + 7] != 0)
                throw new InvalidDataException("GLB BIN chunk missing");
            int binLen = BitConverter.ToInt32(bytes, binHeader);
            int dataStart = binHeader + 8;
            if (dataStart + binLen > bytes.Length) throw new InvalidDataException("GLB BIN chunk truncated");
            doc.bin = new byte[binLen];
            Array.Copy(bytes, dataStart, doc.bin, 0, binLen);
            return doc;
        }

        static int Components(string type)
        {
            switch (type)
            {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                case "MAT4": return 16;
                default: throw new InvalidDataException("Unsupported accessor type " + type);
            }
        }

        void Slice(GlbAccessor a, out int offset, out int stride, out int componentSize)
        {
            if (a.bufferView == null || a.sparse != null) throw new InvalidDataException("Sparse/absent bufferView accessor unsupported");
            var view = bufferViews[a.bufferView.Value];
            if (view.buffer != 0) throw new InvalidDataException("Only the GLB BIN buffer is supported");
            componentSize = a.componentType == TypeFloat ? 4 : a.componentType == TypeUShort || a.componentType == TypeUByte ? 2 : a.componentType == TypeUInt ? 4 : 0;
            if (componentSize == 0) throw new InvalidDataException("Unsupported component type " + a.componentType);
            stride = view.byteStride ?? componentSize * Components(a.type);
            if (stride < componentSize * Components(a.type)) throw new InvalidDataException("Stride smaller than component");
            offset = view.byteOffset + a.byteOffset;
            if (offset + stride * (a.count - 1) + componentSize * Components(a.type) > bin.Length)
                throw new InvalidDataException("Accessor range outside BIN");
        }

        public float[] ReadFloats(int accessorIndex)
        {
            var a = accessors[accessorIndex];
            if (a.componentType != TypeFloat) throw new InvalidDataException("Expected FLOAT accessor");
            int offset, stride, size; Slice(a, out offset, out stride, out size);
            int comps = Components(a.type);
            var result = new float[a.count * comps];
            for (int i = 0; i < a.count; i++)
            {
                int baseOffset = offset + stride * i;
                for (int c = 0; c < comps; c++)
                    result[i * comps + c] = BitConverter.ToSingle(bin, baseOffset + c * 4);
            }
            return result;
        }

        public int[] ReadIndices(int accessorIndex)
        {
            var a = accessors[accessorIndex];
            int offset, stride, size; Slice(a, out offset, out stride, out size);
            var result = new int[a.count];
            for (int i = 0; i < a.count; i++)
            {
                int at = offset + stride * i;
                if (a.componentType == TypeUShort) result[i] = BitConverter.ToUInt16(bin, at);
                else if (a.componentType == TypeUInt) result[i] = BitConverter.ToInt32(bin, at);
                else if (a.componentType == TypeUByte) result[i] = bin[at];
                else throw new InvalidDataException("Unsupported index type " + a.componentType);
            }
            return result;
        }

        public byte[] ReadImage(int imageIndex)
        {
            var image = images[imageIndex];
            if (image.bufferView == null) throw new InvalidDataException("Only embedded GLB images are supported");
            var view = bufferViews[image.bufferView.Value];
            var result = new byte[view.byteLength];
            Array.Copy(bin, view.byteOffset, result, 0, view.byteLength);
            return result;
        }
    }
}
