using GTA;
using GTA.Native;
using GTA.UI;
using System;
using System.Drawing;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    internal static class PoliceWantedHudState
    {
        public static int Level;
        public static bool Owned;

        public static void Set(int level, bool owned)
        {
            if (level <= 0 || !owned) return;
            Level = Math.Max(1, Math.Min(6, level));
            Owned = true;
        }

        public static void Clear()
        {
            Level = 0;
            Owned = false;
        }
    }

    public sealed class PoliceOverhaulVIWantedHudScript : Script
    {
        private const string RedStarPath = "scripts\\PoliceOverhaulVI\\UI\\starRED.png";
        private CustomSprite _redStar;
        private bool _redAttempted;
        private int _lastVanillaTextureRequest;

        public PoliceOverhaulVIWantedHudScript()
        {
            Interval = 0;
            Tick += OnTick;
            Aborted += OnAborted;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!PoliceWantedHudState.Owned || PoliceWantedHudState.Level <= 0) return;

            try { Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, 1); } catch { }

            int nativeWanted = 0;
            try { nativeWanted = Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle); } catch { }

            if (nativeWanted > 0) DrawVanillaStyleRow();
            else DrawRedSearchRow();
        }

        private void DrawVanillaStyleRow()
        {
            try
            {
                if (!Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, "commonmenu"))
                {
                    if (Game.GameTime - _lastVanillaTextureRequest > 250)
                    {
                        _lastVanillaTextureRequest = Game.GameTime;
                        Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, "commonmenu", false);
                    }
                    return;
                }

                float safe = Function.Call<float>(Hash.GET_SAFE_ZONE_SIZE);
                float safeRight = 1f - (1f - safe) * 0.5f;
                float rightMostX = safeRight - 0.0130f;
                const float y = 0.0438f;
                const float width = 0.0158f;
                const float height = 0.0285f;
                const float spacing = 0.0160f;

                for (int i = 0; i < PoliceWantedHudState.Level; i++)
                {
                    float x = rightMostX - i * spacing;
                    Function.Call(Hash.DRAW_SPRITE, "commonmenu", "leaderboard_star_icon",
                        x, y, width, height, 0f, 255, 255, 255, 245, false);
                }
            }
            catch { }
        }

        private void DrawRedSearchRow()
        {
            EnsureRedStar();
            if (_redStar == null) return;

            float right = 1152f;
            float y = 48f;
            float size = 25f;
            for (int i = 0; i < PoliceWantedHudState.Level; i++)
            {
                _redStar.Position = new PointF(right - i * 28f, y);
                _redStar.Size = new SizeF(size, size);
                _redStar.ScaledDraw();
            }
        }

        private void EnsureRedStar()
        {
            if (_redAttempted) return;
            _redAttempted = true;
            try
            {
                HudAssetInstaller.EnsureHighResolutionAssets();
                if (File.Exists(RedStarPath))
                    _redStar = new CustomSprite(Path.GetFullPath(RedStarPath), new SizeF(25f, 25f), new PointF(0f, 0f), Color.White, 0f, true);
            }
            catch { _redStar = null; }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            PoliceWantedHudState.Clear();
        }
    }
}
