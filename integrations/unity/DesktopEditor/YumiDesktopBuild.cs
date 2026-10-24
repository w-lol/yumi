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
        public const string Source="Assets/prefab-id-v1_avtr_072f0a3c-94e1-4ab0-b9b3-772521da0ae8_5472754516.prefab";
        public const string OriginalFx="Assets/Proppa/Animations/Controller/Fenriru FX.controller";
        public const string OriginalParameters="Assets/Proppa/Menu/Fenriru Parameters.asset";
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
            Scene scene=default(Scene);
            try
            {
                // Read the selected asset directly; the currently open scene is irrelevant.
                GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Source);
                if(prefab==null)throw new InvalidOperationException("Selected avatar prefab is missing: "+Source);
                var descriptors=prefab.GetComponentsInChildren<Component>(true)
                    .Where(c=>c!=null&&c.GetType().Name=="VRCAvatarDescriptor").ToArray();
                if(descriptors.Length!=1)throw new InvalidOperationException("Selected prefab must contain one loaded VRCAvatarDescriptor; found "+descriptors.Length+" in "+Source);
                GameObject original=descriptors[0].gameObject;
                if(original!=prefab)throw new InvalidOperationException("The avatar descriptor must be on the selected prefab root; refusing to discard its parent hierarchy.");
                notes.Add("Avatar source prefab: "+Source);
                // Replace the untitled startup scene only in batch mode.
                scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
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
                string fx=FindFxController(settings);
                AnimatorController controller=CreateController(fx,rig);
                ApplyExpressionDefaults(controller,settings);
                animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
                ConfigureHair(clone,animator);
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
                PantsMeshRepair.Apply(clone,mouth,Output,notes);
                var materialErrors=new List<string>();
                foreach(Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
                {
                    var rendererErrors=new List<string>();
                    Material[] materials=renderer.sharedMaterials;
                    for(int slot=0;slot<materials.Length;slot++)
                    {
                        Material material=materials[slot];
                        if(IsUnusedParticleTrailSlot(renderer,slot,material))
                        {
                            notes.Add("Unused particle trail material slot retained: "+AnimationUtility.CalculateTransformPath(renderer.transform,clone.transform));
                            continue;
                        }
                        SkinnedMeshRenderer skinned=renderer as SkinnedMeshRenderer;
                        MeshFilter filter=renderer.GetComponent<MeshFilter>();
                        Mesh materialMesh=skinned!=null?skinned.sharedMesh:(filter==null?null:filter.sharedMesh);
                        if(material==null&&materialMesh!=null&&slot<materialMesh.subMeshCount&&materialMesh.GetIndexCount(slot)==0)continue;
                        if(material!=null&&material.shader!=null&&material.shader.name!="Hidden/InternalErrorShader")continue;
                        string hierarchy=AnimationUtility.CalculateTransformPath(renderer.transform,clone.transform);
                        string path=material==null?"<missing material>":AssetDatabase.GetAssetPath(material);
                        string originalShader=material==null?"":material.GetTag("OriginalShader",false,"");
                        rendererErrors.Add("Renderer="+hierarchy+"; active="+renderer.gameObject.activeInHierarchy+"; enabled="+renderer.enabled+
                            "; slot="+slot+"; material="+path+"; original shader="+originalShader);
                    }
                    if(rendererErrors.Count>0&&renderer!=mouth&&!renderer.gameObject.activeInHierarchy)
                    {
                        // Do not let expression toggles later expose pink/broken geometry.
                        // Only this generated clone loses the renderer; retain its hierarchy
                        // for controller bindings and retain the complete original prefab.
                        foreach(string detail in rendererErrors)notes.Add("OMITTED inactive renderer with unresolved materials: "+detail);
                        Debug.Log("Generated desktop copy omits inactive renderer with missing materials: "+AnimationUtility.CalculateTransformPath(renderer.transform,clone.transform));
                        UnityEngine.Object.DestroyImmediate(renderer);
                    }
                    else materialErrors.AddRange(rendererErrors);
                }
                // Persist omissions even if another active renderer subsequently fails validation.
                File.WriteAllLines(Output+"/standalone-adaptation.txt",notes);
                if(materialErrors.Count>0)
                {
                    string report=Path.GetFullPath(Path.Combine(DuplicateRoot(),"../logs/avatar-unity/material-errors.txt"));
                    Directory.CreateDirectory(Path.GetDirectoryName(report));File.WriteAllLines(report,materialErrors);
                    throw new InvalidOperationException("Original material/shader references unresolved:\n"+String.Join("\n",materialErrors.ToArray())+"\nReport: "+report);
                }
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
                avatar.view=camera;avatar.placement=placement.transform;
                Transform headBone=animator.GetBoneTransform(HumanBodyBones.Head),footBone=animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                avatar.originalHeight=headBone!=null&&footBone!=null?Mathf.Max(.4f,headBone.position.y-footBone.position.y+.16f*clone.transform.lossyScale.y):bounds.size.y;
                rig.SoleDepth=.035f;
                notes.Add("Standing contact uses animated feet/toes, not renderer bounds; anatomical height="+avatar.originalHeight);
                EditorSceneManager.SaveScene(scene,Output+"/YumiDesktop.unity");
                File.WriteAllLines(Output+"/standalone-adaptation.txt",notes);
                AssetDatabase.SaveAssets();
                Debug.Log("Generated original-avatar scene. Review adaptation report; SDK physics/contacts are excluded, not replaced.");
            }
            finally
            {
                // Batch owns the whole session and quits after this method; leave its
                // last scene loaded. Closing it is invalid and obscures the real error.
                if(!Application.isBatchMode)EditorSceneManager.RestoreSceneManagerSetup(previous);
            }
        }
        public static bool IsUnusedParticleTrailSlot(Renderer renderer,int slot,Material material)
        {
            if(material!=null||slot!=1||!(renderer is ParticleSystemRenderer))return false;
            ParticleSystem system=renderer.GetComponent<ParticleSystem>();
            return system!=null&&!system.trails.enabled;
        }
        static string FindFxController(SerializedObject descriptor)
        {
            SerializedProperty layers=descriptor.FindProperty("baseAnimationLayers");
            if(layers!=null&&layers.isArray)
                for(int i=0;i<layers.arraySize;i++)
                {
                    SerializedProperty layer=layers.GetArrayElementAtIndex(i);
                    SerializedProperty type=layer.FindPropertyRelative("type");
                    if(type==null||type.enumValueIndex<0||type.enumValueIndex>=type.enumNames.Length||type.enumNames[type.enumValueIndex]!="FX")continue;
                    SerializedProperty reference=layer.FindPropertyRelative("animatorController");
                    AnimatorController fx=reference==null?null:reference.objectReferenceValue as AnimatorController;
                    if(reference!=null&&reference.objectReferenceValue!=null&&fx==null)
                        throw new InvalidOperationException("Selected prefab's FX controller type is unsupported.");
                    if(fx==null)
                    {
                        fx=AssetDatabase.LoadAssetAtPath<AnimatorController>(OriginalFx);
                        if(fx==null)throw new InvalidOperationException("Prefab FX link is missing and the original Fenriru controller is unavailable: "+OriginalFx);
                        notes.Add("Repaired missing prefab FX link on generated copy using original project controller: "+OriginalFx);
                    }
                    string path=AssetDatabase.GetAssetPath(fx);notes.Add("Prefab FX controller: "+path);return path;
                }
            throw new InvalidOperationException("Selected prefab has no FX animation layer.");
        }
        static void ConfigureHair(GameObject clone,Animator animator)
        {
            var roots=new List<Transform>();var ignored=new HashSet<Transform>();
            foreach(Component component in clone.GetComponentsInChildren<Component>(true))
            {
                if(component==null||component.GetType().Name!="VRCPhysBone")continue;
                var data=new SerializedObject(component);var field=data.FindProperty("rootTransform");
                Transform root=field==null?null:field.objectReferenceValue as Transform;
                if(root==null)root=component.transform;
                if(!System.Text.RegularExpressions.Regex.IsMatch(root.name,"hair|bang",System.Text.RegularExpressions.RegexOptions.IgnoreCase))continue;
                roots.Add(root);
                var exclude=data.FindProperty("ignoreTransforms");
                if(exclude!=null&&exclude.isArray)for(int i=0;i<exclude.arraySize;i++) {var t=exclude.GetArrayElementAtIndex(i).objectReferenceValue as Transform;if(t!=null)ignored.Add(t);}
            }
            var links=new List<HairLink>();var seen=new HashSet<Transform>();
            foreach(Transform root in roots)
            {
                var pending=new Stack<Transform>();pending.Push(root);
                while(pending.Count>0)
                {
                    Transform bone=pending.Pop();if(ignored.Contains(bone)||!seen.Add(bone))continue;
                    var children=new List<Transform>();
                    foreach(Transform child in bone) {if(ignored.Contains(child))continue;pending.Push(child);if(child.localPosition.sqrMagnitude>.000001f)children.Add(child);}
                    if(children.Count==1)links.Add(new HairLink {bone=bone,localTip=children[0].localPosition});
                }
            }
            if(links.Count==0){notes.Add("No supported original hair spring links found; hair dynamics unavailable.");return;}
            var dynamics=clone.AddComponent<YumiHairDynamics>();dynamics.animator=animator;dynamics.links=links.ToArray();
            notes.Add("Standalone hair dynamics: "+roots.Count+" original PhysBone hair roots; "+links.Count+" links; head/body collision approximation. SDK PhysBone behavior is not reproduced exactly.");
        }
        static AnimatorController CreateController(string fx,YumiAvatarRig rig)
        {
            string path=Output+"/YumiStandalone.controller";
            if(AssetDatabase.LoadAssetAtPath<AnimatorController>(path)!=null)AssetDatabase.DeleteAsset(path);
            if(!AssetDatabase.CopyAsset(fx,path))throw new InvalidOperationException("Could not copy the selected prefab's FX controller: "+fx);
            AnimatorController controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            foreach(AnimatorControllerLayer layer in controller.layers)StripBehaviours(layer.stateMachine);
            AnimatorStateMachine machine=new AnimatorStateMachine {name="YumiMotion"};AssetDatabase.AddObjectToAsset(machine,controller);
            var bindings=new List<MotionClip>();
            string pose="Assets/GoGo/GoLoco/Animations/Clips/Poses/Clips/";
            string locomotion="Assets/Import/walkrun/Animation/Locomotion/";
            string proxy="Assets/Imported/Tail&Ears/Assets/AnimationClip/";
            foreach(string[] entry in new[]{
                new[]{"Idle",proxy+"proxy_stand_still.anim",pose+"go_stand.anim"},
                new[]{"Talking",proxy+"proxy_stand_still.anim",pose+"go_stand.anim"},
                new[]{"IdleArms","Assets/Import/Cute Idle Swing.anim"},
                new[]{"Lying",pose+"go_laydown_chill.anim",pose+"go_laydown_relax(ByGireison).anim"},
                new[]{"Walking",locomotion+"Walk.anim",proxy+"proxy_walk_forward.anim"},
                new[]{"Running",locomotion+"rig_a_loco_jog_Loop.anim",proxy+"proxy_run_forward.anim"},
                new[]{"SidestepRight",proxy+"proxy_run_strafe_right.anim"},
                new[]{"SidestepLeft",proxy+"proxy_run_strafe_right.anim"},
                new[]{"Sitting",pose+"go_short_sitting_idle.anim",pose+"go_short_sitting.anim"},
                new[]{"SittingEdge",pose+"go_chair_sit_float.anim"},
                new[]{"Waving","Assets/Import/v3/v3/EMOTE/Level 1/Hi & Bye/human_hello_friendly.anim","Assets/Import/CodeName ¦ K/AnimationClip/proxy_stand_wave.anim"},
                new[]{"Jumping","Assets/GoGo/GoLoco/Animations/Clips/Jump&Fall/go_jump_in_place.anim"}})
            {
                AnimationClip clip=null;string selected="";
                foreach(string candidate in entry.Skip(1))
                {
                    AnimationClip loaded=AssetDatabase.LoadAssetAtPath<AnimationClip>(candidate);
                    if(loaded!=null&&loaded.humanMotion) {clip=loaded;selected=candidate;break;}
                }
                if(clip==null)throw new InvalidOperationException("Original humanoid animation unavailable for "+entry[0]+": "+String.Join(", ",entry.Skip(1)));
                // Generated copies control looping without editing original clips.
                string clipPath=Output+"/Yumi_"+entry[0]+".anim";
                AnimationClip copied=UnityEngine.Object.Instantiate(clip);copied.name="Yumi "+entry[0];
                if(copied.events.Length>0) {notes.Add("Removed runtime event callbacks from generated motion copy: "+selected);copied.events=new AnimationEvent[0];}
                var clipSettings=AnimationUtility.GetAnimationClipSettings(copied);
                clipSettings.loopTime=entry[0]!="Jumping";AnimationUtility.SetAnimationClipSettings(copied,clipSettings);
                AnimationClip existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if(existing!=null){EditorUtility.CopySerialized(copied,existing);UnityEngine.Object.DestroyImmediate(copied);copied=existing;}
                else AssetDatabase.CreateAsset(copied,clipPath);
                AnimatorState state=machine.AddState(entry[0]);state.motion=copied;state.writeDefaultValues=false;
                state.mirror=entry[0]=="SidestepLeft";
                if(entry[0].StartsWith("Sidestep"))state.speed=.5f;
                if(entry[0]=="Idle")machine.defaultState=state;
                bindings.Add(new MotionClip {state=entry[0],clip=copied});
                notes.Add("Original humanoid motion "+entry[0]+": "+selected);
            }
            if(machine.defaultState==null)throw new InvalidOperationException("Original stand pose not imported as humanoid clip");
            var mask=new AvatarMask {name="Yumi body locomotion"};
            for(int part=0;part<(int)AvatarMaskBodyPart.LastBodyPart;part++)mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part,true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head,false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers,false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers,false);
            AssetDatabase.AddObjectToAsset(mask,controller);
            var body=new AnimatorControllerLayer {name="YumiMotion",defaultWeight=1,stateMachine=machine,avatarMask=mask};
            // Last layer owns body motion; original FX keeps face/outfit/finger bindings.
            var armsMask=new AvatarMask {name="Yumi occasional arms"};
            for(int part=0;part<(int)AvatarMaskBodyPart.LastBodyPart;part++)armsMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part,false);
            armsMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm,true);armsMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm,true);
            AssetDatabase.AddObjectToAsset(armsMask,controller);
            var armsMachine=new AnimatorStateMachine {name="YumiIdleArms"};AssetDatabase.AddObjectToAsset(armsMachine,controller);
            var armsState=armsMachine.AddState("Gentle arm sway");armsState.motion=bindings.First(c=>c.state=="IdleArms").clip;armsState.writeDefaultValues=false;armsMachine.defaultState=armsState;
            var armsLayer=new AnimatorControllerLayer {name="YumiIdleArms",defaultWeight=0,stateMachine=armsMachine,avatarMask=armsMask};
            controller.layers=controller.layers.Concat(new[]{body,armsLayer}).ToArray();rig.clips=bindings.ToArray();
            notes.Add("Original FX copied with parameters/layers. VRChat parameter/layer drivers removed; contacts, PhysBones and menus need standalone replacements.");
            return controller;
        }
        // Read-only resolution: never change or save the source prefab/parameter asset.
        public static UnityEngine.Object ResolveExpressionParameters(SerializedObject descriptor)
        {
            SerializedProperty reference=descriptor.FindProperty("expressionParameters");
            if(reference==null)throw new InvalidOperationException("Selected descriptor has no expressionParameters field.");
            UnityEngine.Object asset=reference.objectReferenceValue;
            if(asset==null)
            {
                asset=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(OriginalParameters);
                if(asset==null)throw new InvalidOperationException("Prefab parameter link is missing and original Fenriru parameters are unavailable: "+OriginalParameters);
                notes.Add("Repaired missing prefab parameter link for generated controller using original project defaults: "+OriginalParameters);
            }
            SerializedProperty values=new SerializedObject(asset).FindProperty("parameters");
            if(values==null||!values.isArray)throw new InvalidOperationException("Original expression parameter defaults could not be read.");
            return asset;
        }
        static void ApplyExpressionDefaults(AnimatorController controller,SerializedObject descriptor)
        {
            SerializedProperty values=new SerializedObject(ResolveExpressionParameters(descriptor)).FindProperty("parameters");
            AnimatorControllerParameter[] parameters=controller.parameters;
            for(int i=0;i<values.arraySize;i++)
            {
                SerializedProperty entry=values.GetArrayElementAtIndex(i);
                string name=entry.FindPropertyRelative("name").stringValue;
                float value=entry.FindPropertyRelative("defaultValue").floatValue;
                foreach(AnimatorControllerParameter parameter in parameters)
                {
                    if(parameter.name!=name)continue;
                    if(parameter.type==AnimatorControllerParameterType.Bool)parameter.defaultBool=Mathf.Abs(value)>.5f;
                    else if(parameter.type==AnimatorControllerParameterType.Float)parameter.defaultFloat=value;
                    else if(parameter.type==AnimatorControllerParameterType.Int)parameter.defaultInt=Mathf.RoundToInt(value);
                    notes.Add("Original expression default retained: "+name+" = "+value);
                }
            }
            controller.parameters=parameters;EditorUtility.SetDirty(controller);
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
