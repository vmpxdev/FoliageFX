using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Leaf shapes cut out of a tree's own leaf texture and packed into an atlas,
// for the falling leaves: one extraction's result, and the routine that makes
// it. Separate leaves or petals are the connected opaque regions of the
// texture's alpha; the most typical of them (by size, then colour) go into a
// 4 x 4 atlas, each centred in its tile with its aspect kept and a
// transparent margin, so a particle showing one tile shows one leaf. A
// texture made of a few big leaves (Damarmau's trees draw two to a texture),
// or of one leaf cropped to its edges, gives those leaves whole. Anything
// else gives no shapes at all, and Refusal says why: a solid texture, one
// whose leaves are joined into a cluster, one whose leaves are specks. A
// falling leaf is always a leaf the texture draws, or there is none.
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

    public Texture Source;
    public Texture2D Atlas;
    public Mesh Quad;
    public int ShapeCount;
    // Why the texture gave no shapes, as the end of a sentence ("the
    // texture is solid, ..."); null while it has shapes or is not read yet.
    public string Refusal;

    sealed class Blob
    {
        public int Id;
        public int Area;
        public int MinX, MinY, MaxX, MaxY;
        public bool TouchesEdge;
        public long R, G, B;
        public double SumX, SumY, SumXX, SumYY, SumXY;   // second moments, for the elongation
    }

    // Yields between the heavy steps (the read, each thinning level), so a
    // coroutine spreads the work over a few frames. `into.ShapeCount` is 0
    // when the texture has nothing usable; `done` runs either way.
    public static IEnumerator Extract(Texture source, LeafSprites into, Fn done)
    {
        into.Source = source;
        into.ShapeCount = 0;
        into.Refusal = null;
        if (source == null || source.width < MinTextureSize || source.height < MinTextureSize)
        {
            into.Refusal = "the texture is too small to cut leaves from.";
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
        int solid = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] = px[i].a >= OpaqueAlpha ? (byte)1 : (byte)0;
            solid += mask[i];
        }
        // Leaves are cut out of their background: a texture that is mostly
        // solid is a surface (bark, a wall, an environment's atlas), and any
        // shape found in it would be a piece of that surface.
        if (mask.Length - solid < mask.Length * FoliageFXConfig.LeafClearMin)
        {
            into.Refusal = "the texture is mostly solid, with no leaves cut out of it.";
            done();
            yield break;
        }

        // Leaves and blossoms are often joined to their twigs. Thinning the
        // mask by a texel or two cuts those thin links; the level that
        // separates the most leaf-like shapes wins, and its shapes are grown
        // back to full size.
        List<Blob> best = null;
        int[] bestLabels = null;
        int largest = 0;
        for (int erode = 0; erode <= FoliageFXConfig.LeafErodeMax; erode++)
        {
            int[] labels = new int[w * h];
            List<Blob> blobs = Label(erode == 0 ? mask : Erode(mask, w, h, erode), labels, w, h);
            if (erode > 0) Grow(mask, labels, w, h, erode);
            Measure(blobs, labels, px, w, h);
            for (int i = 0; erode == 0 && i < blobs.Count; i++) if (blobs[i].Area > largest) largest = blobs[i].Area;
            List<Blob> picked = PickLeaves(blobs, labels, w, h);
            if (best == null || picked.Count > best.Count)
            {
                best = picked;
                bestLabels = labels;
            }
            yield return null;
        }

        // Too few shapes to be the texture's leaves rather than strays,
        // unless one is a whole big leaf: two of those are its own leaves.
        if (best.Count < FoliageFXConfig.LeafShapesMin && !HasWholeLeaf(best, w * h))
        {
            into.Refusal = Refuse(solid, largest, w * h);
            done();
            yield break;
        }
        int size = FoliageFXConfig.AtlasSize;
        Color32[] atlasPx = new Color32[size * size];
        for (int i = 0; i < best.Count; i++)
        {
            Blob b = best[i];
            // One texel of margin around the shape, so its soft edge is
            // copied too.
            CopyTile(px, bestLabels, w, h, b.MinX - 1, b.MinY - 1,
                b.MaxX - b.MinX + 3, b.MaxY - b.MinY + 3, b.Id, Fill(b), atlasPx, i);
        }
        int shapeCount = best.Count;

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
            b.SumX += x;
            b.SumY += y;
            b.SumXX += (double)x * x;
            b.SumYY += (double)y * y;
            b.SumXY += (double)x * y;
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

    // Single leaves, petals and buds, and nothing else: a region not cut by
    // the texture's border, neither a speck nor a whole cluster, that does
    // not fill its box (a block of wall or fence does), is not a blade or a
    // needle (too thin across its long axis), bulges like a leaf (a sprig,
    // a clump of needles or blades has deep gaps), has no holes through it
    // (needle clumps do), and is not much darker than the typical shape
    // (that drops berries and bark). Of those, the most typically sized are
    // kept. A region bigger than a leaf among many is one whole leaf only
    // when it bulges more still, and may touch the texture's border, as a
    // leaf cropped to its edges does. The limits were set on the leaf
    // textures of eight tree, grass and environment packs.
    static List<Blob> PickLeaves(List<Blob> blobs, int[] labels, int w, int h)
    {
        float total = w * h;
        float minArea = Mathf.Max(FoliageFXConfig.LeafAreaMinTexels, total * FoliageFXConfig.LeafAreaMinShare);
        float maxArea = total * FoliageFXConfig.LeafAreaMaxShare;
        float wholeMax = total * FoliageFXConfig.LeafWholeMaxShare;
        List<Blob> picked = new List<Blob>();
        for (int i = 0; i < blobs.Count; i++)
        {
            Blob b = blobs[i];
            bool big = b.Area > maxArea;
            if (b.TouchesEdge && !big) continue;
            if (b.Area < minArea || b.Area > wholeMax) continue;
            int bw = b.MaxX - b.MinX + 1, bh = b.MaxY - b.MinY + 1;
            float aspect = bw / (float)bh;
            if (aspect < FoliageFXConfig.LeafAspectMin || aspect > FoliageFXConfig.LeafAspectMax) continue;
            float fill = b.Area / (float)(bw * bh);
            if (fill < FoliageFXConfig.LeafSolidityMin || fill > FoliageFXConfig.LeafFillMax) continue;
            if (Elongation(b) < FoliageFXConfig.LeafElongationMin) continue;
            if (Convexity(b, labels, w) < (big ? FoliageFXConfig.LeafWholeConvexMin : FoliageFXConfig.LeafConvexMin)) continue;
            if (HoleShare(b, labels, w) > FoliageFXConfig.LeafHoleShareMax) continue;
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

    // The shape's width over its length along its own axes, from its second
    // moments, so a blade lying diagonally reads as thin as one standing up:
    // near 1 for a round petal, well under 0.15 for a blade of grass.
    static float Elongation(Blob b)
    {
        double n = b.Area;
        double mx = b.SumX / n, my = b.SumY / n;
        double cxx = b.SumXX / n - mx * mx, cyy = b.SumYY / n - my * my, cxy = b.SumXY / n - mx * my;
        double half = (cxx + cyy) * 0.5;
        double spread = System.Math.Sqrt(System.Math.Max(0.0, half * half - (cxx * cyy - cxy * cxy)));
        double major = half + spread, minor = half - spread;
        return major > 0.0 ? (float)System.Math.Sqrt(System.Math.Max(0.0, minor) / major) : 0f;
    }

    // The see-through texels the shape encloses, over its area: the texels
    // of its box that are not the shape and cannot be reached from the box's
    // edge without crossing it. A leaf has none, a clump of needles many.
    static float HoleShare(Blob b, int[] labels, int w)
    {
        int bw = b.MaxX - b.MinX + 1, bh = b.MaxY - b.MinY + 1;
        bool[] reached = new bool[bw * bh];
        int[] stack = new int[bw * bh];
        int top = 0;
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                if (x != 0 && y != 0 && x != bw - 1 && y != bh - 1) continue;
                int q = y * bw + x;
                if (reached[q] || labels[(b.MinY + y) * w + b.MinX + x] == b.Id) continue;
                reached[q] = true;
                stack[top++] = q;
            }
        while (top > 0)
        {
            int q = stack[--top];
            int x = q % bw, y = q / bw;
            if (x > 0) top = Reach(labels, w, b, bw, reached, stack, top, q - 1);
            if (x < bw - 1) top = Reach(labels, w, b, bw, reached, stack, top, q + 1);
            if (y > 0) top = Reach(labels, w, b, bw, reached, stack, top, q - bw);
            if (y < bh - 1) top = Reach(labels, w, b, bw, reached, stack, top, q + bw);
        }
        int holes = 0;
        for (int q = 0; q < reached.Length; q++)
            if (!reached[q] && labels[(b.MinY + q / bw) * w + b.MinX + q % bw] != b.Id) holes++;
        return holes / (float)Mathf.Max(1, b.Area);
    }

    static int Reach(int[] labels, int w, Blob b, int bw, bool[] reached, int[] stack, int top, int q)
    {
        if (reached[q] || labels[(b.MinY + q / bw) * w + b.MinX + q % bw] == b.Id) return top;
        reached[q] = true;
        stack[top] = q;
        return top + 1;
    }

    // Why nothing was cut, from what the texture's alpha holds.
    static string Refuse(int solid, int largest, int total)
    {
        float minArea = Mathf.Max(FoliageFXConfig.LeafAreaMinTexels, total * FoliageFXConfig.LeafAreaMinShare);
        if (largest < minArea) return solid == 0 ? "the texture has no solid leaf in it." : "the leaves in the texture are too small to cut out.";
        return "the texture has no single leaf to cut out: its leaves are joined in clusters, or are blades, needles or twigs.";
    }

    static bool HasWholeLeaf(List<Blob> picked, int total)
    {
        float maxArea = total * FoliageFXConfig.LeafAreaMaxShare;
        for (int i = 0; i < picked.Count; i++)
            if (picked[i].Area > maxArea) return true;
        return false;
    }

    // The shape's area over its convex hull's, the hull taken round each
    // row's two ends: near 1 for a leaf, well under it for a sprig.
    static float Convexity(Blob b, int[] labels, int w)
    {
        List<Vector2> ends = new List<Vector2>();
        for (int y = b.MinY; y <= b.MaxY; y++)
        {
            int row = y * w, first = -1, last = -1;
            for (int x = b.MinX; x <= b.MaxX; x++)
            {
                if (labels[row + x] != b.Id) continue;
                if (first < 0) first = x;
                last = x;
            }
            if (first < 0) continue;
            // The corners of the row's end texels, so a convex shape's hull
            // covers what its texels cover.
            ends.Add(new Vector2(first, y));
            ends.Add(new Vector2(last + 1, y));
            ends.Add(new Vector2(first, y + 1));
            ends.Add(new Vector2(last + 1, y + 1));
        }
        float hull = HullArea(ends);
        return hull > 0f ? b.Area / hull : 0f;
    }

    // Andrew's monotone chain, then the shoelace formula.
    static float HullArea(List<Vector2> points)
    {
        if (points.Count < 3) return 0f;
        points.Sort(delegate (Vector2 a, Vector2 c) { return a.x != c.x ? a.x.CompareTo(c.x) : a.y.CompareTo(c.y); });
        Vector2[] hull = new Vector2[points.Count * 2];
        int k = 0;
        for (int i = 0; i < points.Count; i++)
        {
            while (k >= 2 && Cross(hull[k - 2], hull[k - 1], points[i]) <= 0f) k--;
            hull[k++] = points[i];
        }
        int lower = k + 1;
        for (int i = points.Count - 2; i >= 0; i--)
        {
            while (k >= lower && Cross(hull[k - 2], hull[k - 1], points[i]) <= 0f) k--;
            hull[k++] = points[i];
        }
        float area = 0f;
        for (int i = 0; i < k - 1; i++) area += hull[i].x * hull[i + 1].y - hull[i + 1].x * hull[i].y;
        return Mathf.Abs(area) * 0.5f;
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
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

    // Copies a source rectangle into atlas tile `index`, scaled to fit inside
    // the padded tile and centred with its aspect kept. Texels that belong to
    // anything else (other leaves, twigs) are dropped.
    static void CopyTile(Color32[] px, int[] labels, int w, int h, int rx, int ry, int rw, int rh,
        int blobId, Color32 fill, Color32[] atlasPx, int index)
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
        for (int ty = 0; ty < fh; ty++)
            for (int tx = 0; tx < fw; tx++)
            {
                float sx = rx + (tx + 0.5f) / scale - 0.5f;
                float sy = ry + (ty + 0.5f) / scale - 0.5f;
                Color32 c = Sample(px, w, h, sx, sy);
                float a = c.a / 255f;
                int nx = Mathf.Clamp(Mathf.RoundToInt(sx), 0, w - 1);
                int ny = Mathf.Clamp(Mathf.RoundToInt(sy), 0, h - 1);
                int np = ny * w + nx;
                // Anything not the shape is dropped, other leaves and the soft
                // edges of twigs alike, but for the shape's own soft rim.
                if (labels[np] != blobId && !Touches(labels, w, h, nx, ny, blobId)) a = 0f;
                byte alpha = (byte)Mathf.RoundToInt(a * 255f);
                // A mostly see-through texel takes the fill colour, so the
                // texture's background never fringes the leaf.
                atlasPx[(y0 + ty) * size + x0 + tx] = a < 0.5f
                    ? new Color32(fill.r, fill.g, fill.b, alpha)
                    : new Color32(c.r, c.g, c.b, alpha);
            }
    }

    static bool Touches(int[] labels, int w, int h, int x, int y, int id)
    {
        return (x > 0 && labels[y * w + x - 1] == id) || (x < w - 1 && labels[y * w + x + 1] == id)
            || (y > 0 && labels[(y - 1) * w + x] == id) || (y < h - 1 && labels[(y + 1) * w + x] == id);
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
