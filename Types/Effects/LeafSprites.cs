using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Leaf shapes cut out of a tree's own leaf texture and packed into an atlas,
// for the falling leaves: one extraction's result, and the routine that makes
// it. Separate leaves or petals are the connected opaque regions of the
// texture's alpha; the most typical of them (by size, then colour) go into a
// 4 x 4 atlas, each centred in its tile with its aspect kept and a
// transparent margin, so a particle showing one tile shows one leaf. A
// texture that is one dense cluster has no such regions, and gives rounded
// cut-outs of its well-covered parts instead.
//
// UnityEngine and FoliageFXConfig only, no VaM type, so it runs outside VaM
// as well (a standalone player tried it). Nothing here catches; a throw
// reaches whoever steps the routine.
sealed class LeafSprites
{
    public delegate void Fn();

    // Alpha at or above this is leaf, below it see-through: the same line a
    // 0.5 cutoff draws.
    const byte OpaqueAlpha = 128;
    // A texture this small or smaller on a side holds no leaf worth cutting.
    const int MinTextureSize = 8;
    // A rounded cut-out is an oval this many times as tall as wide at the
    // least (the rest is random up to round), and never under this many
    // texels tall.
    const float FragmentAspectMin = 0.55f;
    const int FragmentRadiusYMin = 4;
    // The oval's edge fades over this share of its radius: 1/6.
    const float FragmentEdgeSharpness = 6f;
    // A fixed seed, so a texture gives the same cut-outs every time.
    const int FragmentSeed = 1234;

    public Texture Source;
    public Texture2D Atlas;
    public Mesh Quad;
    public int ShapeCount;
    public bool Fragments;

    sealed class Blob
    {
        public int Id;
        public int Area;
        public int MinX, MinY, MaxX, MaxY;
        public bool TouchesEdge;
        public long R, G, B;
    }

    // Yields between the heavy steps (the read, each thinning level), so a
    // coroutine spreads the work over a few frames. `into.ShapeCount` is 0
    // when the texture has nothing usable; `done` runs either way.
    public static IEnumerator Extract(Texture source, LeafSprites into, Fn done)
    {
        into.Source = source;
        into.ShapeCount = 0;
        into.Fragments = false;
        if (source == null || source.width < MinTextureSize || source.height < MinTextureSize)
        {
            done();
            yield break;
        }
        int readMax = FoliageFXConfig.AtlasReadSize;
        int w = Mathf.Min(readMax, source.width);
        int h = Mathf.Max(MinTextureSize, Mathf.RoundToInt(w * (float)source.height / source.width));
        if (h > readMax)
        {
            w = Mathf.Max(MinTextureSize, Mathf.RoundToInt(w * (float)readMax / h));
            h = readMax;
        }
        Color32[] px = ReadTexture(source, w, h);
        yield return null;

        byte[] mask = new byte[w * h];
        for (int i = 0; i < mask.Length; i++) mask[i] = px[i].a >= OpaqueAlpha ? (byte)1 : (byte)0;

        // Leaves and blossoms are often joined to their twigs. Thinning the
        // mask by a texel or two cuts those thin links; the level that
        // separates the most leaf-like shapes wins, and its shapes are grown
        // back to full size.
        List<Blob> best = null;
        int[] bestLabels = null;
        for (int erode = 0; erode <= FoliageFXConfig.LeafErodeMax; erode++)
        {
            int[] labels = new int[w * h];
            List<Blob> blobs = Label(erode == 0 ? mask : Erode(mask, w, h, erode), labels, w, h);
            if (erode > 0) Grow(mask, labels, w, h, erode);
            Measure(blobs, labels, px, w, h);
            List<Blob> picked = PickLeaves(blobs, w, h);
            if (best == null || picked.Count > best.Count)
            {
                best = picked;
                bestLabels = labels;
            }
            yield return null;
        }

        int size = FoliageFXConfig.AtlasSize;
        Color32[] atlasPx = new Color32[size * size];
        int shapeCount;
        bool fragments = best.Count < FoliageFXConfig.LeafFragmentMin;
        if (!fragments)
        {
            for (int i = 0; i < best.Count; i++)
            {
                Blob b = best[i];
                // One texel of margin around the shape, so its soft edge is
                // copied too.
                CopyTile(px, bestLabels, w, h, b.MinX - 1, b.MinY - 1,
                    b.MaxX - b.MinX + 3, b.MaxY - b.MinY + 3, b.Id, false, Fill(b), atlasPx, i);
            }
            shapeCount = best.Count;
        }
        else shapeCount = MakeFragments(px, w, h, atlasPx);
        if (shapeCount == 0)
        {
            done();
            yield break;
        }

        Texture2D atlas = new Texture2D(size, size, TextureFormat.RGBA32, true, false);
        atlas.name = "FoliageFX leaves " + source.name;
        atlas.wrapMode = TextureWrapMode.Clamp;
        atlas.filterMode = FilterMode.Trilinear;
        atlas.anisoLevel = 2;
        atlas.SetPixels32(atlasPx);
        atlas.Apply(true, true);

        into.Atlas = atlas;
        into.Quad = MakeQuad();
        into.ShapeCount = shapeCount;
        into.Fragments = fragments;
        done();
    }

    public void Destroy()
    {
        DestroyObject(Atlas);
        DestroyObject(Quad);
        Atlas = null;
        Quad = null;
    }

    // Outside play mode (the editor) Destroy is refused.
    public static void DestroyObject(UnityEngine.Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(o);
        else UnityEngine.Object.DestroyImmediate(o);
    }

    // Through the GPU, so a texture the asset did not mark readable (most
    // are not) can be read too.
    static Color32[] ReadTexture(Texture source, int w, int h)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;
        Texture2D read = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        Color32[] px = read.GetPixels32();
        DestroyObject(read);
        return px;
    }

    // Removes `steps` texels from every edge of the opaque mask (4-neighbourhood).
    static byte[] Erode(byte[] mask, int w, int h, int steps)
    {
        byte[] current = mask;
        for (int s = 0; s < steps; s++)
        {
            byte[] next = new byte[w * h];
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int p = y * w + x;
                    if (current[p] != 0 && current[p - 1] != 0 && current[p + 1] != 0 && current[p - w] != 0 && current[p + w] != 0)
                        next[p] = 1;
                }
            current = next;
        }
        return current;
    }

    // Grows the labelled shapes back over the original mask, `steps` texels.
    static void Grow(byte[] mask, int[] labels, int w, int h, int steps)
    {
        for (int s = 0; s < steps; s++)
        {
            int[] previous = (int[])labels.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    if (mask[p] == 0 || previous[p] != 0) continue;
                    int l = 0;
                    if (x > 0 && previous[p - 1] != 0) l = previous[p - 1];
                    else if (x < w - 1 && previous[p + 1] != 0) l = previous[p + 1];
                    else if (y > 0 && previous[p - w] != 0) l = previous[p - w];
                    else if (y < h - 1 && previous[p + w] != 0) l = previous[p + w];
                    labels[p] = l;
                }
        }
    }

    // The 4-connected regions of the mask, with an explicit stack: a leaf
    // texture's regions are far too large for recursion. Measure fills in
    // the statistics.
    static List<Blob> Label(byte[] mask, int[] labels, int w, int h)
    {
        List<Blob> blobs = new List<Blob>();
        int[] stack = new int[w * h];
        for (int start = 0; start < mask.Length; start++)
        {
            if (labels[start] != 0 || mask[start] == 0) continue;
            Blob b = new Blob();
            b.Id = blobs.Count + 1;
            int top = 0;
            stack[top++] = start;
            labels[start] = b.Id;
            while (top > 0)
            {
                int p = stack[--top];
                int x = p % w, y = p / w;
                if (x > 0) top = Visit(mask, labels, stack, top, p - 1, b.Id);
                if (x < w - 1) top = Visit(mask, labels, stack, top, p + 1, b.Id);
                if (y > 0) top = Visit(mask, labels, stack, top, p - w, b.Id);
                if (y < h - 1) top = Visit(mask, labels, stack, top, p + w, b.Id);
            }
            blobs.Add(b);
        }
        return blobs;
    }

    static int Visit(byte[] mask, int[] labels, int[] stack, int top, int p, int id)
    {
        if (labels[p] != 0 || mask[p] == 0) return top;
        labels[p] = id;
        stack[top] = p;
        return top + 1;
    }

    static void Measure(List<Blob> blobs, int[] labels, Color32[] px, int w, int h)
    {
        for (int i = 0; i < blobs.Count; i++)
        {
            Blob b = blobs[i];
            b.MinX = w;
            b.MinY = h;
            b.MaxX = -1;
            b.MaxY = -1;
        }
        for (int p = 0; p < labels.Length; p++)
        {
            if (labels[p] == 0) continue;
            Blob b = blobs[labels[p] - 1];
            int x = p % w, y = p / w;
            b.Area++;
            b.R += px[p].r;
            b.G += px[p].g;
            b.B += px[p].b;
            if (x < b.MinX) b.MinX = x;
            if (x > b.MaxX) b.MaxX = x;
            if (y < b.MinY) b.MinY = y;
            if (y > b.MaxY) b.MaxY = y;
            if (x == 0 || y == 0 || x == w - 1 || y == h - 1) b.TouchesEdge = true;
        }
    }

    // Rec. 709 luminance of the shape's mean colour, 0..1.
    static float Luminance(Blob b)
    {
        return (0.2126f * b.R + 0.7152f * b.G + 0.0722f * b.B) / (255f * Mathf.Max(1, b.Area));
    }

    // The shape's mean colour with no alpha: the colour a tile's transparent
    // texels carry, so mip levels and filtering bleed leaf colour into the
    // edge rather than black.
    static Color32 Fill(Blob b)
    {
        int a = Mathf.Max(1, b.Area);
        return new Color32((byte)(b.R / a), (byte)(b.G / a), (byte)(b.B / a), 0);
    }

    // Leaf-like regions: not cut by the texture's border, neither a speck nor
    // a whole cluster, not a long twig, reasonably solid, and not much darker
    // than the typical shape (that drops buds, berries and bark). Of those,
    // the most typically sized are kept rather than the biggest, which tend
    // to be whole sprigs.
    static List<Blob> PickLeaves(List<Blob> blobs, int w, int h)
    {
        float total = w * h;
        float minArea = Mathf.Max(FoliageFXConfig.LeafAreaMinTexels, total * FoliageFXConfig.LeafAreaMinShare);
        float maxArea = total * FoliageFXConfig.LeafAreaMaxShare;
        List<Blob> picked = new List<Blob>();
        for (int i = 0; i < blobs.Count; i++)
        {
            Blob b = blobs[i];
            if (b.TouchesEdge) continue;
            if (b.Area < minArea || b.Area > maxArea) continue;
            int bw = b.MaxX - b.MinX + 1, bh = b.MaxY - b.MinY + 1;
            float aspect = bw / (float)bh;
            if (aspect < FoliageFXConfig.LeafAspectMin || aspect > FoliageFXConfig.LeafAspectMax) continue;
            if (b.Area / (float)(bw * bh) < FoliageFXConfig.LeafSolidityMin) continue;
            picked.Add(b);
        }
        if (picked.Count == 0) return picked;

        float dark = MedianLuminance(picked) * FoliageFXConfig.LeafDarkShare;
        picked.RemoveAll(delegate (Blob b) { return Luminance(b) < dark; });
        if (picked.Count == 0) return picked;

        List<int> areas = new List<int>(picked.Count);
        for (int i = 0; i < picked.Count; i++) areas.Add(picked[i].Area);
        areas.Sort();
        float typical = areas[areas.Count / 2];
        picked.Sort(delegate (Blob first, Blob second) { return SizeDistance(first, typical).CompareTo(SizeDistance(second, typical)); });
        int max = FoliageFXConfig.AtlasMaxShapes;
        if (picked.Count > max) picked.RemoveRange(max, picked.Count - max);
        return picked;
    }

    static float SizeDistance(Blob b, float typical)
    {
        return Mathf.Abs(Mathf.Log(b.Area / typical));
    }

    // Area-weighted, so a few big blossoms outweigh many small buds.
    static float MedianLuminance(List<Blob> blobs)
    {
        List<Blob> sorted = new List<Blob>(blobs);
        sorted.Sort(delegate (Blob first, Blob second) { return Luminance(first).CompareTo(Luminance(second)); });
        long total = 0;
        for (int i = 0; i < sorted.Count; i++) total += sorted[i].Area;
        long half = total / 2, run = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            run += sorted[i].Area;
            if (run >= half) return Luminance(sorted[i]);
        }
        return Luminance(sorted[sorted.Count - 1]);
    }

    // Rounded cut-outs from well-covered, typically coloured parts of a
    // dense cluster. Returns how many tiles were filled.
    static int MakeFragments(Color32[] px, int w, int h, Color32[] atlasPx)
    {
        int[] histogram = new int[256];
        int opaqueTotal = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].a < OpaqueAlpha) continue;
            histogram[(int)(0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b)]++;
            opaqueTotal++;
        }
        if (opaqueTotal == 0) return 0;
        int medianLum = 0;
        for (int run = 0; medianLum < 255; medianLum++)
        {
            run += histogram[medianLum];
            if (run * 2 >= opaqueTotal) break;
        }

        System.Random rng = new System.Random(FragmentSeed);
        int radius = Mathf.Max(FoliageFXConfig.FragmentRadiusMin, Mathf.RoundToInt(Mathf.Min(w, h) * FoliageFXConfig.FragmentRadiusShare));
        int count = 0;
        for (int attempt = 0; attempt < FoliageFXConfig.FragmentAttempts && count < FoliageFXConfig.AtlasMaxShapes; attempt++)
        {
            int rx = radius;
            int ry = Mathf.Max(FragmentRadiusYMin, Mathf.RoundToInt(radius * (FragmentAspectMin + (float)rng.NextDouble() * (1f - FragmentAspectMin))));
            if (w <= rx * 2 + 2 || h <= ry * 2 + 2) return count;
            int cx = rng.Next(rx + 1, w - rx - 1);
            int cy = rng.Next(ry + 1, h - ry - 1);
            if (px[cy * w + cx].a < OpaqueAlpha) continue;

            int inside = 0, opaque = 0;
            long r = 0, g = 0, b = 0;
            for (int y = -ry; y <= ry; y++)
                for (int x = -rx; x <= rx; x++)
                {
                    float dx = x / (float)rx, dy = y / (float)ry;
                    if (dx * dx + dy * dy > 1f) continue;
                    inside++;
                    Color32 c = px[(cy + y) * w + cx + x];
                    if (c.a < OpaqueAlpha) continue;
                    opaque++;
                    r += c.r;
                    g += c.g;
                    b += c.b;
                }
            if (opaque < inside * FoliageFXConfig.FragmentCoverMin) continue;
            if (0.2126f * r / opaque + 0.7152f * g / opaque + 0.0722f * b / opaque < medianLum * FoliageFXConfig.LeafDarkShare) continue;
            Color32 fill = new Color32((byte)(r / opaque), (byte)(g / opaque), (byte)(b / opaque), 0);
            CopyTile(px, null, w, h, cx - rx, cy - ry, rx * 2 + 1, ry * 2 + 1, 0, true, fill, atlasPx, count);
            count++;
        }
        return count;
    }

    // Copies a source rectangle into atlas tile `index`, scaled to fit inside
    // the padded tile and centred with its aspect kept. With `labels`, opaque
    // texels that belong to anything else (other leaves, twigs) are dropped;
    // with `ellipse`, the copy is trimmed to a soft-edged oval.
    static void CopyTile(Color32[] px, int[] labels, int w, int h, int rx, int ry, int rw, int rh,
        int blobId, bool ellipse, Color32 fill, Color32[] atlasPx, int index)
    {
        int size = FoliageFXConfig.AtlasSize;
        int grid = FoliageFXConfig.AtlasGrid;
        int pad = FoliageFXConfig.AtlasPad;
        int tile = size / grid;
        int inner = tile - pad * 2;
        int tileX = (index % grid) * tile, tileY = (index / grid) * tile;
        for (int y = 0; y < tile; y++)
            for (int x = 0; x < tile; x++)
                atlasPx[(tileY + y) * size + tileX + x] = fill;

        float scale = Mathf.Min(inner / (float)rw, inner / (float)rh);
        int fw = Mathf.Max(1, Mathf.RoundToInt(rw * scale));
        int fh = Mathf.Max(1, Mathf.RoundToInt(rh * scale));
        int x0 = tileX + pad + (inner - fw) / 2;
        int y0 = tileY + pad + (inner - fh) / 2;
        float cx = rx + rw * 0.5f, cy = ry + rh * 0.5f;
        for (int ty = 0; ty < fh; ty++)
            for (int tx = 0; tx < fw; tx++)
            {
                float sx = rx + (tx + 0.5f) / scale - 0.5f;
                float sy = ry + (ty + 0.5f) / scale - 0.5f;
                Color32 c = Sample(px, w, h, sx, sy);
                float a = c.a / 255f;
                if (labels != null)
                {
                    int nx = Mathf.Clamp(Mathf.RoundToInt(sx), 0, w - 1);
                    int ny = Mathf.Clamp(Mathf.RoundToInt(sy), 0, h - 1);
                    int np = ny * w + nx;
                    if (labels[np] != blobId && px[np].a >= OpaqueAlpha) a = 0f;
                }
                if (ellipse)
                {
                    float dx = (sx + 0.5f - cx) / (rw * 0.5f), dy = (sy + 0.5f - cy) / (rh * 0.5f);
                    a *= Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * FragmentEdgeSharpness);
                }
                byte alpha = (byte)Mathf.RoundToInt(a * 255f);
                // A mostly see-through texel takes the fill colour, so the
                // texture's background never fringes the leaf.
                atlasPx[(y0 + ty) * size + x0 + tx] = a < 0.5f
                    ? new Color32(fill.r, fill.g, fill.b, alpha)
                    : new Color32(c.r, c.g, c.b, alpha);
            }
    }

    static Color32 Sample(Color32[] px, int w, int h, float x, float y)
    {
        x = Mathf.Clamp(x, 0f, w - 1);
        y = Mathf.Clamp(y, 0f, h - 1);
        int x0 = (int)x, y0 = (int)y;
        int x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
        float fx = x - x0, fy = y - y0;
        Color32 bottom = Color32.Lerp(px[y0 * w + x0], px[y0 * w + x1], fx);
        Color32 top = Color32.Lerp(px[y1 * w + x0], px[y1 * w + x1], fx);
        return Color32.Lerp(bottom, top, fy);
    }

    // A unit quad facing -Z; the particles' texture sheet maps its UVs onto
    // one atlas tile per particle.
    static Mesh MakeQuad()
    {
        Mesh m = new Mesh();
        m.name = "FoliageFX leaf";
        m.vertices = new Vector3[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        m.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        m.normals = new Vector3[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        m.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        return m;
    }
}
