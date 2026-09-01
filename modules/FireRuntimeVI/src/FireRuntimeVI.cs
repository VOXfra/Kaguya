using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;

namespace VOX.FireRuntimeVI
{
    public sealed class FireRuntimeVIScript : Script
    {
        private const string DataDir = "scripts\\FireRuntimeVI";
        private const string LogPath = DataDir + "\\FireRuntimeVI.log";
        private const int MaxClusters = 42;

        private sealed class FireCluster
        {
            public string Key;
            public Vector3 Position;
            public uint Material;
            public float Fuel;
            public float MaxFuel;
            public int LastUpdate;
            public int LastSpread;
            public int LastEnsure;
            public readonly List<int> ScriptFireHandles = new List<int>();
        }

        private static readonly HashSet<uint> VegetationMaterials = new HashSet<uint>
        {
            0xE47A3E41u, 0x4F747B87u, 0xB34E900Du, 0x92B69883u,
            0x22AD7B72u, 0xC98F5B61u, 0x8653C6CDu, 0xED932E53u, 0x8DD4EBB9u
        };

        private static readonly HashSet<uint> WoodMaterials = new HashSet<uint>
        {
            0xE82A6F1Cu,0x2114B37Du,0x309F8BB7u,0x0789C7ABu,0xD35443DEu,
            0x76D9AC2Fu,0xEA3746BDu,0xC8D738E7u,0x461D0E9Bu,0x2B13503Du,
            0x981E5200u,0x77E08A22u,0x0E18DFF5u,0xAC038918u,0x1C42F3BCu,
            0x07519E5Du,0xD9B1CDE0u
        };

        private static readonly HashSet<uint> LiquidFuelMaterials = new HashSet<uint>
        {
            0xDA2E9567u, 0x9E98536Cu
        };

        private readonly Dictionary<string, FireCluster> _clusters = new Dictionary<string, FireCluster>();
        private readonly Random _random = new Random();
        private int _lastExternalScan;
        private int _lastPrune;
        private int _storyYieldUntil;

        public FireRuntimeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            Interval = 100;
            Tick += OnTick;
            Aborted += OnAborted;
            Log("Fire Runtime VI 0.1.0 loaded: material fuel, wind/rain propagation and persistent fire clusters.");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Ped player = Game.LocalPlayerPed;
                if (player == null || !player.Exists() || player.IsDead) return;
                if (RockstarOwnsScene()) { _storyYieldUntil = Game.GameTime + 5000; return; }
                if (Game.GameTime < _storyYieldUntil) return;

                if (Game.GameTime - _lastExternalScan >= 650)
                {
                    _lastExternalScan = Game.GameTime;
                    DetectWorldFire(player.Position);
                }
                UpdateClusters(player.Position);

                if (Game.GameTime - _lastPrune >= 5000)
                {
                    _lastPrune = Game.GameTime;
                    PruneFarClusters(player.Position);
                }
            }
            catch (Exception ex) { Log("Fire tick error: " + ex.Message); }
        }

        private void DetectWorldFire(Vector3 around)
        {
            var posOut = new OutputArgument();
            bool found = false;
            try { found = Function.Call<bool>(Hash.GET_CLOSEST_FIRE_POS, posOut, around.X, around.Y, around.Z); } catch { }
            if (!found) return;
            Vector3 p;
            try { p = posOut.GetResult<Vector3>(); } catch { return; }
            if (Distance(p, around) > 110f) return;

            GroundSample sample;
            if (!TryGroundSample(p, out sample) || !IsFlammable(sample.Material)) return;
            AddOrFeedCluster(sample.Position, sample.Material, InitialFuel(sample.Material), true);
        }

        private void UpdateClusters(Vector3 playerPos)
        {
            if (_clusters.Count == 0) return;
            float rain = SafeFloat(Hash.GET_RAIN_LEVEL);
            float windSpeed = Math.Max(0f, SafeFloat(Hash.GET_WIND_SPEED));
            Vector3 wind = Vector3.Zero;
            try { wind = Function.Call<Vector3>(Hash.GET_WIND_DIRECTION); } catch { }
            float windLen = (float)Math.Sqrt(wind.X*wind.X + wind.Y*wind.Y);
            if (windLen > 0.01f) wind = new Vector3(wind.X/windLen, wind.Y/windLen, 0f);
            else wind = new Vector3(1f,0f,0f);

            var dead = new List<string>();
            var snapshot = new List<FireCluster>(_clusters.Values);
            foreach (FireCluster c in snapshot)
            {
                int now = Game.GameTime;
                if (c.LastUpdate <= 0) c.LastUpdate = now;
                float dt = Math.Max(0f, Math.Min(1.0f, (now-c.LastUpdate)/1000f));
                c.LastUpdate = now;

                float wet = Clamp(rain * (VegetationMaterials.Contains(c.Material) ? 1.35f : 0.85f),0f,1.5f);
                float burnRate = BurnRate(c.Material) * (1f + windSpeed * 0.015f) * Math.Max(0.15f,1f-wet*0.65f);
                c.Fuel -= burnRate * dt;
                if (rain > 0.72f) c.Fuel -= c.MaxFuel * 0.018f * rain * dt;

                if (c.Fuel <= 0.01f)
                {
                    RemoveClusterFires(c);
                    dead.Add(c.Key);
                    continue;
                }

                if (Distance(c.Position, playerPos) <= 160f && now-c.LastEnsure >= 1200)
                {
                    c.LastEnsure = now;
                    EnsureVisibleFire(c);
                }

                int spreadInterval = SpreadIntervalMs(c.Material, windSpeed, rain);
                if (c.Fuel > c.MaxFuel * 0.22f && rain < 0.78f && now-c.LastSpread >= spreadInterval && _clusters.Count < MaxClusters)
                {
                    c.LastSpread = now;
                    TrySpread(c, wind, windSpeed, rain);
                }
            }
            foreach (string key in dead) _clusters.Remove(key);
        }

        private void EnsureVisibleFire(FireCluster c)
        {
            bool nearbyFire = false;
            var posOut = new OutputArgument();
            try
            {
                if (Function.Call<bool>(Hash.GET_CLOSEST_FIRE_POS,posOut,c.Position.X,c.Position.Y,c.Position.Z))
                {
                    Vector3 p=posOut.GetResult<Vector3>();
                    nearbyFire=Distance(p,c.Position)<7.0f;
                }
            }
            catch { }
            if (nearbyFire) return;

            RemoveClusterFires(c);
            int intensity = c.Fuel > c.MaxFuel*0.66f ? 3 : (c.Fuel > c.MaxFuel*0.33f ? 2 : 1);
            int children = VegetationMaterials.Contains(c.Material) ? 12 + intensity*5 : (WoodMaterials.Contains(c.Material) ? 8 + intensity*4 : 5 + intensity*3);
            SpawnFire(c,c.Position,children,LiquidFuelMaterials.Contains(c.Material));
            if (VegetationMaterials.Contains(c.Material) && intensity >= 2)
            {
                for(int i=0;i<intensity;i++)
                {
                    double a=(i*2.37)+(c.Position.X+c.Position.Y)*0.01;
                    Vector3 p=new Vector3(c.Position.X+(float)Math.Cos(a)*1.8f,c.Position.Y+(float)Math.Sin(a)*1.8f,c.Position.Z);
                    SpawnFire(c,p,children,true);
                }
            }
        }

        private void TrySpread(FireCluster source, Vector3 wind, float windSpeed, float rain)
        {
            int attempts = source.Fuel > source.MaxFuel*0.60f ? 3 : 1;
            for (int i=0;i<attempts && _clusters.Count<MaxClusters;i++)
            {
                float angle=(float)((_random.NextDouble()-0.5)*1.8);
                float cx=(float)Math.Cos(angle), sx=(float)Math.Sin(angle);
                Vector3 dir=new Vector3(wind.X*cx-wind.Y*sx,wind.X*sx+wind.Y*cx,0f);
                float randomAngle=(float)(_random.NextDouble()*Math.PI*2.0);
                Vector3 randomDir=new Vector3((float)Math.Cos(randomAngle),(float)Math.Sin(randomAngle),0f);
                float windWeight=Clamp(0.35f+windSpeed/12f*0.55f,0.35f,0.90f);
                dir=new Vector3(dir.X*windWeight+randomDir.X*(1f-windWeight),dir.Y*windWeight+randomDir.Y*(1f-windWeight),0f);
                float len=(float)Math.Sqrt(dir.X*dir.X+dir.Y*dir.Y); if(len<0.01f)len=1f;
                dir=new Vector3(dir.X/len,dir.Y/len,0f);
                float distance=2.2f+(float)_random.NextDouble()*(VegetationMaterials.Contains(source.Material)?5.8f:3.2f)+(windSpeed*0.10f);
                Vector3 candidate=source.Position+dir*distance+new Vector3(0f,0f,1.5f);
                GroundSample sample;
                if(!TryGroundSample(candidate,out sample)||!IsFlammable(sample.Material))continue;
                if(rain>0.45f && VegetationMaterials.Contains(sample.Material) && _random.NextDouble()<rain*0.65f)continue;
                AddOrFeedCluster(sample.Position,sample.Material,InitialFuel(sample.Material)*(0.55f+(float)_random.NextDouble()*0.30f),false);
            }
        }

        private void AddOrFeedCluster(Vector3 p,uint material,float fuel,bool external)
        {
            string key=GridKey(p);
            FireCluster c;
            if(_clusters.TryGetValue(key,out c))
            {
                c.Fuel=Math.Min(c.MaxFuel,c.Fuel+fuel*0.16f);
                return;
            }
            if(_clusters.Count>=MaxClusters)return;
            c=new FireCluster{Key=key,Position=p,Material=material,MaxFuel=InitialFuel(material),Fuel=Math.Min(InitialFuel(material),fuel),LastUpdate=Game.GameTime,LastSpread=Game.GameTime-(external?2500:0)};
            _clusters[key]=c;
            EnsureVisibleFire(c);
            Log("Fire cluster " + (external?"adopted":"spread") + " material=0x"+material.ToString("X8")+" fuel="+c.Fuel.ToString("0.0")+" at "+((int)p.X)+","+((int)p.Y)+".");
        }

        private static string GridKey(Vector3 p)
        {
            int x=(int)Math.Floor(p.X/3.2f),y=(int)Math.Floor(p.Y/3.2f),z=(int)Math.Floor(p.Z/2.0f);
            return x+":"+y+":"+z;
        }

        private static bool IsFlammable(uint m){return VegetationMaterials.Contains(m)||WoodMaterials.Contains(m)||LiquidFuelMaterials.Contains(m);}
        private static float InitialFuel(uint m){if(LiquidFuelMaterials.Contains(m))return 55f;if(VegetationMaterials.Contains(m))return 85f;if(WoodMaterials.Contains(m))return 140f;return 0f;}
        private static float BurnRate(uint m){if(LiquidFuelMaterials.Contains(m))return 2.8f;if(VegetationMaterials.Contains(m))return 0.85f;if(WoodMaterials.Contains(m))return 0.42f;return 1f;}
        private static int SpreadIntervalMs(uint m,float wind,float rain)
        {
            float baseMs=VegetationMaterials.Contains(m)?2700f:(LiquidFuelMaterials.Contains(m)?1800f:5200f);
            baseMs*=1f+rain*1.5f;
            baseMs/=1f+Math.Min(12f,wind)*0.055f;
            return Math.Max(900,(int)baseMs);
        }

        private void SpawnFire(FireCluster c,Vector3 p,int children,bool gas)
        {
            try
            {
                int h=Function.Call<int>(Hash.START_SCRIPT_FIRE,p.X,p.Y,p.Z,Math.Max(1,Math.Min(25,children)),gas);
                if(h!=-1)c.ScriptFireHandles.Add(h);
            }
            catch { }
        }

        private static void RemoveClusterFires(FireCluster c)
        {
            foreach(int h in c.ScriptFireHandles){try{Function.Call(Hash.REMOVE_SCRIPT_FIRE,h);}catch{}}
            c.ScriptFireHandles.Clear();
        }

        private void PruneFarClusters(Vector3 player)
        {
            var remove=new List<string>();
            foreach(var pair in _clusters)
            {
                FireCluster c=pair.Value;
                if(Distance(c.Position,player)>650f){RemoveClusterFires(c);remove.Add(pair.Key);}
            }
            foreach(string k in remove)_clusters.Remove(k);
        }

        private struct GroundSample{public Vector3 Position;public uint Material;}

        private static bool TryGroundSample(Vector3 p,out GroundSample sample)
        {
            sample=new GroundSample();
            try
            {
                int test=Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,p.X,p.Y,p.Z+2.5f,p.X,p.Y,p.Z-5.0f,1,0,7);
                var hit=new OutputArgument();var end=new OutputArgument();var normal=new OutputArgument();var material=new OutputArgument();var entity=new OutputArgument();
                Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT_INCLUDING_MATERIAL,test,hit,end,normal,material,entity);
                if(!hit.GetResult<bool>())return false;
                sample.Position=end.GetResult<Vector3>();
                sample.Material=material.GetResult<uint>();
                return true;
            }
            catch{return false;}
        }

        private static float SafeFloat(Hash h){try{return Function.Call<float>(h);}catch{return 0f;}}
        private static float Distance(Vector3 a,Vector3 b){double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return(float)Math.Sqrt(x*x+y*y+z*z);}
        private static float Clamp(float v,float min,float max){return v<min?min:(v>max?max:v);}
        private static bool RockstarOwnsScene()
        {
            try{if(Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE))return true;}catch{}
            try{if(Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))return true;}catch{}
            try{if(Function.Call<bool>(Hash.GET_MISSION_FLAG))return true;}catch{}
            try{if(!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON,Game.Player.Handle))return true;}catch{}
            try{if(Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT)||Function.Call<bool>(Hash.IS_SCREEN_FADING_IN))return true;}catch{}
            return false;
        }
        private void OnAborted(object sender,EventArgs e){foreach(FireCluster c in _clusters.Values)RemoveClusterFires(c);_clusters.Clear();}
        private static void Log(string s){try{Directory.CreateDirectory(DataDir);File.AppendAllText(LogPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+" | "+s+Environment.NewLine);}catch{}}
    }
}
