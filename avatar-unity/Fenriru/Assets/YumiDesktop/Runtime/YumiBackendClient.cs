// Native local transport only. This component neither renders nor assumes an avatar rig.
// Install into the designated duplicate after checking its Unity/.NET compatibility.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Yumi.Desktop
{
    [Serializable] public class MouthState { public float aa, ih, ou; }
    [Serializable] public class AffectState { public float energy, amusement, curiosity, confidence, calm; }
    [Serializable] public class AvatarState
    {
        public string state, style, lip_sync_source;
        public bool active, speaking, topmost, clickthrough, stationary, autonomous, visible=true;
        public int frame_rate=30;
        public float animation_speed=1;
        public bool adaptive_jumps=true,drag_enabled=true,hair_physics=false,typing_attention=true,idle_gestures=false,cursor_reach=false;
        public float hair_damping=.06f,hair_spring=60;
        public float jump_limit=1200,head_yaw=70,head_pitch=35;
        public long audio_epoch, mouth_time_ms;
        public float scale=1;
        public float audio_level, frequency, movement_speed, jump_height;
        public MouthState mouth;
        public AffectState affect;
    }
    [Serializable] public class AvatarAction { public string type, target, expression; }
    [Serializable] class Descriptor { public int protocol; public string session, url, token; }
    [Serializable] class StateEnvelope { public int protocol; public string type, session; public long sequence; public AvatarState payload; }
    [Serializable] class ActionEnvelope { public int protocol; public string type, id; public AvatarAction payload; }
    [Serializable] class MessageHeader { public int protocol; public string type; }
    [Serializable] class SceneWindow {public string id,title,app;}
    [Serializable] class SceneMonitor {public string id,label;}
    [Serializable] class ClientStatus { public bool ready, success; public string renderer, message, action_id,current_window,pointer_window;public SceneWindow[] windows;public SceneMonitor[] monitors; }
    [Serializable] class ClientEnvelope { public int protocol = 1; public string type; public ClientStatus payload; }

    public sealed class YumiBackendClient : MonoBehaviour
    {
        [Tooltip("Yumi installation root. Player may also receive --yumi-root <path>.")]
        public string backendRoot = "";
        public event Action<AvatarState> StateReceived;
        public event Action<string, AvatarAction> ActionReceived;
        public event Action Disconnected;
        public event Action ConnectedToBackend;
        public bool Connected { get { return connected; } }
        public string Diagnostic { get { return diagnostic; } }
        volatile bool connected;
        volatile string diagnostic = "Not connected";
        volatile StateEnvelope latest;
        volatile int generation;
        string lastSession = "";
        long lastSequence;
        CancellationTokenSource cancellation;
        Task worker;
        readonly ConcurrentQueue<ActionEnvelope> actions = new ConcurrentQueue<ActionEnvelope>();
        readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
        bool ready;
        bool reportedConnected;

        void OnEnable()
        {
            int ticket=++generation;ready=false;
            string[] args = Environment.GetCommandLineArgs();
            for (int i=0; i+1<args.Length; ++i) if (args[i]=="--yumi-root") backendRoot=args[i+1];
#if UNITY_EDITOR
            if (String.IsNullOrEmpty(backendRoot)) backendRoot=Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
#endif
            if (String.IsNullOrEmpty(backendRoot)) { diagnostic="Set backendRoot or pass --yumi-root"; return; }
            string descriptor=Path.Combine(Path.GetFullPath(backendRoot),"tmp/avatar-bridge.json");
            cancellation=new CancellationTokenSource();
            worker=ConnectLoop(descriptor,cancellation.Token,ticket);
        }

        void Update()
        {
            if(!reportedConnected && connected && ConnectedToBackend!=null)ConnectedToBackend();
            if (reportedConnected && !connected)
            {
                ready=false;latest=null;lastSequence=0;lastSession="";
                if (Disconnected!=null) Disconnected();
            }
            reportedConnected=connected;
            StateEnvelope state=latest;latest=null;
            if (state!=null && (state.session!=lastSession || state.sequence>lastSequence))
            {
                lastSession=state.session;lastSequence=state.sequence;
                if (StateReceived!=null) StateReceived(state.payload);
            }
            ActionEnvelope action;
            int count=0;
            while(count++<16 && actions.TryDequeue(out action))
            {
                if (!ready || !Allowed(action.payload)) CompleteAction(action.id,false,"Avatar/executor not ready or action unsupported");
                else if (ActionReceived!=null) ActionReceived(action.id,action.payload);
                else CompleteAction(action.id,false,"No Unity action executor is installed");
            }
        }

        public void ReportReady(bool value, string message)
        {
            ready=value;
            Queue(new ClientEnvelope { type="status",payload=new ClientStatus { ready=value,renderer="Unity",message=message } });
        }
        public void CompleteAction(string id,bool success,string message)
        {
            Queue(new ClientEnvelope { type="action_result",payload=new ClientStatus { ready=ready,action_id=id,success=success,message=message } });
        }
        public void ReportScene(DesktopSnapshot scene,string currentWindow,string pointerWindow)
        {
            var windows=new System.Collections.Generic.List<SceneWindow>();
            foreach(var window in scene.Windows)
            {
                if(windows.Count>=32)break;
                string title=window.Title??"",app=window.Application??"";
                windows.Add(new SceneWindow {id=window.Id,title=title.Substring(0,Math.Min(120,title.Length)),app=app.Substring(0,Math.Min(64,app.Length))});
            }
            var monitors=new System.Collections.Generic.List<SceneMonitor>();
            foreach(var monitor in scene.Monitors) {if(monitors.Count>=12)break;monitors.Add(new SceneMonitor {id=monitor.Id,label=monitor.Id});}
            Queue(new ClientEnvelope {type="scene",payload=new ClientStatus {windows=windows.ToArray(),monitors=monitors.ToArray(),current_window=currentWindow,pointer_window=pointerWindow}});
        }
        void Queue(ClientEnvelope value)
        {
            if (outgoing.Count<32) outgoing.Enqueue(JsonUtility.ToJson(value));
        }
        public static bool Allowed(AvatarAction action)
        {
            if (action==null || String.IsNullOrEmpty(action.type) || (action.target!=null && action.target.Length>160)) return false;
            switch(action.type)
            {
                case "move_to_window": case "jump_to_platform": case "walk_to_position": case "run_to_position":
                case "sit": case "stand": case "wave": case "look_at": case "stop_movement": case "set_expression":
                case "stay": case "follow_cursor": case "other_monitor": case "reset_avatar": case "lie_down":case "come_closer":case "step_back":case "resume_roaming":return true;
                default: return false;
            }
        }

        async Task ConnectLoop(string descriptorPath,CancellationToken token,int ticket)
        {
            while(!token.IsCancellationRequested)
            {
                try
                {
                    Descriptor descriptor=JsonUtility.FromJson<Descriptor>(File.ReadAllText(descriptorPath));
                    Uri uri=new Uri(descriptor.url);
                    if (descriptor.protocol!=1 || uri.Scheme!="ws" || uri.Host!="127.0.0.1" || uri.AbsolutePath!="/avatar/v1" || uri.Port<1 || String.IsNullOrEmpty(descriptor.token))
                        throw new InvalidDataException("Invalid loopback bridge descriptor");
                    using(ClientWebSocket socket=new ClientWebSocket())
                    {
                        socket.Options.SetRequestHeader("Authorization","Bearer "+descriptor.token);
                        await socket.ConnectAsync(uri,token).ConfigureAwait(false);
                        if(ticket!=generation)return;
                        connected=true;diagnostic="Connected; avatar readiness is not assumed";
                        string stale;while(outgoing.TryDequeue(out stale)) { }
                        outgoing.Enqueue(JsonUtility.ToJson(new ClientEnvelope { type="hello",payload=new ClientStatus { ready=false,renderer="Unity",message="Transport connected; waiting for actual avatar" } }));
                        using(CancellationTokenSource connection=CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            Task sender=SendLoop(socket,connection.Token);
                            try { await ReceiveLoop(socket,descriptor.session,connection.Token,ticket).ConfigureAwait(false); }
                            finally
                            {
                                connection.Cancel();
                                try { await sender.ConfigureAwait(false); } catch(OperationCanceledException) { }
                            }
                        }
                    }
                }
                catch(OperationCanceledException) { break; }
                catch(Exception error) { if(ticket==generation)diagnostic=error.GetType().Name+": "+error.Message; }
                finally { if(ticket==generation) { connected=false;latest=null;ActionEnvelope a;while(actions.TryDequeue(out a)) { } } }
                try { await Task.Delay(1000,token).ConfigureAwait(false); } catch(OperationCanceledException) { break; }
            }
        }

        async Task SendLoop(ClientWebSocket socket,CancellationToken token)
        {
            while(socket.State==WebSocketState.Open && !token.IsCancellationRequested)
            {
                string message;
                if (outgoing.TryDequeue(out message))
                {
                    byte[] bytes=Encoding.UTF8.GetBytes(message);
                    await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,token).ConfigureAwait(false);
                }
                else await Task.Delay(20,token).ConfigureAwait(false);
            }
        }
        async Task ReceiveLoop(ClientWebSocket socket,string session,CancellationToken token,int ticket)
        {
            byte[] buffer=new byte[16384];
            while(socket.State==WebSocketState.Open && !token.IsCancellationRequested)
            {
                using(MemoryStream message=new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),token).ConfigureAwait(false);
                        if(result.MessageType==WebSocketMessageType.Close)return;
                        if(result.MessageType!=WebSocketMessageType.Text || message.Length+result.Count>16384)throw new InvalidDataException("Invalid or oversized bridge message");
                        message.Write(buffer,0,result.Count);
                    } while(!result.EndOfMessage);
                    string json=Encoding.UTF8.GetString(message.ToArray());
                    MessageHeader header=JsonUtility.FromJson<MessageHeader>(json);
                    if(header.protocol!=1)throw new InvalidDataException("Unsupported bridge protocol");
                    if(header.type=="state")
                    {
                        StateEnvelope state=JsonUtility.FromJson<StateEnvelope>(json);
                        if(state.session!=session || state.payload==null)throw new InvalidDataException("Stale or invalid state");
                        if(ticket==generation)latest=state;
                    }
                    else if(header.type=="action")
                    {
                        ActionEnvelope action=JsonUtility.FromJson<ActionEnvelope>(json);
                        if(!Allowed(action.payload) || actions.Count>=32)throw new InvalidDataException("Invalid action or full action queue");
                        actions.Enqueue(action);
                    }
                    else throw new InvalidDataException("Unsupported bridge frame");
                }
            }
        }

        async void OnDisable()
        {
            ++generation;ready=false;connected=false;latest=null;
            CancellationTokenSource stop=cancellation;Task previous=worker;
            cancellation=null;worker=null;
            if(stop!=null)stop.Cancel();
            if(previous!=null)try { await previous; } catch(Exception) { }
            if(stop!=null)stop.Dispose();
        }
    }
}
