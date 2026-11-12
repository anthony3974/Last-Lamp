using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = System.Random;

/// <summary>
/// One-off tool (task 26): gives every crate in both levels, hiding crates included, the look of an old wooden
/// shipping crate. A crate is a box on the old <c>Crate_Brown</c> material with Unity's cube mesh.
/// For each one it adds a <c>Crate_Wooden</c> child, a mesh built in metres at the crate's real size with no collider,
/// and switches the crate's own renderer off. The crate keeps its collider, transform, layer and components, so
/// sight and stealth don't change. Crates in a prefab (the hiding spot) are changed in the prefab, once.
/// Makes the wood textures, materials and one mesh per crate size into Assets/Art/Crate.
/// Safe to run again: it updates what it made before and never adds a second copy. Delete once the crates are done.
/// </summary>
public static class CrateBuilder
{
    static readonly string[] ScenePaths = { "Assets/Scenes/Corridor_01.unity", "Assets/Scenes/Warehouse_01.unity" };
    const string PrefabFolder = "Assets/Prefabs";
    const string OldMaterialPath = "Assets/Materials/Crate_Brown.mat";

    const string ArtFolder = "Assets/Art/Crate";
    const string AlbedoPath = ArtFolder + "/Crate_Wood_Albedo.png";
    const string NormalPath = ArtFolder + "/Crate_Wood_Normal.png";
    const string MeshPrefix = "Crate_Wooden";
    // The mesh the test crate (task 26 trial) made; it is the 1.5 m crate's mesh and is renamed to say so.
    const string TrialMeshPath = ArtFolder + "/Crate_Wooden.asset";
    const string VisualName = "Crate_Wooden";

    // Below this the frame and planks no longer fit on a face.
    const float SmallestSide = 0.5f;

    // The frame (corner boards, rails and braces) stands proud of the planks, so its edges catch the light and the
    // crate reads as a crate even in the dark. The outside of the frame is exactly the old box, so nothing the crate
    // looks like sticks out past it (only the iron corners, by 4 mm).
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
    // it, at 512 pixels a metre. A board longer than that has the texture squeezed a little along it to fit.
    const float TextureWidth = 1f;
    const float TextureLength = 2f;
    const int PixelsWide = 512;
    const int PixelsHigh = 1024;

    /// <summary>What a run did, logged at the end.</summary>
    class Report
    {
        public readonly List<string> Converted = new List<string>();
        public readonly List<string> Skipped = new List<string>();
        public readonly List<string> Meshes = new List<string>();
        public bool Failed;
    }

    static readonly Dictionary<string, Mesh> MeshesBySize = new Dictionary<string, Mesh>();
    static Material oldMaterial;
    static Material[] materials;

    [MenuItem("Tools/Build Wooden Crates")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Build Wooden Crates: leave Play mode first.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        oldMaterial = AssetDatabase.LoadAssetAtPath<Material>(OldMaterialPath);
        if (oldMaterial == null)
        {
            Debug.LogError($"Build Wooden Crates: {OldMaterialPath} was not found, so no crate can be recognised.");
            return;
        }

        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        var report = new Report();
        MeshesBySize.Clear();
        try
        {
            EditorUtility.DisplayProgressBar("Build Wooden Crates", "Wood textures and materials", 0f);
            PrepareArt();

            EditorUtility.DisplayProgressBar("Build Wooden Crates", "Prefabs", 0.2f);
            var convertedPrefabs = ConvertPrefabs(report);

            for (int i = 0; i < ScenePaths.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Build Wooden Crates", ScenePaths[i], 0.3f + 0.6f * i / ScenePaths.Length);
                Scene scene = EditorSceneManager.OpenScene(ScenePaths[i], OpenSceneMode.Single);
                ConvertScene(scene, convertedPrefabs, report);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    report.Failed = true;
                    Debug.LogError($"Build Wooden Crates: {ScenePaths[i]} could not be saved.");
                }
            }

            if (!report.Failed) RemoveUnusedMeshes(report);
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            report.Failed = true;
            Debug.LogException(e);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (setup.Length > 0 && setup.All(s => File.Exists(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        var text = new StringBuilder();
        text.AppendLine($"Build Wooden Crates: {(report.Failed ? "FAILED part way, see the errors above" : "finished")}. " +
                        $"{report.Converted.Count} crates converted, {report.Skipped.Count} skipped.");
        text.AppendLine("Converted:");
        foreach (string line in report.Converted) text.AppendLine("  " + line);
        text.AppendLine("Skipped:");
        foreach (string line in report.Skipped) text.AppendLine("  " + line);
        text.AppendLine("Meshes:");
        foreach (string line in report.Meshes) text.AppendLine("  " + line);
        if (report.Failed) Debug.LogError(text.ToString());
        else Debug.Log(text.ToString());
    }

    // --- Finding and converting crates ---

    /// <summary>Converts the crates in every prefab under Assets/Prefabs, once each.</summary>
    /// <returns>The prefabs that hold crates, so their instances in the scenes can be checked.</returns>
    static HashSet<string> ConvertPrefabs(Report report)
    {
        var converted = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || !asset.GetComponentsInChildren<MeshRenderer>(true).Any(UsesOldMaterial)) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int before = report.Converted.Count;
                ConvertAll(root, $"{Path.GetFileName(path)} (prefab asset)", null, report);
                if (report.Converted.Count > before)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    converted.Add(path);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        return converted;
    }

    static void ConvertScene(Scene scene, HashSet<string> convertedPrefabs, Report report)
    {
        string where = Path.GetFileNameWithoutExtension(scene.path);
        foreach (GameObject root in scene.GetRootGameObjects())
            ConvertAll(root, where, convertedPrefabs, report);
    }

    /// <summary>Converts every crate under the root, and notes anything named like a crate that isn't one.</summary>
    /// <param name="convertedPrefabs">Null when the root is a prefab asset being edited.</param>
    static void ConvertAll(GameObject root, string where, HashSet<string> convertedPrefabs, Report report)
    {
        var crates = new List<Transform>();
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            // Null if an extra copy of a visual was removed earlier in this loop.
            if (renderer == null || renderer.gameObject.name == VisualName || !UsesOldMaterial(renderer)) continue;
            string path = PathOf(renderer.transform);

            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || !IsBuiltInCube(filter.sharedMesh))
            {
                report.Skipped.Add($"{where}: {path}: on Crate_Brown but not a plain box (mesh " +
                                   $"{(filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "none")}), left as it is");
                continue;
            }

            if (convertedPrefabs != null && PrefabUtility.IsPartOfPrefabInstance(renderer))
            {
                string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(renderer);
                if (!convertedPrefabs.Contains(source))
                {
                    report.Skipped.Add($"{where}: {path}: part of {source}, which was not converted, left as it is");
                    continue;
                }
            }

            string result = Convert(renderer, convertedPrefabs != null);
            if (result.StartsWith("skipped"))
            {
                report.Skipped.Add($"{where}: {path}: {result}");
                continue;
            }

            crates.Add(renderer.transform);
            string prefab = convertedPrefabs == null ? "in the prefab"
                : PrefabUtility.IsPartOfPrefabInstance(renderer)
                    ? $"instance of {Path.GetFileName(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(renderer))}"
                    : "scene object";
            bool hiding = renderer.GetComponentInParent<HidingSpot>(true) != null;
            report.Converted.Add($"{where}: {path}: {result}, {prefab}{(hiding ? ", hiding spot" : "")}");
        }

        // Anything else named like a crate, so the log shows it was looked at and left alone on purpose.
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == VisualName || t.name.IndexOf("crate", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (crates.Any(crate => crate == t || crate.IsChildOf(t))) continue;
            var renderer = t.GetComponent<Renderer>();
            if (renderer != null && UsesOldMaterial(renderer)) continue; // Already reported above.
            string why = renderer == null ? "no renderer" : $"not on Crate_Brown ({string.Join(", ", renderer.sharedMaterials.Select(m => m != null ? m.name : "none"))})";
            report.Skipped.Add($"{where}: {PathOf(t)}: named like a crate but {why}, so not a crate, left as it is");
        }
    }

    /// <summary>Gives one crate its wooden look. Returns a line for the log.</summary>
    /// <param name="inScene">False when editing a prefab asset's contents.</param>
    static string Convert(MeshRenderer oldRenderer, bool inScene)
    {
        Transform crate = oldRenderer.transform;
        Mesh box = oldRenderer.GetComponent<MeshFilter>().sharedMesh;

        // The mesh is built in metres at the crate's real size, and its object undoes the crate's scale, so the wood
        // keeps its real size on the boards whatever size the crate is.
        Vector3 scale = Round(crate.lossyScale, 100000f);
        if (Mathf.Abs(scale.x) < 1e-4f || Mathf.Abs(scale.y) < 1e-4f || Mathf.Abs(scale.z) < 1e-4f)
            return $"skipped: scaled to nothing ({scale})";
        Vector3 size = Round(Vector3.Scale(box.bounds.size, Abs(scale)), 1000f);
        if (Mathf.Min(size.x, Mathf.Min(size.y, size.z)) < SmallestSide)
            return $"skipped: {SizeName(size)} is too small for the wooden crate (smallest side {SmallestSide} m)";

        Mesh mesh = MeshFor(size);
        var localPosition = box.bounds.center;
        var localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);

        // Keep one visual child (the prefab's, if the crate is in a prefab instance) and remove any extra copies.
        var visuals = crate.Cast<Transform>().Where(child => child.name == VisualName).ToList();
        Transform visual = visuals.FirstOrDefault(v => PrefabUtility.IsPartOfPrefabInstance(v) && !PrefabUtility.IsAddedGameObjectOverride(v.gameObject))
                           ?? visuals.FirstOrDefault();
        foreach (var extra in visuals.Where(v => v != visual)) Object.DestroyImmediate(extra.gameObject);

        bool fromPrefab = inScene && visual != null && PrefabUtility.IsPartOfPrefabInstance(visual);
        bool resized = false;
        if (fromPrefab)
        {
            // Hide_Crate scales the prefab's body up, so its visual needs its own mesh and scale: an instance override.
            var filter = visual.GetComponent<MeshFilter>();
            var sourceFilter = PrefabUtility.GetCorrespondingObjectFromSource(filter);
            var sourceTransform = PrefabUtility.GetCorrespondingObjectFromSource(visual);
            bool sameAsPrefab = sourceFilter != null && sourceFilter.sharedMesh == mesh && sourceTransform != null &&
                                sourceTransform.localPosition == localPosition && sourceTransform.localScale == localScale;
            if (sameAsPrefab)
            {
                PrefabUtility.RevertObjectOverride(visual, InteractionMode.AutomatedAction);
                PrefabUtility.RevertObjectOverride(filter, InteractionMode.AutomatedAction);
            }
            else
            {
                resized = true;
                visual.localPosition = localPosition;
                visual.localRotation = Quaternion.identity;
                visual.localScale = localScale;
                filter.sharedMesh = mesh;
                PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            }
        }
        else
        {
            if (visual == null)
            {
                visual = new GameObject(VisualName, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                visual.SetParent(crate, false);
            }
            foreach (var collider in visual.GetComponents<Collider>()) Object.DestroyImmediate(collider);

            visual.gameObject.layer = crate.gameObject.layer;
            visual.gameObject.SetActive(true);
            GameObjectUtility.SetStaticEditorFlags(visual.gameObject, GameObjectUtility.GetStaticEditorFlags(crate.gameObject));
            visual.localPosition = localPosition;
            visual.localRotation = Quaternion.identity;
            visual.localScale = localScale;

            // Not ??: a missing component is Unity's fake null in the editor.
            var filter = visual.GetComponent<MeshFilter>();
            if (filter == null) filter = visual.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = visual.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = visual.gameObject.AddComponent<MeshRenderer>();
            renderer.enabled = true;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = oldRenderer.shadowCastingMode;
            renderer.receiveShadows = oldRenderer.receiveShadows;
            renderer.staticShadowCaster = oldRenderer.staticShadowCaster;
            renderer.lightProbeUsage = oldRenderer.lightProbeUsage;
            renderer.reflectionProbeUsage = oldRenderer.reflectionProbeUsage;
            renderer.motionVectorGenerationMode = oldRenderer.motionVectorGenerationMode;
            renderer.allowOcclusionWhenDynamic = oldRenderer.allowOcclusionWhenDynamic;
            renderer.renderingLayerMask = oldRenderer.renderingLayerMask;
        }

        // Only the look changes: the collider, layer, transform and components stay as they were.
        if (oldRenderer.enabled)
        {
            oldRenderer.enabled = false;
            if (inScene && PrefabUtility.IsPartOfPrefabInstance(oldRenderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(oldRenderer);
        }
        EditorUtility.SetDirty(oldRenderer);
        EditorUtility.SetDirty(visual.gameObject);

        return $"{SizeName(size)} m{(fromPrefab ? " (visual from the prefab" + (resized ? ", resized by an instance override)" : ")") : "")}";
    }

    static bool UsesOldMaterial(Renderer renderer) => renderer.sharedMaterials.Contains(oldMaterial);

    static bool IsBuiltInCube(Mesh mesh) =>
        mesh != null && mesh.name == "Cube" && AssetDatabase.GetAssetPath(mesh) == "Library/unity default resources";

    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

    static Vector3 Round(Vector3 v, float steps) =>
        new Vector3(Mathf.Round(v.x * steps) / steps, Mathf.Round(v.y * steps) / steps, Mathf.Round(v.z * steps) / steps);

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static string SizeName(Vector3 size) =>
        string.Join("x", new[] { size.x, size.y, size.z }.Select(f => f.ToString("0.###", CultureInfo.InvariantCulture)));

    static string MeshPath(Vector3 size) => $"{ArtFolder}/{MeshPrefix}_{SizeName(size)}.asset";

    // --- Art ---

    static void PrepareArt()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets/Art", "Crate");

        if (AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath) == null || AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath) == null)
            MakeWoodTextures();
        materials = new[] { WoodMaterial(), FrameMaterial(), IronMaterial() };

        // The trial's mesh is the 1.5 m crate's: rename it rather than leave it behind.
        string trialTarget = MeshPath(new Vector3(1.5f, 1.5f, 1.5f));
        if (AssetDatabase.LoadAssetAtPath<Mesh>(TrialMeshPath) != null && AssetDatabase.LoadAssetAtPath<Mesh>(trialTarget) == null)
        {
            string error = AssetDatabase.MoveAsset(TrialMeshPath, trialTarget);
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"Build Wooden Crates: could not rename {TrialMeshPath}: {error}");
        }
    }

    /// <summary>The mesh for a crate of this size, built once a run and saved over last run's.</summary>
    static Mesh MeshFor(Vector3 size)
    {
        string path = MeshPath(size);
        if (MeshesBySize.TryGetValue(path, out Mesh mesh)) return mesh;
        mesh = BuildMesh(size / 2f).Save(path, Path.GetFileNameWithoutExtension(path));
        MeshesBySize[path] = mesh;
        return mesh;
    }

    /// <summary>Deletes crate meshes that no crate uses any more, such as the trial's or a size that has gone.</summary>
    static void RemoveUnusedMeshes(Report report)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { ArtFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileName(path).StartsWith(MeshPrefix)) continue;
            if (MeshesBySize.ContainsKey(path))
            {
                var mesh = MeshesBySize[path];
                report.Meshes.Add($"{path}: {mesh.triangles.Length / 3} triangles, bounds {mesh.bounds.size.ToString("F3")}");
            }
            else if (AssetDatabase.DeleteAsset(path))
            {
                report.Meshes.Add($"{path}: deleted, no crate uses it");
            }
        }
    }

    // --- Crate mesh ---

    // Which face's boards run on past a shared edge, so boards from two faces never fill the same space: the left and
    // right faces wrap their corner boards over the front and back ones, and the sides over the lid.
    static int Rank(int axis) => axis == X ? 2 : axis == Z ? 1 : 0;
    static bool Outranks(int a, int b) => Rank(a) > Rank(b);

    /// <param name="h">Half the crate's size, in metres.</param>
    static CrateMesh BuildMesh(Vector3 h)
    {
        var mesh = new CrateMesh();
        // The same seed for every size, so the 1.5 m crate is exactly the approved trial crate.
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
        return mesh;
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
            // A board longer than the texture gets it squeezed along the grain to fit, rather than run off its end.
            float squeeze = length > TextureLength ? TextureLength / length : 1f;
            Vector3 At(Vector2 p, float z) => centre + across * p.x + outward * p.y + along * z;
            Vector2 Uv(float u, float v) => grain ? new Vector2(u / TextureWidth + wood.x, (v * squeeze + wood.y) / TextureLength) : Vector2.zero;

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

    // --- Materials (made only if missing, so changes made to them by hand are kept) ---

    static Material WoodMaterial()
    {
        return LoadOrMake("Crate_Wood", material =>
        {
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
            material.SetColor("_BaseColor", Color.white);
            SetNormalMap(material);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.15f);
        });
    }

    // The same wood, darker and greyer, so the frame stands out from the planks by value alone once the volume has
    // taken most of the colour out.
    static Material FrameMaterial()
    {
        return LoadOrMake("Crate_Frame", material =>
        {
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
            material.SetColor("_BaseColor", new Color(0.56f, 0.5f, 0.44f));
            SetNormalMap(material);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.12f);
        });
    }

    static Material IronMaterial()
    {
        return LoadOrMake("Crate_Iron", material =>
        {
            material.SetColor("_BaseColor", new Color(0.1f, 0.09f, 0.08f));
            material.SetFloat("_Metallic", 0.6f);
            material.SetFloat("_Smoothness", 0.32f);
        });
    }

    static void SetNormalMap(Material material)
    {
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        material.SetFloat("_BumpScale", 1f);
        material.EnableKeyword("_NORMALMAP");
    }

    static Material LoadOrMake(string name, Action<Material> setUp)
    {
        string path = $"{ArtFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        setUp(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // --- Wood texture (made only if missing; the same as the trial's) ---

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
