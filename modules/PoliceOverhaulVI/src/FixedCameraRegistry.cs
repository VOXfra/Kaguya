using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class FixedCameraRecord
    {
        public string ModelName = string.Empty;
        public int ModelHash;
        public Vector3 Position;
        public float Heading;
        public bool TrafficValidated;
        public string Id { get { return ModelName + "@" + Position.X.ToString("0.0",CultureInfo.InvariantCulture) + "," + Position.Y.ToString("0.0",CultureInfo.InvariantCulture); } }
    }

    internal static class FixedCameraRegistry
    {
        private const string DataDirectory = "scripts\\PoliceOverhaulVI";
        private const string PathCsv = DataDirectory + "\\TrafficCameras.csv";
        private static readonly List<FixedCameraRecord> Records = new List<FixedCameraRecord>();
        private static bool _loaded;
        private static int _lastDiscovery;

        // Camera props only: no prop_cctv_pole_* entries.
        private static readonly string[] CameraModels = {
            "prop_cctv_cam_01a","prop_cctv_cam_01b","prop_cctv_cam_02a","prop_cctv_cam_03a",
            "prop_cctv_cam_04a","prop_cctv_cam_04b","prop_cctv_cam_04c","prop_cctv_cam_05a","prop_cctv_cam_06a",
            "prop_cs_cctv","hei_prop_bank_cctv_01","ch_prop_ch_cctv_cam_01a","ch_prop_ch_cctv_cam_02a",
            "xm_prop_x17_server_farm_cctv_01","tr_prop_tr_camhedz_cctv_01a"
        };

        public static CameraObservation FindTrafficCamera(Ped player, Config cfg, Action<string> log)
        {
            if (player == null || !player.Exists()) return null;
            EnsureLoaded(log);
            DiscoverNearby(player, cfg, log);

            CameraObservation best = null;
            float radius = Math.Max(15f, cfg.FixedRadarRange);
            foreach (FixedCameraRecord r in Records)
            {
                float d = Distance(r.Position, player.Position);
                if (d > radius) continue;
                int handle = FindExactProp(r);
                if (handle == 0) continue;
                Vector3 camPos, forward;
                try
                {
                    camPos = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, handle, true);
                    forward = Function.Call<Vector3>(Hash.GET_ENTITY_FORWARD_VECTOR, handle);
                }
                catch { continue; }
                if (!WithinDirectionalCone(camPos, forward, player.Position, cfg.FixedRadarFovDegrees)) continue;
                bool los = false;
                try { los = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, handle, player.Handle, 17); } catch { }
                if (!los) continue;

                // A camera becomes traffic-validated only after it has physically
                // seen a player vehicle on the road. This avoids turning indoor
                // bank/shop CCTV into speed cameras simply because it is nearby.
                if (!r.TrafficValidated)
                {
                    bool inVehicle = false, onRoad = false;
                    try
                    {
                        inVehicle = player.IsInVehicle();
                        if (inVehicle && player.CurrentVehicle != null)
                            onRoad = Function.Call<bool>(Hash.IS_POINT_ON_ROAD, player.Position.X, player.Position.Y, player.Position.Z, player.CurrentVehicle.Handle);
                    }
                    catch { }
                    if (!inVehicle || !onRoad) continue;
                    r.TrafficValidated = true;
                    Save();
                    if (log != null) log("Traffic camera validated at " + r.Id + ".");
                }

                if (best == null || d < best.Distance)
                    best = new CameraObservation { CameraHandle = handle, Distance = d, TrafficCapable = true, CameraId = r.Id };
            }
            return best;
        }

        private static void EnsureLoaded(Action<string> log)
        {
            if (_loaded) return;
            _loaded = true;
            SeedKnownWorldCameras();
            try
            {
                if (File.Exists(PathCsv))
                {
                    foreach (string raw in File.ReadAllLines(PathCsv))
                    {
                        string line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
                        string[] p = line.Split(','); if (p.Length < 6) continue;
                        float x,y,z,h; bool valid;
                        if (!float.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out x) ||
                            !float.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out y) ||
                            !float.TryParse(p[3],NumberStyles.Float,CultureInfo.InvariantCulture,out z) ||
                            !float.TryParse(p[4],NumberStyles.Float,CultureInfo.InvariantCulture,out h) ||
                            !bool.TryParse(p[5],out valid)) continue;
                        AddOrMerge(p[0],new Vector3(x,y,z),h,valid);
                    }
                }
            }
            catch { }
            Save();
            if (log != null) log("Precise CCTV registry loaded: " + Records.Count + " cam-only positions.");
        }

        private static void SeedKnownWorldCameras()
        {
            // Exact positions/rotations from the public GTA V world CCTV dump.
            AddOrMerge("prop_cctv_cam_06a",new Vector3(525.0725f,-935.23926f,21.316833f),160.031f,false);
            AddOrMerge("prop_cctv_cam_06a",new Vector3(513.19073f,-891.6398f,18.506533f),160.02614f,false);
            AddOrMerge("prop_cctv_cam_06a",new Vector3(515.7284f,-805.1051f,18.102577f),10.717559f,false);
            AddOrMerge("prop_cctv_cam_05a",new Vector3(-112.88005f,-592.1388f,42.92307f),0f,false);
            AddOrMerge("prop_cctv_cam_05a",new Vector3(-121.60687f,-616.918f,42.922974f),113.037796f,false);
            AddOrMerge("prop_cctv_cam_05a",new Vector3(-93.067375f,-563.4311f,42.95774f),-41.935314f,false);
            AddOrMerge("prop_cctv_cam_05a",new Vector3(-129.6135f,-647.60785f,43.17935f),126.019066f,false);
            AddOrMerge("prop_cctv_cam_01a",new Vector3(-215.28726f,-622.3633f,37.269066f),-110.00003f,false);
            AddOrMerge("prop_cctv_cam_01b",new Vector3(473.77554f,-575.8618f,31.16212f),-4.7975154f,false);
            AddOrMerge("prop_cctv_cam_01b",new Vector3(451.20682f,-571.4407f,30.87635f),-95.48724f,false);
            AddOrMerge("prop_cctv_cam_01b",new Vector3(441.24643f,-595.799f,32.6864f),174.06436f,false);
        }

        private static void DiscoverNearby(Ped player, Config cfg, Action<string> log)
        {
            int now = Game.GameTime;
            if (now - _lastDiscovery < Math.Max(800, cfg.FixedRadarDiscoveryIntervalMs)) return;
            _lastDiscovery = now;
            bool changed = false;
            foreach (string name in CameraModels)
            {
                int hash; try { hash = Function.Call<int>(Hash.GET_HASH_KEY,name); } catch { continue; }
                int handle = 0;
                try { handle = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,player.Position.X,player.Position.Y,player.Position.Z,Math.Max(35f,cfg.CctvScanRadius),hash,false,false,false); } catch { }
                if (handle == 0) continue;
                try
                {
                    Vector3 p = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS,handle,true);
                    float heading = Function.Call<float>(Hash.GET_ENTITY_HEADING,handle);
                    if (!HasNear(name,p,1.25f)) { AddOrMerge(name,p,heading,false); changed = true; }
                }
                catch { }
            }
            if (changed) { Save(); if (log != null) log("Enhanced world CCTV positions discovered and persisted."); }
        }

        private static int FindExactProp(FixedCameraRecord r)
        {
            try
            {
                int h = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,r.Position.X,r.Position.Y,r.Position.Z,3.0f,r.ModelHash,false,false,false);
                return h != 0 && Function.Call<bool>(Hash.DOES_ENTITY_EXIST,h) ? h : 0;
            }
            catch { return 0; }
        }

        private static bool WithinDirectionalCone(Vector3 origin, Vector3 forward, Vector3 target, float fov)
        {
            double dx=target.X-origin.X,dy=target.Y-origin.Y,dz=target.Z-origin.Z,len=Math.Sqrt(dx*dx+dy*dy+dz*dz); if(len<0.01)return true;
            double dot=(forward.X*dx+forward.Y*dy+forward.Z*dz)/len;
            double threshold=Math.Cos(Math.Max(20f,Math.Min(150f,fov))*0.5*Math.PI/180.0);
            return dot>=threshold;
        }

        private static void AddOrMerge(string name,Vector3 p,float heading,bool valid)
        {
            foreach(FixedCameraRecord r in Records)
            {
                if(string.Equals(r.ModelName,name,StringComparison.OrdinalIgnoreCase)&&Distance(r.Position,p)<1.25f){r.TrafficValidated|=valid;return;}
            }
            int hash=0;try{hash=Function.Call<int>(Hash.GET_HASH_KEY,name);}catch{}
            Records.Add(new FixedCameraRecord{ModelName=name,ModelHash=hash,Position=p,Heading=heading,TrafficValidated=valid});
        }
        private static bool HasNear(string name,Vector3 p,float radius){foreach(FixedCameraRecord r in Records)if(string.Equals(r.ModelName,name,StringComparison.OrdinalIgnoreCase)&&Distance(r.Position,p)<=radius)return true;return false;}
        private static float Distance(Vector3 a,Vector3 b){Vector3 d=a-b;return(float)Math.Sqrt(d.X*d.X+d.Y*d.Y+d.Z*d.Z);}
        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory); using(var w=new StreamWriter(PathCsv,false))
                {
                    w.WriteLine("# model,x,y,z,heading,trafficValidated");
                    foreach(FixedCameraRecord r in Records)w.WriteLine(r.ModelName+","+r.Position.X.ToString(CultureInfo.InvariantCulture)+","+r.Position.Y.ToString(CultureInfo.InvariantCulture)+","+r.Position.Z.ToString(CultureInfo.InvariantCulture)+","+r.Heading.ToString(CultureInfo.InvariantCulture)+","+r.TrafficValidated.ToString());
                }
            }
            catch { }
        }
    }
}
