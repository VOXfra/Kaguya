using GTA;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class TrafficSystem
    {
        private readonly List<CitationRecord> _pending = new List<CitationRecord>();
        private readonly FineMailSystem _mail = new FineMailSystem();
        private int _speedingSince, _lastCitationAt, _policeObservedSince, _lastTrafficScan;
        private bool _escalationRequested;
        private static readonly HashSet<string> HighwayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Los Santos Freeway","Del Perro Freeway","Olympic Freeway","Elysian Fields Freeway",
            "La Puerta Freeway","Palomino Freeway","Senora Freeway","Great Ocean Highway"
        };

        public void Update(Ped player, CaseMemory memory, Config cfg, Action<string> log)
        {
            DeliverPending(memory, cfg, log);
            if (!cfg.TrafficEnforcementEnabled || player == null || !player.Exists() || !player.IsInVehicle())
            {
                _speedingSince = 0; _policeObservedSince = 0; return;
            }
            int now = Game.GameTime;
            if (now - _lastTrafficScan < 250) return;
            _lastTrafficScan = now;
            Vehicle vehicle = player.CurrentVehicle;
            if (vehicle == null || !vehicle.Exists()) return;

            float speedKphF = Function.Call<float>(Hash.GET_ENTITY_SPEED, vehicle.Handle) * 3.6f;
            int speedKph = (int)Math.Round(speedKphF);
            string street = GetStreetName(player);
            int limit = GetSpeedLimit(street, cfg);
            int over = speedKph - limit;
            if (over <= cfg.SpeedToleranceKph)
            {
                _speedingSince = 0; _policeObservedSince = 0; return;
            }
            if (_speedingSince == 0) _speedingSince = now;
            if (now - _speedingSince < cfg.SpeedingGraceMs || now - _lastCitationAt < cfg.CitationCooldownMs) return;

            bool cameraSaw = cfg.TrafficCameraEnforcement && cfg.CctvEnabled && CameraSystem.FindSeeingPlayer(player, cfg, true) != null;
            WitnessObservation police = Perception.FindSeeingPolice(player, cfg.PoliceWitnessDistance);
            bool policeSaw = police != null;

            if (cameraSaw)
            {
                IssueSpeedingCitation(memory, cfg, speedKph, limit, over, street, "fixed traffic camera", false, log);
                return;
            }

            if (policeSaw)
            {
                if (_policeObservedSince == 0) _policeObservedSince = now;
                if (now - _policeObservedSince >= cfg.PoliceSpeedingReportDelayMs)
                {
                    bool reckless = over >= Math.Max(cfg.SpeedToleranceKph + 1, cfg.RecklessSpeedOverKph);
                    IssueSpeedingCitation(memory, cfg, speedKph, limit, over, street, "police observation", reckless, log);
                    if (reckless && cfg.PoliceObservedSpeedingCanEscalate)
                    {
                        _escalationRequested = true;
                        if (log != null) log("Traffic officer requested a one-star stop for reckless speeding.");
                    }
                }
            }
            else _policeObservedSince = 0;
        }

        public bool ConsumeEscalationRequest()
        {
            bool value = _escalationRequested;
            _escalationRequested = false;
            return value;
        }

        public void ResetTransient()
        {
            _speedingSince = 0; _policeObservedSince = 0; _escalationRequested = false;
        }

        private void IssueSpeedingCitation(CaseMemory memory, Config cfg, int speedKph, int limit, int overKph, string street, string source, bool reckless, Action<string> log)
        {
            int now = Game.GameTime;
            int chargeableOver = Math.Max(1, overKph - cfg.SpeedToleranceKph);
            int amount = Math.Max(1, cfg.SpeedingBaseFine + chargeableOver * Math.Max(0, cfg.SpeedingFinePerKph));
            if (reckless) amount += Math.Max(0, cfg.RecklessFineBonus);
            _pending.Add(new CitationRecord
            {
                SuspectModelHash = memory == null ? 0 : memory.SuspectModelHash,
                Amount = amount,
                IssuedAtGameTime = now,
                DeliverAtGameTime = now + Math.Max(1000, cfg.FineDeliveryDelayMs),
                Reason = reckless ? "Reckless speeding" : "Speeding",
                Source = source,
                Street = street,
                SpeedKph = speedKph,
                LimitKph = limit,
                OverKph = overKph,
                Delivered = false
            });
            _lastCitationAt = now; _speedingSince = 0; _policeObservedSince = 0;
            if (log != null) log("Traffic citation recorded: " + speedKph + "/" + limit + " km/h (+" + overKph + "), $" + amount + ", source=" + source + ".");
        }

        private void DeliverPending(CaseMemory memory, Config cfg, Action<string> log)
        {
            if (_pending.Count == 0) return;
            int now = Game.GameTime;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                CitationRecord citation = _pending[i];
                if (citation.Delivered || now < citation.DeliverAtGameTime) continue;
                int unpaid = citation.Amount, paid = 0;
                if (cfg.AutoDeductFines)
                {
                    try
                    {
                        int money = Game.Player.Money;
                        paid = Math.Min(Math.Max(0, money), citation.Amount);
                        if (paid > 0) Game.Player.Money = money - paid;
                        unpaid -= paid;
                    }
                    catch { }
                }
                if (memory != null && (citation.SuspectModelHash == 0 || citation.SuspectModelHash == memory.SuspectModelHash) && unpaid > 0)
                    memory.UnpaidFines += unpaid;
                citation.Delivered = true;
                _pending.RemoveAt(i);
                _mail.Deliver(citation, paid, unpaid, cfg, log);
                if (log != null) log("Traffic citation settled. amount=$" + citation.Amount + ", paid=$" + paid + ", unpaid=$" + Math.Max(0, unpaid) + ".");
            }
        }

        private static string GetStreetName(Ped player)
        {
            try
            {
                var streetHash = new OutputArgument(); var crossingHash = new OutputArgument();
                Function.Call(Hash.GET_STREET_NAME_AT_COORD, player.Position.X, player.Position.Y, player.Position.Z, streetHash, crossingHash);
                int hash = streetHash.GetResult<int>();
                return hash == 0 ? string.Empty : (Function.Call<string>(Hash.GET_STREET_NAME_FROM_HASH_KEY, hash) ?? string.Empty).Trim();
            }
            catch { return string.Empty; }
        }

        private static int GetSpeedLimit(string street, Config cfg)
        {
            if (!string.IsNullOrEmpty(street) && HighwayNames.Contains(street)) return Math.Max(1, cfg.HighwaySpeedLimitKph);
            return Math.Max(1, cfg.UrbanSpeedLimitKph);
        }
    }
}
