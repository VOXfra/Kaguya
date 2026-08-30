using GTA;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class TrafficSystem
    {
        private readonly List<CitationRecord> _pending=new List<CitationRecord>(); private int _speedingSince; private int _lastCitationAt; private int _policeObservedSince; private int _lastTrafficScan;
        private static readonly HashSet<string> HighwayNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Los Santos Freeway","Del Perro Freeway","Olympic Freeway","Elysian Fields Freeway","La Puerta Freeway","Palomino Freeway","Senora Freeway","Great Ocean Highway"};
        public void Update(Ped player,CaseMemory memory,Config cfg,Action<string> log)
        {
            DeliverPending(memory,cfg,log); if(!cfg.TrafficEnforcementEnabled||player==null||!player.Exists()||!player.IsInVehicle()){_speedingSince=0;_policeObservedSince=0;return;} int now=Game.GameTime;if(now-_lastTrafficScan<250)return;_lastTrafficScan=now;Vehicle vehicle=player.CurrentVehicle;if(vehicle==null||!vehicle.Exists())return;
            float speedKph=Function.Call<float>(Hash.GET_ENTITY_SPEED,vehicle.Handle)*3.6f;int limit=GetSpeedLimit(player,cfg);int over=(int)Math.Floor(speedKph-limit);if(over<=cfg.SpeedToleranceKph){_speedingSince=0;_policeObservedSince=0;return;}if(_speedingSince==0)_speedingSince=now;if(now-_speedingSince<cfg.SpeedingGraceMs||now-_lastCitationAt<cfg.CitationCooldownMs)return;
            bool cameraSaw=cfg.TrafficCameraEnforcement&&cfg.CctvEnabled&&CameraSystem.FindSeeingPlayer(player,cfg,true)!=null;WitnessObservation police=Perception.FindSeeingPolice(player,cfg.PoliceWitnessDistance);bool policeSaw=police!=null;
            if(cameraSaw){IssueSpeedingCitation(memory,cfg,over,"fixed traffic camera",log);return;}if(policeSaw){if(_policeObservedSince==0)_policeObservedSince=now;if(now-_policeObservedSince>=cfg.PoliceSpeedingReportDelayMs)IssueSpeedingCitation(memory,cfg,over,"police observation",log);}else _policeObservedSince=0;
        }
        public void ResetTransient(){_speedingSince=0;_policeObservedSince=0;}
        private void IssueSpeedingCitation(CaseMemory memory,Config cfg,int overKph,string source,Action<string> log){int now=Game.GameTime;int chargeableOver=Math.Max(1,overKph-cfg.SpeedToleranceKph);int amount=Math.Max(1,cfg.SpeedingBaseFine+chargeableOver*Math.Max(0,cfg.SpeedingFinePerKph));_pending.Add(new CitationRecord{SuspectModelHash=memory==null?0:memory.SuspectModelHash,Amount=amount,IssuedAtGameTime=now,DeliverAtGameTime=now+Math.Max(1000,cfg.FineDeliveryDelayMs),Reason="Speeding",Delivered=false});_lastCitationAt=now;_speedingSince=0;_policeObservedSince=0;if(log!=null)log("Traffic citation recorded: speeding +"+overKph+" kph, $"+amount+", source="+source+".");}
        private void DeliverPending(CaseMemory memory,Config cfg,Action<string> log){if(_pending.Count==0)return;int now=Game.GameTime;for(int i=_pending.Count-1;i>=0;i--){CitationRecord citation=_pending[i];if(citation.Delivered||now<citation.DeliverAtGameTime)continue;int unpaid=citation.Amount;if(cfg.AutoDeductFines){try{int money=Game.Player.Money;int paid=Math.Min(Math.Max(0,money),citation.Amount);if(paid>0)Game.Player.Money=money-paid;unpaid-=paid;}catch{}}if(memory!=null&&citation.SuspectModelHash==memory.SuspectModelHash&&unpaid>0)memory.UnpaidFines+=unpaid;citation.Delivered=true;_pending.RemoveAt(i);if(log!=null)log("Traffic citation delivered. amount=$"+citation.Amount+", unpaid=$"+Math.Max(0,unpaid)+".");}}
        private static int GetSpeedLimit(Ped player,Config cfg){try{var streetHash=new OutputArgument();var crossingHash=new OutputArgument();Function.Call(Hash.GET_STREET_NAME_AT_COORD,player.Position.X,player.Position.Y,player.Position.Z,streetHash,crossingHash);int hash=streetHash.GetResult<int>();string street=hash==0?string.Empty:Function.Call<string>(Hash.GET_STREET_NAME_FROM_HASH_KEY,hash);if(!string.IsNullOrEmpty(street)&&HighwayNames.Contains(street.Trim()))return Math.Max(1,cfg.HighwaySpeedLimitKph);}catch{}return Math.Max(1,cfg.UrbanSpeedLimitKph);}
    }
}
