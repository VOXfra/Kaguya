using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class CameraObservation { public int CameraHandle; public float Distance; public bool TrafficCapable; }
    internal static class CameraSystem
    {
        private static readonly string[] CameraModelNames={"prop_cctv_cam_01a","prop_cctv_cam_01b","prop_cctv_cam_02a","prop_cctv_cam_03a","prop_cctv_cam_04a","prop_cctv_cam_04b","prop_cctv_cam_04c","prop_cctv_cam_05a","prop_cctv_cam_06a","prop_cctv_pole_01a","prop_cctv_pole_02","prop_cctv_pole_03","prop_cctv_pole_04","prop_cs_cctv","hei_prop_bank_cctv_01","ch_prop_ch_cctv_cam_01a","ch_prop_ch_cctv_cam_02a","xm_prop_x17_server_farm_cctv_01","tr_prop_tr_camhedz_cctv_01a","m24_1_prop_m24_1_carrier_bank_cctv_01","m26_1_prop_m61_cctvcam_03a","m26_1_prop_m61_cctvcam_04a"};
        private static readonly HashSet<string> TrafficModels=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"prop_cctv_pole_01a","prop_cctv_pole_02","prop_cctv_pole_03","prop_cctv_pole_04","prop_cctv_cam_05a","prop_cctv_cam_06a"};
        private static int[] _hashes;
        public static CameraObservation FindSeeingPlayer(Ped player,Config cfg,bool trafficOnly)
        {
            if(player==null||!player.Exists()||!cfg.CctvEnabled)return null; EnsureHashes(); Vector3 target=player.Position; CameraObservation best=null; float radius=Math.Max(10f,cfg.CctvScanRadius);
            for(int i=0;i<_hashes.Length;i++)
            {
                bool traffic=TrafficModels.Contains(CameraModelNames[i]); if(trafficOnly&&!traffic)continue; int handle=0;
                try{handle=Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,target.X,target.Y,target.Z,radius,_hashes[i],false,false,false);}catch{continue;}
                if(handle==0||!Function.Call<bool>(Hash.DOES_ENTITY_EXIST,handle))continue; Vector3 cameraPos; Vector3 forward;
                try{cameraPos=Function.Call<Vector3>(Hash.GET_ENTITY_COORDS,handle,true);forward=Function.Call<Vector3>(Hash.GET_ENTITY_FORWARD_VECTOR,handle);}catch{continue;}
                float distance=Perception.Distance(cameraPos,target); if(distance>radius||!WithinCameraCone(cameraPos,forward,target,cfg.CctvFovDegrees))continue;
                if(!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,handle,player.Handle,17))continue;
                if(best==null||distance<best.Distance)best=new CameraObservation{CameraHandle=handle,Distance=distance,TrafficCapable=traffic};
            }
            return best;
        }
        private static bool WithinCameraCone(Vector3 origin,Vector3 forward,Vector3 target,float fovDegrees){double dx=target.X-origin.X,dy=target.Y-origin.Y,dz=target.Z-origin.Z,len=Math.Sqrt(dx*dx+dy*dy+dz*dz);if(len<0.01)return true;double dot=(forward.X*dx+forward.Y*dy+forward.Z*dz)/len;dot=Math.Abs(dot);double threshold=Math.Cos(Math.Max(10f,Math.Min(170f,fovDegrees))*0.5*Math.PI/180.0);return dot>=threshold;}
        private static void EnsureHashes(){if(_hashes!=null)return;_hashes=new int[CameraModelNames.Length];for(int i=0;i<CameraModelNames.Length;i++){try{_hashes[i]=Function.Call<int>(Hash.GET_HASH_KEY,CameraModelNames[i]);}catch{_hashes[i]=0;}}}
    }
}
