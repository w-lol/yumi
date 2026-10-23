using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yumi.Desktop.Editor
{
    public static class PantsMeshRepair
    {
        public static void Apply(GameObject clone,SkinnedMeshRenderer primaryFace,string output,List<string> notes)
        {
            Transform pants=clone.transform.Find("pants/Body");
            if(pants==null)return;
            SkinnedMeshRenderer renderer=pants.GetComponent<SkinnedMeshRenderer>();
            if(renderer==null||renderer.sharedMesh==null)return;
            Mesh source=renderer.sharedMesh;Material[] materials=renderer.sharedMaterials;
            if(materials.Length!=3||materials[0]!=null||materials[1]!=null)return;
            string meshPath=AssetDatabase.GetAssetPath(source),hash="";
            if(!String.IsNullOrEmpty(meshPath)&&File.Exists(meshPath))
                using(var algorithm=SHA256.Create())using(var stream=File.OpenRead(meshPath))
                    hash=BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
            Material clothing=materials[2];bool healthy=clothing!=null&&clothing.shader!=null&&clothing.shader.name!="Hidden/InternalErrorShader";
            if(!PantsRepairPolicy.Matches("pants/Body",AssetDatabase.AssetPathToGUID(meshPath),hash,
                clothing==null?"":AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clothing)),
                materials.Length,source.subMeshCount,true,true,renderer!=primaryFace&&source!=primaryFace.sharedMesh,healthy))
                return; // Unrecognized assets remain a clear material error; never guess.
            if(source.GetTopology(0)!=MeshTopology.Triangles||source.GetTopology(1)!=MeshTopology.Triangles||source.GetTopology(2)!=MeshTopology.Triangles)
                throw new InvalidOperationException("Recognized pants has unexpected primitive topology; repair refused.");
            int[] retained=source.GetTriangles(2);
            if(retained.Length==0)throw new InvalidOperationException("Pants clothing submesh is empty; repair refused.");
            Mesh copy=UnityEngine.Object.Instantiate(source);copy.name="YumiPants_ClothingOnly";
            copy.SetTriangles(new int[0],0,false);copy.SetTriangles(new int[0],1,false);
            if(!copy.GetTriangles(2).SequenceEqual(retained)||copy.vertexCount!=source.vertexCount||copy.blendShapeCount!=source.blendShapeCount)
            {UnityEngine.Object.DestroyImmediate(copy);throw new InvalidOperationException("Clothing mesh preservation check failed.");}
            string path=output+"/YumiPants_ClothingOnly.asset";
            Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing==null) {AssetDatabase.CreateAsset(copy,path);existing=copy;}
            else {EditorUtility.CopySerialized(copy,existing);UnityEngine.Object.DestroyImmediate(copy);EditorUtility.SetDirty(existing);}
            renderer.sharedMesh=existing;
            notes.Add("PANTS REPAIR: exact FBX SHA256 "+hash+"; generated copy excludes orphaned donor-eye/blush submeshes 0/1 ("+
                source.GetIndexCount(0)/3+" / "+source.GetIndexCount(1)/3+" triangles). Retained clothing submesh 2: "+
                retained.Length/3+" triangles, original Belts material, vertices, skin weights and blendshapes. Original FBX untouched; visual acceptance still required.");
        }
    }
}
