using System.Collections.Generic;
using UnityEngine;

namespace ABetterU.Coach
{
    /// Runtime-generated artwork: rounded "glass" panel textures and the sunset sky. Generating these in code
    /// keeps the project free of binary art files and lets every panel size get a correctly proportioned corner.
    public static class VRArt
    {
        const float PixelsPerMetre = 600f;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        // ── Rounded rectangle with a vertical gradient fill and an inner border, anti-aliased by signed distance ──

        public static Texture2D RoundedRect(float widthM, float heightM, float radiusM, Color fillTop, Color fillBottom, Color border, float borderM)
        {
            int w = Mathf.Max(8, Mathf.RoundToInt(widthM * PixelsPerMetre));
            int h = Mathf.Max(8, Mathf.RoundToInt(heightM * PixelsPerMetre));
            string key = $"rr:{w}x{h}:{radiusM:0.000}:{fillTop}:{fillBottom}:{border}:{borderM:0.000}";
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            float r = Mathf.Min(radiusM * PixelsPerMetre, Mathf.Min(w, h) * 0.5f);
            float bw = borderM * PixelsPerMetre;
            var pixels = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                Color fill = Color.Lerp(fillBottom, fillTop, (y + 0.5f) / h);
                float py = y + 0.5f - h * 0.5f;
                for (int x = 0; x < w; x++)
                {
                    float px = x + 0.5f - w * 0.5f;
                    float qx = Mathf.Abs(px) - (w * 0.5f - r);
                    float qy = Mathf.Abs(py) - (h * 0.5f - r);
                    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                    float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;   // < 0 inside

                    float edge = Mathf.Clamp01(0.5f - d);                          // 1 inside, 0 outside, 1 px antialias
                    if (edge <= 0f) { pixels[y * w + x] = new Color32(0, 0, 0, 0); continue; }

                    float borderMix = bw > 0f ? Mathf.Clamp01(d + bw + 0.5f) : 0f;   // 1 within the border band
                    Color c = Color.Lerp(fill, border, borderMix);

                    // Glass light: a soft sheen toward the top, a faint diagonal specular streak, and a
                    // brighter rim where light would catch the top edge. Kept subtle so text stays readable.
                    float vTop = (y + 0.5f) / h;
                    float uAcross = (x + 0.5f) / w;
                    float sheen = Mathf.Pow(vTop, 3f) * 0.07f;
                    float streakBand = (uAcross * 0.72f + vTop * 0.28f - 0.62f) * 3.2f;
                    float streak = Mathf.Exp(-streakBand * streakBand) * 0.055f;
                    float topRim = bw > 0f ? borderMix * Mathf.Pow(vTop, 6f) * 0.35f : 0f;
                    float lift = sheen + streak + topRim;
                    c.r += lift; c.g += lift; c.b += lift * 1.05f;

                    c.a *= edge;
                    pixels[y * w + x] = c;
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                name = "glass",
            };
            tex.SetPixels32(pixels);
            // Trilinear filtering samples mip levels at dashboard viewing distances.
            // Populate those levels before discarding the CPU copy of the pixels.
            tex.Apply(true, true);
            cache[key] = tex;
            return tex;
        }

        // ── Sunset over mountains and a lake, as an equirectangular texture for the sky sphere ──────────────────

        public static Texture2D Sky(int w = 2560, int h = 1280)
        {
            const float TwoPi = Mathf.PI * 2f;
            const float sunU = 0.5f, sunElev = 0.045f;                 // low sun, just above the horizon

            // Three mountain ridges per column, farthest to nearest (heights in radians above the horizon).
            var ridge = new float[3, w];
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * TwoPi;
                ridge[0, x] = 0.115f + 0.050f * Mathf.Sin(u * 3f + 1f) + 0.030f * Mathf.Sin(u * 7f + 2f) + 0.014f * Mathf.Sin(u * 19f);
                ridge[1, x] = 0.070f + 0.040f * Mathf.Sin(u * 4f + 4f) + 0.022f * Mathf.Sin(u * 11f + 1f) + 0.010f * Mathf.Sin(u * 23f + 2f);
                ridge[2, x] = 0.030f + 0.022f * Mathf.Sin(u * 5f + 2f) + 0.012f * Mathf.Sin(u * 13f + 5f);
            }
            Color[] ridgeColour =
            {
                new Color(0.42f, 0.33f, 0.52f),    // far: hazy violet
                new Color(0.27f, 0.21f, 0.36f),
                new Color(0.14f, 0.11f, 0.21f),    // near: dark
            };

            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float elev = ((y + 0.5f) / h - 0.5f) * Mathf.PI;       // -90 deg (down) to +90 deg (up)
                bool water = elev < 0f;
                float e = Mathf.Abs(elev);                              // water mirrors the sky above it

                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    Color c = SkyColour(e, u, sunU, sunElev);

                    // Mountains rise from the horizon (and are mirrored in the water, darker).
                    for (int layer = 0; layer < 3; layer++)
                    {
                        if (e < ridge[layer, x])
                        {
                            float haze = 1f - Mathf.Clamp01(e / ridge[layer, x]) * 0.0f;
                            c = Color.Lerp(ridgeColour[layer], new Color(0.95f, 0.55f, 0.45f), Mathf.Clamp01(1f - e / 0.12f) * 0.18f * haze);
                            break;
                        }
                    }

                    if (water)
                    {
                        float deep = Mathf.Clamp01(e / 0.9f);
                        c = Color.Lerp(c * 0.62f, new Color(0.03f, 0.07f, 0.12f), deep * 0.85f);
                        c.r += 0.02f; c.g += 0.03f; c.b += 0.05f;
                    }
                    c.a = 1f;
                    pixels[y * w + x] = c;
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "sunset-sky",
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        // ── Small flat icons and the assistant orb, drawn from simple shapes ─────────────────────────────────

        public enum IconKind { Person, Group, Dumbbell, Compass, Apple, Heart }

        const int IconPx = 128;

        public static Texture2D Icon(IconKind kind, Color color)
        {
            string key = $"icon:{kind}:{color}";
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var pixels = new Color32[IconPx * IconPx];
            for (int y = 0; y < IconPx; y++)
                for (int x = 0; x < IconPx; x++)
                {
                    float u = (x + 0.5f) / IconPx, v = (y + 0.5f) / IconPx;
                    Color c = color;
                    c.a = Mathf.Clamp01(IconCoverage(kind, u, v)) * color.a;
                    pixels[y * IconPx + x] = c;
                }
            return Store(key, pixels, IconPx, IconPx, "icon");
        }

        static float IconCoverage(IconKind kind, float u, float v)
        {
            switch (kind)
            {
                case IconKind.Person:
                    return Person(u, v, 0.5f, 0.5f, 1f);
                case IconKind.Group:
                    return Mathf.Max(Person(u, v, 0.5f, 0.52f, 0.9f), Mathf.Max(Person(u, v, 0.22f, 0.46f, 0.7f), Person(u, v, 0.78f, 0.46f, 0.7f)));
                case IconKind.Dumbbell:
                    return Mathf.Max(Mathf.Max(Box(u, v, 0.5f, 0.5f, 0.28f, 0.035f, 0.02f), Box(u, v, 0.30f, 0.5f, 0.045f, 0.20f, 0.02f)),
                        Mathf.Max(Box(u, v, 0.70f, 0.5f, 0.045f, 0.20f, 0.02f), Mathf.Max(Box(u, v, 0.20f, 0.5f, 0.03f, 0.13f, 0.015f), Box(u, v, 0.80f, 0.5f, 0.03f, 0.13f, 0.015f))));
                case IconKind.Compass:
                {
                    float du = u - 0.5f, dv = v - 0.5f;
                    float ring = Soft(Mathf.Abs(Mathf.Sqrt(du * du + dv * dv) - 0.34f) - 0.035f);
                    float ru = (du + dv) * 0.7071f, rv = (dv - du) * 0.7071f;
                    float needle = Soft((Mathf.Abs(ru) * 0.55f + Mathf.Abs(rv) - 0.17f) * 0.7f);
                    return Mathf.Max(ring, needle);
                }
                case IconKind.Apple:
                    return Mathf.Max(Mathf.Max(Disc(u, v, 0.40f, 0.42f, 0.23f), Disc(u, v, 0.60f, 0.42f, 0.23f)), Box(u, v, 0.5f, 0.76f, 0.018f, 0.08f, 0.01f));
                default: // Heart
                {
                    float lobes = Mathf.Max(Disc(u, v, 0.36f, 0.60f, 0.19f), Disc(u, v, 0.64f, 0.60f, 0.19f));
                    float point = Soft((Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.50f)) * 0.9f - 0.28f) * (v < 0.60f ? 1f : 0f);
                    return Mathf.Max(lobes, point);
                }
            }
        }

        static float Soft(float d) => Mathf.Clamp01(0.5f - d * IconPx);       // d in normalised units, < 0 inside

        static float Disc(float u, float v, float cx, float cy, float r) =>
            Soft(Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy)) - r);

        static float Box(float u, float v, float cx, float cy, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(u - cx) - (hw - r), qy = Mathf.Abs(v - cy) - (hh - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Soft(Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r);
        }

        static float Person(float u, float v, float cx, float cy, float s)
        {
            float head = Disc(u, v, cx, cy + 0.20f * s, 0.12f * s);
            float body = Disc(u, v, cx, cy - 0.16f * s, 0.26f * s) * (v < cy + 0.02f * s ? 1f : 0f);
            return Mathf.Max(head, body);
        }

        /// A soft round indicator: a solid core with a faint glow around it.
        public static Texture2D Orb(Color core, Color glow)
        {
            string key = $"orb:{core}:{glow}";
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int n = 96;
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float du = (x + 0.5f) / n - 0.5f, dv = (y + 0.5f) / n - 0.5f;
                    float rn = Mathf.Sqrt(du * du + dv * dv) * 2f;               // 0 in the centre, 1 at the texture's inscribed edge
                    float coreA = Mathf.Clamp01((0.56f - rn) / 0.05f);
                    float glowA = rn > 0.5f ? Mathf.Pow(1f - Mathf.Clamp01((rn - 0.5f) / 0.5f), 2f) * 0.65f : 0f;
                    Color c = Color.Lerp(glow, Color.Lerp(core, Color.white, 0.25f * (1f - rn)), coreA);
                    c.a = Mathf.Max(coreA, glowA);
                    pixels[y * n + x] = c;
                }
            return Store(key, pixels, n, n, "orb");
        }

        // ── Premium data-ink: donut ring, sparkline and week bars, drawn as textures like the panels ─────

        /// A donut ring: dark track with a coloured arc for fraction (0 to 1), starting at the top.
        public static Texture2D Ring(float fraction, Color fill, int px = 256)
        {
            float f = Mathf.Clamp01(fraction);
            string key = $"ring:{f:0.00}:{fill}:{px}";
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var pixels = new Color32[px * px];
            float cx = px * 0.5f, cy = px * 0.5f, radius = px * 0.40f, halfThick = px * 0.055f;
            Color track = new Color(0.14f, 0.18f, 0.26f, 0.95f);
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float band = Mathf.Clamp01(halfThick - Mathf.Abs(dist - radius) + 0.75f);
                    if (band <= 0f) { pixels[y * px + x] = new Color32(0, 0, 0, 0); continue; }

                    // Angle from the top, clockwise in texture space (y is up after upload).
                    float ang = Mathf.Atan2(dx, dy) / (Mathf.PI * 2f);
                    if (ang < 0f) ang += 1f;
                    bool filled = ang <= f;
                    Color c = filled ? fill : track;
                    if (filled)
                    {
                        float head = Mathf.Clamp01((f - ang) * 40f);          // soft leading edge
                        c = Color.Lerp(fill, Color.white, 0.25f * (1f - head));
                    }
                    c.a = band * 0.98f;
                    pixels[y * px + x] = c;
                }
            return Store(key, pixels, px, px, "ring");
        }

        /// A thin glowing line chart through values (any scale; normalised internally). Transparent elsewhere.
        public static Texture2D Sparkline(float[] values, Color line)
        {
            if (values == null || values.Length < 2) values = new float[] { 0f, 0f };
            string key = $"spark:{string.Join(",", values)}:{line}";
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int w = 512, h = 160;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in values) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
            if (max - min < 0.001f) { max = min + 1f; }

            var pts = new Vector2[values.Length];
            for (int i = 0; i < values.Length; i++)
                pts[i] = new Vector2((i + 0.5f) / values.Length * w, Mathf.Lerp(h * 0.18f, h * 0.82f, (values[i] - min) / (max - min)));

            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < pts.Length - 1; i++)
                    {
                        var a = pts[i]; var b = pts[i + 1];
                        var ab = b - a;
                        float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(new Vector2(x - a.x, y - a.y), ab) / ab.sqrMagnitude) : 0f;
                        var p = a + ab * t;
                        best = Mathf.Min(best, Vector2.Distance(new Vector2(x, y), p));
                    }
                    float core = Mathf.Clamp01(2.6f - best);
                    float glow = Mathf.Clamp01(7f - best) * 0.35f;
                    float a2 = Mathf.Max(core, glow);
                    if (a2 <= 0f) { pixels[y * w + x] = new Color32(0, 0, 0, 0); continue; }
                    Color c = Color.Lerp(line, Color.white, core * 0.35f);
                    c.a = a2;
                    pixels[y * w + x] = c;
                }
            return Store(key, pixels, w, h, "sparkline");
        }

        /// Seven slim vertical bars (oldest to newest), heights from values normalised to max.
        public static Texture2D WeekBars(float[] values, Color fill)
        {
            if (values == null || values.Length == 0) values = new float[] { 0f };
            string key = $"bars:{string.Join(",", values)}:{fill}";
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int w = 512, h = 160;
            float max = 0.001f;
            foreach (var v in values) max = Mathf.Max(max, v);

            var pixels = new Color32[w * h];
            int n = values.Length;
            float slot = w / (float)n;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = Mathf.Min(n - 1, (int)(x / slot));
                    float local = (x - i * slot) / slot;                       // 0 to 1 across the slot
                    float barHalf = 0.26f;
                    float bh = Mathf.Lerp(0.08f, 0.92f, Mathf.Clamp01(values[i] / max)) * h;
                    bool inX = Mathf.Abs(local - 0.5f) <= barHalf;
                    bool inY = y <= bh;
                    bool newest = i == n - 1;
                    if (!inX || !inY) { pixels[y * w + x] = new Color32(0, 0, 0, 0); continue; }
                    Color c = newest ? Color.Lerp(fill, Color.white, 0.25f) : fill;
                    c.a = newest ? 1f : 0.75f;
                    pixels[y * w + x] = c;
                }
            return Store(key, pixels, w, h, "week-bars");
        }

        static Texture2D Store(string key, Color32[] pixels, int w, int h, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                name = name,
            };
            tex.SetPixels32(pixels);
            // Trilinear filtering samples mip levels at dashboard viewing distances.
            // Populate those levels before discarding the CPU copy of the pixels.
            tex.Apply(true, true);
            cache[key] = tex;
            return tex;
        }

        // Sky colour at an angle above the horizon, with the sun's glow around (sunU, sunElev).
        static Color SkyColour(float elev, float u, float sunU, float sunElev)
        {
            Color horizon = new Color(1.00f, 0.60f, 0.32f);
            Color mid = new Color(0.78f, 0.38f, 0.50f);
            Color zenith = new Color(0.10f, 0.13f, 0.32f);

            float t = Mathf.Clamp01(elev / (Mathf.PI * 0.5f));
            Color c = t < 0.35f
                ? Color.Lerp(horizon, mid, Mathf.SmoothStep(0f, 1f, t / 0.35f))
                : Color.Lerp(mid, zenith, Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.65f));

            float du = Mathf.Abs(u - sunU); du = Mathf.Min(du, 1f - du) * Mathf.PI * 2f;
            float dv = elev - sunElev;
            float dist = Mathf.Sqrt(du * du * Mathf.Cos(elev) * Mathf.Cos(elev) + dv * dv);

            float glow = Mathf.Exp(-dist * dist / 0.10f);
            c += new Color(1.0f, 0.62f, 0.25f) * glow * 0.65f;
            if (dist < 0.05f) c = Color.Lerp(c, new Color(1f, 0.93f, 0.75f), Mathf.Clamp01((0.05f - dist) / 0.012f));
            return c;
        }
    }
}
