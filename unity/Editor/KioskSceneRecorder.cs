using System;
using System.IO;
using System.Linq;
using System.Globalization;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace ImmersiveSmile.StarHeart.Editor
{
    /// <summary>Offline Play Mode capture using the real BLE parser and presentation layer.
    /// Editor-only: never runs in a player, and refuses scenes with network clients.</summary>
    [InitializeOnLoad]
    public static class KioskSceneRecorder
    {
        const string Pending="KioskSceneRecorder.Pending", Root="Temp/KioskRecordings", Report="Web/Kiosk/recordings";
        const int Fps=24, Frames=576, Width=960, Height=540;
        static readonly string[] Names={"StarHeart","ImpossibleGarden","ImpossibleGarden_NoVideos","ImpossibleGarden_Mochi","ImpossibleGarden_Mochi_NoVideos","SleepingOcean","LumiStarfall","DreamRoad","CloudVoyage","FastLumiValley","LumiValley","spaceIsland","Scene_1_Car_dealership","Scene_2_Castle","Scene_R1","Scene_3_farm"};
        static string[] paths;
        static int index,frame,lastFrame,phase,errors,triggers,channels,rate;
        static float warmStart,force,gsr,peak;
        static int bpm;
        static double began;
        static bool audioActive;
        static long audioSamples;
        static Camera camera;
        static AdaptivePresentation presentation;
        static RenderTexture target;
        static Texture2D pixels;
        static StreamWriter trace;
        static BinaryWriter audio;
        static System.Random random;
        static float amplitude=.8f;
        static int cycle=-1;
        static string folder;
        static KioskSceneRecorder(){EditorApplication.playModeStateChanged+=Changed;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}
        [MenuItem("ImmersiveSmile/Kiosk/Record All Scene Previews (Simulated Input)")]
        public static void Begin()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode before starting a recording batch.");
            if(EditorApplication.isCompiling)throw new InvalidOperationException("Wait for compilation.");
            paths=Names.Select(name=>EditorBuildSettings.scenes.Single(s=>s.enabled&&Path.GetFileNameWithoutExtension(s.path)==name).path).ToArray();
            // Check serialized dependencies BEFORE any runtime Awake/Start can connect.
            string[] networkGuids={"79353642dfb05d440b05952b160693f5","9136ed2990ceb42ac977b003254f804a","654dd1b8195819f4e8ea596c7e0d68f1","6b87d95a3dd667d4f9a3c1355e920262"};
            foreach(var path in paths)foreach(var dependency in AssetDatabase.GetDependencies(path,true))
                if(dependency.EndsWith(".unity")||dependency.EndsWith(".prefab"))
                    if(networkGuids.Any(guid=>File.ReadAllText(dependency).Contains(guid)))throw new InvalidOperationException("Network component in capture scene: "+dependency);
            Directory.CreateDirectory(Root);Directory.CreateDirectory(Report);
            File.WriteAllText(Root+"/errors.txt","");
            // Completion markers belong to this batch; never encode an earlier unfinished take.
            foreach(var name in Names)File.Delete(Report+"/"+name+".txt");
            SessionState.SetString("KioskSceneRecorder.Paths",string.Join("|",paths));
            SessionState.SetString("KioskSceneRecorder.OldStart",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Pending,true);
            File.WriteAllText(Root+"/status.txt","Starting offline Unity Play Mode recording batch.");
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(paths[0]);
            EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
        }
        public static void Cancel(){if(SessionState.GetBool(Pending,false)){File.WriteAllText(Root+"/status.txt","Cancelled by operator.");EditorApplication.isPlaying=false;}}
        static void Log(string message,string stack,LogType type)
        {if(SessionState.GetBool(Pending,false)&&(type==LogType.Error||type==LogType.Exception)){errors++;File.AppendAllText(Root+"/errors.txt",message+"\n");}}
        static void Trigger(){triggers++;}
        static void Changed(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Pending,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                paths=SessionState.GetString("KioskSceneRecorder.Paths","").Split('|');index=0;
                target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32);target.Create();pixels=new Texture2D(Width,Height,TextureFormat.RGB24,false);
                RemoteUserInput.Triggered+=Trigger;Time.timeScale=1;Time.captureDeltaTime=1f/Fps;lastFrame=-1;Prepare();
            }
            if(state==PlayModeStateChange.ExitingPlayMode)
            {
                CloseStreams();RemoteUserInput.Triggered-=Trigger;Time.captureDeltaTime=0;Time.timeScale=1;
                if(BLEController.Instance)BLEController.Instance.OnBleDisconnected("Offline recording finished");
                if(presentation)presentation.Release();
                if(target){target.Release();Object.DestroyImmediate(target);}if(pixels)Object.DestroyImmediate(pixels);
            }
            if(state==PlayModeStateChange.EnteredEditMode)
            {
                string old=SessionState.GetString("KioskSceneRecorder.OldStart","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(old)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(old);
                SessionState.SetBool(Pending,false);
            }
        }
        static void Prepare()
        {
            phase=0;frame=errors=triggers=0;cycle=-1;random=new System.Random(20260927+index);camera=null;
            warmStart=Time.time;began=EditorApplication.timeSinceStartup;
            folder=Root+"/"+Names[index];Directory.CreateDirectory(folder);
            // Warm up the actual scene lifecycle. A larger simulation step skips only unrecorded setup.
            Time.captureDeltaTime=.1f;
            File.WriteAllText(Root+"/status.txt",$"{index+1}/{Names.Length}: warming {Names[index]}");
        }
        static void Setup()
        {
            if(Object.FindAnyObjectByType<SceneSelector>(FindObjectsInactive.Include)||Object.FindAnyObjectByType<AdaptiveFirebaseClient>(FindObjectsInactive.Include)||Object.FindAnyObjectByType<BleUploader>(FindObjectsInactive.Include)||Object.FindAnyObjectByType<RemoteFirebaseTriggerSource>(FindObjectsInactive.Include))
                throw new InvalidOperationException("Unexpected network client in offline capture.");
            if(!BLEController.Instance)new GameObject("Offline simulated BLE").AddComponent<BLEController>();
            if(!BleSensorInput.Instance)BLEController.Instance.gameObject.AddComponent<BleSensorInput>();
            if(!Object.FindAnyObjectByType<BleTriggerSource>())BLEController.Instance.gameObject.AddComponent<BleTriggerSource>();
            BLEController.Instance.OnBleStatus("Reading simulated kiosk recording packets");
            camera=Camera.main;
            if(!camera)camera=Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c=>c.enabled&&c.gameObject.scene==SceneManager.GetActiveScene());
            if(!camera)throw new InvalidOperationException("No active scene camera.");
            var host=new GameObject("Offline kiosk adaptation simulation");presentation=host.AddComponent<AdaptivePresentation>();presentation.BindScene(SceneManager.GetActiveScene());
        }
        static void Feed(float t)
        {
            int nextCycle=Mathf.FloorToInt(t/4f);
            if(nextCycle!=cycle){cycle=nextCycle;amplitude=.7f+(float)random.NextDouble()*.48f;}
            float pulse=Mathf.Repeat(t,4f);
            // Rest, gentle rise, variable squeeze, release. Each scene has a repeatable random seed.
            force=pulse<.7f||pulse>3.1f?.1f:Mathf.Lerp(.1f,amplitude,Mathf.Sin(Mathf.PI*(pulse-.7f)/2.4f));
            bpm=72+Mathf.RoundToInt(7*Mathf.Sin(t*.21f));gsr=2.4f+.6f*Mathf.Sin(t*.17f);
            BLEController.Instance.OnBleDataReceived(string.Format(CultureInfo.InvariantCulture,"{0},{1:F3},{2:F3}|",bpm,force,gsr));
        }
        static void BeginFrames()
        {
            frame=0;phase=2;Time.captureDeltaTime=1f/Fps;triggers=0;
            trace=new StreamWriter(Report+"/"+Names[index]+".csv");trace.WriteLine("frame,seconds,simulated_force_N,simulated_BPM,simulated_GSR_uS,sensor_fresh,applied_calm,trigger_count");
            rate=AudioSettings.outputSampleRate;
            switch(AudioSettings.speakerMode){case AudioSpeakerMode.Mono:channels=1;break;case AudioSpeakerMode.Quad:channels=4;break;case AudioSpeakerMode.Surround:channels=5;break;case AudioSpeakerMode.Mode5point1:channels=6;break;case AudioSpeakerMode.Mode7point1:channels=8;break;default:channels=2;break;}
            audioSamples=0;peak=0;audioActive=AudioRenderer.Start();if(audioActive)audio=new BinaryWriter(File.Open(folder+"/audio.f32",FileMode.Create));
        }
        static void Capture()
        {
            var old=RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,Width,Height),0,0);pixels.Apply();
                File.WriteAllBytes(folder+"/"+frame.ToString("D5")+".jpg",pixels.EncodeToJPG(90));
                if(frame==288)File.WriteAllBytes(Report+"/"+Names[index]+".jpg",pixels.EncodeToJPG(92));
            }
            finally{RenderTexture.active=old;}
            if(audioActive)
            {
                int count=AudioRenderer.GetSampleCountForCaptureFrame()*channels;
                using(var data=new NativeArray<float>(count,Allocator.Temp))
                {
                    if(AudioRenderer.Render(data))for(int i=0;i<count;i++){audio.Write(data[i]);peak=Mathf.Max(peak,Mathf.Abs(data[i]));audioSamples++;}
                }
            }
            trace.WriteLine(string.Format(CultureInfo.InvariantCulture,"{0},{1:F3},{2:F3},{3},{4:F3},{5},{6:F3},{7}",frame,frame/(float)Fps,force,bpm,gsr,BleSensorInput.Instance.HasFreshReading,presentation.Calm,triggers));
        }
        static void CloseStreams(){if(audioActive){AudioRenderer.Stop();audioActive=false;}audio?.Dispose();audio=null;trace?.Dispose();trace=null;}
        static void Tick()
        {
            if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isPaused||!target||EditorApplication.isCompiling)return;
            if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            try
            {
                if(EditorApplication.timeSinceStartup-began>300)throw new TimeoutException("Scene recording exceeded five minutes.");
                if(phase==0){Setup();phase=1;return;}
                if(phase==1)
                {
                    Feed(Time.time-warmStart);
                    if(Time.time-warmStart>=(index==0?70:5))BeginFrames();return;
                }
                float t=frame/(float)Fps;
                Feed(t);
                if(frame%192==0)
                {
                    float calm=frame==0?.15f:frame==192?.95f:.35f;
                    presentation.Apply(new AdaptiveFirebaseClient.Command{source="offline_kiosk_simulation",mode="simulated_presentation",visualCalm=calm,motionScale=1-.25f*calm,musicGain=1-.35f*calm,vfxGain=1-.45f*calm,focusStrength=.18f*calm,musicVariant=frame==192?1:0,effectVariant=frame==192?1:0});
                }
                Capture();frame++;
                if(frame%48==0)File.WriteAllText(Root+"/status.txt",$"{index+1}/{Names.Length}: {Names[index]} {frame}/{Frames} frames; BLE triggers {triggers}; calm {presentation.Calm:F2}; errors {errors}");
                if(frame<Frames)return;
                CloseStreams();
                File.WriteAllText(Report+"/"+Names[index]+".txt",$"Actual Unity Play Mode render: {Names[index]}\n{Width}x{Height}, {Fps} fps, {Frames} frames.\nSeed {20260927+index}; synthetic Newton/BPM/GSR packets passed through BLEController. Trigger events={triggers}.\nThree scripted adaptation targets applied through AdaptivePresentation. No DQN inference or training. No Firebase client.\nAudio sample rate={rate}; channels={channels}; samples={audioSamples}; peak={peak.ToString(CultureInfo.InvariantCulture)}.\nRuntime errors={errors}.\nCamera={camera.name}. Scene assets not saved.\n");
                presentation.Release();index++;
                if(index>=Names.Length){File.WriteAllText(Root+"/status.txt","COMPLETE: 16 scene recordings captured.");EditorApplication.isPlaying=false;return;}
                SceneManager.LoadScene(paths[index],LoadSceneMode.Single);Prepare();
            }
            catch(Exception e){CloseStreams();File.WriteAllText(Root+"/status.txt","FAILED: "+Names[Math.Min(index,Names.Length-1)]+"\n"+e);EditorApplication.isPlaying=false;}
        }
    }
}
