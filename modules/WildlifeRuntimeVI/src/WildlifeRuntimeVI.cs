using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VOX.WildlifeRuntimeVI
{
    public sealed class WildlifeRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\WildlifeRuntimeVI";
        private const string LogPath = DataDir + "\\WildlifeRuntimeVI.log";
        private const int MaxOwnedAnimals = 12;

        private enum Species { Deer, Coyote, Cougar, Boar, Rabbit, Cow, Hen, Other }
        private sealed class AnimalState
        {
            public int Handle;
            public int ModelHash;
            public Species Species;
            public int BornAt;
            public int LastDecision;
            public int LastThreatAt;
            public int PackSeed;
        }

        private readonly Dictionary<int, AnimalState> _owned = new Dictionary<int, AnimalState>();
        private readonly Random _random = new Random();
        private int _lastSpawnCheck;
        private int _lastBehavior;
        private int _lastCleanup;
        private int _storyYieldUntil;

        private readonly int _deer = H("a_c_deer");
        private readonly int _coyote = H("a_c_coyote");
        private readonly int _cougar = H("a_c_mtlion");
        private readonly int _boar = H("a_c_boar");
        private readonly int _rabbit = H("a_c_rabbit_01");
        private readonly int _cow = H("a_c_cow");
        private readonly int _hen = H("a_c_hen");

        public WildlifeRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Interval = 100;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Wildlife Runtime VI 0.1.0 loaded: bounded natural spawning, herd/pack threat response and predator-prey behavior.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) return;
                if (RockstarOwnsScene()) { _storyYieldUntil = Game.GameTime + 5000; return; }
                if (Game.GameTime < _storyYieldUntil) return;

                int now = Game.GameTime;
                if (now - _lastSpawnCheck >= 6000)
                {
                    _lastSpawnCheck = now;
                    TryNaturalSpawn(player);
                }
                if (now - _lastBehavior >= 650)
                {
                    _lastBehavior = now;
                    UpdateEcology(player);
                }
                if (now - _lastCleanup >= 5000)
                {
                    _lastCleanup = now;
                    Cleanup(player);
                }
            }
            catch (Exception ex) { Log("Wildlife tick error: " + ex.Message); }
        }

        private void TryNaturalSpawn(Ped player)
        {
            if (_owned.Count >= MaxOwnedAnimals || player.IsInVehicle() && SafeSpeed(player.CurrentVehicle) > 38f) return;
            string zone = Zone(player.Position);
            if (!IsRural(zone)) return;

            int hour = SafeHour();
            Species species = ChooseSpecies(zone, hour);
            int model = ModelFor(species);
            if (model == 0 || !IsPedModel(model)) return;

            int group = species == Species.Deer ? _random.Next(2,5) : species == Species.Coyote ? _random.Next(1,4) : species == Species.Rabbit ? _random.Next(1,3) : 1;
            group = Math.Min(group, MaxOwnedAnimals - _owned.Count);
            if (group <= 0) return;

            float baseAngle = (float)(_random.NextDouble() * Math.PI * 2.0);
            float baseDistance = 72f + (float)_random.NextDouble() * 60f;
            Vector3 center = player.Position + new Vector3((float)Math.Cos(baseAngle)*baseDistance,(float)Math.Sin(baseAngle)*baseDistance,10f);
            if (HasPlayerLineOfSightToPoint(player, center)) return;

            int seed = _random.Next();
            for (int i=0;i<group;i++)
            {
                float a=baseAngle+(float)(_random.NextDouble()-0.5)*0.35f;
                Vector3 candidate=center+new Vector3((float)Math.Cos(a)*i*2.4f,(float)Math.Sin(a)*i*2.4f,0f);
                Vector3 ground;
                if (!TryGround(candidate,out ground)) continue;
                if (IsPointOnRoad(ground)) continue;
                Ped animal = SpawnAnimal(model, ground, (float)(_random.NextDouble()*360.0));
                if (animal == null || !animal.Exists()) continue;
                var s=new AnimalState{Handle=animal.Handle,ModelHash=model,Species=species,BornAt=Game.GameTime,LastDecision=0,PackSeed=seed};
                _owned[animal.Handle]=s;
                ConfigureAnimal(animal,species);
            }
        }

        private Species ChooseSpecies(string zone,int hour)
        {
            int r=_random.Next(100);
            bool night=hour>=20||hour<6;
            bool farmland=zone=="GRAPES"||zone=="HARMO"||zone=="SANDY";
            if(farmland&&r<10)return Species.Cow;
            if(farmland&&r>=10&&r<17)return Species.Hen;
            if(night)
            {
                if(r<36)return Species.Coyote;
                if(r<45)return Species.Cougar;
                if(r<60)return Species.Rabbit;
                if(r<73)return Species.Boar;
                return Species.Deer;
            }
            if(r<52)return Species.Deer;
            if(r<68)return Species.Rabbit;
            if(r<82)return Species.Boar;
            if(r<94)return Species.Coyote;
            return Species.Cougar;
        }

        private void UpdateEcology(Ped player)
        {
            bool playerShooting=false, playerArmed=false;
            try{playerShooting=Function.Call<bool>(Hash.IS_PED_SHOOTING,player.Handle);}catch{}
            try{playerArmed=Function.Call<bool>(Hash.IS_PED_ARMED,player.Handle,7);}catch{}
            List<AnimalState> live=_owned.Values.ToList();
            foreach(AnimalState s in live)
            {
                Ped animal=PedFrom(s.Handle);
                if(animal==null||!animal.Exists()||animal.IsDead)continue;
                float d=Distance(animal.Position,player.Position);
                bool prey=s.Species==Species.Deer||s.Species==Species.Rabbit||s.Species==Species.Cow||s.Species==Species.Hen;
                bool predator=s.Species==Species.Coyote||s.Species==Species.Cougar;

                Vector3 firePos;
                bool fire=TryClosestFire(animal.Position,out firePos)&&Distance(animal.Position,firePos)<32f;
                Vehicle threatVehicle=FastVehicleNear(animal.Position);
                bool playerThreat=(playerShooting&&d<90f)||(playerArmed&&d<28f)||(d<10f&&prey);

                if(fire)
                {
                    FleeCoord(animal,firePos,55f,12000);
                    s.LastThreatAt=Game.GameTime;s.LastDecision=Game.GameTime;continue;
                }
                if(threatVehicle!=null)
                {
                    FleeCoord(animal,threatVehicle.Position,40f,8000);
                    s.LastThreatAt=Game.GameTime;s.LastDecision=Game.GameTime;continue;
                }
                if(playerThreat&&prey)
                {
                    FleePed(animal,player,55f,12000);
                    s.LastThreatAt=Game.GameTime;s.LastDecision=Game.GameTime;
                    PropagateHerdThreat(s,player);
                    continue;
                }

                if(predator&&Game.GameTime-s.LastDecision>2500)
                {
                    Ped preyTarget=FindPrey(animal,s.Species);
                    if(preyTarget!=null&&preyTarget.Exists())
                    {
                        float pd=Distance(animal.Position,preyTarget.Position);
                        if(pd<34f)
                        {
                            try{Function.Call(Hash.TASK_COMBAT_PED,animal.Handle,preyTarget.Handle,0,16);}catch{}
                            s.LastDecision=Game.GameTime;continue;
                        }
                    }
                    if(d<13f)
                    {
                        // Coyotes are bolder as a pack; cougars may commit alone.
                        int nearbyPack=CountPackNear(s,animal.Position,16f);
                        bool commit=s.Species==Species.Cougar||nearbyPack>=2;
                        if(commit){try{Function.Call(Hash.TASK_COMBAT_PED,animal.Handle,player.Handle,0,16);}catch{}s.LastDecision=Game.GameTime;continue;}
                    }
                }

                if(Game.GameTime-s.LastDecision>7000)GiveAmbientWander(animal,s.Species,s);
            }
        }

        private void PropagateHerdThreat(AnimalState source,Ped player)
        {
            foreach(AnimalState other in _owned.Values)
            {
                if(other.Handle==source.Handle||other.Species!=source.Species||other.PackSeed!=source.PackSeed)continue;
                Ped p=PedFrom(other.Handle);if(p==null||!p.Exists()||p.IsDead)continue;
                if(Distance(p.Position,player.Position)<85f){FleePed(p,player,55f,12000);other.LastThreatAt=Game.GameTime;other.LastDecision=Game.GameTime;}
            }
        }

        private int CountPackNear(AnimalState source,Vector3 p,float radius)
        {
            int n=0;
            foreach(AnimalState s in _owned.Values)
            {
                if(s.Species!=source.Species||s.PackSeed!=source.PackSeed)continue;
                Ped a=PedFrom(s.Handle);if(a!=null&&a.Exists()&&!a.IsDead&&Distance(a.Position,p)<=radius)n++;
            }
            return n;
        }

        private Ped FindPrey(Ped predator,Species species)
        {
            Ped best=null;float bd=float.MaxValue;
            foreach(AnimalState s in _owned.Values)
            {
                bool valid=species==Species.Cougar?(s.Species==Species.Deer||s.Species==Species.Rabbit):(s.Species==Species.Rabbit||s.Species==Species.Deer);
                if(!valid)continue;
                Ped p=PedFrom(s.Handle);if(p==null||!p.Exists()||p.IsDead)continue;
                float d=Distance(predator.Position,p.Position);if(d<bd){bd=d;best=p;}
            }
            return bd<=42f?best:null;
        }

        private void GiveAmbientWander(Ped animal,Species species,AnimalState s)
        {
            try
            {
                if(species==Species.Cow||species==Species.Hen)Function.Call(Hash.TASK_WANDER_STANDARD,animal.Handle,5f,10);
                else Function.Call(Hash.TASK_WANDER_STANDARD,animal.Handle,10f,10);
                s.LastDecision=Game.GameTime;
            }
            catch{}
        }

        private void Cleanup(Ped player)
        {
            var remove=new List<int>();
            foreach(var pair in _owned)
            {
                Ped p=PedFrom(pair.Key);
                if(p==null||!p.Exists()){remove.Add(pair.Key);continue;}
                if(p.IsDead)
                {
                    if(Game.GameTime-pair.Value.BornAt>180000&&Distance(p.Position,player.Position)>120f){try{p.Delete();}catch{}remove.Add(pair.Key);}
                    continue;
                }
                if(Distance(p.Position,player.Position)>260f)
                {
                    try{p.Delete();}catch{}
                    remove.Add(pair.Key);
                }
            }
            foreach(int h in remove)_owned.Remove(h);
        }

        private static void ConfigureAnimal(Ped p,Species s)
        {
            try
            {
                Function.Call(Hash.SET_PED_KEEP_TASK,p.Handle,true);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL,p.Handle,true);
                Function.Call(Hash.SET_PED_SEEING_RANGE,p.Handle,s==Species.Cougar||s==Species.Coyote?55f:42f);
                Function.Call(Hash.SET_PED_HEARING_RANGE,p.Handle,s==Species.Deer?65f:48f);
                Function.Call(Hash.SET_PED_ALERTNESS,p.Handle,1);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS,p.Handle,false);
                Function.Call(Hash.TASK_WANDER_STANDARD,p.Handle,10f,10);
            }
            catch{}
        }

        private static Ped SpawnAnimal(int model,Vector3 pos,float heading)
        {
            try
            {
                Function.Call(Hash.REQUEST_MODEL,model);
                if(!Function.Call<bool>(Hash.HAS_MODEL_LOADED,model))return null;
                int h=Function.Call<int>(Hash.CREATE_PED,28,model,pos.X,pos.Y,pos.Z,heading,false,false);
                Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED,model);
                return h==0?null:Entity.FromHandle(h) as Ped;
            }
            catch{return null;}
        }

        private static Vehicle FastVehicleNear(Vector3 p)
        {
            Vehicle[] vs;try{vs=World.GetNearbyVehicles(p,18f);}catch{return null;}
            Vehicle best=null;float speed=0f;
            foreach(Vehicle v in vs){if(v==null||!v.Exists())continue;float s=SafeSpeed(v);if(s>8f&&s>speed){speed=s;best=v;}}
            return best;
        }

        private static bool TryClosestFire(Vector3 p,out Vector3 fire)
        {
            fire=Vector3.Zero;var o=new OutputArgument();
            try{if(!Function.Call<bool>(Hash.GET_CLOSEST_FIRE_POS,o,p.X,p.Y,p.Z))return false;fire=o.GetResult<Vector3>();return true;}catch{return false;}
        }
        private static void FleePed(Ped a,Ped threat,float d,int ms){try{Function.Call(Hash.TASK_SMART_FLEE_PED,a.Handle,threat.Handle,d,ms,false,false);}catch{}}
        private static void FleeCoord(Ped a,Vector3 threat,float d,int ms){try{Function.Call(Hash.TASK_SMART_FLEE_COORD,a.Handle,threat.X,threat.Y,threat.Z,d,ms,false,false);}catch{}}
        private static Ped PedFrom(int h){try{return Entity.FromHandle(h) as Ped;}catch{return null;}}
        private static float SafeSpeed(Entity e){try{return e==null?0f:Function.Call<float>(Hash.GET_ENTITY_SPEED,e.Handle);}catch{return 0f;}}
        private static bool IsPedModel(int h){try{return h!=0&&Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE,h)&&Function.Call<bool>(Hash.IS_MODEL_VALID,h)&&Function.Call<bool>(Hash.IS_MODEL_A_PED,h);}catch{return false;}}
        private static bool IsPointOnRoad(Vector3 p){try{return Function.Call<bool>(Hash.IS_POINT_ON_ROAD,p.X,p.Y,p.Z,0);}catch{return true;}}
        private static bool TryGround(Vector3 p,out Vector3 ground)
        {
            ground=p;var z=new OutputArgument();try{if(!Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD,p.X,p.Y,p.Z+40f,z,false,false))return false;ground=new Vector3(p.X,p.Y,z.GetResult<float>());return true;}catch{return false;}
        }
        private static bool HasPlayerLineOfSightToPoint(Ped player,Vector3 p)
        {
            try
            {
                int test=Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,GameplayCamera.Position.X,GameplayCamera.Position.Y,GameplayCamera.Position.Z,p.X,p.Y,p.Z+0.7f,1,player.Handle,7);
                var hit=new OutputArgument();var end=new OutputArgument();var normal=new OutputArgument();var ent=new OutputArgument();
                Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT,test,hit,end,normal,ent);
                return !hit.GetResult<bool>();
            }
            catch{return false;}
        }
        private static string Zone(Vector3 p){try{return(Function.Call<string>(Hash.GET_NAME_OF_ZONE,p.X,p.Y,p.Z)??"").ToUpperInvariant();}catch{return"";}}
        private static bool IsRural(string z)
        {
            switch(z){case"SANDY":case"GRAPES":case"PALETO":case"DESRT":case"ALAMO":case"ZANCUDO":case"HARMO":case"GREATC":case"MTCHIL":case"MTGORDO":case"MTJOSE":case"CANNY":case"TATAMO":case"LAGO":case"PALCOV":case"PROCOB":case"NCHU":return true;default:return false;}
        }
        private static int SafeHour(){try{return Function.Call<int>(Hash.GET_CLOCK_HOURS);}catch{return 12;}}
        private int ModelFor(Species s){switch(s){case Species.Deer:return _deer;case Species.Coyote:return _coyote;case Species.Cougar:return _cougar;case Species.Boar:return _boar;case Species.Rabbit:return _rabbit;case Species.Cow:return _cow;case Species.Hen:return _hen;default:return 0;}}
        private static int H(string n){try{return Function.Call<int>(Hash.GET_HASH_KEY,n);}catch{return 0;}}
        private static float Distance(Vector3 a,Vector3 b){double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return(float)Math.Sqrt(x*x+y*y+z*z);}
        private static bool RockstarOwnsScene()
        {
            try{if(Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE))return true;}catch{}
            try{if(Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))return true;}catch{}
            try{if(Function.Call<bool>(Hash.GET_MISSION_FLAG))return true;}catch{}
            try{if(!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON,Game.Player.Handle))return true;}catch{}
            try{if(Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_IN))return true;}catch{}
            return false;
        }
        private void OnAborted(object sender,EventArgs e){foreach(int h in _owned.Keys.ToList()){try{Ped p=PedFrom(h);if(p!=null&&p.Exists())p.Delete();}catch{}}_owned.Clear();}
        private static void Log(string s){try{Directory.CreateDirectory(DataDir);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+s+Environment.NewLine);}catch{}}
    }
}
