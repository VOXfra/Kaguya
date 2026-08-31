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
        private int _searchLatchedUntil, _visualContactSince;
        private Vector3 _lastCenter;
        private float _lastInnerRadius, _lastOuterRadius;
        private CustomSprite _face, _clothes, _vehicle, _star;
        private bool _spritesAttempted, _suppressCurrentPhase;

        public SearchHudSystem() { _current = this; }

        public static void NotifyPlayerDeath()
        {
            SearchHudSystem current = _current;
            PoliceSearchRuntimeState.ResetSearch(true);
            if (current == null) return;
            current._suppressCurrentPhase = true;
            current._lastNativeWanted = 0;
            current._lastUpdateAt = Game.GameTime;
            current._searchLatchedUntil = 0;
            current._visualContactSince = 0;
            current.ClearSearchCircles();
        }

        public void Update(Ped player, CaseMemory memory, int nativeWanted, Config cfg)
        {
            if (memory != null) PoliceSearchRuntimeState.BindCase(memory);
            CaseMemory effective = memory ?? PoliceSearchRuntimeState.CaseFor(player);
            int now = Game.GameTime;
            bool runtimeSearch = PoliceSearchRuntimeState.SearchActive;

            bool hardGap = _lastUpdateAt > 0 && now - _lastUpdateAt > HardTransitionGapMs;
            if (nativeWanted > 0 || runtimeSearch) _suppressCurrentPhase = false;
            else if (_lastNativeWanted > 0 && hardGap) _suppressCurrentPhase = true;
            _lastUpdateAt = now;
            _lastNativeWanted = nativeWanted;

            bool hasCase = effective != null && (effective.Active || effective.WarrantActive);
            if (_suppressCurrentPhase || !cfg.SearchHudEnabled || (!hasCase && !runtimeSearch))
            {
                _searchLatchedUntil = 0;
                _visualContactSince = 0;
                ClearSearchCircles();
                return;
            }

            int threat = runtimeSearch
                ? Math.Max(1, Math.Min(6, PoliceSearchRuntimeState.ThreatLevel))
                : Math.Max(1, Math.Min(6, effective.ThreatLevel));
            int lifetime = SearchLifetimeForThreat(threat, cfg);

            if (runtimeSearch)
            {
                Vector3 center = PoliceSearchRuntimeState.LastKnownPosition != Vector3.Zero
                    ? PoliceSearchRuntimeState.LastKnownPosition
                    : effective.LastKnownPosition;
                int ageMs = PoliceSearchRuntimeState.LastDirectObservationAt > 0
                    ? Math.Max(0, now - PoliceSearchRuntimeState.LastDirectObservationAt)
                    : Math.Max(0, now - PoliceSearchRuntimeState.SearchStartedAt);
                DrawSearch(center, threat, ageMs, cfg);
                if (cfg.ShowEvidenceIcons) DrawEvidence(effective, cfg, true);
                return;
            }

            bool recentlyObserved = effective.LastObservedGameTime > 0 && now - effective.LastObservedGameTime < cfg.SearchCircleObservationGraceMs;
            int sinceObservation = effective.LastObservedGameTime > 0 ? Math.Max(0, now - effective.LastObservedGameTime) : int.MaxValue;
            bool lostContact = nativeWanted > 0 && !recentlyObserved && sinceObservation >= Math.Max(250, cfg.SearchLostContactDelayMs);
            bool postPursuitSearch = nativeWanted == 0 && effective.LastWantedEndedAt > 0 && now - effective.LastWantedEndedAt <= lifetime;

            if (lostContact || postPursuitSearch)
                _searchLatchedUntil = Math.Max(_searchLatchedUntil, now + lifetime);

            bool latched = now <= _searchLatchedUntil;
            if (nativeWanted > 0 && recentlyObserved)
            {
                if (_visualContactSince == 0) _visualContactSince = now;
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
                if (cfg.ShowEvidenceIcons && nativeWanted > 0) DrawEvidence(effective, cfg, false);
                return;
            }

            int age = effective.LastObservedGameTime > 0 ? Math.Max(0, now - effective.LastObservedGameTime) : 0;
            DrawSearch(effective.LastKnownPosition, threat, age, cfg);
            if (cfg.ShowEvidenceIcons) DrawEvidence(effective, cfg, false);
        }

        public void Cleanup()
        {
            ClearSearchCircles();
            _lastUpdateAt = 0;
            _lastNativeWanted = 0;
            _searchLatchedUntil = 0;
            _visualContactSince = 0;
            _suppressCurrentPhase = false;
        }

        private void DrawSearch(Vector3 center, int threat, int ageMs, Config cfg)
        {
            float growth = Math.Min(Math.Max(0f, cfg.SearchMaxGrowth), ageMs / 1000f * Math.Max(0f, cfg.SearchUncertaintyGrowthPerSecond));
            float inner = cfg.SearchInnerBaseRadius + Math.Max(0, threat - 1) * cfg.SearchRadiusPerStar + growth * 0.55f;
            float outer = inner + cfg.SearchOuterExtraRadius + growth;

            // High wanted levels represent district-scale searches. Six stars is
            // intentionally much larger and longer than vanilla five-star search.
            if (threat >= 5)
            {
                inner = Math.Max(inner, threat >= 6 ? 760f : 520f);
                outer = Math.Max(outer, threat >= 6 ? 1350f : 860f);
            }

            if (cfg.ShowSearchCircles) EnsureCircles(center, threat, inner, outer, cfg);
            else ClearSearchCircles();
        }

        private static int SearchLifetimeForThreat(int threat, Config cfg)
        {
            int baseMs = Math.Max(60000, cfg.SearchPhaseLifetimeMs);
            if (threat >= 6) return Math.Max(baseMs * 5, 300000);
            if (threat == 5) return Math.Max(baseMs * 3, 220000);
            if (threat == 4) return Math.Max(baseMs * 2, 160000);
            if (threat == 3) return Math.Max((int)(baseMs * 1.6f), 120000);
            if (threat == 2) return Math.Max((int)(baseMs * 1.25f), 90000);
            return Math.Max(baseMs, 75000);
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

        private void DrawEvidence(CaseMemory memory, Config cfg, bool runtimeSearch)
        {
            if (memory == null) return;
            EnsureSprites();
            float x = 1134f, y = 61f, size = cfg.EvidenceIconSize;
            int stars = Math.Max(1, Math.Min(6, runtimeSearch ? PoliceSearchRuntimeState.ThreatLevel : memory.ThreatLevel));
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

            // Face knowledge is historical identity evidence and remains valid once
            // police genuinely know the face. Outfit/vehicle are current signalment:
            // changing either out of sight immediately removes that red badge.
            if (memory.FaceKnown && _face != null) DrawIcon(_face, ref iconX, iconY, size);

            bool outfit = PoliceSearchRuntimeState.ActiveOutfit != null
                ? PoliceSearchRuntimeState.ActiveOutfitValid
                : memory.OutfitKnown;
            bool vehicle = PoliceSearchRuntimeState.ActiveVehicle != null
                ? PoliceSearchRuntimeState.ActiveVehicleValid
                : memory.Vehicle != null;

            if (outfit && _clothes != null) DrawIcon(_clothes, ref iconX, iconY, size);
            if (vehicle && _vehicle != null) DrawIcon(_vehicle, ref iconX, iconY, size);
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
