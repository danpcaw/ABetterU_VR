using System.Collections.Generic;
using UnityEngine;

namespace ABetterU.Coach
{
    /// Runtime-generated artwork: rounded "glass" panel textures and the sunset sky. Generating these in code
    /// keeps the project free of binary art files and lets every panel size get a correctly proportioned corner.
    public static class VRArt
    {
        const float PixelsPerMetre = 300f;
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
                    c.a *= edge;
                    pixels[y * w + x] = c;
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "glass",
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            cache[key] = tex;
            return tex;
        }

        // ── Sunset over mountains and a lake, as an equirectangular texture for the sky sphere ──────────────────

        public static Texture2D Sky(int w = 1536, int h = 768)
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
