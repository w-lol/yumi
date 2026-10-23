// Additive, read-only inspection of the designated duplicated project.
// Uses public Unity APIs and reflection; no VRChat or Poiyomi assembly dependency.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace Yumi.Desktop.Editor
{
    public static class YumiProjectInspection
    {
        [Serializable] public class Report
        {
            public string unityVersion;
            public string inspection = "Read-only Editor asset inspection; no standalone build performed";
            public List<AvatarReport> avatars = new List<AvatarReport>();
        }
        [Serializable] public class AvatarReport
        {
            public string path;
            public List<string> humanoidAnimators = new List<string>();
            public List<MeshReport> skinnedMeshes = new List<MeshReport>();
            public List<MaterialReport> materials = new List<MaterialReport>();
            public List<string> components = new List<string>();
            public List<ControllerReport> controllers = new List<ControllerReport>();
            public List<string> descriptorReferences = new List<string>();
            public List<string> errors = new List<string>();
        }
        [Serializable] public class MeshReport
        {
            public string name;
            public string meshAsset;
            public string[] blendshapes;
            public string[] bones;
        }
        [Serializable] public class MaterialReport
        {
            public string asset;
            public string shader;
            public bool shaderSupportedInEditor;
            public string[] keywords;
        }
        [Serializable] public class ControllerReport
        {
            public string asset;
            public string[] clips;
            public string[] parameters;
            public string[] layers;
            public string[] stateBehaviours;
        }

        [MenuItem("Yumi/Inspect duplicated avatar project (read only)")]
        public static void Inspect()
        {
            DirectoryInfo parent=Directory.GetParent(Application.dataPath);
            while(parent!=null&&!String.Equals(parent.Name,"avatar-unity",StringComparison.OrdinalIgnoreCase))parent=parent.Parent;
            if(parent==null)throw new InvalidOperationException("Only the designated avatar-unity duplicate is allowed.");
            string boundary=parent.FullName;
            string project=Directory.GetParent(Application.dataPath).FullName;
            Report report = new Report { unityVersion = Application.unityVersion };
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;
                InspectRoot(report,root,path);
            }
            foreach(string guid in AssetDatabase.FindAssets("t:Scene",new[]{"Assets"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                if(path.StartsWith("Assets/YumiDesktopGenerated/"))continue;
                Scene scene=SceneManager.GetSceneByPath(path);bool alreadyLoaded=scene.IsValid()&&scene.isLoaded;
                try
                {
                    if(!alreadyLoaded)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                    foreach(GameObject root in scene.GetRootGameObjects())InspectRoot(report,root,path+"::"+root.name);
                }
                finally {if(!alreadyLoaded&&scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);}
            }
            string output = Path.Combine(Directory.GetParent(boundary).FullName, "logs/avatar-unity/editor-audit.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("Yumi read-only avatar inspection saved: " + output);
        }

        static void InspectRoot(Report report,GameObject root,string path)
        {
                Animator[] animators = root.GetComponentsInChildren<Animator>(true);
                if (!animators.Any(a => a.avatar != null && a.avatar.isHuman)) return;
                AvatarReport avatar = new AvatarReport { path = path };
                try
                {
                    foreach (Animator animator in animators)
                    {
                        avatar.humanoidAnimators.Add(animator.name + ": human=" + (animator.avatar != null && animator.avatar.isHuman));
                        if (animator.runtimeAnimatorController != null) AddController(avatar, animator.runtimeAnimatorController);
                    }
                    foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        Mesh mesh = renderer.sharedMesh;
                        avatar.skinnedMeshes.Add(new MeshReport {
                            name = renderer.name, meshAsset = AssetDatabase.GetAssetPath(mesh),
                            blendshapes = mesh == null ? new string[0] : Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray(),
                            bones = renderer.bones.Select(b => b == null ? "<missing>" : b.name).ToArray()
                        });
                    }
                    HashSet<Material> materials = new HashSet<Material>();
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                        foreach (Material material in renderer.sharedMaterials)
                            if (material != null && materials.Add(material)) avatar.materials.Add(new MaterialReport {
                                asset = AssetDatabase.GetAssetPath(material), shader = material.shader == null ? "<missing>" : material.shader.name,
                                shaderSupportedInEditor = material.shader != null && material.shader.isSupported, keywords = material.shaderKeywords
                            });
                    foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) { avatar.components.Add("<missing script>"); continue; }
                        string type = component.GetType().FullName;
                        avatar.components.Add(type);
                        if (type.IndexOf("AvatarDescriptor", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        SerializedObject serialized = new SerializedObject(component);
                        SerializedProperty property = serialized.GetIterator();
                        while (property.Next(true))
                        {
                            if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null)
                            {
                                UnityEngine.Object value = property.objectReferenceValue;
                                avatar.descriptorReferences.Add(property.propertyPath + " = " + AssetDatabase.GetAssetPath(value));
                                RuntimeAnimatorController controller = value as RuntimeAnimatorController;
                                if (controller != null) AddController(avatar, controller);
                            }
                            else if (property.propertyType == SerializedPropertyType.String &&
                                (property.propertyPath.IndexOf("Viseme", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 property.propertyPath.IndexOf("BlendShape", StringComparison.OrdinalIgnoreCase) >= 0))
                                avatar.descriptorReferences.Add(property.propertyPath + " = " + property.stringValue);
                        }
                    }
                }
                catch (Exception error) { avatar.errors.Add(error.ToString()); }
                report.avatars.Add(avatar);
        }

        static void AddController(AvatarReport avatar, RuntimeAnimatorController source)
        {
            string path = AssetDatabase.GetAssetPath(source);
            if (avatar.controllers.Any(c => c.asset == path)) return;
            AnimatorController controller = source as AnimatorController;
            HashSet<string> behaviours = new HashSet<string>();
            if (controller != null) foreach (AnimatorControllerLayer layer in controller.layers) Collect(layer.stateMachine, behaviours);
            avatar.controllers.Add(new ControllerReport {
                asset = path, clips = source.animationClips.Where(c => c != null).Select(AssetDatabase.GetAssetPath).Distinct().ToArray(),
                parameters = controller == null ? new string[0] : controller.parameters.Select(p => p.name + ":" + p.type).ToArray(),
                layers = controller == null ? new string[0] : controller.layers.Select(l => l.name).ToArray(),
                stateBehaviours = behaviours.ToArray()
            });
        }
        static void Collect(AnimatorStateMachine machine, HashSet<string> behaviours)
        {
            foreach (StateMachineBehaviour behaviour in machine.behaviours) if (behaviour != null) behaviours.Add(behaviour.GetType().FullName);
            foreach (ChildAnimatorState state in machine.states)
                foreach (StateMachineBehaviour behaviour in state.state.behaviours) if (behaviour != null) behaviours.Add(behaviour.GetType().FullName);
            foreach (ChildAnimatorStateMachine child in machine.stateMachines) Collect(child.stateMachine, behaviours);
        }
    }
}
