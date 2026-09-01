using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.VehicleRuntimeVI
{
    public sealed class VehicleRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\VehicleRuntimeVI";
        private const string LogPath = DataDir + "\\VehicleRuntimeVI.log";
        private const string ProfilesPath = DataDir + "\\VehicleProfiles.txt";
        private const string ActiveStatePath = DataDir + "\\ActiveVehicleState.txt";
        private const string QuietDict = "veh@break_in@0h@p_m_one@";
        private const string QuietAnim = "low_force_entry_ds";
        private const string ForceDict = "veh@break_in@0h@p_m_zero@";
        private const string ForceAnim = "std_force_entry_ds";
        private const int InputEnter = 23;
        private const int InputAttack = 24;
        private const int InputCancel = 194;

        private enum BreakPhase { None, Choice, Approach, Animate }
        private enum BreakMode { None, Quiet, Smash }

        private sealed class VehicleProfile
        {
            public string Key = string.Empty;
            public int ModelHash;
            public string Plate = string.Empty;
            public bool HasKey;
            public bool Locked;
            public int LockTier;
            public bool AccessBypassed;
            public bool Hotwired;
            public bool Stolen;
            public bool TrackerPresent;
            public bool TrackerDisabled;
        }

        private readonly Dictionary<string,VehicleProfile> _profiles = new Dictionary<string,VehicleProfile>(StringComparer.OrdinalIgnoreCase);
        private int _breakVehicle, _phaseStarted, _enterHeldSince, _postBreakCooldownUntil;
        private BreakPhase _phase;
        private BreakMode _mode;
        private Vector3 _standPoint, _doorPoint;
        private float _standHeading;
        private bool _enterWasDown, _attackWasDown, _cancelWasDown, _smashDone;
        private int _hotwireVehicle, _hotwireStarted, _lastSave, _lastStateWrite, _lastLockScan, _lastHelp, _storyYieldUntil;

        public VehicleRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            LoadProfiles();
            Interval = 25;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Vehicle Runtime VI 0.6.0 loaded: explicit quiet/smash choice, one-shot door alignment and no break-in task loops.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) { ResetTransient(player); return; }
                if (RockstarOwnsScene()) { _storyYieldUntil = Game.GameTime + 5000; ResetTransient(player); return; }
                if (Game.GameTime < _storyYieldUntil) { ResetTransient(player); return; }

                if (player.IsInVehicle())
                {
                    CancelBreak(player, false);
                    Vehicle current = player.CurrentVehicle;
                    if (current != null && current.Exists() && !IsMissionEntity(current)) UpdateInsideVehicle(current);
                }
                else
                {
                    _hotwireVehicle = 0; _hotwireStarted = 0;
                    if (Game.GameTime - _lastLockScan >= 1400) { _lastLockScan = Game.GameTime; ApplyNearbyAmbientLocks(player); }
                    UpdateEntryInteraction(player);
                }

                int now = Game.GameTime;
                if (now - _lastSave > 12000) { _lastSave = now; SaveProfiles(); }
                if (now - _lastStateWrite > 1000) { _lastStateWrite = now; WriteActiveState(player); }
            }
            catch (Exception ex) { Log("Tick error: " + ex); }
        }

        private void UpdateEntryInteraction(Ped player)
        {
            bool enter = Pressed(InputEnter), attack = Pressed(InputAttack), cancel = Pressed(InputCancel);
            bool enterJust = enter && !_enterWasDown, attackJust = attack && !_attackWasDown, cancelJust = cancel && !_cancelWasDown;
            _enterWasDown = enter; _attackWasDown = attack; _cancelWasDown = cancel;

            if (_phase != BreakPhase.None)
            {
                Vehicle active = FindVehicleByHandle(player.Position, 8f, _breakVehicle);
                if (active == null || !active.Exists() || IsMissionEntity(active) || Distance(player.Position, active.Position) > 5.5f) { CancelBreak(player, true); return; }
                DisableControl(InputEnter);
                if (cancelJust) { CancelBreak(player, true); return; }
                UpdateBreakState(player, active, enter, attackJust);
                return;
            }

            if (Game.GameTime < _postBreakCooldownUntil || !enterJust) return;
            Vehicle target = IntendedEntryVehicle(player);
            if (target == null || !target.Exists() || IsMissionEntity(target)) return;
            VehicleProfile profile = GetProfile(target);
            ApplyDoorState(target, profile);
            if (!profile.Locked || profile.HasKey || profile.AccessBypassed) return;

            DisableControl(InputEnter);
            _breakVehicle = target.Handle;
            _phase = BreakPhase.Choice;
            _mode = BreakMode.None;
            _phaseStarted = Game.GameTime;
            _enterHeldSince = enter ? Game.GameTime : 0;
            _smashDone = false;
            RequestAnim(QuietDict); RequestAnim(ForceDict);
            Log("Locked entry choice opened vehicle=" + target.Handle + " tier=" + profile.LockTier + ". No method selected automatically.");
        }

        private void UpdateBreakState(Ped player, Vehicle vehicle, bool enter, bool attackJust)
        {
            VehicleProfile profile = GetProfile(vehicle);
            if (_phase == BreakPhase.Choice)
            {
                ShowHelp("Maintenir ~INPUT_ENTER~ : crocheter   ~INPUT_ATTACK~ : briser la vitre   ~INPUT_FRONTEND_CANCEL~ : annuler");
                if (attackJust) { BeginChosenMethod(player, vehicle, BreakMode.Smash); return; }
                if (!enter) _enterHeldSince = 0;
                else
                {
                    if (_enterHeldSince == 0) _enterHeldSince = Game.GameTime;
                    if (Game.GameTime - _enterHeldSince >= 650) { BeginChosenMethod(player, vehicle, BreakMode.Quiet); return; }
                }
                if (Game.GameTime - _phaseStarted > 5000) CancelBreak(player, true);
                return;
            }

            if (_phase == BreakPhase.Approach)
            {
                ShowHelp(_mode == BreakMode.Quiet ? "Crochetage..." : "Entrée forcée...");
                if (Distance(player.Position, _standPoint) <= 0.34f)
                {
                    StartAnchoredAnimation(player, vehicle);
                    return;
                }
                if (Game.GameTime - _phaseStarted > 2600)
                {
                    Log("Break-in approach cancelled: player never reached driver-door anchor.");
                    CancelBreak(player, true);
                }
                return;
            }

            if (_phase != BreakPhase.Animate) return;
            int elapsed = Game.GameTime - _phaseStarted;
            if (_mode == BreakMode.Smash && !_smashDone && elapsed >= 580)
            {
                _smashDone = true;
                try { Function.Call(Hash.SMASH_VEHICLE_WINDOW, vehicle.Handle, 0); } catch { }
            }
            int duration = _mode == BreakMode.Smash ? 1200 : 1700 + Math.Max(1, profile.LockTier) * 650;
            if (elapsed < duration) return;

            profile.AccessBypassed = true; profile.Locked = false; profile.Stolen = true;
            try { Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, 1); } catch { }
            TriggerAlarm(vehicle, profile, _mode == BreakMode.Smash ? 88 : (profile.LockTier >= 3 ? 55 : profile.LockTier == 2 ? 32 : 15));
            SaveProfiles();
            StopBreakAnimation(player);
            int handle = vehicle.Handle;
            string method = _mode == BreakMode.Smash ? "smash" : "quiet-lockwork";
            ClearBreakState();
            _postBreakCooldownUntil = Game.GameTime + 1700;
            try { Function.Call(Hash.TASK_ENTER_VEHICLE, player.Handle, handle, 7000, -1, 1.0f, 1, 0); } catch { }
            Log("Vehicle access completed method=" + method + " key=" + profile.Key + ".");
        }

        private void BeginChosenMethod(Ped player, Vehicle vehicle, BreakMode mode)
        {
            _mode = mode;
            _phase = BreakPhase.Approach;
            _phaseStarted = Game.GameTime;
            _doorPoint = DriverDoorPosition(vehicle);
            Vector3 outward = OutwardFromVehicle(vehicle, _doorPoint);
            _standPoint = _doorPoint + outward * 0.68f;
            _standHeading = HeadingTo(_standPoint, _doorPoint);
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, player.Handle);
                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, player.Handle, _standPoint.X, _standPoint.Y, _standPoint.Z, 1.0f, 2200, _standHeading, 0.06f);
            }
            catch { }
            Log("Break-in method selected=" + mode + " vehicle=" + vehicle.Handle + "; one approach task issued.");
        }

        private void StartAnchoredAnimation(Ped player, Vehicle vehicle)
        {
            string dict = _mode == BreakMode.Smash ? ForceDict : QuietDict;
            string anim = _mode == BreakMode.Smash ? ForceAnim : QuietAnim;
            RequestAnim(dict);
            if (!AnimLoaded(dict)) return;
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, player.Handle);
                Function.Call(Hash.TASK_PLAY_ANIM_ADVANCED, player.Handle, dict, anim,
                    _standPoint.X, _standPoint.Y, _standPoint.Z,
                    0f, 0f, _standHeading, 4.0f, -4.0f, -1, 0, 0f, 0, 0);
            }
            catch
            {
                try { Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, dict, anim, 4f, -4f, -1, 0, 0f, false, false, false); } catch { }
            }
            _phase = BreakPhase.Animate;
            _phaseStarted = Game.GameTime;
            _smashDone = false;
            Log("Break-in animation started once at driver-door anchor vehicle=" + vehicle.Handle + ".");
        }

        private void ApplyNearbyAmbientLocks(Ped player)
        {
            Vehicle[] vehicles;
            try { vehicles = World.GetNearbyVehicles(player, 38f); } catch { return; }
            foreach (Vehicle v in vehicles)
            {
                if (v == null || !v.Exists() || IsMissionEntity(v) || IsLikelyPersonalVehicle(v)) continue;
                Ped driver = null; try { driver = v.Driver; } catch { }
                if (driver != null && driver.Exists()) continue;
                float speed = SafeSpeed(v); if (speed > 0.8f) continue;
                VehicleProfile p = GetProfile(v); ApplyDoorState(v, p);
            }
        }

        private void UpdateInsideVehicle(Vehicle vehicle)
        {
            VehicleProfile profile = GetProfile(vehicle);
            bool personal = IsLikelyPersonalVehicle(vehicle);
            if (profile.HasKey || personal) { NormalizePersonalProfile(profile); return; }
            profile.Stolen = true; profile.Locked = false; profile.AccessBypassed = true;
            if (IsEngineRunning(vehicle))
            {
                if (!profile.Hotwired) { profile.Hotwired = true; SaveProfiles(); }
                _hotwireVehicle = 0; _hotwireStarted = 0; return;
            }
            if (profile.Hotwired) return;
            if (_hotwireVehicle != vehicle.Handle) { _hotwireVehicle = vehicle.Handle; _hotwireStarted = Game.GameTime; Log("Ignition bypass started key=" + profile.Key + "."); }
            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, false, true, true); } catch { }
            int duration = 1800 + Math.Max(1, profile.LockTier) * 700;
            if (Game.GameTime - _hotwireStarted < duration) return;
            profile.Hotwired = true;
            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false); } catch { }
            SaveProfiles(); Log("Ignition bypass completed key=" + profile.Key + " tracker=" + profile.TrackerPresent + ".");
        }

        private VehicleProfile GetProfile(Vehicle vehicle)
        {
            string plate = Plate(vehicle); int model = vehicle.Model.Hash; string key = model.ToString(CultureInfo.InvariantCulture) + ":" + plate; bool personal = IsLikelyPersonalVehicle(vehicle);
            VehicleProfile p;
            if (_profiles.TryGetValue(key, out p)) { if (personal) NormalizePersonalProfile(p); return p; }
            bool occupied = false; try { occupied = vehicle.Driver != null && vehicle.Driver.Exists(); } catch { }
            int roll = StableRoll(key);
            bool locked = !personal && !occupied && roll < 90;
            p = new VehicleProfile { Key=key,ModelHash=model,Plate=plate,HasKey=personal,Locked=locked,LockTier=personal?0:(roll<35?1:roll<78?2:3),TrackerPresent=!personal&&StableRoll(key+":tracker")<(IsPremium(vehicle)?74:30) };
            if (personal) NormalizePersonalProfile(p); _profiles[key] = p; return p;
        }

        private static void NormalizePersonalProfile(VehicleProfile p){if(p==null)return;p.HasKey=true;p.Locked=false;p.AccessBypassed=false;p.Hotwired=false;p.Stolen=false;p.TrackerDisabled=false;}
        private static void ApplyDoorState(Vehicle v,VehicleProfile p){bool lockIt=p.Locked&&!p.AccessBypassed&&!p.HasKey;try{Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED,v.Handle,lockIt?2:1);}catch{}}

        private static void TriggerAlarm(Vehicle v,VehicleProfile p,int chance)
        {
            if(StableRoll(p.Key+":alarm:"+Game.GameTime/10000)>=chance)return;
            try{Function.Call(Hash.SET_VEHICLE_ALARM,v.Handle,true);Function.Call(Hash.START_VEHICLE_ALARM,v.Handle);}catch{}
        }

        private static Vehicle IntendedEntryVehicle(Ped player)
        {
            try { int handle=Function.Call<int>(Hash.GET_VEHICLE_PED_IS_TRYING_TO_ENTER,player.Handle);if(handle!=0){Vehicle v=Entity.FromHandle(handle) as Vehicle;if(v!=null&&v.Exists())return v;} } catch { }
            Vehicle[] vehicles;try{vehicles=World.GetNearbyVehicles(player,2.6f);}catch{return null;}Vector3 cam=GameplayCamera.Direction;Vehicle best=null;float scoreBest=float.MinValue;
            foreach(Vehicle v in vehicles){if(v==null||!v.Exists())continue;Vector3 d=v.Position-player.Position;float len=Length(d);if(len<0.1f||len>2.6f)continue;float dot=Dot(cam,d)/len;if(dot<0.45f)continue;float score=dot*10f-len;if(score>scoreBest){scoreBest=score;best=v;}}return best;
        }

        private static Vector3 DriverDoorPosition(Vehicle v)
        {
            try{int bone=Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME,v.Handle,"door_dside_f");if(bone>=0)return Function.Call<Vector3>(Hash.GET_WORLD_POSITION_OF_ENTITY_BONE,v.Handle,bone);}catch{}
            try{return Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS,v.Handle,-0.85f,0.35f,0.35f);}catch{return v.Position;}
        }
        private static Vector3 OutwardFromVehicle(Vehicle v,Vector3 door){try{Vector3 d=door-v.Position;float len=(float)Math.Sqrt(d.X*d.X+d.Y*d.Y);if(len>0.05f)return new Vector3(d.X/len,d.Y/len,0f);}catch{}return new Vector3(-1f,0f,0f);}
        private static Vehicle FindVehicleByHandle(Vector3 pos,float radius,int handle){if(handle==0)return null;Vehicle[] vs;try{vs=World.GetNearbyVehicles(pos,radius);}catch{return null;}foreach(Vehicle v in vs)if(v!=null&&v.Exists()&&v.Handle==handle)return v;return null;}
        private static bool IsLikelyPersonalVehicle(Vehicle v){int h=v.Model.Hash;return h==SafeHash("tailgater")||h==SafeHash("buffalo2")||h==SafeHash("bodhi2");}
        private static bool IsEngineRunning(Vehicle v){try{return v!=null&&v.Exists()&&Function.Call<bool>(Hash.GET_IS_VEHICLE_ENGINE_RUNNING,v.Handle);}catch{return false;}}
        private static bool IsPremium(Vehicle v){try{int cls=Function.Call<int>(Hash.GET_VEHICLE_CLASS,v.Handle);return cls==3||cls==5||cls==6||cls==7||cls==22;}catch{return false;}}
        private static bool IsMissionEntity(Entity e){try{return Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY,e.Handle);}catch{return true;}}
        private static float SafeSpeed(Entity e){try{return Function.Call<float>(Hash.GET_ENTITY_SPEED,e.Handle);}catch{return 0f;}}
        private static int SafeHash(string n){try{return Function.Call<int>(Hash.GET_HASH_KEY,n);}catch{return 0;}}
        private static void RequestAnim(string d){try{Function.Call(Hash.REQUEST_ANIM_DICT,d);}catch{}}
        private static bool AnimLoaded(string d){try{return Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED,d);}catch{return false;}}
        private static void StopBreakAnimation(Ped p){if(p==null||!p.Exists())return;try{Function.Call(Hash.STOP_ANIM_TASK,p.Handle,QuietDict,QuietAnim,1.5f);Function.Call(Hash.STOP_ANIM_TASK,p.Handle,ForceDict,ForceAnim,1.5f);}catch{}}

        private void CancelBreak(Ped player,bool log)
        {
            if(_phase==BreakPhase.None)return;StopBreakAnimation(player);if(log)Log("Break-in choice/action cancelled cleanly vehicle="+_breakVehicle+".");ClearBreakState();_postBreakCooldownUntil=Game.GameTime+650;
        }
        private void ClearBreakState(){_breakVehicle=0;_phase=BreakPhase.None;_mode=BreakMode.None;_phaseStarted=0;_enterHeldSince=0;_standPoint=Vector3.Zero;_doorPoint=Vector3.Zero;_standHeading=0f;_smashDone=false;}

        private static bool RockstarOwnsScene()
        {
            try{if(Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE))return true;}catch{}
            try{if(Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))return true;}catch{}
            try{if(Function.Call<bool>(Hash.GET_MISSION_FLAG))return true;}catch{}
            try{if(!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON,Game.Player.Handle))return true;}catch{}
            try{if(Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_IN))return true;}catch{}
            return false;
        }
        private static void DisableControl(int c){try{Function.Call(Hash.DISABLE_CONTROL_ACTION,0,c,true);}catch{}}
        private static bool Pressed(int c){try{return Function.Call<bool>(Hash.IS_CONTROL_PRESSED,0,c)||Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED,0,c);}catch{return false;}}
        private void ShowHelp(string text){if(Game.GameTime-_lastHelp<80)return;_lastHelp=Game.GameTime;try{Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP,"STRING");Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,text);Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP,0,false,true,-1);}catch{}}

        private void WriteActiveState(Ped player)
        {
            try{if(!player.IsInVehicle()){File.WriteAllText(ActiveStatePath,"none");return;}Vehicle v=player.CurrentVehicle;if(v==null||!v.Exists()||IsMissionEntity(v)){File.WriteAllText(ActiveStatePath,"none");return;}VehicleProfile p=GetProfile(v);File.WriteAllText(ActiveStatePath,"model="+p.ModelHash+"\nplate="+p.Plate+"\nstolen="+p.Stolen+"\nhotwired="+p.Hotwired+"\ntrackerPresent="+p.TrackerPresent+"\ntrackerDisabled="+p.TrackerDisabled+"\n");}catch{}
        }
        private void LoadProfiles(){if(!File.Exists(ProfilesPath))return;try{foreach(string line in File.ReadAllLines(ProfilesPath)){string[] p=line.Split('|');if(p.Length<10)continue;var v=new VehicleProfile{Key=p[0],ModelHash=PI(p[1]),Plate=p[2],HasKey=PB(p[3]),Locked=PB(p[4]),LockTier=PI(p[5]),AccessBypassed=PB(p[6]),Hotwired=PB(p[7]),Stolen=PB(p[8]),TrackerPresent=PB(p[9]),TrackerDisabled=p.Length>10&&PB(p[10])};if(!string.IsNullOrWhiteSpace(v.Key))_profiles[v.Key]=v;}}catch(Exception ex){Log("Profile load failed safely: "+ex.Message);}}
        private void SaveProfiles(){try{var lines=new List<string>();foreach(VehicleProfile p in _profiles.Values)lines.Add(string.Join("|",p.Key,p.ModelHash,p.Plate,p.HasKey,p.Locked,p.LockTier,p.AccessBypassed,p.Hotwired,p.Stolen,p.TrackerPresent,p.TrackerDisabled));File.WriteAllLines(ProfilesPath,lines);}catch(Exception ex){Log("Profile save failed safely: "+ex.Message);}}
        private static string Plate(Vehicle v){try{return(Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT,v.Handle)??"").Trim().ToUpperInvariant();}catch{return string.Empty;}}
        private static int StableRoll(string text){unchecked{int h=17;foreach(char c in text??string.Empty)h=h*31+c;if(h==int.MinValue)h=0;return Math.Abs(h)%100;}}
        private static float HeadingTo(Vector3 from,Vector3 to){try{return Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D,to.X-from.X,to.Y-from.Y);}catch{return 0f;}}
        private static float Dot(Vector3 a,Vector3 b){return a.X*b.X+a.Y*b.Y+a.Z*b.Z;}
        private static float Length(Vector3 v){return(float)Math.Sqrt(v.X*v.X+v.Y*v.Y+v.Z*v.Z);}
        private static float Distance(Vector3 a,Vector3 b){return Length(a-b);}
        private static int PI(string s){int v;return int.TryParse(s,out v)?v:0;}private static bool PB(string s){bool v;return bool.TryParse(s,out v)&&v;}
        private void ResetTransient(Ped p){CancelBreak(p,false);_hotwireVehicle=0;_hotwireStarted=0;_enterWasDown=Pressed(InputEnter);_attackWasDown=Pressed(InputAttack);_cancelWasDown=Pressed(InputCancel);}
        private void OnAborted(object sender,EventArgs e){SaveProfiles();ResetTransient(Game.LocalPlayerPed);}
        private static void Log(string text){try{Directory.CreateDirectory(DataDir);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+text+Environment.NewLine);}catch{}}
    }
}
