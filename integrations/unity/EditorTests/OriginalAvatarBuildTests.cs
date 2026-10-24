using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yumi.Desktop.Editor.Tests
{
    public class OriginalAvatarBuildTests
    {
        const string Pants="Assets/ITEMS/Model/tda_dl_pouchy_v1_cyberpunk__by_kakaiya_ddawp25/untitled.fbx";
        const string Belts="Assets/ITEMS/Model/tda_dl_pouchy_v1_cyberpunk__by_kakaiya_ddawp25/Material/Belts.mat";
        [Test] public void OnlyDisabledParticleTrailSlotsAreOptional()
        {
            var host=new GameObject("Particle material validation test");
            try
            {
                var system=host.AddComponent<ParticleSystem>();
                var renderer=host.GetComponent<ParticleSystemRenderer>();
                var trails=system.trails;trails.enabled=false;
                Assert.IsTrue(YumiDesktopBuild.IsUnusedParticleTrailSlot(renderer,1,null));
                Assert.IsFalse(YumiDesktopBuild.IsUnusedParticleTrailSlot(renderer,0,null));
                trails.enabled=true;
                Assert.IsFalse(YumiDesktopBuild.IsUnusedParticleTrailSlot(renderer,1,null));
                var mesh=new GameObject("Mesh material validation test");mesh.transform.SetParent(host.transform);
                Assert.IsFalse(YumiDesktopBuild.IsUnusedParticleTrailSlot(mesh.AddComponent<MeshRenderer>(),1,null));
            }
            finally {UnityEngine.Object.DestroyImmediate(host);}
        }
        [Test] public void OriginalProjectVersionAndFaceDescriptorSourceAreAvailable()
        {
            Assert.AreEqual("2022.3.22f1",Application.unityVersion);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(YumiDesktopBuild.Source);
            Assert.IsNotNull(prefab,"Selected avatar prefab is missing");
            var descriptor=prefab.GetComponents<Component>().SingleOrDefault(c=>c!=null&&c.GetType().Name=="VRCAvatarDescriptor");
            Assert.IsNotNull(descriptor,"Selected prefab root has no loaded descriptor");
            var animator=prefab.GetComponent<Animator>();
            Assert.IsNotNull(animator);Assert.IsNotNull(animator.avatar);Assert.IsTrue(animator.avatar.isHuman);
            var settings=new SerializedObject(descriptor);
            var parameters=YumiDesktopBuild.ResolveExpressionParameters(settings);
            Assert.IsNotNull(parameters,"Neither prefab parameters nor original Fenriru parameters are available");
            if(settings.FindProperty("expressionParameters").objectReferenceValue==null)
                Assert.AreEqual(YumiDesktopBuild.OriginalParameters,AssetDatabase.GetAssetPath(parameters));
        }
        [Test] public void PantsRepairPreservesActualClothingGeometryAndOriginalAsset()
        {
            string output="Assets/YumiDesktopTestTemp_"+Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(output);AssetDatabase.Refresh();
            GameObject root=new GameObject("Pants repair test");
            Mesh faceMesh=new Mesh();
            try
            {
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(Pants);
                Assert.IsNotNull(model,"Original pants FBX is missing");
                var instance=UnityEngine.Object.Instantiate(model,root.transform);instance.name="pants";
                var renderer=instance.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                var primary=new GameObject("Separate Karin face").AddComponent<SkinnedMeshRenderer>();primary.transform.SetParent(root.transform);primary.sharedMesh=faceMesh;
                Mesh original=renderer.sharedMesh;
                Assert.AreEqual(3,original.subMeshCount,"Unexpected FBX import layout");
                int[][] before=Enumerable.Range(0,3).Select(i=>original.GetTriangles(i)).ToArray();
                Assert.Greater(before[0].Length,0);Assert.Greater(before[1].Length,0);Assert.Greater(before[2].Length,0);
                var clothing=AssetDatabase.LoadAssetAtPath<Material>(Belts);Assert.IsNotNull(clothing);
                Assert.IsNotNull(clothing.shader);Assert.AreNotEqual("Hidden/InternalErrorShader",clothing.shader.name,"Restore original Belts shader before building");
                renderer.sharedMaterials=new Material[]{null,null,clothing};
                var notes=new System.Collections.Generic.List<string>();PantsMeshRepair.Apply(root,primary,output,notes);
                Mesh repaired=renderer.sharedMesh;Assert.AreNotSame(original,repaired,"Exact repair signature did not match");
                Assert.AreEqual(0,repaired.GetIndexCount(0));Assert.AreEqual(0,repaired.GetIndexCount(1));
                CollectionAssert.AreEqual(before[2],repaired.GetTriangles(2));
                for(int i=0;i<3;i++)CollectionAssert.AreEqual(before[i],original.GetTriangles(i),"Original mesh was modified");
                CollectionAssert.AreEqual(original.vertices,repaired.vertices);
                CollectionAssert.AreEqual(original.boneWeights,repaired.boneWeights);
                CollectionAssert.AreEqual(original.bindposes,repaired.bindposes);
                Assert.AreEqual(original.blendShapeCount,repaired.blendShapeCount);
                for(int shape=0;shape<original.blendShapeCount;shape++)
                {
                    Assert.AreEqual(original.GetBlendShapeName(shape),repaired.GetBlendShapeName(shape));
                    for(int frame=0;frame<original.GetBlendShapeFrameCount(shape);frame++)
                    {
                        var a=new Vector3[original.vertexCount];var b=new Vector3[original.vertexCount];
                        var an=new Vector3[original.vertexCount];var bn=new Vector3[original.vertexCount];
                        var at=new Vector3[original.vertexCount];var bt=new Vector3[original.vertexCount];
                        original.GetBlendShapeFrameVertices(shape,frame,a,an,at);
                        repaired.GetBlendShapeFrameVertices(shape,frame,b,bn,bt);
                        CollectionAssert.AreEqual(a,b);CollectionAssert.AreEqual(an,bn);CollectionAssert.AreEqual(at,bt);
                    }
                }
                Assert.AreSame(clothing,renderer.sharedMaterials[2]);Assert.AreSame(faceMesh,primary.sharedMesh);Assert.IsNotEmpty(notes);
            }
            finally {UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(faceMesh);AssetDatabase.DeleteAsset(output);}
        }
        [Test] public void PlaybackInterruptionClearsFaceImmediately()
        {
            var timeline=new Yumi.Desktop.PlaybackTimeline();
            timeline.Push(new Yumi.Desktop.PlaybackFrame {Epoch=1,Due=1000,Speaking=true,Aa=1},1000);
            Assert.IsTrue(timeline.Sample(1000).Speaking);
            timeline.Push(new Yumi.Desktop.PlaybackFrame {Epoch=2,Due=1001,Speaking=false},1001);
            Assert.IsFalse(timeline.Sample(1001).Speaking);Assert.AreEqual(0,timeline.Sample(1001).Aa);
        }
        [Test] public void GeneratedSceneRetainsRigMaterialsAndNoSdkRuntimeComponents()
        {
            byte[] originalPrefab=File.ReadAllBytes(YumiDesktopBuild.Source);
            byte[] originalFx=File.ReadAllBytes(YumiDesktopBuild.OriginalFx);
            byte[] originalParameters=File.ReadAllBytes(YumiDesktopBuild.OriginalParameters);
            YumiDesktopBuild.CreateScene();
            CollectionAssert.AreEqual(originalPrefab,File.ReadAllBytes(YumiDesktopBuild.Source),"Source prefab was modified");
            CollectionAssert.AreEqual(originalFx,File.ReadAllBytes(YumiDesktopBuild.OriginalFx),"Original FX controller was modified");
            CollectionAssert.AreEqual(originalParameters,File.ReadAllBytes(YumiDesktopBuild.OriginalParameters),"Original parameters were modified");
            Scene scene=EditorSceneManager.OpenScene("Assets/YumiDesktopGenerated/YumiDesktop.unity",OpenSceneMode.Single);
            var rig=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<YumiAvatarRig>(true)).Single();
            Assert.IsTrue(rig.Ready);Assert.Greater(rig.face.mesh.sharedMesh.blendShapeCount,0);
            Assert.IsNotNull(rig.animator.runtimeAnimatorController);
            foreach(string required in new[]{"Walking","Running","SittingEdge","SidestepLeft","SidestepRight"})
            {
                var clip=rig.clips.Single(c=>c.state==required).clip;
                Assert.IsNotNull(clip,required);Assert.IsTrue(clip.humanMotion,required+" is not humanoid");
            }

            StringAssert.Contains(YumiDesktopBuild.Source,File.ReadAllText("Assets/YumiDesktopGenerated/standalone-adaptation.txt"));
            var controller=rig.animator.runtimeAnimatorController as AnimatorController;Assert.IsNotNull(controller);
            Assert.AreEqual("YumiMotion",controller.layers.Last().name,"Original FX must not override locomotion");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(YumiDesktopBuild.Source);
            var descriptor=prefab.GetComponents<Component>().Single(c=>c!=null&&c.GetType().Name=="VRCAvatarDescriptor");
            var parameterAsset=YumiDesktopBuild.ResolveExpressionParameters(new SerializedObject(descriptor));
            Assert.IsNotNull(parameterAsset);
            var defaults=new SerializedObject(parameterAsset).FindProperty("parameters");
            for(int i=0;i<defaults.arraySize;i++)
            {
                var entry=defaults.GetArrayElementAtIndex(i);
                string name=entry.FindPropertyRelative("name").stringValue;
                float value=entry.FindPropertyRelative("defaultValue").floatValue;
                foreach(var parameter in controller.parameters.Where(p=>p.name==name))
                {
                    if(parameter.type==AnimatorControllerParameterType.Bool)Assert.AreEqual(Mathf.Abs(value)>.5f,parameter.defaultBool,name);
                    else if(parameter.type==AnimatorControllerParameterType.Int)Assert.AreEqual(Mathf.RoundToInt(value),parameter.defaultInt,name);
                    else if(parameter.type==AnimatorControllerParameterType.Float)Assert.AreEqual(value,parameter.defaultFloat,name);
                }
            }
            foreach(var renderer in rig.GetComponentsInChildren<Renderer>(true))
                foreach(var material in renderer.sharedMaterials.Where(m=>m!=null))
                {Assert.IsNotNull(material.shader);Assert.AreNotEqual("Hidden/InternalErrorShader",material.shader.name);}
            foreach(var component in rig.GetComponentsInChildren<Component>(true).Where(c=>c!=null))
                Assert.IsFalse(component.GetType().FullName.StartsWith("VRC."),"SDK runtime component remains on generated rig");
        }
    }
}
