// Builds only a generated scene inside the existing duplicate. No VRChat compile dependency.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Yumi.Desktop;

namespace Yumi.Desktop.Editor
{
    public static class YumiDesktopBuild
    {
        const string Output="Assets/YumiDesktopGenerated";
        const string Source="Assets/lordhavemercy.unity";
        static readonly List<string> notes=new List<string>();
        public static string DuplicateRoot()
        {
            DirectoryInfo dir=Directory.GetParent(Application.dataPath);
            while(dir!=null&&!String.Equals(dir.Name,"avatar-unity",StringComparison.OrdinalIgnoreCase))dir=dir.Parent;
            if(dir==null)throw new InvalidOperationException("Use only the designated avatar-unity duplicate.");
            return dir.FullName;
        }
        [MenuItem("Yumi/Create desktop scene from original avatar")]
        public static void CreateScene()
        {
            DuplicateRoot();
            if(Application.unityVersion!="2022.3.22f1")throw new InvalidOperationException("Use the project's original Unity 2022.3.22f1.");
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())throw new OperationCanceledException("Scene generation canceled");
            notes.Clear();Directory.CreateDirectory(Output);AssetDatabase.Refresh();
            SceneSetup[] previous=EditorSceneManager.GetSceneManagerSetup();
            Scene sourceScene=default(Scene),scene=default(Scene);
            try
            {
                // Batch startup has an unsaved untitled scene; replace it before creating an additive scene.
                // Interactive mode retains the saved Editor scene setup for restoration below.
                sourceScene=EditorSceneManager.OpenScene(Source,Application.isBatchMode ? OpenSceneMode.Single : OpenSceneMode.Additive);
                var descriptors=sourceScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true))
                    .Where(c=>c!=null&&c.GetType().Name=="VRCAvatarDescriptor").ToArray();
                if(descriptors.Length!=1)throw new InvalidOperationException("Expected one original avatar descriptor. Restore pinned VPM dependencies first; found "+descriptors.Length);
                GameObject original=descriptors[0].gameObject;
                scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                GameObject placement=new GameObject("Desktop placement");
                GameObject clone=UnityEngine.Object.Instantiate(original,placement.transform,false);clone.name="Yumi original avatar";
                clone.SetActive(true);
                // Unpack only the generated scene instance, leaving original prefab/FBX untouched.
                if(PrefabUtility.IsPartOfPrefabInstance(clone))PrefabUtility.UnpackPrefabInstance(clone,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                var descriptor=clone.GetComponents<Component>().First(c=>c!=null&&c.GetType().Name=="VRCAvatarDescriptor");
                SerializedObject settings=new SerializedObject(descriptor);
                SkinnedMeshRenderer mouth=settings.FindProperty("VisemeSkinnedMesh").objectReferenceValue as SkinnedMeshRenderer;
                if(mouth==null||mouth.sharedMesh==null)throw new InvalidOperationException("Original descriptor face mesh unavailable");
                Animator animator=clone.GetComponent<Animator>();
                if(animator==null||animator.avatar==null||!animator.avatar.isHuman)throw new InvalidOperationException("Original humanoid rig failed to import");
                YumiAvatarRig rig=clone.AddComponent<YumiAvatarRig>();rig.animator=animator;rig.face.mesh=mouth;
                SerializedProperty visemes=settings.FindProperty("VisemeBlendShapes");
                rig.face.aa=visemes.GetArrayElementAtIndex(10).stringValue;
                rig.face.ih=visemes.GetArrayElementAtIndex(12).stringValue;
                rig.face.ou=visemes.GetArrayElementAtIndex(14).stringValue;
                foreach(string shape in new[]{rig.face.aa,rig.face.ih,rig.face.ou})
                    if(mouth.sharedMesh.GetBlendShapeIndex(shape)<0)throw new InvalidOperationException("Missing mapped viseme: "+shape);
                foreach(string shape in new[]{"blink","smile","jitome"})
                    if(mouth.sharedMesh.GetBlendShapeIndex(shape)<0)notes.Add("Optional facial blendshape absent: "+shape);
                string fx="Assets/Proppa/Animations/Controller/Fenriru FX.controller";
                AnimatorController controller=CreateController(fx,rig);
                animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
                // SDK runtime behaviours do not work standalone. Remove them only from this
                // generated clone, and record each missing feature rather than imply it works.
                foreach(Component component in clone.GetComponentsInChildren<Component>(true))
                {
                    if(component==null)continue;
                    string type=component.GetType().FullName;
                    if(type.StartsWith("VRC.")||type.StartsWith("VRCSDK")||type.StartsWith("nadena.")||type.StartsWith("VF.")||type.StartsWith("Lyuma."))
                    {notes.Add("Standalone clone excludes "+type+" on "+component.gameObject.name);UnityEngine.Object.DestroyImmediate(component);}
                }
                foreach(Transform t in clone.GetComponentsInChildren<Transform>(true))GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                foreach(Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
                    foreach(Material material in renderer.sharedMaterials)
                        if(material==null||material.shader==null||material.shader.name=="Hidden/InternalErrorShader")
                            throw new InvalidOperationException("Missing original material/shader on "+renderer.name);
                clone.transform.localPosition=Vector3.zero;clone.transform.localRotation=Quaternion.Euler(0,180,0);
                Bounds bounds=new Bounds(clone.transform.position,Vector3.zero);bool first=true;
                foreach(SkinnedMeshRenderer renderer in clone.GetComponentsInChildren<SkinnedMeshRenderer>())
                {renderer.updateWhenOffscreen=true;if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);}
                if(first||bounds.size.y<.1f)throw new InvalidOperationException("Avatar has no valid visible bounds");
                clone.transform.position-=new Vector3(bounds.center.x,bounds.min.y,0);
                var cameraObject=new GameObject("Overlay camera");Camera camera=cameraObject.AddComponent<Camera>();
                camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=3;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0,0,0,0);
                camera.allowHDR=false;camera.allowMSAA=false;camera.nearClipPlane=.1f;camera.farClipPlane=30;
                var lightObject=new GameObject("Desktop key light");Light light=lightObject.AddComponent<Light>();
                light.type=LightType.Directional;light.intensity=.8f;light.transform.rotation=Quaternion.Euler(35,-25,0);
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
                GameObject host=new GameObject("Yumi desktop runtime");
                var backend=host.AddComponent<YumiBackendClient>();var overlay=host.AddComponent<YumiWindowsOverlay>();
                var avatar=host.AddComponent<YumiDesktopAvatar>();avatar.backend=backend;avatar.overlay=overlay;avatar.rig=rig;
                avatar.view=camera;avatar.placement=placement.transform;avatar.originalHeight=bounds.size.y;
                EditorSceneManager.SaveScene(scene,Output+"/YumiDesktop.unity");
                File.WriteAllLines(Output+"/standalone-adaptation.txt",notes);
                AssetDatabase.SaveAssets();
                Debug.Log("Generated original-avatar scene. Review adaptation report; SDK physics/contacts are excluded, not replaced.");
            }
            finally
            {
                if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
                if(sourceScene.IsValid()&&sourceScene.isLoaded)EditorSceneManager.CloseScene(sourceScene,true);
                if(!Application.isBatchMode)EditorSceneManager.RestoreSceneManagerSetup(previous);
            }
        }
        static AnimatorController CreateController(string fx,YumiAvatarRig rig)
        {
            string path=Output+"/YumiStandalone.controller";
            if(AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!=null)AssetDatabase.DeleteAsset(path);
            if(!AssetDatabase.CopyAsset(fx,path))throw new InvalidOperationException("Original Fenriru FX controller missing");
            AnimatorController controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            foreach(AnimatorControllerLayer layer in controller.layers)StripBehaviours(layer.stateMachine);
            AnimatorStateMachine machine=new AnimatorStateMachine {name="YumiMotion"};AssetDatabase.AddObjectToAsset(machine,controller);
            var bindings=new List<MotionClip>();
            string pose="Assets/GoGo/GoLoco/Animations/Clips/Poses/Clips/";
            foreach(string[] entry in new[]{new[]{"Idle",pose+"go_stand.anim"},new[]{"Talking",pose+"go_stand.anim"},
                new[]{"Sitting",pose+"go_sit_crossed_leg.anim"},new[]{"Waving",pose+"go_stand.anim"},
                new[]{"Jumping","Assets/GoGo/GoLoco/Animations/Clips/Jump&Fall/go_jump_in_place.anim"}})
            {
                AnimationClip clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(entry[1]);
                if(clip==null||!clip.humanMotion) {notes.Add("Unavailable humanoid motion: "+entry[0]);continue;}
                AnimatorState state=machine.AddState(entry[0]);state.motion=clip;state.writeDefaultValues=true;
                if(entry[0]=="Idle")machine.defaultState=state;
                bindings.Add(new MotionClip {state=entry[0],clip=clip});
            }
            if(machine.defaultState==null)throw new InvalidOperationException("Original stand pose not imported as humanoid clip");
            var body=new AnimatorControllerLayer {name="YumiMotion",defaultWeight=1,stateMachine=machine};
            var prior=controller.layers;foreach(var layer in prior)if(layer.syncedLayerIndex>=0)layer.syncedLayerIndex++;
            controller.layers=new[]{body}.Concat(prior).ToArray();rig.clips=bindings.ToArray();
            notes.Add("Original FX copied with parameters/layers. VRChat parameter/layer drivers removed; contacts, PhysBones and menus need standalone replacements.");
            return controller;
        }
        static void StripBehaviours(AnimatorStateMachine machine)
        {
            foreach(StateMachineBehaviour b in machine.behaviours)if(b!=null){notes.Add("Excluded state behaviour "+b.GetType().FullName);UnityEngine.Object.DestroyImmediate(b,true);}
            foreach(ChildAnimatorState state in machine.states)
                foreach(StateMachineBehaviour b in state.state.behaviours)if(b!=null){notes.Add("Excluded state behaviour "+b.GetType().FullName);UnityEngine.Object.DestroyImmediate(b,true);}
            foreach(ChildAnimatorStateMachine child in machine.stateMachines)StripBehaviours(child.stateMachine);
        }
        [MenuItem("Yumi/Build Windows desktop player")]
        public static void BuildWindows()
        {
            CreateScene();
            string output=Path.GetFullPath(Path.Combine(DuplicateRoot(),"../Builds/YumiAvatar/YumiAvatar.exe"));
            string[] args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-yumiOutput")output=Path.GetFullPath(args[i+1]);
            bool flip=PlayerSettings.useFlipModelSwapchain,background=PlayerSettings.runInBackground;
            FullScreenMode full=PlayerSettings.fullScreenMode;
            bool automatic=PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64);
            GraphicsDeviceType[] graphics=PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            try
            {
                PlayerSettings.useFlipModelSwapchain=false;PlayerSettings.runInBackground=true;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{Output+"/YumiDesktop.unity"},locationPathName=output,
                    target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
                if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Unity build failed: "+report.summary.result);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"yumi-avatar-build.json"),"{\"unity\":\""+Application.unityVersion+"\",\"source\":\""+Source+"\",\"render_acceptance\":\"pending Windows visual testing\"}");
            }
            finally
            {PlayerSettings.useFlipModelSwapchain=flip;PlayerSettings.runInBackground=background;PlayerSettings.fullScreenMode=full;
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,graphics);PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,automatic);}
        }
    }
}
