using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.IO;

namespace VOX.CharacterRuntimeVI
{
    public sealed class CharacterRuntimeVIWorkoutScript : Script
    {
        private const string DataDir="scripts\\CharacterRuntimeVI";
        private const string LogPath=DataDir+"\\CharacterRuntimeVI.log";
        private const int ContextControl=51;
        private const int SessionDurationMs=20000;
        private enum WorkoutKind{FreeWeights,BenchPress,ChinUps}

        private sealed class FixedStation{public Vector3 Position;public WorkoutKind Kind;public FixedStation(Vector3 p,WorkoutKind k){Position=p;Kind=k;}}
        private static readonly FixedStation[] MuscleBeachStations=
        {
            new FixedStation(new Vector3(-1202.67f,-1565.53f,4.61f),WorkoutKind.ChinUps),
            new FixedStation(new Vector3(-1210.31f,-1561.34f,4.61f),WorkoutKind.BenchPress),
            new FixedStation(new Vector3(-1198.52f,-1564.12f,4.61f),WorkoutKind.FreeWeights)
        };
        private static readonly string[] BenchProps={"prop_weight_bench_02","prop_muscle_bench_01","prop_muscle_bench_02","prop_muscle_bench_03","prop_muscle_bench_04","prop_muscle_bench_05","prop_muscle_bench_06"};
        private static readonly string[] WeightProps={"prop_barbell_01","prop_barbell_02","prop_barbell_10kg","prop_barbell_20kg","prop_barbell_30kg","prop_barbell_40kg","prop_barbell_50kg","prop_barbell_60kg","prop_barbell_80kg","prop_barbell_100kg","prop_curl_bar_01"};

        private bool _training,_contextWasDown;
        private int _trainingStarted,_cancelAllowedAt,_lastHelp,_lastProbe,_lastScenarioCheck,_storyYieldUntil,_restartCount;
        private bool _nearEquipment;
        private Vector3 _equipmentPosition;
        private float _equipmentHeading;
        private WorkoutKind _kind;

        public CharacterRuntimeVIWorkoutScript()
        {
            Directory.CreateDirectory(DataDir);Interval=40;Tick+=OnTick;Aborted+=OnAborted;
            Log("Workout activity 0.3.0 loaded: chin-ups, bench press and barbell/free-weight sessions with scenario recovery.");
        }

        private void OnTick(object sender,EventArgs e)
        {
            try
            {
                Ped player=Game.LocalPlayerPed;
                if(player==null||!player.Exists()||player.IsDead){Cancel(player,false);return;}
                if(RockstarOwnsScene()){_storyYieldUntil=Game.GameTime+5000;Cancel(player,false);return;}
                if(Game.GameTime<_storyYieldUntil){Cancel(player,false);return;}
                if(_training){UpdateSession(player);return;}
                if(player.IsInVehicle())return;
                int now=Game.GameTime;
                if(now-_lastProbe>=400){_lastProbe=now;_nearEquipment=FindWorkoutEquipment(player,out _equipmentPosition,out _equipmentHeading,out _kind);}
                if(!_nearEquipment)return;
                ShowHelp("~INPUT_CONTEXT~  S'entrainer : "+KindLabel(_kind));
                bool down=ContextDown();bool just=down&&!_contextWasDown;_contextWasDown=down;
                if(!just)return;DisableContext();BeginSession(player);
            }
            catch(Exception ex){Log("Workout tick error: "+ex);}
        }

        private void BeginSession(Ped player)
        {
            if(player==null||!player.Exists())return;
            _training=true;_trainingStarted=Game.GameTime;_cancelAllowedAt=_trainingStarted+1400;_lastScenarioCheck=0;_restartCount=0;
            if(!StartExercise(player)){_training=false;Log("Workout start failed safely for "+_kind+".");return;}
            Log("Workout started kind="+_kind+" equipment="+F(_equipmentPosition)+".");
        }

        private bool StartExercise(Ped player)
        {
            try
            {
                string scenario;
                if(_kind==WorkoutKind.ChinUps)scenario="PROP_HUMAN_MUSCLE_CHIN_UPS";
                else if(_kind==WorkoutKind.BenchPress)scenario="PROP_HUMAN_SEAT_MUSCLE_BENCH_PRESS";
                else scenario="WORLD_HUMAN_MUSCLE_FREE_WEIGHTS";

                if(_kind==WorkoutKind.FreeWeights)Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE,player.Handle,scenario,-1,true);
                else Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION,player.Handle,scenario,_equipmentPosition.X,_equipmentPosition.Y,_equipmentPosition.Z,_equipmentHeading,-1,true,false);
                return true;
            }
            catch(Exception ex){Log("StartExercise "+_kind+" failed: "+ex.Message);return false;}
        }

        private void UpdateSession(Ped player)
        {
            DisableContext();int now=Game.GameTime;
            bool down=ContextDown();bool just=down&&!_contextWasDown;_contextWasDown=down;
            if(now>=_cancelAllowedAt){ShowHelp("~INPUT_CONTEXT~  Arreter   |   "+KindLabel(_kind));if(just){Cancel(player,true);return;}}

            if(now-_lastScenarioCheck>=1100)
            {
                _lastScenarioCheck=now;bool active=false;
                try{active=Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO,player.Handle);}catch{}
                if(!active&&now-_trainingStarted<SessionDurationMs-1000)
                {
                    _restartCount++;
                    if(_restartCount<=4){StartExercise(player);Log("Workout scenario naturally ended/stopped; restarted kind="+_kind+" cycle="+_restartCount+" instead of leaving player frozen.");}
                    else{Cancel(player,false);Notify("Seance interrompue.");return;}
                }
            }

            if(now-_trainingStarted<SessionDurationMs)return;
            try{Function.Call(Hash.CLEAR_PED_TASKS,player.Handle);}catch{}
            _training=false;_trainingStarted=0;_cancelAllowedAt=0;
            if(_kind==WorkoutKind.ChinUps)FitnessRuntimeBridge.Train(1.0f,0.12f,0.58f);
            else if(_kind==WorkoutKind.BenchPress)FitnessRuntimeBridge.Train(1.15f,0.10f,0.52f);
            else FitnessRuntimeBridge.Train(0.85f,0.12f,0.50f);
            Notify("Seance terminee : "+KindLabel(_kind)+".");Log("Workout completed kind="+_kind+" fitness credited.");
        }

        private void Cancel(Ped player,bool manual)
        {
            if(!_training)return;try{if(player!=null&&player.Exists())Function.Call(Hash.CLEAR_PED_TASKS,player.Handle);}catch{}
            _training=false;_trainingStarted=0;_cancelAllowedAt=0;_restartCount=0;if(manual)Log("Workout cancelled manually; no reward.");
        }

        private static bool FindWorkoutEquipment(Ped player,out Vector3 equipment,out float heading,out WorkoutKind kind)
        {
            Vector3 playerPos=player.Position;
            foreach(string name in BenchProps)
            {
                int model=SafeHash(name),obj=ClosestObject(playerPos,model,3.8f);if(obj==0)continue;
                equipment=EntityPos(obj);heading=EntityHeading(obj);kind=WorkoutKind.BenchPress;return true;
            }
            foreach(string name in WeightProps)
            {
                int model=SafeHash(name),obj=ClosestObject(playerPos,model,3.8f);if(obj==0)continue;
                equipment=EntityPos(obj);heading=EntityHeading(obj);kind=WorkoutKind.FreeWeights;return true;
            }
            foreach(FixedStation s in MuscleBeachStations)
            {
                if(Distance(playerPos,s.Position)>3.4f)continue;equipment=s.Position;heading=player.Heading;kind=s.Kind;return true;
            }
            try
            {
                if(Function.Call<bool>(Hash.DOES_SCENARIO_OF_TYPE_EXIST_IN_AREA,playerPos.X,playerPos.Y,playerPos.Z,"PROP_HUMAN_MUSCLE_CHIN_UPS",5f,false))
                {equipment=playerPos;heading=player.Heading;kind=WorkoutKind.ChinUps;return true;}
                if(Function.Call<bool>(Hash.DOES_SCENARIO_OF_TYPE_EXIST_IN_AREA,playerPos.X,playerPos.Y,playerPos.Z,"PROP_HUMAN_SEAT_MUSCLE_BENCH_PRESS",5f,false))
                {equipment=playerPos;heading=player.Heading;kind=WorkoutKind.BenchPress;return true;}
            }
            catch{}
            equipment=Vector3.Zero;heading=0f;kind=WorkoutKind.FreeWeights;return false;
        }

        private static int ClosestObject(Vector3 p,int model,float radius){if(model==0)return 0;try{int h=Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,p.X,p.Y,p.Z,radius,model,false,false,false);if(h!=0&&Function.Call<bool>(Hash.DOES_ENTITY_EXIST,h)&&Distance(p,EntityPos(h))<=radius)return h;}catch{}return 0;}
        private static Vector3 EntityPos(int h){try{return Function.Call<Vector3>(Hash.GET_ENTITY_COORDS,h,true);}catch{return Vector3.Zero;}}
        private static float EntityHeading(int h){try{return Function.Call<float>(Hash.GET_ENTITY_HEADING,h);}catch{return 0f;}}
        private static string KindLabel(WorkoutKind k){return k==WorkoutKind.ChinUps?"tractions":k==WorkoutKind.BenchPress?"developpe couche":"barre / poids libres";}
        private static void DisableContext(){try{Function.Call(Hash.DISABLE_CONTROL_ACTION,0,ContextControl,true);}catch{}}
        private static bool ContextDown(){try{return Function.Call<bool>(Hash.IS_CONTROL_PRESSED,0,ContextControl)||Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED,0,ContextControl);}catch{return false;}}
        private void ShowHelp(string text){if(Game.GameTime-_lastHelp<80)return;_lastHelp=Game.GameTime;try{Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,text);Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP,0,false,true,-1);}catch{}}
        private static void Notify(string text){try{Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,text);Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER,false,false);}catch{}}
        private static bool RockstarOwnsScene(){try{if(Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE))return true;}catch{}try{if(Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))return true;}catch{}try{if(Function.Call<bool>(Hash.GET_MISSION_FLAG))return true;}catch{}try{if(!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON,Game.Player.Handle))return true;}catch{}try{if(Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_IN))return true;}catch{}return false;}
        private static int SafeHash(string name){try{return Function.Call<int>(Hash.GET_HASH_KEY,name);}catch{return 0;}}
        private static float Distance(Vector3 a,Vector3 b){double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return(float)Math.Sqrt(x*x+y*y+z*z);}private static string F(Vector3 p){return p.X.ToString("0.0")+","+p.Y.ToString("0.0")+","+p.Z.ToString("0.0");}
        private void OnAborted(object sender,EventArgs e){Cancel(Game.LocalPlayerPed,false);}
        private static void Log(string text){try{Directory.CreateDirectory(DataDir);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+text+Environment.NewLine);}catch{}}
    }
}
