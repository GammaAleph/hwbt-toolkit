// 3D models of a PC arcbank -> Wavefront OBJ + MTL + textures (opens in Blender, 3ds Max, ...).
// Geometry layout as in PredatorCZ/Technyx arc_extract (Street Racing Syndicate), which Hot Wheels: Beat That!
// shares: Mesh = 13 u32 header (materialBase, indexBase, vertexBase ..., numPrimitives) + primitives
// { material, indexBuffer, vertexBuffer, vertexBegin, numUsedVertices, u32, u32, numMods, mods }, a cluster mod
// being { indexStart, indexCount, vertexStart, vertexCount }; VertexBuffer = count, stride, flags + vertices;
// IndexBuffer = count + u16 triangle list; Model = node (2 u32, 4x4 local matrix, 4x4, bbox, unk, parent,
// numChildren, firstChild) + mesh index. Materials are different from SRS (that is where Technyx stops):
// 13 u32 header ([10] = parameter count, [11] = texture slot count), parameters { id, size, data },
// then texture slots { u16 type, u16 count, count x { u32 texture, u32, u32 } }; slot type 0 = diffuse.
// The game is Direct3D (left-handed, clockwise); OBJ is right-handed, so Z is negated (winding follows).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Hwbt
{
    public static class ArcModels
    {
        const byte T_Mesh = 0x09, T_IndexBuffer = 0x0F, T_VertexBuffer = 0x10, T_Model = 0x1D;
        // entry types that are scene nodes (their order gives the parent indices)
        static readonly byte[] NodeTypes = { 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x25, 0x27, 0x28, 0x35, 0x36 };

        class VertexBuffer { public int Count, Stride, Data; public uint Flags; public int Pos = -1, Nrm = -1, Uv = -1; }
        class Cluster { public int IndexStart, IndexCount, VertexStart, VertexCount; }
        class Primitive { public int Material, IndexBuffer, VertexBuffer; public List<Cluster> Clusters = new List<Cluster>(); }
        class Mesh { public string Name; public int MaterialBase, IndexBase, VertexBase; public List<Primitive> Prims = new List<Primitive>(); }
        class Node { public string Name; public byte Type; public float[] Local; public int Parent, Mesh = -1; public int[] Lods; }

        public class Result { public int Models, Triangles, Textures; public List<string> ObjFiles = new List<string>(); }

        const byte T_DeformedModel = 0x1E, T_AnimatedModel = 0x20;

        static int I32(byte[] d, int p) { return BitConverter.ToInt32(d, p); }
        static float F32(byte[] d, int p) { return BitConverter.ToSingle(d, p); }

        public static bool HasModels(ArcBank b)
        {
            return b.Entries.Any(e => e.Type == T_Model || e.Type == T_DeformedModel || e.Type == T_AnimatedModel) && b.Entries.Any(e => e.Type == T_Mesh);
        }

        // Writes <outDir>\<baseName>.obj + .mtl and the textures the materials use. Returns null if the bank has no models.
        // sharedTextures: relative folder (e.g. "../textures") where the bank's textures already are (Unpack and decompile);
        // the .mtl then points there instead of writing copies.
        public static Result ExportObj(byte[] arc, string outDir, string baseName, bool allPng, string sharedTextures = null)
        {
            var b = ArcBank.Parse(arc);   // PC (ARCC) banks only
            if (!HasModels(b)) return null;
            byte[] d = b.Raw;

            var vbs = new List<VertexBuffer>();
            var ibs = new List<int>();          // position of the u16 count of every index buffer
            var meshes = new List<Mesh>();
            var nodes = new List<Node>();
            var materials = new List<ArcEntry>();
            var textures = b.Entries.Where(e => e.Type == ArcBank.T_Texture || e.Type == ArcBank.T_LightmapTexture || e.Type == ArcBank.T_ReferencedTexture).ToList();

            foreach (var e in b.Entries)
            {
                int p = b.AbsOffset(e);
                if (e.Type == T_VertexBuffer) vbs.Add(ReadVertexBuffer(d, p));
                else if (e.Type == T_IndexBuffer) ibs.Add(p);
                else if (e.Type == T_Mesh) meshes.Add(ReadMesh(d, p, e.Name));
                else if (e.Type == ArcBank.T_Material) materials.Add(e);
                else if (Array.IndexOf(NodeTypes, e.Type) >= 0)
                {
                    var n = new Node { Name = e.Name, Type = e.Type, Local = new float[16], Parent = I32(d, p + 8 + 128 + 24 + 4) };
                    for (int i = 0; i < 16; i++) n.Local[i] = F32(d, p + 8 + i * 4);
                    if (e.Type == T_Model || e.Type == T_DeformedModel || e.Type == T_AnimatedModel) n.Mesh = I32(d, p + 176);
                    if ((e.Type == T_DeformedModel || e.Type == T_AnimatedModel) && e.Size >= 276 + 64)
                    {
                        // meshes[16]: level-of-detail versions (LOD1, LOD2, LOD3 in the cars' LODX banks)
                        n.Lods = new int[16];
                        for (int i = 0; i < 16; i++) n.Lods[i] = I32(d, p + 276 + i * 4);
                    }
                    nodes.Add(n);
                }
            }

            Directory.CreateDirectory(outDir);
            var texNames = ArcTools.TextureFileNames(b);
            var res = new Result();

            // materials -> .mtl (diffuse texture written next to it)
            var mtl = new StringBuilder("# HWBT Toolkit - materials of " + baseName + "\r\n");
            var matNames = new string[materials.Count];
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int m = 0; m < materials.Count; m++)
            {
                matNames[m] = SafeName(materials[m].Name, "material" + m) + "_" + m;
                mtl.AppendFormat("\r\nnewmtl {0}\r\nKd 1 1 1\r\n", matNames[m]);
                int tex = DiffuseTexture(d, b.AbsOffset(materials[m]), (int)materials[m].Size);
                if (tex < 0 || tex >= textures.Count || textures[tex].Type == ArcBank.T_ReferencedTexture) continue;
                string file;
                if (!texNames.TryGetValue(textures[tex], out file)) continue;
                file += ArcTools.ImageExtFor(textures[tex], allPng);
                if (written.Add(file))
                {
                    if (sharedTextures != null) res.Textures++;
                    else
                    {
                        try { ArcTools.SaveImage(b.GetData(textures[tex]), Path.Combine(outDir, file)); res.Textures++; }
                        catch (Exception ex) { Archive.Log("  ! " + file + ": " + ex.Message); continue; }
                    }
                }
                mtl.AppendFormat("map_Kd {0}\r\n", sharedTextures != null ? sharedTextures + "/" + file : file);
            }
            File.WriteAllText(Path.Combine(outDir, baseName + ".mtl"), mtl.ToString(), new UTF8Encoding(false));

            // models -> one .obj per level of detail (world transforms baked in); banks without LOD lists
            // (the cars' LOD0 banks, scenery) give a single <bank>.obj
            var levels = new List<int>();
            for (int l = 0; l < 16; l++)
                if (nodes.Any(n => n.Lods != null && n.Lods[l] >= 0 && n.Lods[l] < meshes.Count)) levels.Add(l);
            if (levels.Count == 0) levels.Add(-1);
            foreach (int level in levels)
            {
                string file = Path.Combine(outDir, baseName + (level < 0 ? "" : "_lod" + (level + 1)) + ".obj");
                WriteObj(file, baseName, level, d, nodes, meshes, vbs, ibs, matNames, res);
                res.ObjFiles.Add(file);
            }
            return res;
        }

        static void WriteObj(string file, string baseName, int level, byte[] d, List<Node> nodes, List<Mesh> meshes,
            List<VertexBuffer> vbs, List<int> ibs, string[] matNames, Result res)
        {
            var obj = new StringBuilder();
            obj.AppendFormat("# HWBT Toolkit - models of {0}{1}\r\nmtllib {0}.mtl\r\n", baseName, level < 0 ? "" : ", level of detail " + (level + 1));
            int vBase = 1;
            var ci = CultureInfo.InvariantCulture;
            for (int ni = 0; ni < nodes.Count; ni++)
            {
                var n = nodes[ni];
                int meshIndex = level >= 0 && n.Lods != null ? n.Lods[level] : n.Mesh;
                if (meshIndex < 0 || meshIndex >= meshes.Count) continue;
                float[] world = World(nodes, ni, 0);
                var mesh = meshes[meshIndex];
                obj.AppendFormat("\r\no {0}\r\n", SafeName(n.Name, "model" + ni));
                res.Models++;
                foreach (var prim in mesh.Prims)
                {
                    int vbi = mesh.VertexBase + prim.VertexBuffer, ibi = mesh.IndexBase + prim.IndexBuffer, mi = mesh.MaterialBase + prim.Material;
                    if (vbi < 0 || vbi >= vbs.Count || ibi < 0 || ibi >= ibs.Count) continue;
                    var vb = vbs[vbi];
                    if (vb.Pos < 0) continue;
                    int ib = ibs[ibi], indexCount = I32(d, ib);
                    if (mi >= 0 && mi < matNames.Length) obj.AppendFormat("usemtl {0}\r\n", matNames[mi]);
                    foreach (var c in prim.Clusters)
                    {
                        if (c.VertexStart + c.VertexCount > vb.Count || c.IndexStart + c.IndexCount > indexCount) continue;
                        for (int v = 0; v < c.VertexCount; v++)
                        {
                            int q = vb.Data + (c.VertexStart + v) * vb.Stride;
                            float x = F32(d, q + vb.Pos), y = F32(d, q + vb.Pos + 4), z = F32(d, q + vb.Pos + 8);
                            float wx = x * world[0] + y * world[4] + z * world[8] + world[12];
                            float wy = x * world[1] + y * world[5] + z * world[9] + world[13];
                            float wz = x * world[2] + y * world[6] + z * world[10] + world[14];
                            obj.AppendFormat(ci, "v {0:0.######} {1:0.######} {2:0.######}\r\n", wx, wy, -wz);
                            if (vb.Uv >= 0) obj.AppendFormat(ci, "vt {0:0.######} {1:0.######}\r\n", F32(d, q + vb.Uv), 1 - F32(d, q + vb.Uv + 4));
                            if (vb.Nrm >= 0)
                            {
                                float nx = F32(d, q + vb.Nrm), ny = F32(d, q + vb.Nrm + 4), nz = F32(d, q + vb.Nrm + 8);
                                float tx = nx * world[0] + ny * world[4] + nz * world[8];
                                float ty = nx * world[1] + ny * world[5] + nz * world[9];
                                float tz = nx * world[2] + ny * world[6] + nz * world[10];
                                obj.AppendFormat(ci, "vn {0:0.####} {1:0.####} {2:0.####}\r\n", tx, ty, -tz);
                            }
                        }
                        for (int t = 0; t + 2 < c.IndexCount; t += 3)
                        {
                            int a = U16(d, ib, c.IndexStart + t), bb = U16(d, ib, c.IndexStart + t + 1), cc = U16(d, ib, c.IndexStart + t + 2);
                            if (a == 0xFFFF || bb == 0xFFFF || cc == 0xFFFF) continue;
                            a -= c.VertexStart; bb -= c.VertexStart; cc -= c.VertexStart;
                            if (a < 0 || bb < 0 || cc < 0 || a >= c.VertexCount || bb >= c.VertexCount || cc >= c.VertexCount) continue;
                            obj.Append("f ").Append(Face(vBase + a, vb)).Append(' ').Append(Face(vBase + bb, vb)).Append(' ').Append(Face(vBase + cc, vb)).Append("\r\n");
                            res.Triangles++;
                        }
                        vBase += c.VertexCount;
                    }
                }
            }
            File.WriteAllText(file, obj.ToString(), new UTF8Encoding(false));
        }

        static int U16(byte[] d, int ib, int i) { return BitConverter.ToUInt16(d, ib + 4 + i * 2); }

        static string Face(int i, VertexBuffer vb)
        {
            string s = i.ToString(CultureInfo.InvariantCulture);
            if (vb.Uv >= 0 && vb.Nrm >= 0) return s + "/" + s + "/" + s;
            if (vb.Uv >= 0) return s + "/" + s;
            if (vb.Nrm >= 0) return s + "//" + s;
            return s;
        }

        static VertexBuffer ReadVertexBuffer(byte[] d, int p)
        {
            var vb = new VertexBuffer { Count = I32(d, p), Stride = I32(d, p + 4), Flags = BitConverter.ToUInt32(d, p + 8), Data = p + 12 };
            // attribute order: position, normal, colour, uv0, uv1, uv2, bone weights, deform curve
            int off = 0;
            if ((vb.Flags & 1) != 0) { vb.Pos = off; off += 12; }
            if ((vb.Flags & 4) != 0) { vb.Nrm = off; off += 12; }
            if ((vb.Flags & 2) != 0) off += 4;
            if ((vb.Flags & 8) != 0) { vb.Uv = off; off += 8; }
            if (off > vb.Stride) { vb.Pos = vb.Nrm = vb.Uv = -1; }
            return vb;
        }

        static Mesh ReadMesh(byte[] d, int p, string name)
        {
            var m = new Mesh { Name = name, MaterialBase = I32(d, p + 4), IndexBase = I32(d, p + 8), VertexBase = I32(d, p + 12) };
            int numPrims = I32(d, p + 48);
            p += 52;
            for (int i = 0; i < numPrims && i < 4096; i++)
            {
                var prim = new Primitive { Material = I32(d, p), IndexBuffer = I32(d, p + 4), VertexBuffer = I32(d, p + 8) };
                int numMods = I32(d, p + 28);
                p += 32;
                for (int k = 0; k < numMods && k < 4096; k++)
                {
                    int type = I32(d, p); p += 4;
                    if (type == 0)
                    {
                        prim.Clusters.Add(new Cluster { IndexStart = I32(d, p), IndexCount = I32(d, p + 4), VertexStart = I32(d, p + 8), VertexCount = I32(d, p + 12) });
                        p += 16;
                    }
                    else if (type == 1) p += 4 + I32(d, p) * 4;   // skin: bone list
                    else throw new InvalidDataException("Unknown primitive modifier " + type + " in mesh " + name);
                }
                m.Prims.Add(prim);
            }
            return m;
        }

        // Texture index of the first diffuse slot (type 0), or -1.
        static int DiffuseTexture(byte[] d, int p, int size)
        {
            int end = p + size;
            int numParams = I32(d, p + 40), numSlots = I32(d, p + 44);
            int q = p + 52;
            for (int i = 0; i < numParams && q + 8 <= end; i++) q += 8 + I32(d, q + 4);
            for (int s = 0; s < numSlots && q + 4 <= end; s++)
            {
                int type = BitConverter.ToUInt16(d, q), count = BitConverter.ToUInt16(d, q + 2);
                q += 4;
                if (count > 0 && q + 4 <= end && type == 0)
                {
                    int tex = I32(d, q);
                    return tex == 0xFFFF ? -1 : tex + I32(d, p);   // + textureBaseIndex
                }
                q += count * 12;
            }
            return -1;
        }

        // Local matrices are row-vector (Direct3D) style; world = local * parent's world.
        static float[] World(List<Node> nodes, int i, int depth)
        {
            var n = nodes[i];
            if (n.Parent < 0 || n.Parent >= nodes.Count || n.Parent == i || depth > 64) return n.Local;
            return Mul(n.Local, World(nodes, n.Parent, depth + 1));
        }

        static float[] Mul(float[] a, float[] b)
        {
            var r = new float[16];
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                {
                    float s = 0;
                    for (int k = 0; k < 4; k++) s += a[row * 4 + k] * b[k * 4 + col];
                    r[row * 4 + col] = s;
                }
            return r;
        }

        static string SafeName(string name, string fallback)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            return sb.Length > 0 ? sb.ToString() : fallback;
        }
    }
}
