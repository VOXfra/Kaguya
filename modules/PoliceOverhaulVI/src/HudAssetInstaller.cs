using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace VOX.PoliceOverhaulVI
{
    internal static class HudAssetInstaller
    {
        private const string UiDirectory = "scripts\\PoliceOverhaulVI\\UI";

        public static void EnsureHighResolutionAssets()
        {
            try
            {
                Directory.CreateDirectory(UiDirectory);
                Ensure(Path.Combine(UiDirectory, "face.png"), DrawFace);
                Ensure(Path.Combine(UiDirectory, "clothes.png"), DrawClothes);
                Ensure(Path.Combine(UiDirectory, "vehicle.png"), DrawVehicle);
                Ensure(Path.Combine(UiDirectory, "weapon.png"), DrawWeapon);
                Ensure(Path.Combine(UiDirectory, "mask.png"), DrawMask);
                Ensure(Path.Combine(UiDirectory, "starRED.png"), DrawStar);
            }
            catch { }
        }

        private static void Ensure(string path, Action<Graphics> draw)
        {
            try
            {
                if (File.Exists(path))
                {
                    using (Image existing = Image.FromFile(path))
                        if (existing.Width >= 128 && existing.Height >= 128) return;
                }
            }
            catch { }

            using (var bmp = new Bitmap(256, 256))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                DrawBadge(g);
                draw(g);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static void DrawBadge(Graphics g)
        {
            var rect = new RectangleF(7, 7, 242, 242);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(194, 27, 43);
                    brush.SurroundColors = new[] { Color.FromArgb(95, 2, 15) };
                    g.FillPath(brush, path);
                }
            }
            using (var outline = new Pen(Color.FromArgb(180, 0, 0, 0), 7f))
                g.DrawEllipse(outline, rect);
        }

        private static void DrawFace(Graphics g)
        {
            using (Brush white = new SolidBrush(Color.White))
            {
                g.FillEllipse(white, 91, 65, 74, 74);
                using (var body = new GraphicsPath())
                {
                    body.AddBezier(62, 192, 67, 151, 91, 140, 128, 140);
                    body.AddBezier(128, 140, 165, 140, 189, 151, 194, 192);
                    body.CloseFigure();
                    g.FillPath(white, body);
                }
            }
        }

        private static void DrawClothes(Graphics g)
        {
            using (var pen = new Pen(Color.White, 14f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawArc(pen, 111, 62, 35, 35, 180, 260);
                g.DrawLine(pen, 128, 94, 128, 112);
                g.DrawLine(pen, 128, 112, 56, 173);
                g.DrawLine(pen, 56, 173, 200, 173);
                g.DrawLine(pen, 200, 173, 128, 112);
            }
        }

        private static void DrawVehicle(Graphics g)
        {
            using (Brush white = new SolidBrush(Color.White))
            using (Brush red = new SolidBrush(Color.FromArgb(118, 10, 27)))
            {
                var body = new GraphicsPath();
                body.AddPolygon(new[]
                {
                    new PointF(54,112), new PointF(72,78), new PointF(91,66), new PointF(165,66),
                    new PointF(184,78), new PointF(202,112), new PointF(211,124), new PointF(207,179),
                    new PointF(181,188), new PointF(75,188), new PointF(49,179), new PointF(45,124)
                });
                g.FillPath(white, body);
                g.FillEllipse(white, 43, 102, 35, 22);
                g.FillEllipse(white, 178, 102, 35, 22);
                g.FillPolygon(red, new[] { new PointF(91,79), new PointF(165,79), new PointF(181,113), new PointF(75,113) });
                g.FillPolygon(red, new[] { new PointF(62,142), new PointF(94,148), new PointF(99,163), new PointF(65,160) });
                g.FillPolygon(red, new[] { new PointF(194,142), new PointF(162,148), new PointF(157,163), new PointF(191,160) });
            }
        }

        private static void DrawWeapon(Graphics g)
        {
            using (Brush white = new SolidBrush(Color.White))
            {
                PointF[] slide =
                {
                    new PointF(55,91), new PointF(174,91), new PointF(196,106),
                    new PointF(185,126), new PointF(107,126), new PointF(95,139),
                    new PointF(65,139), new PointF(65,121), new PointF(55,115)
                };
                g.FillPolygon(white, slide);
                PointF[] grip =
                {
                    new PointF(116,122), new PointF(156,122), new PointF(148,184),
                    new PointF(113,184), new PointF(104,143)
                };
                g.FillPolygon(white, grip);
                g.FillRectangle(white, 174, 101, 28, 10);
                using (var pen = new Pen(white, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, 88, 116, 45, 39, 200, 135);
            }
        }

        private static void DrawMask(Graphics g)
        {
            using (Brush white = new SolidBrush(Color.White))
            using (Brush red = new SolidBrush(Color.FromArgb(118, 10, 27)))
            {
                g.FillEllipse(white, 71, 61, 114, 132);
                PointF[] bandana =
                {
                    new PointF(69,119), new PointF(187,119), new PointF(175,174),
                    new PointF(128,201), new PointF(81,174)
                };
                g.FillPolygon(white, bandana);
                g.FillEllipse(red, 91, 91, 25, 14);
                g.FillEllipse(red, 140, 91, 25, 14);
                g.FillPolygon(red, new[]
                {
                    new PointF(103,139), new PointF(153,139), new PointF(128,160)
                });
            }
        }

        private static void DrawStar(Graphics g)
        {
            PointF[] points = new PointF[10];
            const double start = -Math.PI / 2.0;
            for (int i = 0; i < 10; i++)
            {
                double a = start + i * Math.PI / 5.0;
                float r = (i & 1) == 0 ? 105f : 45f;
                points[i] = new PointF(128f + (float)Math.Cos(a) * r, 128f + (float)Math.Sin(a) * r);
            }
            using (Brush fill = new SolidBrush(Color.FromArgb(220, 0, 0))) g.FillPolygon(fill, points);
            using (var pen = new Pen(Color.Black, 12f) { LineJoin = LineJoin.Round }) g.DrawPolygon(pen, points);
        }
    }
}
