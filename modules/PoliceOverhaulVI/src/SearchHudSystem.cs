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
        // Internal evidence/case memory can persist for hours. The visible VI
        // search HUD must represent only a live/recent search phase.
        private const int SearchPhaseLifetimeMs = 60000;
        // The main police script intentionally yields while the protagonist is
        // dead. A wanted->0 transition after a long update gap is therefore a
        // reliable indication that the previous encounter was interrupted by
        // death/respawn (or another hard scripted transition), not a normal
        // search phase that should keep drawing evidence icons.
        private const int HardTransitionGapMs = 2000;

        private int _innerBlip;
        private int _outerBlip;
        private Vector3 _lastCenter;
        private int _lastThreat;
        private CustomSprite _face;
        private CustomSprite _clothes;
        private CustomSprite _vehicle;
        private CustomSprite _star;
        private bool _spritesAttempted;
        private int _lastUpdateAt;
        private int _lastNativeWanted;
        private bool _suppressCurrentPhase;

        public void Update(Ped player, CaseMemory memory, int nativeWanted, Config cfg)
        {
            int now = Game.GameTime;
            bool hardGap = _lastUpdateAt > 0 && now - _lastUpdateAt > HardTransitionGapMs;

            // A genuinely new police encounter always re-arms the search HUD.
            if (nativeWanted > 0)
                _suppressCurrentPhase = false;
            // Death/respawn used to leave _lastWanted > 0 while the game reset
            // native wanted to zero. Do not resurrect the previous case HUD.
            else if (_lastNativeWanted > 0 && hardGap)
                _suppressCurrentPhase = true;

            _lastUpdateAt = now;
            _lastNativeWanted = nativeWanted;

            if (_suppressCurrentPhase || !cfg.SearchHudEnabled || memory == null || (!memory.Active && !memory.WarrantActive))
            {
                ClearSearchCircles();
                return;
            }

            // Once the active wanted phase is over, only keep the VI-style
            // search/evidence HUD for a bounded search window. CaseMemory.Active
            // means police memory exists; it does NOT mean officers are still
            // visibly searching right now.
            if (nativeWanted == 0)
            {
                if (memory.LastWantedEndedAt <= 0 || now - memory.LastWantedEndedAt > SearchPhaseLifetimeMs)
                {
                    ClearSearchCircles();
                    return;
                }
            }

            bool recentlyObserved = memory.LastObservedGameTime > 0 && now - memory.LastObservedGameTime < cfg.SearchCircleObservationGraceMs;
            bool shouldShowCircles = cfg.ShowSearchCircles && (nativeWanted == 0 || !recentlyObserved);
            if (shouldShowCircles) EnsureCircles(memory.LastKnownPosition, Math.Max(1, memory.ThreatLevel), cfg); else ClearSearchCircles();
            if (cfg.ShowEvidenceIcons && nativeWanted == 0) DrawEvidence(memory, cfg);
        }

        public void Cleanup()
        {
            ClearSearchCircles();
            _lastUpdateAt = 0;
            _lastNativeWanted = 0;
            _suppressCurrentPhase = false;
        }

        private void EnsureCircles(Vector3 center, int threat, Config cfg)
        {
            if (center == Vector3.Zero) return;
            if (_innerBlip != 0 && _outerBlip != 0 && _lastThreat == threat && Perception.Distance(center, _lastCenter) < 12f) return;
            ClearSearchCircles();
            float inner = cfg.SearchInnerBaseRadius + Math.Max(0, threat - 1) * cfg.SearchRadiusPerStar;
            float outer = inner + cfg.SearchOuterExtraRadius;
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
            }
            catch { ClearSearchCircles(); }
        }

        private void ClearSearchCircles()
        {
            DeleteBlip(ref _innerBlip);
            DeleteBlip(ref _outerBlip);
            _lastThreat = 0;
            _lastCenter = Vector3.Zero;
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
