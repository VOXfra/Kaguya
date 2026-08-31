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
            current.ClearSearchCircles();
        }

        public void Update(Ped player, CaseMemory memory, int nativeWanted, Config cfg)
        {
            int now = Game.GameTime;
            bool hardGap = _lastUpdateAt > 0 && now - _lastUpdateAt > HardTransitionGapMs;
            if (nativeWanted > 0) _suppressCurrentPhase = false;
            else if (_lastNativeWanted > 0 && hardGap) _suppressCurrentPhase = true;
            _lastUpdateAt = now; _lastNativeWanted = nativeWanted;

            if (_suppressCurrentPhase || !cfg.SearchHudEnabled || memory == null || (!memory.Active && !memory.WarrantActive))
            {
                ClearSearchCircles(); return;
            }

            bool recentlyObserved = memory.LastObservedGameTime > 0 && now - memory.LastObservedGameTime < cfg.SearchCircleObservationGraceMs;
            int sinceObservation = memory.LastObservedGameTime > 0 ? Math.Max(0, now - memory.LastObservedGameTime) : int.MaxValue;
            bool lostContact = nativeWanted > 0 && !recentlyObserved && sinceObservation >= Math.Max(250, cfg.SearchLostContactDelayMs);
            bool postPursuitSearch = nativeWanted == 0 && memory.LastWantedEndedAt > 0 && now - memory.LastWantedEndedAt <= Math.Max(1000, cfg.SearchPhaseLifetimeMs);
            bool searchActive = lostContact || postPursuitSearch;

            if (!searchActive)
            {
                ClearSearchCircles();
                return;
            }

            int ageMs = memory.LastObservedGameTime > 0 ? Math.Max(0, now - memory.LastObservedGameTime) : 0;
            float growth = Math.Min(Math.Max(0f, cfg.SearchMaxGrowth), ageMs / 1000f * Math.Max(0f, cfg.SearchUncertaintyGrowthPerSecond));
            float inner = cfg.SearchInnerBaseRadius + Math.Max(0, memory.ThreatLevel - 1) * cfg.SearchRadiusPerStar + growth * 0.55f;
            float outer = inner + cfg.SearchOuterExtraRadius + growth;

            if (cfg.ShowSearchCircles) EnsureCircles(memory.LastKnownPosition, Math.Max(1, memory.ThreatLevel), inner, outer, cfg);
            else ClearSearchCircles();
            if (cfg.ShowEvidenceIcons) DrawEvidence(memory, cfg);
        }

        public void Cleanup()
        {
            ClearSearchCircles(); _lastUpdateAt = 0; _lastNativeWanted = 0; _suppressCurrentPhase = false;
        }

        private void EnsureCircles(Vector3 center, int threat, float inner, float outer, Config cfg)
        {
            if (center == Vector3.Zero) return;
            bool same = _innerBlip != 0 && _outerBlip != 0 && _lastThreat == threat &&
                        Perception.Distance(center, _lastCenter) < 10f && Math.Abs(inner - _lastInnerRadius) < 7f && Math.Abs(outer - _lastOuterRadius) < 10f;
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
                _lastCenter = center; _lastThreat = threat; _lastInnerRadius = inner; _lastOuterRadius = outer;
            }
            catch { ClearSearchCircles(); }
        }

        private void ClearSearchCircles()
        {
            DeleteBlip(ref _innerBlip); DeleteBlip(ref _outerBlip);
            _lastThreat = 0; _lastCenter = Vector3.Zero; _lastInnerRadius = _lastOuterRadius = 0f;
        }

        private static void DeleteBlip(ref int handle)
        {
            if (handle == 0) return;
            try { Blip b = new Blip(handle); if (b.Exists()) b.Delete(); } catch { }
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
                    _star.Position = new PointF(x - i * 25f, y); _star.Size = new SizeF(22f,22f); _star.ScaledDraw();
                }
            }
            float iconX = x - (stars - 1) * 25f, iconY = y + 31f;
            if (memory.FaceKnown && _face != null) DrawIcon(_face, ref iconX, iconY, size);
            if (memory.OutfitKnown && _clothes != null) DrawIcon(_clothes, ref iconX, iconY, size);
            if (memory.Vehicle != null && _vehicle != null) DrawIcon(_vehicle, ref iconX, iconY, size);
        }

        private static void DrawIcon(CustomSprite sprite, ref float x, float y, float size)
        {
            sprite.Position = new PointF(x,y); sprite.Size = new SizeF(size,size); sprite.ScaledDraw(); x += size + 5f;
        }

        private void EnsureSprites()
        {
            if (_spritesAttempted) return; _spritesAttempted = true;
            try
            {
                _face=TrySprite(Path.Combine(UiDirectory,"face.png")); _clothes=TrySprite(Path.Combine(UiDirectory,"clothes.png"));
                _vehicle=TrySprite(Path.Combine(UiDirectory,"vehicle.png")); _star=TrySprite(Path.Combine(UiDirectory,"starRED.png"));
            }
            catch { }
        }

        private static CustomSprite TrySprite(string path)
        {
            if (!File.Exists(path)) return null;
            return new CustomSprite(Path.GetFullPath(path),new SizeF(28f,28f),new PointF(0f,0f),Color.White,0f,true);
        }
    }
}
