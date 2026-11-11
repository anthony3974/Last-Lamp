using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastLamp;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = System.Random;

/// <summary>
/// One-off tool: hangs an old wooden double door, in a wooden frame, in the front doorway of Warehouse_01.
/// Makes the weathered wood texture, the meshes and the materials, and lets the player open the door.
/// Safe to run again: it replaces the door it made before. Delete once the door is confirmed.
/// </summary>
public static class FrontDoorBuilder
{
    const string ScenePath = "Assets/Scenes/Warehouse_01.unity";
    const string ArtFolder = "Assets/Art/Door";
    const string AlbedoPath = ArtFolder + "/Door_Wood_Albedo.png";
    const string NormalPath = ArtFolder + "/Door_Wood_Normal.png";
    const string DoorName = "Front_Door";

    // The doorway in the front wall runs from X 13 to 17, 3.5 m high, and the wall's centre line is at Z -0.2.
    static readonly Vector3 DoorwayCentre = new Vector3(15f, 0f, -0.2f);
    const float HalfOpening = 2f;

    // The frame: posts set 4 cm into the brick on each side, and a beam across the top set into the wall above.
    const float PostWidth = 0.18f;
    const float PostEmbed = 0.04f;
    const float FrameDepth = 0.5f;
    const float LintelBottom = 3.38f;
    const float LintelTop = 3.72f;
    const float LintelHalfLength = 2.3f;

    // The leaves, each hung 6 cm in from its post so it clears the post when open.
    const float HingeGap = 0.06f;
    const float MeetingGap = 0.004f;
    const float PlankThickness = 0.045f;
    const float PlankGap = 0.006f;
    const int PlanksPerLeaf = 5;
    const float LedgeThickness = 0.035f;
    const float LedgeHeight = 0.17f;
    const float BraceWidth = 0.15f;
    const float IronThickness = 0.008f;
    static readonly float[] LedgeCentres = { 0.3f, 1.62f, 2.95f };

    // The wood texture covers 1.5 m across the grain, so each plank is cut from a different part of it, and 6 m along
    // it, long enough for the lintel, at about 340 pixels a metre. Its first 0.6 m is the damp foot of the door,
    // so only the planks start their wood from the bottom.
    const float TextureWidth = 1.5f;
    const float TextureLength = 6f;
    const int PixelsWide = 512;
    const int PixelsHigh = 2048;

    static float PostInner => HalfOpening - (PostWidth - PostEmbed);
    static float HingeX => PostInner - HingeGap;
    static float LeafLength => HingeX - MeetingGap;

    [MenuItem("Tools/Build Front Door")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Build Front Door: open {ScenePath} first.");
            return;
        }
        Build(scene);
    }

    /// <summary>For the command line: Unity -batchmode -executeMethod FrontDoorBuilder.BuildFromCommandLine -quit</summary>
    public static void BuildFromCommandLine()
    {
        Build(EditorSceneManager.OpenScene(ScenePath));
    }

    static void Build(Scene scene)
    {
        if (!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets/Art", "Door");

        var creak = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door_Creak.wav");
        var shut = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door_Shut.wav");
        if (creak == null || shut == null)
        {
            Debug.LogError("Build Front Door: Door_Creak.wav or Door_Shut.wav is missing from Assets/Audio.");
            return;
        }

        MakeWoodTextures();
        Material wood = MakeWoodMaterial();
        Material iron = MakeIronMaterial();
        Material[] materials = { wood, iron };

        Transform hall = Root(scene, "Warehouse").transform;
        Transform old = hall.Find(DoorName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        int blocker = LayerMask.NameToLayer("VisionBlocker");
        var door = new GameObject(DoorName) { layer = blocker };
        door.transform.SetParent(hall, false);
        door.transform.position = DoorwayCentre;

        // Monsters are kept out by Door_No_Monsters, so the NavMesh never depends on the door being open or shut.
        var modifier = door.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = true;

        BuildFrame(door.transform, materials, blocker);
        Transform left = BuildLeaf("Leaf_Left", door.transform, -1, 11, materials, blocker);
        Transform right = BuildLeaf("Leaf_Right", door.transform, 1, 23, materials, blocker);

        var voice = door.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 1f;
        voice.rolloffMode = AudioRolloffMode.Linear;
        voice.minDistance = 2f;
        voice.maxDistance = 30f;

        var serialized = new SerializedObject(door.AddComponent<Door>());
        serialized.FindProperty("leftLeaf").objectReferenceValue = left;
        serialized.FindProperty("rightLeaf").objectReferenceValue = right;
        serialized.FindProperty("creak").objectReferenceValue = creak;
        serialized.FindProperty("shut").objectReferenceValue = shut;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        WirePlayer(scene);
        CoverDoorwayFloor(hall);
        Rebake(scene);
        CheckMonstersStayInside();
        CheckRoutes(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Build Front Door: finished.");
    }

    static GameObject Root(Scene scene, string name) => scene.GetRootGameObjects().First(root => root.name == name);

    static void WirePlayer(Scene scene)
    {
        GameObject player = Root(scene, "Player");
        var doors = player.GetComponent<PlayerDoors>() ?? player.AddComponent<PlayerDoors>();

        var prompt = Root(scene, "HUD").GetComponentInChildren<InteractPrompt>(true);
        var serialized = new SerializedObject(prompt);
        serialized.FindProperty("doors").objectReferenceValue = doors;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // Door_No_Monsters was built 0.5 m above the floor, so it never marked the floor and monsters could walk out
    // through the doorway onto the street lamp's pool. Drop it so it reaches below the floor.
    static void CoverDoorwayFloor(Transform hall)
    {
        var volume = hall.Find("Door_No_Monsters").GetComponent<NavMeshModifierVolume>();
        float floorBelowCentre = volume.transform.position.y + 0.5f;
        volume.center = new Vector3(0f, volume.size.y / 2f - floorBelowCentre, 0f);
        EditorUtility.SetDirty(volume);
    }

    /// <summary>The same as pressing Bake on the scene's NavMesh surface, saved over its NavMesh asset.</summary>
    static void Rebake(Scene scene)
    {
        Physics.SyncTransforms();
        foreach (NavMeshSurface surface in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NavMeshSurface>(true)))
        {
            string assetPath = AssetDatabase.GetAssetPath(surface.navMeshData);
            surface.BuildNavMesh();
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
            EditorUtility.SetDirty(surface);
        }
    }

    /// <summary>Checks every patrol route still joins up outside the light after the rebake.</summary>
    static void CheckRoutes(Scene scene)
    {
        int dark = NavMesh.AllAreas & ~(1 << NavMesh.GetAreaFromName("Lit"));
        var path = new NavMeshPath();
        foreach (Enemy enemy in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Enemy>(true)))
        {
            var route = new SerializedObject(enemy).FindProperty("patrolRoute");
            var stops = new List<Vector3> { enemy.transform.position };
            for (int i = 0; i < route.arraySize; i++) stops.Add(((Transform)route.GetArrayElementAtIndex(i).objectReferenceValue).position);

            for (int i = 0; i < stops.Count - 1; i++)
            {
                bool joined = NavMesh.SamplePosition(stops[i], out NavMeshHit from, 1f, dark)
                    && NavMesh.SamplePosition(stops[i + 1], out NavMeshHit to, 1f, dark)
                    && NavMesh.CalculatePath(from.position, to.position, dark, path)
                    && path.status == NavMeshPathStatus.PathComplete;
                if (!joined) Debug.LogError($"Build Front Door: {enemy.name}'s route breaks between stops {i} and {i + 1}.");
            }
        }
        Debug.Log("Build Front Door: patrol routes checked.");
    }

    // Logs how close to the street a monster can get, from the NavMesh as baked.
    static void CheckMonstersStayInside()
    {
        if (!NavMesh.SamplePosition(new Vector3(15f, 0f, 8f), out NavMeshHit inside, 1f, NavMesh.AllAreas))
        {
            Debug.LogWarning("Build Front Door: no NavMesh loaded, so the doorway could not be checked.");
            return;
        }

        var path = new NavMeshPath();
        var street = new Vector3(15f, 0f, -3f);
        NavMesh.SamplePosition(street, out NavMeshHit nearest, 10f, NavMesh.AllAreas);
        NavMesh.CalculatePath(inside.position, nearest.position, NavMesh.AllAreas, path);
        Vector3 end = path.corners.Length > 0 ? path.corners[^1] : inside.position;

        if (end.z < 0.5f) Debug.LogError($"Build Front Door: a monster can reach Z {end.z:F2}, past the doorway.");
        else Debug.Log($"Build Front Door: the nearest a monster gets to the street is Z {end.z:F2} ({path.status}); the door is at Z -0.2.");
    }

    // --- Frame ---

    static void BuildFrame(Transform door, Material[] materials, int layer)
    {
        var mesh = new MeshBuilder();
        var rng = new Random(5);
        float postCentre = HalfOpening + PostEmbed - PostWidth / 2f;
        foreach (float sign in new[] { -1f, 1f })
        {
            var centre = new Vector3(sign * postCentre, LintelBottom / 2f, 0f);
            mesh.Box(centre, new Vector3(PostWidth, LintelBottom, FrameDepth), Quaternion.identity, 0, Grain.AlongY,
                new Vector2((float)rng.NextDouble(), centre.y));
        }

        mesh.Box(new Vector3(0f, (LintelBottom + LintelTop) / 2f, 0f), new Vector3(LintelHalfLength * 2f, LintelTop - LintelBottom, FrameDepth),
            Quaternion.identity, 0, Grain.AlongX, new Vector2((float)rng.NextDouble(), 3f));

        var frame = new GameObject("Frame", typeof(MeshFilter), typeof(MeshRenderer)) { layer = layer };
        frame.transform.SetParent(door, false);
        frame.GetComponent<MeshFilter>().sharedMesh = mesh.Save($"{ArtFolder}/Door_Frame.asset");
        frame.GetComponent<MeshRenderer>().sharedMaterials = new[] { materials[0] };

        // Only the posts need colliders: the brick above already stops anything at the lintel.
        foreach (float sign in new[] { -1f, 1f })
        {
            var post = frame.AddComponent<BoxCollider>();
            post.center = new Vector3(sign * postCentre, LintelBottom / 2f, 0f);
            post.size = new Vector3(PostWidth, LintelBottom, FrameDepth);
        }
    }

    // --- Leaves ---

    /// <param name="hingeSide">-1 for the leaf hinged on the left (-X), +1 for the right.</param>
    static Transform BuildLeaf(string name, Transform door, int hingeSide, int seed, Material[] materials, int layer)
    {
        int along = -hingeSide;
        var rng = new Random(seed);
        var mesh = new MeshBuilder();
        Vector3 At(float x, float y, float z) => new Vector3(x * along, y, z);
        float Next(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        // Planks, each a little different: rotted at the foot, slightly warped, cut from a different part of the wood.
        float[] widths = Enumerable.Range(0, PlanksPerLeaf).Select(_ => Next(0.9f, 1.1f)).ToArray();
        float scale = (LeafLength - PlankGap * (PlanksPerLeaf - 1)) / widths.Sum();
        float cursor = 0f;
        float lowestFoot = float.MaxValue, highestTop = 0f;
        foreach (float share in widths)
        {
            float width = share * scale;
            float foot = 0.03f + Next(0f, 0.07f);
            float top = LintelBottom - 0.025f - Next(0f, 0.015f);
            lowestFoot = Mathf.Min(lowestFoot, foot);
            highestTop = Mathf.Max(highestTop, top);

            Vector3 centre = At(cursor + width / 2f, (foot + top) / 2f, Next(-0.004f, 0.004f));
            mesh.Box(centre, new Vector3(width, top - foot, PlankThickness), Quaternion.identity, 0, Grain.AlongY,
                new Vector2(Next(0f, 1f), centre.y));
            cursor += width + PlankGap;
        }

        // Inside: three ledges across, and a brace in each gap between them, rising from the hinge side.
        float inside = PlankThickness / 2f + LedgeThickness / 2f;
        foreach (float height in LedgeCentres)
        {
            mesh.Box(At(LeafLength / 2f, height, inside), new Vector3(LeafLength - 0.08f, LedgeHeight, LedgeThickness),
                Quaternion.identity, 0, Grain.AlongX, new Vector2(Next(0f, 1f), Next(1.6f, 4.2f)));
        }
        for (int i = 0; i < LedgeCentres.Length - 1; i++)
        {
            Vector3 low = At(0.12f, LedgeCentres[i] + LedgeHeight / 2f, inside);
            Vector3 high = At(LeafLength - 0.12f, LedgeCentres[i + 1] - LedgeHeight / 2f, inside);
            Vector3 span = high - low;
            float angle = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg;
            mesh.Box((low + high) / 2f, new Vector3(span.magnitude - 0.12f, BraceWidth, LedgeThickness),
                Quaternion.Euler(0f, 0f, angle), 0, Grain.AlongX, new Vector2(Next(0f, 1f), Next(1.6f, 4.2f)));
        }

        // Outside: a black iron strap hinge at each ledge, riveted through, and a ring pull near the meeting edge.
        float outside = -(PlankThickness / 2f + IronThickness / 2f);
        foreach (float height in LedgeCentres)
        {
            Iron(mesh, At(0.235f, height, outside), new Vector3(0.53f, 0.075f, IronThickness));
            Iron(mesh, At(0.75f, height, outside), new Vector3(0.5f, 0.06f, IronThickness));
            Iron(mesh, At(1.15f, height, outside), new Vector3(0.3f, 0.048f, IronThickness));
            mesh.Box(At(1.32f, height, outside), new Vector3(0.065f, 0.065f, IronThickness), Quaternion.Euler(0f, 0f, 45f), 1, Grain.None, Vector2.zero);
            foreach (float x in new[] { 0.12f, 0.45f, 0.8f, 1.15f })
                Iron(mesh, At(x, height, outside - IronThickness / 2f - 0.006f), new Vector3(0.022f, 0.022f, 0.012f));

            // The knuckle the leaf turns on, at the hinge edge.
            Iron(mesh, At(-0.012f, height, -PlankThickness / 2f - 0.012f), new Vector3(0.045f, 0.2f, 0.045f));
        }

        float ringX = LeafLength - 0.2f;
        foreach (float face in new[] { -1f, 1f })
        {
            float plateZ = face * (PlankThickness / 2f + IronThickness / 2f);
            Iron(mesh, At(ringX, 1.12f, plateZ), new Vector3(0.1f, 0.1f, IronThickness));
            Ring(mesh, At(ringX, 1.02f, plateZ + face * 0.014f), 0.075f, 0.014f);
        }

        var leaf = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = layer };
        leaf.transform.SetParent(door, false);
        leaf.transform.localPosition = new Vector3(hingeSide * HingeX, 0f, 0f);
        leaf.GetComponent<MeshFilter>().sharedMesh = mesh.Save($"{ArtFolder}/Door_{name}.asset");
        leaf.GetComponent<MeshRenderer>().sharedMaterials = materials;

        var collider = leaf.AddComponent<BoxCollider>();
        collider.center = At(LeafLength / 2f, (lowestFoot + highestTop) / 2f, 0.008f);
        collider.size = new Vector3(LeafLength, highestTop - lowestFoot, PlankThickness + LedgeThickness + 0.03f);

        // The leaves move, so give them kinematic bodies rather than moving static colliders.
        var body = leaf.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        return leaf.transform;
    }

    static void Iron(MeshBuilder mesh, Vector3 centre, Vector3 size)
    {
        mesh.Box(centre, size, Quaternion.identity, 1, Grain.None, Vector2.zero);
    }

    static void Ring(MeshBuilder mesh, Vector3 centre, float radius, float bar)
    {
        const int segments = 12;
        float length = 2f * Mathf.PI * radius / segments * 1.15f;
        for (int i = 0; i < segments; i++)
        {
            float angle = 360f * i / segments;
            float radians = angle * Mathf.Deg2Rad;
            var offset = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * radius;
            mesh.Box(centre + offset, new Vector3(bar, length, bar), Quaternion.Euler(0f, 0f, angle), 1, Grain.None, Vector2.zero);
        }
    }

    // --- Materials ---

    static Material MakeWoodMaterial()
    {
        var material = NewMaterial("Door_Wood");
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        material.SetFloat("_BumpScale", 1f);
        material.EnableKeyword("_NORMALMAP");
        material.SetFloat("_Smoothness", 0.12f);
        return material;
    }

    static Material MakeIronMaterial()
    {
        var material = NewMaterial("Door_Iron");
        material.SetColor("_BaseColor", new Color(0.1f, 0.085f, 0.075f));
        material.SetFloat("_Metallic", 0.55f);
        material.SetFloat("_Smoothness", 0.3f);
        return material;
    }

    static Material NewMaterial(string name)
    {
        string path = $"Assets/Materials/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // --- Wood texture ---

    struct Knot { public float U, Y, Radius; }
    struct Crack { public float U, Y0, Y1, Width; }

    // Old weathered planks: grey-brown grain, silvered patches, a few knots and splits, dark and damp at the foot.
    static void MakeWoodTextures()
    {
        var rng = new Random(42);
        float Next(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        var knots = Enumerable.Range(0, 6).Select(_ => new Knot { U = Next(0f, 1f), Y = Next(0.4f, 5.7f), Radius = Next(0.012f, 0.026f) }).ToArray();
        var cracks = Enumerable.Range(0, 20).Select(_ =>
        {
            float y0 = Next(0f, 5f);
            return new Crack { U = Next(0f, 1f), Y0 = y0, Y1 = y0 + Next(0.15f, 0.9f), Width = Next(0.0025f, 0.006f) };
        }).ToArray();

        var dark = new Color(0.15f, 0.105f, 0.072f);
        var light = new Color(0.36f, 0.27f, 0.185f);
        var silver = new Color(0.31f, 0.295f, 0.27f);
        var damp = new Color(0.07f, 0.065f, 0.05f);

        var albedo = new Color[PixelsWide * PixelsHigh];
        var height = new float[PixelsWide * PixelsHigh];

        for (int py = 0; py < PixelsHigh; py++)
        {
            float y = (py + 0.5f) / PixelsHigh * TextureLength;
            for (int px = 0; px < PixelsWide; px++)
            {
                float u = (px + 0.5f) / PixelsWide;

                // The grain wanders sideways, and bends around knots.
                float warp = (Fbm(u, y, 9, 0.5f, 3, 1) - 0.5f) * 0.04f;
                float knotShade = 0f;
                foreach (var knot in knots)
                {
                    // The grain swells smoothly around the knot, a little further along the grain than across it.
                    float du = WrapDelta(u - knot.U) * TextureWidth;
                    float dy = y - knot.Y;
                    float d2 = (du * du + dy * dy / 6f) / (knot.Radius * knot.Radius);
                    warp -= du * 0.85f * Mathf.Exp(-d2 * 0.15f) / TextureWidth;
                    knotShade = Mathf.Max(knotShade, Mathf.Exp(-(du * du + dy * dy) / (knot.Radius * knot.Radius) * 1.5f));
                }
                float gu = u + warp;

                float bands = Fbm(gu, y, 120, 0.25f, 2, 2);
                float fibre = Fbm(gu, y, 384, 3f, 2, 3);
                float lines = Mathf.Pow(Mathf.Abs(Mathf.Sin((gu * 54f + bands * 2f) * Mathf.PI)), 8f);
                float tone = Mathf.Clamp01(0.25f + 0.55f * bands + 0.35f * (fibre - 0.5f) - 0.3f * lines);

                Color colour = Color.Lerp(dark, light, tone);
                float weathered = Mathf.SmoothStep(0.42f, 0.72f, Fbm(u, y, 15, 0.9f, 4, 4));
                colour = Color.Lerp(colour, silver * (0.8f + 0.4f * fibre), weathered * 0.75f);

                float h = 0.5f * bands + 0.3f * fibre - 0.35f * lines - 0.2f * weathered * (1f - fibre);

                colour = Color.Lerp(colour, dark * 0.6f, knotShade * 0.85f);
                h += knotShade * 0.25f;

                foreach (var crack in cracks)
                {
                    if (y < crack.Y0 || y > crack.Y1) continue;
                    float along = (y - crack.Y0) / (crack.Y1 - crack.Y0);
                    float width = crack.Width * Mathf.Sin(along * Mathf.PI);
                    float across = Mathf.Abs(WrapDelta(gu - crack.U)) * TextureWidth;
                    if (across >= width) continue;
                    float depth = 1f - across / width;
                    colour = Color.Lerp(colour, dark * 0.25f, depth);
                    h -= depth * 0.8f;
                }

                // Rain splashes up the foot of the door: darker, damper and greener toward the ground.
                float foot = 1f - Mathf.SmoothStep(0f, 0.6f, y + (Fbm(u, y, 24, 2f, 2, 5) - 0.5f) * 0.25f);
                colour = Color.Lerp(colour, damp, foot * 0.7f);

                colour.a = 1f;
                albedo[py * PixelsWide + px] = colour;
                height[py * PixelsWide + px] = h;
            }
        }

        SaveTexture(AlbedoPath, albedo, false);
        SaveTexture(NormalPath, NormalsFrom(height, 2.5f), true);
    }

    static float WrapDelta(float delta) => delta - Mathf.Round(delta);

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

    // Value noise that repeats across the texture's width, so the planks tile side by side.
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

    // --- Mesh building ---

    enum Grain { None, AlongX, AlongY }

    /// <summary>Boxes merged into one mesh, with UVs in metres so the wood keeps its scale on every board.</summary>
    class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int>[] triangles = { new List<int>(), new List<int>() };

        /// <param name="wood">X: where across the texture this board was cut (0 to 1). Y: metres added along the grain,
        /// so an upright plank's grain lines up with its height off the floor.</param>
        public void Box(Vector3 centre, Vector3 size, Quaternion rotation, int submesh, Grain grain, Vector2 wood)
        {
            int grainAxis = grain == Grain.AlongX ? 0 : 1;
            for (int axis = 0; axis < 3; axis++)
            {
                int a = (axis + 1) % 3, b = (axis + 2) % 3;
                foreach (int facing in new[] { 1, -1 })
                {
                    int first = vertices.Count;
                    var normal = new Vector3 { [axis] = facing };
                    foreach (var (sa, sb) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                    {
                        var local = new Vector3 { [axis] = facing * size[axis] / 2f, [a] = sa * size[a] / 2f, [b] = sb * size[b] / 2f };
                        vertices.Add(centre + rotation * local);
                        normals.Add(rotation * normal);
                        uvs.Add(Uv(local, axis, a, b, grainAxis, grain, wood));
                    }

                    var list = triangles[submesh];
                    if (facing > 0) list.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                    else list.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
                }
            }
        }

        static Vector2 Uv(Vector3 local, int axis, int a, int b, int grainAxis, Grain grain, Vector2 wood)
        {
            if (grain == Grain.None) return Vector2.zero;

            // On a face along the grain, V runs with the grain. On the end grain, just spread the texture flat.
            int along = axis == grainAxis ? b : grainAxis;
            int across = axis == grainAxis ? a : (a == grainAxis ? b : a);
            return new Vector2(local[across] / TextureWidth + wood.x, (local[along] + wood.y) / TextureLength);
        }

        public Mesh Save(string path)
        {
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles[1].Count > 0 ? 2 : 1;
            for (int i = 0; i < mesh.subMeshCount; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }

    // --- Preview ---

    /// <summary>
    /// For the command line: renders the door from the street (shut and open), from inside, and under a work light,
    /// into the folder after -previewDir. Changes nothing on disk in the project.
    /// </summary>
    public static void RenderPreviews()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-previewDir");
        string folder = index >= 0 && index + 1 < args.Length ? args[index + 1] : "Temp/DoorPreview";
        Directory.CreateDirectory(folder);

        Scene scene = EditorSceneManager.OpenScene(ScenePath);
        foreach (var main in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) main.enabled = false;
        Transform door = Root(scene, "Warehouse").transform.Find(DoorName);
        Transform left = door.Find("Leaf_Left"), right = door.Find("Leaf_Right");

        void Swing(int side, float amount)
        {
            var (l, r) = (-90f * side * amount, 90f * side * amount);
            left.localRotation = Quaternion.Euler(0f, l, 0f);
            right.localRotation = Quaternion.Euler(0f, r, 0f);
        }

        // The first render of a session comes out without its ambient light, so throw one away.
        Shoot(folder, "0_warm_up", new Vector3(15f, 2f, -8f), new Vector3(15f, 1.5f, 0f));
        File.Delete(Path.Combine(folder, "0_warm_up.png"));

        Shoot(folder, "1_street_shut", new Vector3(15.6f, 2.3f, -9.5f), new Vector3(15f, 1.5f, 0f));
        Swing(1, 1f);
        Shoot(folder, "2_street_open", new Vector3(15.6f, 2.3f, -9.5f), new Vector3(15f, 1.5f, 0f));
        Swing(-1, 0.55f);
        Shoot(folder, "3_street_swinging_out", new Vector3(19f, 1.7f, -5.5f), new Vector3(15f, 1.6f, -0.5f));
        Swing(1, 0f);
        Shoot(folder, "4_inside_shut", new Vector3(15.6f, 1.7f, 4.5f), new Vector3(15f, 1.6f, -0.2f));

        var workLight = new GameObject("Work Light").AddComponent<Light>();
        workLight.type = LightType.Spot;
        workLight.spotAngle = 70f;
        workLight.range = 15f;
        workLight.intensity = 25f;
        workLight.shadows = LightShadows.Soft;
        workLight.transform.position = new Vector3(13f, 2.6f, -3f);
        workLight.transform.LookAt(new Vector3(15f, 1.4f, -0.2f));
        Shoot(folder, "5_work_light_outside", new Vector3(16.6f, 1.5f, -2.4f), new Vector3(15.4f, 1.3f, -0.2f));

        workLight.transform.position = new Vector3(17f, 2.6f, 2.6f);
        workLight.transform.LookAt(new Vector3(15f, 1.4f, -0.2f));
        Shoot(folder, "6_work_light_inside", new Vector3(13.6f, 1.5f, 2.4f), new Vector3(14.6f, 1.3f, -0.2f));

        Debug.Log($"Build Front Door: previews saved to {Path.GetFullPath(folder)}");
    }

    static void Shoot(string folder, string name, Vector3 from, Vector3 at)
    {
        var holder = new GameObject("Preview Camera");
        var camera = holder.AddComponent<Camera>();
        camera.fieldOfView = 55f;
        camera.nearClipPlane = 0.05f;
        holder.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        holder.transform.position = from;
        holder.transform.LookAt(at);

        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        Physics.SyncTransforms();
        camera.Render();

        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());

        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(holder);
    }
}
