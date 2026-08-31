using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using System;
using System.Drawing;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    internal sealed class SearchHudSystem
    {
        private const string UiDirectory = "scripts\\PoliceOverhaulVI\\UI";
        private const int HardTransitionGapMs = 2000;
        private static SearchHudSystem _current;

        private int _innerBlip, _outerBlip, _lastThreat, _lastUpdateAt, _lastNativeWanted;
        private int _searchLatchedUntil, _visualContactSince, _trackerCandidateSince, _lastTrackerReacquireAt;
        private Vector3 _lastCenter;
        private float _lastInnerRadius, _lastOuterRadius;
        private CustomSprite _face, _clothes, _vehicle, _star;
        private bool _spritesAttempted, _suppressCurrentPhase;

        public SearchHudSystem() { _current = this; }

        public static void NotifyPlayerDeath()
        {
            SearchHudSystem current = _current;
            if (current == null) return;
            current._suppressCurrentPhase = true;
            current._lastNativeWanted = 0;
            current._lastUpdateAt = Game.GameTime;
            current._searchLatchedUntil = 0;
            current._visualContactSince = 0;
            current._trackerCandidateSince = 0;
            current.ClearSearchCircles();
        }

        public void Update(Ped player, CaseMemory memory, int nativeWanted, Config cfg)
        {
            int now = Game.GameTime;
            bool hardGap = _lastUpdateAt > 0 && now - _lastUpdateAt > HardTransitionGapMs;
            if (nativeWanted > 0) _suppressCurrentPhase = false;
            else if (_lastNativeWanted > 0 && hardGap) _suppressCurrentPhase = true;
            _lastUpdateAt = now;

            if (_suppressCurrentPhase || !cfg.SearchHudEnabled || memory == null || (!memory.Active && !memory.WarrantActive))
            {
                _lastNativeWanted = nativeWanted;
                _searchLatchedUntil = 0;
                _visualContactSince = 0;
                _trackerCandidateSince = 0;
                ClearSearchCircles();
                return;
            }

            nativeWanted = TryTrackerReacquire(player, memory, nativeWanted, cfg, now);
            _lastNativeWanted = nativeWanted;

            int threat = Math.Max(1, Math.Min(6, memory.ThreatLevel));
            int lifetime = SearchLifetimeForThreat(threat, cfg);
            bool recentlyObserved = memory.LastObservedGameTime > 0 && now - memory.LastObservedGameTime < cfg.SearchCircleObservationGraceMs;
            int sinceObservation = memory.LastObservedGameTime > 0 ? Math.Max(0, now - memory.LastObservedGameTime) : int.MaxValue;
            bool lostContact = nativeWanted > 0 && !recentlyObserved && sinceObservation >= Math.Max(250, cfg.SearchLostContactDelayMs);
            bool postPursuitSearch = nativeWanted == 0 && memory.LastWantedEndedAt > 0 && now - memory.LastWantedEndedAt <= lifetime;

            if (lostContact || postPursuitSearch)
                _searchLatchedUntil = Math.Max(_searchLatchedUntil, now + lifetime);

            bool latched = now <= _searchLatchedUntil;
            if (nativeWanted > 0 && recentlyObserved)
            {
                if (_visualContactSince == 0) _visualContactSince = now;
                // Do not blink the circles because LOS flickered for one scan.
                // Only sustained reacquisition ends the search visualization.
                if (now - _visualContactSince >= Math.Max(900, cfg.SearchCircleObservationGraceMs))
                {
                    _searchLatchedUntil = 0;
                    latched = false;
                }
            }
            else _visualContactSince = 0;

            bool searchActive = lostContact || postPursuitSearch || latched;
            if (!searchActive)
            {
                ClearSearchCircles();
                if (cfg.ShowEvidenceIcons && nativeWanted > 0) DrawEvidence(memory, cfg);
                return;
            }

            int ageMs = memory.LastObservedGameTime > 0 ? Math.Max(0, now - memory.LastObservedGameTime) : 0;
            float growth = Math.Min(Math.Max(0f, cfg.SearchMaxGrowth), ageMs / 1000f * Math.Max(0f, cfg.SearchUncertaintyGrowthPerSecond));
            float inner = cfg.SearchInnerBaseRadius + Math.Max(0, threat - 1) * cfg.SearchRadiusPerStar + growth * 0.55f;
            float outer = inner + cfg.SearchOuterExtraRadius + growth;

            // High-level searches cover districts rather than a couple of blocks.
            if (threat >= 5)
            {
                inner = Math.Max(inner, threat >= 6 ? 650f : 460f);
                outer = Math.Max(outer, threat >= 6 ? 1100f : 760f);
            }

            if (cfg.ShowSearchCircles) EnsureCircles(memory.LastKnownPosition, threat, inner, outer, cfg);
            else ClearSearchCircles();
            if (cfg.ShowEvidenceIcons) DrawEvidence(memory, cfg);
        }

        public void Cleanup()
        {
            ClearSearchCircles();
            _lastUpdateAt = 0;
            _lastNativeWanted = 0;
            _searchLatchedUntil = 0;
            _visualContactSince = 0;
            _trackerCandidateSince = 0;
            _lastTrackerReacquireAt = 0;
            _suppressCurrentPhase = false;
        }

        private int TryTrackerReacquire(Ped player, CaseMemory memory, int nativeWanted, Config cfg, int now)
        {
            if (nativeWanted > 0 || player == null || !player.Exists() || memory == null || !cfg.TrackersEnabled)
            {
                _trackerCandidateSince = 0;
                return nativeWanted;
            }

            bool usable = false;
            try { usable = TrackerSystem.HasPoliceUsableTracker(memory, player, cfg); } catch { }
            if (!usable)
            {
                _trackerCandidateSince = 0;
                return nativeWanted;
            }

            if (_trackerCandidateSince == 0)
            {
                _trackerCandidateSince = now;
                return nativeWanted;
            }

            if (now - _trackerCandidateSince < Math.Max(500, cfg.TrackerReacquireDelayMs)) return nativeWanted;
            if (now - _lastTrackerReacquireAt < Math.Max(8000, cfg.TrackerPingIntervalMs * 2)) return nativeWanted;

            int restored = Math.Max(1, Math.Min(5, memory.ThreatLevel));
            try
            {
                memory.LastKnownPosition = player.Position;
                memory.LastSource = ObservationSource.Tracker;
                memory.LastObservedGameTime = now;
                memory.Touch(cfg);
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, Game.Player.Handle, restored, false);
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player.Handle, false);
                Function.Call(Hash.SET_PLAYER_WANTED_CENTRE_POSITION, Game.Player.Handle, player.Position.X, player.Position.Y, player.Position.Z);
                _lastTrackerReacquireAt = now;
                _trackerCandidateSince = 0;
                _searchLatchedUntil = now + SearchLifetimeForThreat(Math.Max(1, memory.ThreatLevel), cfg);
                return restored;
            }
            catch
            {
                return nativeWanted;
            }
        }

        private static int SearchLifetimeForThreat(int threat, Config cfg)
        {
            int baseMs = Math.Max(1000, cfg.SearchPhaseLifetimeMs);
            if (threat >= 6) return Math.Max(baseMs * 4, 240000);
            if (threat == 5) return Math.Max((int)(baseMs * 2.5f), 150000);
            if (threat == 4) return Math.Max((int)(baseMs * 1.8f), 100000);
            if (threat == 3) return Math.Max((int)(baseMs * 1.35f), 75000);
            return baseMs;
        }

        private void EnsureCircles(Vector3 center, int threat, float inner, float outer, Config cfg)
        {
            if (center == Vector3.Zero) return;
            bool same = _innerBlip != 0 && _outerBlip != 0 && _lastThreat == threat &&
                        Perception.Distance(center, _lastCenter) < 16f && Math.Abs(inner - _lastInnerRadius) < 12f && Math.Abs(outer - _lastOuterRadius) < 18f;
            if (same) return;
            ClearSearchCircles();
            try
            {
                _outerBlip = Function.Call<int>(Hash.ADD_BLIP_FOR_RADIUS, center.X, center.Y, center.Z, outer);
                Function.Call(Hash.SET_BLIP_COLOUR, _outerBlip, 1);
                Function.Call(Hash.SET_BLIP_ALPHA, _outerBlip, cfg.SearchOuterAlpha);
                _innerBlip = Function.Call<int>(Hash.ADD_BLIP_FOR_RADIUS, center.X, center.Y, center.Z, inner);
                Function.Call(Hash.SET_BLIP_COLOUR, _innerBlip, 1);
                Function.Call(Hash.SET_BLIP_ALPHA, _innerBlip, cfg.SearchInnerAlpha);
                _lastCenter = center;
                _lastThreat = threat;
                _lastInnerRadius = inner;
                _lastOuterRadius = outer;
            }
            catch { ClearSearchCircles(); }
        }

        private void ClearSearchCircles()
        {
            DeleteBlip(ref _innerBlip);
            DeleteBlip(ref _outerBlip);
            _lastThreat = 0;
            _lastCenter = Vector3.Zero;
            _lastInnerRadius = _lastOuterRadius = 0f;
        }

        private static void DeleteBlip(ref int handle)
        {
            if (handle == 0) return;
            try
            {
                Blip b = new Blip(handle);
                if (b.Exists()) b.Delete();
            }
            catch { }
            handle = 0;
        }

        private void DrawEvidence(CaseMemory memory, Config cfg)
        {
            EnsureSprites();
            float x = 1134f, y = 61f, size = cfg.EvidenceIconSize;
            int stars = Math.Max(1, Math.Min(6, memory.ThreatLevel));
            if (_star != null)
            {
                for (int i = 0; i < stars; i++)
                {
                    _star.Position = new PointF(x - i * 25f, y);
                    _star.Size = new SizeF(22f, 22f);
                    _star.ScaledDraw();
                }
            }
            float iconX = x - (stars - 1) * 25f, iconY = y + 31f;
            if (memory.FaceKnown && _face != null) DrawIcon(_face, ref iconX, iconY, size);
            if (memory.OutfitKnown && _clothes != null) DrawIcon(_clothes, ref iconX, iconY, size);
            if (memory.Vehicle != null && _vehicle != null) DrawIcon(_vehicle, ref iconX, iconY, size);
        }

        private static void DrawIcon(CustomSprite sprite, ref float x, float y, float size)
        {
            sprite.Position = new PointF(x, y);
            sprite.Size = new SizeF(size, size);
            sprite.ScaledDraw();
            x += size + 5f;
        }

        private void EnsureSprites()
        {
            if (_spritesAttempted) return;
            _spritesAttempted = true;
            try
            {
                _face = TrySprite(Path.Combine(UiDirectory, "face.png"));
                _clothes = TrySprite(Path.Combine(UiDirectory, "clothes.png"));
                _vehicle = TrySprite(Path.Combine(UiDirectory, "vehicle.png"));
                _star = TrySprite(Path.Combine(UiDirectory, "starRED.png"));
            }
            catch { }
        }

        private static CustomSprite TrySprite(string path)
        {
            if (!File.Exists(path)) return null;
            return new CustomSprite(Path.GetFullPath(path), new SizeF(28f, 28f), new PointF(0f, 0f), Color.White, 0f, true);
        }
    }
}
