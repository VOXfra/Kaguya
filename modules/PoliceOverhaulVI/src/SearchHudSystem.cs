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
        private int _innerBlip;
        private int _outerBlip;
        private Vector3 _lastCenter;
        private int _lastThreat;
        private CustomSprite _face;
        private CustomSprite _clothes;
        private CustomSprite _vehicle;
        private CustomSprite _star;
        private bool _spritesAttempted;

        public void Update(Ped player, CaseMemory memory, int nativeWanted, Config cfg)
        {
            if (!cfg.SearchHudEnabled || memory == null || (!memory.Active && !memory.WarrantActive))
            {
                ClearSearchCircles();
                return;
            }
            bool recentlyObserved = memory.LastObservedGameTime > 0 && Game.GameTime - memory.LastObservedGameTime < cfg.SearchCircleObservationGraceMs;
            bool shouldShowCircles = cfg.ShowSearchCircles && (nativeWanted == 0 || !recentlyObserved);
            if (shouldShowCircles) EnsureCircles(memory.LastKnownPosition, Math.Max(1, memory.ThreatLevel), cfg); else ClearSearchCircles();
            if (cfg.ShowEvidenceIcons && nativeWanted == 0) DrawEvidence(memory, cfg);
        }

        public void Cleanup() { ClearSearchCircles(); }

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
