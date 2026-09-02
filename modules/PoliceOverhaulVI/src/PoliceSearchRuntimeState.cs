using GTA;
using GTA.Math;

namespace VOX.PoliceOverhaulVI
{
    internal static class PoliceSearchRuntimeState
    {
        private static CaseMemory _boundCase;
        private static int _boundModel;
        private static CaseMemory _fallbackCase;
        private static int _fallbackModel;

        public static bool SearchActive;
        public static int ThreatLevel;
        public static int SearchStartedAt;
        public static int SearchDeadlineAt;
        public static int LastCustomSearchEndedAt;
        public static int LastDirectObservationAt;
        public static int LastTrackerPingAt;
        public static Vector3 LastKnownPosition;

        public static OutfitSignature ActiveOutfit;
        public static bool ActiveOutfitValid;
        public static VehicleSignature ActiveVehicle;
        public static bool ActiveVehicleValid;
        public static bool MaskDescriptorValid;
        public static bool MaskedDescriptor;

        public static int CandidateKey;
        public static int CandidateSince;
        public static float CandidateConfidence;

        public static void BindCase(CaseMemory memory)
        {
            if(memory==null)return;_boundCase=memory;_boundModel=memory.SuspectModelHash;
            if(LastCustomSearchEndedAt>0)memory.LastWantedEndedAt=0;
        }

        public static CaseMemory CaseFor(Ped player)
        {
            if(player==null||!player.Exists())return _boundCase;int model=player.Model.Hash;
            if(_boundCase!=null&&_boundModel==model)return _boundCase;
            if(_fallbackCase==null||_fallbackModel!=model){_fallbackModel=model;_fallbackCase=new CaseMemory{SuspectModelHash=model};}
            return _fallbackCase;
        }

        // Death ends the active incident. Evidence/history remains in the case, but
        // reacquisition is deliberately held off after hospital respawn instead of
        // immediately recreating wanted state from a retained warrant/signalment.
        public static void NotifyPlayerDeath()
        {
            CaseMemory memory=_boundCase;
            if(memory!=null)
            {
                memory.LastWantedEndedAt=Game.GameTime+30000;
                memory.LastObservedGameTime=0;
                memory.LastKnownPosition=Vector3.Zero;
            }
            ResetSearch(true);
        }

        public static void CaptureActiveSignalment(Ped player,CaseMemory memory,bool plateKnown)
        {
            if(player==null||!player.Exists())return;LastCustomSearchEndedAt=0;if(memory!=null)BindCase(memory);
            ActiveOutfit=OutfitSignature.Capture(player);ActiveOutfitValid=ActiveOutfit!=null;MaskedDescriptor=OutfitSignature.FaceObscured(player);MaskDescriptorValid=true;
            ActiveVehicle=null;ActiveVehicleValid=false;
            if(player.IsInVehicle())
            {
                Vehicle vehicle=player.CurrentVehicle;
                if(vehicle!=null&&vehicle.Exists())
                {
                    ActiveVehicle=VehicleSignature.Capture(vehicle,plateKnown);ActiveVehicleValid=ActiveVehicle!=null;
                    if(ActiveVehicle!=null&&memory!=null&&memory.Vehicle!=null&&memory.Vehicle.Matches(vehicle,false))
                    {
                        ActiveVehicle.TrackerPresent=memory.Vehicle.TrackerPresent;ActiveVehicle.TrackerKnownByPolice=memory.Vehicle.TrackerKnownByPolice;if(memory.Vehicle.PlateKnown)ActiveVehicle.PlateKnown=true;
                    }
                }
            }
        }

        public static void InvalidateChangedSignalment(Ped player)
        {
            if(player==null||!player.Exists())return;
            if(ActiveOutfitValid&&(ActiveOutfit==null||!ActiveOutfit.Matches(player)))ActiveOutfitValid=false;
            if(MaskDescriptorValid&&OutfitSignature.FaceObscured(player)!=MaskedDescriptor)MaskDescriptorValid=false;
            if(ActiveVehicleValid)
            {
                if(!player.IsInVehicle())ActiveVehicleValid=false;
                else{Vehicle v=player.CurrentVehicle;if(v==null||!v.Exists()||ActiveVehicle==null||!ActiveVehicle.Matches(v,ActiveVehicle.PlateKnown))ActiveVehicleValid=false;}
            }
        }

        public static void ResetCandidate(){CandidateKey=0;CandidateSince=0;CandidateConfidence=0f;}
        public static void MarkSearchExpired(){LastCustomSearchEndedAt=Game.GameTime;}
        public static void ResetSearch(bool clearSignalment)
        {
            if(!clearSignalment&&SearchActive&&SearchDeadlineAt>0&&Game.GameTime>=SearchDeadlineAt)LastCustomSearchEndedAt=Game.GameTime;
            SearchActive=false;ThreatLevel=0;SearchStartedAt=0;SearchDeadlineAt=0;LastDirectObservationAt=0;LastTrackerPingAt=0;LastKnownPosition=Vector3.Zero;ResetCandidate();
            if(!clearSignalment)return;LastCustomSearchEndedAt=0;ActiveOutfit=null;ActiveOutfitValid=false;ActiveVehicle=null;ActiveVehicleValid=false;MaskDescriptorValid=false;MaskedDescriptor=false;
        }
    }
}
