using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = System.Random;

/// <summary>
/// One-off tool (task 26 trial): gives ONE crate, the one beside the player's start in Corridor_01, the look of an
/// old wooden shipping crate, so the owner can judge the design before every crate gets it.
/// Makes the wood texture, the mesh and the materials into Assets/Art/Crate, then adds the mesh as a child of the
/// crate. The crate itself keeps its collider, size, place, layer and everything else; only its grey box renderer is
/// switched off (switch it back on to compare). Safe to run again: it replaces what it made before.
/// Delete once the crate design is confirmed.
/// </summary>
public static class TestCrateBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const string ArtFolder = "Assets/Art/Crate";
    const string AlbedoPath = ArtFolder + "/Crate_Wood_Albedo.png";
    const string NormalPath = ArtFolder + "/Crate_Wood_Normal.png";
    const string MeshPath = ArtFolder + "/Crate_Wooden.asset";
    const string VisualName = "Crate_Wooden";

    // The crate closest to the Player's start (0, 0.05, -20): 3 m to its right in the start yard.
    const string CratePath = "Yard_Start/Crate_03";
    static readonly Vector3 ExpectedPosition = new Vector3(3f, 0.75f, -20f);

    // The frame (corner boards, rails and braces) stands proud of the planks, so its edges catch the light and the
    // crate reads as a crate even in the dark. The outside of the frame is exactly the old box, so nothing the crate
    // looks like sticks out past its collider (only the iron corners, by 4 mm).
    const float FrameWidth = 0.11f;
    const float FrameThickness = 0.03f;
    const float FrameChamfer = 0.008f;
    const float BraceWidth = 0.1f;
    // The brace sits a few millimetres below the frame's face, so where it runs under a rail the two never fight.
    const float BraceInset = 0.004f;

    // Planks butt edge to edge with no gaps, so nothing shows through. Their chamfered edges make a V groove between
    // each pair that reads as separate boards without any hole.
    const float PlankWidth = 0.28f;
    const float PlankThickness = 0.02f;
    const float PlankChamfer = 0.007f;
    const float PlankSink = 0.003f;

    const float PlateSize = 0.1f;
    const float IronThickness = 0.004f;

    const int X = 0, Y = 1, Z = 2;
    const int WoodSubmesh = 0, FrameSubmesh = 1, IronSubmesh = 2;
    static readonly int[] Sides = { -1, 1 };

    // The wood texture covers 1 m across the grain, so each board is cut from a different part of it, and 2 m along
    // it, longer than any crate board, at 512 pixels a metre.
    const float TextureWidth = 1f;
    const float TextureLength = 2f;
    const int PixelsWide = 512;
    const int PixelsHigh = 1024;

    [MenuItem("Tools/Build Test Crate")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Build Test Crate: leave Play mode first.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath);
        }
        Build(scene);
    }

    static void Build(Scene scene)
    {
        GameObject yard = scene.GetRootGameObjects().FirstOrDefault(root => root.name == CratePath.Split('/')[0]);
        Transform crate = yard != null ? yard.transform.Find(CratePath.Substring(CratePath.IndexOf('/') + 1)) : null;
        var box = crate != null ? crate.GetComponent<BoxCollider>() : null;
        var oldRenderer = crate != null ? crate.GetComponent<MeshRenderer>() : null;
        if (box == null || oldRenderer == null)
        {
            Debug.LogError($"Build Test Crate: {CratePath} with a BoxCollider and MeshRenderer was not found in {ScenePath}.");
            return;
        }
        if (Vector3.Distance(crate.position, ExpectedPosition) > 0.5f)
            Debug.LogWarning($"Build Test Crate: {CratePath} has moved to {crate.position}; building on it anyway.");

        if (!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets/Art", "Crate");

        MakeWoodTextures();
        Material[] materials = { MakeWoodMaterial(), MakeFrameMaterial(), MakeIronMaterial() };

        // The mesh is built in metres at the crate's real size, and its object undoes the crate's scale, so the wood
        // texture keeps its scale on the boards whatever size the crate is.
        Vector3 scale = crate.lossyScale;
        Vector3 size = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        Mesh mesh = BuildMesh(size / 2f);

        Transform old = crate.Find(VisualName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var visual = new GameObject(VisualName, typeof(MeshFilter), typeof(MeshRenderer)) { layer = crate.gameObject.layer };
        visual.transform.SetParent(crate, false);
        visual.transform.localPosition = box.center;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        GameObjectUtility.SetStaticEditorFlags(visual, GameObjectUtility.GetStaticEditorFlags(crate.gameObject));

        visual.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = visual.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = oldRenderer.shadowCastingMode;
        renderer.receiveShadows = oldRenderer.receiveShadows;
        renderer.staticShadowCaster = oldRenderer.staticShadowCaster;
        renderer.lightProbeUsage = oldRenderer.lightProbeUsage;
        renderer.reflectionProbeUsage = oldRenderer.reflectionProbeUsage;
        renderer.motionVectorGenerationMode = oldRenderer.motionVectorGenerationMode;
        renderer.allowOcclusionWhenDynamic = oldRenderer.allowOcclusionWhenDynamic;
        renderer.renderingLayerMask = oldRenderer.renderingLayerMask;

        // Only the look changes: the collider, layer and transform stay as they were, so sight and stealth don't.
        oldRenderer.enabled = false;
        EditorUtility.SetDirty(oldRenderer);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Selection.activeGameObject = visual;
        EditorGUIUtility.PingObject(visual);
        Debug.Log($"Build Test Crate: finished on {CratePath}, {mesh.triangles.Length / 3} triangles, " +
                  $"mesh bounds {mesh.bounds.size} for a crate of {size}.");
    }

    // --- Crate mesh ---

    // Which face's boards run on past a shared edge, so boards from two faces never fill the same space: the left and
    // right faces wrap their corner boards over the front and back ones, and the sides over the lid.
    static int Rank(int axis) => axis == X ? 2 : axis == Z ? 1 : 0;
    static bool Outranks(int a, int b) => Rank(a) > Rank(b);

    static Mesh BuildMesh(Vector3 h)
    {
        var mesh = new CrateMesh();
        var rng = new Random(26);

        foreach (int s in Sides)
        {
            AddFace(mesh, h, X, s, rng);
            AddFace(mesh, h, Z, s, rng);
        }
        AddFace(mesh, h, Y, 1, rng);

        // The floor hides the bottom, so it is one plain board that only closes the box.
        var lo = new Vector3(-(h.x - FrameThickness), -h.y, -(h.z - FrameThickness));
        var hi = new Vector3(h.x - FrameThickness, -(h.y - FrameThickness), h.z - FrameThickness);
        mesh.Block(lo, hi, X, Y, -1, 0f, WoodSubmesh, rng);

        AddCornerIron(mesh, h);
        return mesh.Save(MeshPath, VisualName);
    }

    /// <summary>One face: planks, a frame of two corner boards and two rails over them, and a diagonal brace on the sides.</summary>
    /// <param name="n">The axis the face looks along.</param>
    /// <param name="s">Which way it looks along it.</param>
    static void AddFace(CrateMesh mesh, Vector3 h, int n, int s, Random rng)
    {
        // On the sides the corner boards stand upright; on the lid they run along X.
        int stileAxis = n == Y ? X : Y;
        int railAxis = 3 - n - stileAxis;
        (float, float) Depth(float from, float to) => Side(h[n] - to, h[n] - from, s);
        float Next(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        float stileEnd = h[stileAxis] - (Outranks(stileAxis, n) ? FrameThickness : 0f);
        float stileOuter = h[railAxis] - (Outranks(railAxis, n) ? FrameThickness : 0f);
        foreach (int e in Sides)
        {
            Vector3 lo = default, hi = default;
            Set(ref lo, ref hi, n, Depth(0f, FrameThickness));
            Set(ref lo, ref hi, stileAxis, (-stileEnd, stileEnd));
            Set(ref lo, ref hi, railAxis, Side(h[railAxis] - FrameWidth, stileOuter, e));
            mesh.Block(lo, hi, stileAxis, n, s, FrameChamfer, FrameSubmesh, rng);
        }

        // The rails fit between the corner boards.
        float railEnd = h[railAxis] - FrameWidth;
        float railOuter = h[stileAxis] - (Outranks(stileAxis, n) ? FrameThickness : 0f);
        foreach (int e in Sides)
        {
            Vector3 lo = default, hi = default;
            Set(ref lo, ref hi, n, Depth(0f, FrameThickness));
            Set(ref lo, ref hi, railAxis, (-railEnd, railEnd));
            Set(ref lo, ref hi, stileAxis, Side(h[stileAxis] - FrameWidth, railOuter, e));
            mesh.Block(lo, hi, railAxis, n, s, FrameChamfer, FrameSubmesh, rng);
        }

        // Planks under the frame, edge to edge across the whole face: horizontal on the sides, along X on the lid.
        // Each is a slightly different width and sits up to 3 mm lower than its neighbour, like old boards do.
        int plankAxis = n == Y ? stileAxis : railAxis;
        int stackAxis = 3 - n - plankAxis;
        float plankEnd = h[plankAxis] - FrameThickness;
        float stackHalf = h[stackAxis] - FrameThickness;
        int count = Mathf.Max(3, Mathf.RoundToInt(2f * stackHalf / PlankWidth));
        float[] shares = Enumerable.Range(0, count).Select(_ => Next(0.85f, 1.15f)).ToArray();
        float unit = 2f * stackHalf / shares.Sum();
        float cursor = -stackHalf;
        foreach (float share in shares)
        {
            float width = share * unit;
            float sink = Next(0f, PlankSink);
            Vector3 lo = default, hi = default;
            Set(ref lo, ref hi, n, Depth(FrameThickness + sink, FrameThickness + PlankThickness + sink));
            Set(ref lo, ref hi, plankAxis, (-plankEnd, plankEnd));
            Set(ref lo, ref hi, stackAxis, (cursor, cursor + width));
            mesh.Block(lo, hi, plankAxis, n, s, PlankChamfer, WoodSubmesh, rng);
            cursor += width;
        }

        if (n == Y) return;

        // A brace from corner to corner of the panel inside the frame. Its square-cut ends finish exactly under the
        // frame, so no end shows.
        int way = rng.Next(2) == 0 ? -1 : 1;
        Vector3 low = Vector3.zero;
        low[railAxis] = -way * (h[railAxis] - FrameWidth);
        low[stileAxis] = -(h[stileAxis] - FrameWidth);
        Vector3 high = -low;
        low[n] = high[n] = s * (h[n] - (BraceInset + FrameThickness) / 2f);
        mesh.Board((low + high) / 2f, (high - low).normalized, Axis(n) * s, (high - low).magnitude, BraceWidth,
            FrameThickness - BraceInset, FrameChamfer, FrameSubmesh, rng);
    }

    // Dark iron plates over the corners, the kind that keep a shipping crate's corners from splitting. They are on
    // the top corners on all three faces and the bottom corners on the two sides, and sit 4 mm proud of the wood.
    static void AddCornerIron(CrateMesh mesh, Vector3 h)
    {
        foreach (int cx in Sides)
        foreach (int cy in Sides)
        foreach (int cz in Sides)
        {
            int[] corner = { cx, cy, cz };
            foreach (int n in new[] { X, Y, Z })
            {
                if (!HasPlate(n, cy)) continue;
                Vector3 lo = default, hi = default;
                Set(ref lo, ref hi, n, Side(h[n], h[n] + IronThickness, corner[n]));
                foreach (int a in new[] { (n + 1) % 3, (n + 2) % 3 })
                {
                    // The plate that outranks its neighbour covers its edge too, so the corner is closed.
                    float outer = h[a] + (Outranks(n, a) && HasPlate(a, cy) ? IronThickness : 0f);
                    Set(ref lo, ref hi, a, Side(h[a] - PlateSize, outer, corner[a]));
                }
                mesh.Block(lo, hi, (n + 1) % 3, n, corner[n], 0f, IronSubmesh, null);
            }
        }
    }

    static bool HasPlate(int axis, int cornerY) => axis != Y || cornerY > 0;

    /// <summary>A range measured inward from a face's edge, flipped to the face's side of the crate.</summary>
    static (float, float) Side(float from, float to, int sign) => sign > 0 ? (from, to) : (-to, -from);

    static void Set(ref Vector3 lo, ref Vector3 hi, int axis, (float min, float max) range)
    {
        lo[axis] = range.min;
        hi[axis] = range.max;
    }

    static Vector3 Axis(int axis) => axis == X ? Vector3.right : axis == Y ? Vector3.up : Vector3.forward;

    /// <summary>Boards merged into one mesh, with UVs in metres so the wood keeps its scale on every board.</summary>
    class CrateMesh
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };

        /// <summary>A board filling the box from lo to hi, its grain along one axis and its chamfered face out along another.</summary>
        public void Block(Vector3 lo, Vector3 hi, int alongAxis, int outAxis, int outSign, float chamfer, int submesh, Random rng)
        {
            Vector3 size = hi - lo;
            int acrossAxis = 3 - alongAxis - outAxis;
            Board((lo + hi) / 2f, Axis(alongAxis), Axis(outAxis) * outSign, size[alongAxis], size[acrossAxis], size[outAxis],
                chamfer, submesh, rng);
        }

        /// <summary>
        /// A board with its two outer long edges chamfered. With a random number source the board gets wood UVs cut
        /// from a random part of the texture; without one (iron) it gets none.
        /// </summary>
        public void Board(Vector3 centre, Vector3 along, Vector3 outward, float length, float width, float thickness,
            float chamfer, int submesh, Random rng)
        {
            Vector3 across = Vector3.Cross(outward, along).normalized;
            float w = width / 2f, t = thickness / 2f, half = length / 2f;
            chamfer = Mathf.Min(chamfer, Mathf.Min(width, thickness) * 0.45f);

            // The cross-section, anticlockwise in (across, out).
            Vector2[] profile = chamfer > 0f
                ? new[] { new Vector2(-w, -t), new Vector2(w, -t), new Vector2(w, t - chamfer), new Vector2(w - chamfer, t), new Vector2(-w + chamfer, t), new Vector2(-w, t - chamfer) }
                : new[] { new Vector2(-w, -t), new Vector2(w, -t), new Vector2(w, t), new Vector2(-w, t) };

            bool grain = rng != null;
            var wood = grain
                ? new Vector2((float)rng.NextDouble(), (float)rng.NextDouble() * Mathf.Max(0f, TextureLength - length))
                : Vector2.zero;
            Vector3 At(Vector2 p, float z) => centre + across * p.x + outward * p.y + along * z;
            Vector2 Uv(float u, float v) => grain ? new Vector2(u / TextureWidth + wood.x, (v + wood.y) / TextureLength) : Vector2.zero;

            // Long faces: the texture wraps round the board, with the grain along it.
            float perimeter = 0f;
            for (int i = 0; i < profile.Length; i++)
            {
                Vector2 a = profile[i], b = profile[(i + 1) % profile.Length];
                Vector2 edge = b - a;
                Vector3 normal = (across * edge.y - outward * edge.x).normalized;
                float u0 = perimeter, u1 = perimeter + edge.magnitude;
                perimeter = u1;
                Polygon(new[] { At(a, -half), At(b, -half), At(b, half), At(a, half) }, normal,
                    new[] { Uv(u0, 0f), Uv(u1, 0f), Uv(u1, length), Uv(u0, length) }, submesh);
            }

            // End grain: just spread the texture flat.
            foreach (int end in Sides)
            {
                Polygon(profile.Select(p => At(p, end * half)).ToArray(), along * end,
                    profile.Select(p => Uv(p.x, p.y + t)).ToArray(), submesh);
            }
        }

        // A flat convex polygon, wound so it faces along its normal.
        void Polygon(Vector3[] points, Vector3 normal, Vector2[] polygonUvs, int submesh)
        {
            int first = vertices.Count;
            for (int i = 0; i < points.Length; i++)
            {
                vertices.Add(points[i]);
                normals.Add(normal);
                uvs.Add(polygonUvs[i]);
            }

            var list = triangles[submesh];
            for (int i = 1; i < points.Length - 1; i++)
            {
                bool facing = Vector3.Dot(Vector3.Cross(points[i] - points[0], points[i + 1] - points[0]), normal) >= 0f;
                if (facing) list.AddRange(new[] { first, first + i, first + i + 1 });
                else list.AddRange(new[] { first, first + i + 1, first + i });
            }
        }

        /// <summary>Saves over the mesh made last time, if there is one, so anything using it keeps its reference.</summary>
        public Mesh Save(string path, string name)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh();
            mesh.Clear();
            mesh.name = name;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            if (isNew) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }

    // --- Materials ---

    static Material MakeWoodMaterial()
    {
        var material = NewMaterial("Crate_Wood");
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
        material.SetColor("_BaseColor", Color.white);
        SetNormalMap(material);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // The same wood, darker and greyer, so the frame stands out from the planks by value alone once the volume has
    // taken most of the colour out.
    static Material MakeFrameMaterial()
    {
        var material = NewMaterial("Crate_Frame");
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
        material.SetColor("_BaseColor", new Color(0.56f, 0.5f, 0.44f));
        SetNormalMap(material);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.12f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material MakeIronMaterial()
    {
        var material = NewMaterial("Crate_Iron");
        material.SetColor("_BaseColor", new Color(0.1f, 0.09f, 0.08f));
        material.SetFloat("_Metallic", 0.6f);
        material.SetFloat("_Smoothness", 0.32f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void SetNormalMap(Material material)
    {
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        material.SetFloat("_BumpScale", 1f);
        material.EnableKeyword("_NORMALMAP");
    }

    static Material NewMaterial(string name)
    {
        string path = $"{ArtFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // --- Wood texture ---

    struct Knot { public float U, Y, Radius; }
    struct Crack { public float U, Y0, Y1, Width; }

    // Old crate pine: brown grain, a few grey weathered patches and grime, knots and splits. Broad light and dark
    // streaks across the texture give each board, cut from a different part of it, its own tone.
    static void MakeWoodTextures()
    {
        var rng = new Random(126);
        float Next(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        var knots = Enumerable.Range(0, 5).Select(_ => new Knot { U = Next(0f, 1f), Y = Next(0.2f, TextureLength - 0.2f), Radius = Next(0.01f, 0.022f) }).ToArray();
        var cracks = Enumerable.Range(0, 14).Select(_ =>
        {
            float y0 = Next(0f, TextureLength - 0.3f);
            return new Crack { U = Next(0f, 1f), Y0 = y0, Y1 = y0 + Next(0.1f, 0.6f), Width = Next(0.002f, 0.005f) };
        }).ToArray();

        var dark = new Color(0.17f, 0.11f, 0.065f);
        var light = new Color(0.5f, 0.37f, 0.23f);
        var silver = new Color(0.4f, 0.385f, 0.35f);
        var grime = new Color(0.09f, 0.075f, 0.06f);

        var albedo = new Color[PixelsWide * PixelsHigh];
        var height = new float[PixelsWide * PixelsHigh];

        for (int py = 0; py < PixelsHigh; py++)
        {
            float y = (py + 0.5f) / PixelsHigh * TextureLength;
            for (int px = 0; px < PixelsWide; px++)
            {
                float u = (px + 0.5f) / PixelsWide;

                // The grain wanders sideways, and bends around knots.
                float warp = (Fbm(u, y, 6, 0.5f, 3, 1) - 0.5f) * 0.04f;
                float knotShade = 0f;
                foreach (var knot in knots)
                {
                    float du = WrapDelta(u - knot.U) * TextureWidth;
                    float dy = y - knot.Y;
                    float d2 = (du * du + dy * dy / 6f) / (knot.Radius * knot.Radius);
                    warp -= du * 0.85f * Mathf.Exp(-d2 * 0.15f) / TextureWidth;
                    knotShade = Mathf.Max(knotShade, Mathf.Exp(-(du * du + dy * dy) / (knot.Radius * knot.Radius) * 1.5f));
                }
                float gu = u + warp;

                float bands = Fbm(gu, y, 80, 0.25f, 2, 2);
                float fibre = Fbm(gu, y, 256, 3f, 2, 3);
                float lines = Mathf.Pow(Mathf.Abs(Mathf.Sin((gu * 40f + bands * 2f) * Mathf.PI)), 8f);
                float board = Fbm(u, y, 5, 0.15f, 2, 6);
                float tone = Mathf.Clamp01(0.2f + 0.5f * bands + 0.3f * (fibre - 0.5f) - 0.3f * lines + 0.6f * (board - 0.5f));

                Color colour = Color.Lerp(dark, light, tone);
                float weathered = Smooth(0.5f, 0.78f, Fbm(u, y, 12, 0.9f, 4, 4));
                colour = Color.Lerp(colour, silver * (0.8f + 0.4f * fibre), weathered * 0.6f);
                float dirty = Smooth(0.55f, 0.85f, Fbm(u, y, 8, 1.5f, 3, 7));
                colour = Color.Lerp(colour, grime, dirty * 0.45f);

                float h = 0.5f * bands + 0.3f * fibre - 0.35f * lines - 0.2f * weathered * (1f - fibre);

                colour = Color.Lerp(colour, dark * 0.6f, knotShade * 0.85f);
                h += knotShade * 0.25f;

                foreach (var crack in cracks)
                {
                    if (y < crack.Y0 || y > crack.Y1) continue;
                    float along = (y - crack.Y0) / (crack.Y1 - crack.Y0);
                    float width = crack.Width * Mathf.Sin(along * Mathf.PI);
                    float acrossCrack = Mathf.Abs(WrapDelta(gu - crack.U)) * TextureWidth;
                    if (acrossCrack >= width) continue;
                    float depth = 1f - acrossCrack / width;
                    colour = Color.Lerp(colour, dark * 0.25f, depth);
                    h -= depth * 0.8f;
                }

                colour.a = 1f;
                albedo[py * PixelsWide + px] = colour;
                height[py * PixelsWide + px] = h;
            }
        }

        SaveTexture(AlbedoPath, albedo, false);
        SaveTexture(NormalPath, NormalsFrom(height, 2.5f), true);
    }

    static float WrapDelta(float delta) => delta - Mathf.Round(delta);

    // A 0 to 1 ramp between two edges, like a shader's smoothstep (Mathf.SmoothStep does something else).
    static float Smooth(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    static Color[] NormalsFrom(float[] height, float strength)
    {
        var normals = new Color[height.Length];
        for (int py = 0; py < PixelsHigh; py++)
        {
            int down = Mathf.Max(py - 1, 0), up = Mathf.Min(py + 1, PixelsHigh - 1);
            for (int px = 0; px < PixelsWide; px++)
            {
                int leftX = (px + PixelsWide - 1) % PixelsWide, rightX = (px + 1) % PixelsWide;
                float dx = height[py * PixelsWide + rightX] - height[py * PixelsWide + leftX];
                float dy = height[up * PixelsWide + px] - height[down * PixelsWide + px];
                var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                normals[py * PixelsWide + px] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        }
        return normals;
    }

    static void SaveTexture(string path, Color[] pixels, bool normalMap)
    {
        var texture = new Texture2D(PixelsWide, PixelsHigh, TextureFormat.RGBA32, false, normalMap);
        texture.SetPixels(pixels);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Clamp;
        importer.anisoLevel = 4;
        importer.SaveAndReimport();
    }

    // Value noise that repeats across the texture's width, so a board cut near the edge wraps round seamlessly.
    static float Fbm(float u, float y, int cellsAcross, float cellsPerMetre, int octaves, int seed)
    {
        float sum = 0f, weight = 0.5f, total = 0f;
        for (int o = 0; o < octaves; o++)
        {
            int period = cellsAcross << o;
            sum += weight * ValueNoise(u * period, y * cellsPerMetre * (1 << o), period, seed + o * 31);
            total += weight;
            weight *= 0.5f;
        }
        return sum / total;
    }

    static float ValueNoise(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int xa = ((x0 % period) + period) % period, xb = (xa + 1) % period;

        float bottom = Mathf.Lerp(Hash(xa, y0, seed), Hash(xb, y0, seed), fx);
        float top = Mathf.Lerp(Hash(xa, y0 + 1, seed), Hash(xb, y0 + 1, seed), fx);
        return Mathf.Lerp(bottom, top, fy);
    }

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}
