using System.IO;
using System.Linq;
using LastLamp;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: replaces the empty shell in Warehouse_01 with the warehouse from the build spec.
/// Origin is the front-left inside corner: +X right, +Z toward the back wall, in metres.
/// Everything it makes is a normal object that can be edited by hand. Delete once the warehouse is confirmed.
/// </summary>
public static class WarehouseBuilder
{
    const string ScenePath = "Assets/Scenes/Warehouse_01.unity";
    const string CorridorPath = "Assets/Scenes/Corridor_01.unity";
    const string SceneFolder = "Assets/Scenes/Warehouse_01";
    const string CookiePath = "Assets/Materials/Warehouse_Skylight_Cookie.png";
    const string TestOnlyTag = "TestOnly";

    const float Width = 30f;
    const float Length = 50f;
    const float Eave = 13f;
    const float Rise = 5f;
    const float Thick = 0.4f;
    const float Deck = 8f;
    const float RailHeight = 1.1f;
    const float HideCrateZ = 34f;
    const int NotWalkableArea = 1;
    const int LitArea = 3;

    static readonly Vector3 PlayerStart = new Vector3(15f, 0.05f, -6f);

    static readonly Vector2[] FloorCrates =
    {
        new Vector2(19.5f, 6f), new Vector2(24.4f, 9.7f), new Vector2(22.8f, 18f), new Vector2(3.8f, 16.4f),
        new Vector2(19.5f, 24.7f), new Vector2(25.3f, 30.7f), new Vector2(2.8f, 35.6f), new Vector2(8.8f, 41.4f),
        new Vector2(4.3f, 24f), new Vector2(20.3f, 39.8f), new Vector2(25.3f, 4f),
    };

    static readonly Vector2[] Skylights =
    {
        new Vector2(18.3f, 35.8f), new Vector2(11.7f, 17.5f), new Vector2(21.7f, 25f), new Vector2(5f, 40.8f),
    };

    static Material brick, concrete, steel, roof, glass, asphalt, wood, glow;
    static int blocker;

    [MenuItem("Tools/Build Warehouse")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || scene.GetRootGameObjects().Any(root => root.name == "Monsters"))
        {
            Debug.LogError($"Build Warehouse: open {ScenePath} first, or this has already been built.");
            return;
        }

        var lamp = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Lamp.prefab");
        var hidingSpot = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HidingSpot.prefab");
        wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Crate_Brown.mat");
        glow = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Lamp_Glow.mat");
        if (lamp == null || hidingSpot == null || wood == null || glow == null)
        {
            Debug.LogError("Build Warehouse: a prefab or material was not found.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets/Scenes", "Warehouse_01");
        AddTestOnlyTag();
        MakeMaterials();
        blocker = LayerMask.NameToLayer("VisionBlocker");

        Object.DestroyImmediate(Root(scene, "Warehouse"));

        Transform hall = NewRoot(scene, "Warehouse").transform;
        BuildShell(hall);
        BuildRoof(hall);
        BuildUpperLevel(hall, hidingSpot);
        BuildGroundFloor(hall);

        BuildLighting(scene, lamp);
        BuildStreet(scene);
        MovePlayerToStart(scene);
        BuildTestPlatform(scene);
        BuildMonsters(scene);
        BakeNavMesh(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Build Warehouse: finished.");
    }

    static GameObject Root(Scene scene, string name) => scene.GetRootGameObjects().First(root => root.name == name);

    static GameObject NewRoot(Scene scene, string name)
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        return root;
    }

    static Transform Group(string name, Transform parent)
    {
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        return group;
    }

    static void AddTestOnlyTag()
    {
        var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tags = manager.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
        {
            if (tags.GetArrayElementAtIndex(i).stringValue == TestOnlyTag) return;
        }

        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = TestOnlyTag;
        manager.ApplyModifiedPropertiesWithoutUndo();
    }

    static void MakeMaterials()
    {
        brick = Surface("Brick", new Color(0.27f, 0.16f, 0.13f));
        concrete = Surface("Concrete", new Color(0.33f, 0.33f, 0.31f));
        steel = Surface("Rusted_Steel", new Color(0.24f, 0.15f, 0.1f));
        roof = Surface("Roof_Metal", new Color(0.2f, 0.15f, 0.12f));
        asphalt = Surface("Asphalt", new Color(0.12f, 0.12f, 0.13f));

        glass = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        glass.SetColor("_BaseColor", new Color(0.32f, 0.38f, 0.45f));
        AssetDatabase.CreateAsset(glass, "Assets/Materials/Warehouse_Glass.mat");
    }

    static Material Surface(string name, Color colour)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", colour);
        material.SetFloat("_Smoothness", 0.1f);
        AssetDatabase.CreateAsset(material, $"Assets/Materials/Warehouse_{name}.mat");
        return material;
    }

    static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material,
        int layer = 0, bool solid = true, bool castsShadow = true, Quaternion? rotation = null)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = layer;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localRotation = rotation ?? Quaternion.identity;
        box.transform.localScale = size;

        var renderer = box.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        if (!castsShadow) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (!solid) Object.DestroyImmediate(box.GetComponent<Collider>());
        return box;
    }

    static GameObject Slab(string name, Transform parent, float x0, float x1, float z0, float z1, float top, float thickness, Material material, int layer = 0)
    {
        var centre = new Vector3((x0 + x1) / 2f, top - thickness / 2f, (z0 + z1) / 2f);
        return Box(name, parent, centre, new Vector3(x1 - x0, thickness, z1 - z0), material, layer);
    }

    static void Crate(string name, Transform parent, float x, float z, float floorY = 0f)
    {
        Box(name, parent, new Vector3(x, floorY + 1f, z), new Vector3(2f, 2f, 2f), wood, blocker);
    }

    // --- Shell ---

    struct Opening
    {
        public float From, To, Bottom, Top;
        public Opening(float from, float to, float bottom, float top) { From = from; To = to; Bottom = bottom; Top = top; }
    }

    static void BuildShell(Transform hall)
    {
        Slab("Floor", hall, -Thick, Width + Thick, -Thick, Length + Thick, 0f, 0.2f, concrete);

        Transform walls = Group("Walls", hall);
        var door = new[] { new Opening(13f, 17f, 0f, 3.5f) };
        var leftOpenings = new[]
        {
            new Opening(6f, 9f, Deck + 0.5f, Deck + 2.7f),
            new Opening(20.2f, 24.2f, 2f, 6f),
            new Opening(32.7f, 36.7f, 2f, 6f),
        };

        Wall("Wall_Left", walls, true, -Thick / 2f, -Thick, Length + Thick, leftOpenings);
        Wall("Wall_Right", walls, true, Width + Thick / 2f, -Thick, Length + Thick, new Opening[0]);
        Wall("Wall_Front", walls, false, -Thick / 2f, 0f, Width, door);
        Wall("Wall_Back", walls, false, Length + Thick / 2f, 0f, Width, door);

        Mesh gable = GableMesh();
        Gable("Gable_Front", walls, -Thick / 2f, gable);
        Gable("Gable_Back", walls, Length + Thick / 2f, gable);

        Box("Back_Door", walls, new Vector3(15f, 1.75f, Length + Thick / 2f), new Vector3(4f, 3.5f, 0.2f), steel, blocker);
        WindowFrame("W1", walls, 34.7f, 4f, 2f, 6f, true);
        WindowFrame("W2", walls, 22.2f, 4f, 2f, 6f, true);
        WindowFrame("Escape_Window", walls, 7.5f, 3f, Deck + 0.5f, Deck + 2.7f, false);
    }

    static void Wall(string name, Transform parent, bool alongZ, float line, float from, float to, Opening[] openings)
    {
        Transform wall = Group(name, parent);
        float cursor = from;
        int piece = 1;

        void Piece(float a0, float a1, float y0, float y1)
        {
            if (a1 - a0 <= 0f || y1 - y0 <= 0f) return;
            float a = (a0 + a1) / 2f;
            float y = (y0 + y1) / 2f;
            Vector3 position = alongZ ? new Vector3(line, y, a) : new Vector3(a, y, line);
            Vector3 size = alongZ ? new Vector3(Thick, y1 - y0, a1 - a0) : new Vector3(a1 - a0, y1 - y0, Thick);
            Box($"Piece_{piece++:00}", wall, position, size, brick, blocker);
        }

        foreach (var opening in openings.OrderBy(o => o.From))
        {
            Piece(cursor, opening.From, 0f, Eave);
            Piece(opening.From, opening.To, 0f, opening.Bottom);
            Piece(opening.From, opening.To, opening.Top, Eave);
            cursor = opening.To;
        }
        Piece(cursor, to, 0f, Eave);
    }

    static Mesh GableMesh()
    {
        float half = Width / 2f;
        float t = Thick / 2f;
        var mesh = new Mesh { name = "Warehouse_Gable" };
        mesh.vertices = new[]
        {
            new Vector3(-half, 0f, -t), new Vector3(0f, Rise, -t), new Vector3(half, 0f, -t),
            new Vector3(half, 0f, t), new Vector3(0f, Rise, t), new Vector3(-half, 0f, t),
        };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.forward, Vector3.forward, Vector3.forward };
        mesh.uv = new Vector2[6];
        mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{SceneFolder}/Gable.asset");
        return mesh;
    }

    static void Gable(string name, Transform parent, float z, Mesh mesh)
    {
        var gable = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = blocker };
        gable.transform.SetParent(parent, false);
        gable.transform.localPosition = new Vector3(Width / 2f, Eave, z);
        gable.GetComponent<MeshFilter>().sharedMesh = mesh;
        gable.GetComponent<MeshRenderer>().sharedMaterial = brick;
    }

    static void WindowFrame(string name, Transform parent, float z, float width, float bottom, float top, bool glazed)
    {
        Transform frame = Group(name, parent);
        float height = top - bottom;
        float x = -Thick / 2f;
        float middleY = (bottom + top) / 2f;

        if (glazed)
            Box("Glass", frame, new Vector3(x, middleY, z), new Vector3(0.04f, height, width), glass, 0, true, false);

        foreach (float offset in Spaced(width, glazed ? 1f : width))
            Box("Bar_Upright", frame, new Vector3(x, middleY, z - width / 2f + offset), new Vector3(0.08f, height, 0.06f), steel, 0, false);

        foreach (float offset in Spaced(height, glazed ? 1f : height))
            Box("Bar_Across", frame, new Vector3(x, bottom + offset, z), new Vector3(0.08f, 0.06f, width), steel, 0, false);
    }

    static float[] Spaced(float length, float step)
    {
        int count = Mathf.RoundToInt(length / step);
        return Enumerable.Range(0, count + 1).Select(i => length * i / count).ToArray();
    }

    // --- Roof ---

    static float RoofAngle => Mathf.Atan2(Rise, Width / 2f) * Mathf.Rad2Deg;

    static float RoofHeight(float x) => Eave + Rise * (1f - Mathf.Abs(x - Width / 2f) / (Width / 2f));

    static void BuildRoof(Transform hall)
    {
        Transform top = Group("Roof", hall);
        float slope = Mathf.Sqrt(Width * Width / 4f + Rise * Rise);
        float angle = RoofAngle;
        float radians = angle * Mathf.Deg2Rad;
        const float thickness = 0.2f;
        var size = new Vector3(slope + 0.8f, thickness, Length + Thick * 2f);
        float midY = Eave + Rise / 2f;
        float liftX = Mathf.Sin(radians) * thickness / 2f;
        float liftY = Mathf.Cos(radians) * thickness / 2f;

        Box("Slope_Left", top, new Vector3(Width / 4f - liftX, midY + liftY, Length / 2f), size, roof, blocker, false, true, Quaternion.Euler(0f, 0f, angle));
        Box("Slope_Right", top, new Vector3(Width * 0.75f + liftX, midY + liftY, Length / 2f), size, roof, blocker, false, true, Quaternion.Euler(0f, 0f, -angle));

        Transform trusses = Group("Trusses", top);
        for (int z = 5; z < Length; z += 5) Truss(trusses, z, slope, angle);

        Transform lights = Group("Skylights", top);
        for (int i = 0; i < Skylights.Length; i++) Skylight($"S{i + 1}", lights, Skylights[i], angle);
    }

    static void Truss(Transform parent, float z, float slope, float angle)
    {
        Transform truss = Group($"Truss_Z{z:00}", parent);
        const float beam = 0.3f;
        float midY = Eave + Rise / 2f - 0.18f;

        Box("Bottom_Chord", truss, new Vector3(Width / 2f, Eave, z), new Vector3(Width, beam, beam), steel, 0, false, false);
        Box("Top_Left", truss, new Vector3(Width / 4f, midY, z), new Vector3(slope, beam, beam), steel, 0, false, false, Quaternion.Euler(0f, 0f, angle));
        Box("Top_Right", truss, new Vector3(Width * 0.75f, midY, z), new Vector3(slope, beam, beam), steel, 0, false, false, Quaternion.Euler(0f, 0f, -angle));
        Box("King_Post", truss, new Vector3(Width / 2f, Eave + Rise / 2f, z), new Vector3(beam, Rise, beam), steel, 0, false, false);

        foreach (float x in new[] { Width / 4f, Width * 0.75f })
            Box("Post", truss, new Vector3(x, Eave + Rise / 4f, z), new Vector3(0.2f, Rise / 2f, 0.2f), steel, 0, false, false);
    }

    static void Skylight(string name, Transform parent, Vector2 place, float angle)
    {
        var light = Group(name, parent);
        light.localPosition = new Vector3(place.x, RoofHeight(place.x) - 0.06f, place.y);
        light.localRotation = Quaternion.Euler(0f, 0f, place.x < Width / 2f ? angle : -angle);

        Box("Glass", light, Vector3.zero, new Vector3(3f, 0.03f, 3f), glass, 0, false, false);
        Box("Mullion_X", light, Vector3.down * 0.05f, new Vector3(3f, 0.08f, 0.08f), steel, 0, false, false);
        Box("Mullion_Z", light, Vector3.down * 0.05f, new Vector3(0.08f, 0.08f, 3f), steel, 0, false, false);
    }

    // --- Upper level ---

    static void BuildUpperLevel(Transform hall, GameObject hidingSpot)
    {
        Transform upper = Group("Upper Level", hall);
        BuildStairs(upper);
        BuildCatwalk(upper);
        BuildSetPiece(upper);
        PlaceHideCrate(upper, hidingSpot);
    }

    // The foot moved to Z 36.5 from the spec's 38, to fit a landing at the top that opens onto the catwalk.
    static void BuildStairs(Transform parent)
    {
        Transform stairs = Group("Stairs", parent);
        const float x0 = 25.5f, x1 = 28.5f, foot = 36.5f, top = 48.5f;
        const int count = 40;
        float run = (top - foot) / count;
        float rise = Deck / count;
        float width = x1 - x0;
        float middleX = (x0 + x1) / 2f;

        for (int i = 0; i < count; i++)
        {
            float tread = rise * i + 0.1f;
            Box($"Step_{i + 1:00}", stairs, new Vector3(middleX, tread - rise / 2f, foot + run * (i + 0.5f)), new Vector3(width, rise, run), steel, 0, false);
        }

        // One smooth collider under the steps, so the player and the NavMesh treat the stairs as a ramp.
        float angle = Mathf.Atan2(Deck, top - foot) * Mathf.Rad2Deg;
        float length = Mathf.Sqrt((top - foot) * (top - foot) + Deck * Deck);
        const float rampThickness = 0.2f;
        float radians = angle * Mathf.Deg2Rad;
        var ramp = new GameObject("Ramp_Collider").transform;
        ramp.SetParent(stairs, false);
        ramp.localPosition = new Vector3(middleX, Deck / 2f - Mathf.Cos(radians) * rampThickness / 2f, (foot + top) / 2f + Mathf.Sin(radians) * rampThickness / 2f);
        ramp.localRotation = Quaternion.Euler(-angle, 0f, 0f);
        ramp.gameObject.AddComponent<BoxCollider>().size = new Vector3(width, rampThickness, length);

        Slab("Landing", stairs, x0, x1, top, Length, Deck, 0.2f, steel);

        Rail("Rail_Left", stairs, new Vector3(x0 + 0.05f, 0f, foot), new Vector3(x0 + 0.05f, Deck, top));
        Rail("Rail_Right", stairs, new Vector3(x1 - 0.05f, 0f, foot), new Vector3(x1 - 0.05f, Deck, top));
        Rail("Rail_Landing", stairs, new Vector3(x0 + 0.05f, Deck, top), new Vector3(x0 + 0.05f, Deck, Length));
    }

    static void BuildCatwalk(Transform parent)
    {
        Transform catwalk = Group("Catwalk", parent);
        Slab("Deck_Right", catwalk, 28.5f, Width, 0f, Length, Deck, 0.2f, steel);
        Slab("Deck_Front", catwalk, 10f, 28.5f, 0f, 1.5f, Deck, 0.2f, steel);
        Slab("Hide_Crate_Landing", catwalk, 26.5f, 28.5f, HideCrateZ - 1f, HideCrateZ + 1f, Deck, 0.2f, steel);

        BuildRailsAroundHideCrate(catwalk);
        Rail("Rail_Front", catwalk, new Vector3(10f, Deck, 1.5f), new Vector3(28.5f, Deck, 1.5f));

        Transform columns = Group("Columns", catwalk);
        foreach (float z in new[] { 1.3f, 7f, 13f, 19f, 25f, 31f, 37f, 43f, 49f })
            Column(columns, 28.7f, z);
        foreach (float x in new[] { 11.5f, 18f, 24f })
            Column(columns, x, 1.3f);
        Column(columns, 26.7f, HideCrateZ);
    }

    static void BuildRailsAroundHideCrate(Transform catwalk)
    {
        Rail("Rail_Right_Near", catwalk, new Vector3(28.5f, Deck, 1.5f), new Vector3(28.5f, Deck, HideCrateZ - 1f));
        Rail("Rail_Right_Far", catwalk, new Vector3(28.5f, Deck, HideCrateZ + 1f), new Vector3(28.5f, Deck, 48.5f));
    }

    static void BuildSetPiece(Transform parent)
    {
        Transform piece = Group("Set Piece", parent);
        Slab("Deck", piece, 0f, 10f, 0f, 15f, Deck, 0.3f, concrete, blocker);

        Rail("Rail_East", piece, new Vector3(10f, Deck, 1.5f), new Vector3(10f, Deck, 15f));
        Rail("Rail_North", piece, new Vector3(0f, Deck, 15f), new Vector3(10f, Deck, 15f));

        Crate("Crate_A", piece, 2.1f, 11.3f, Deck);
        Crate("Crate_B", piece, 5.4f, 11.7f, Deck);
        Crate("Crate_C", piece, 7.4f, 9.3f, Deck);
        Crate("Crate_D", piece, 2.1f, 3.7f, Deck);

        Transform columns = Group("Columns", piece);
        Column(columns, 9.85f, 5f);
        Column(columns, 9.85f, 10f);
        Column(columns, 9.85f, 14.85f);
        Column(columns, 5f, 14.85f);
    }

    static void Column(Transform parent, float x, float z)
    {
        Box("Column", parent, new Vector3(x, (Deck - 0.2f) / 2f, z), new Vector3(0.3f, Deck - 0.2f, 0.3f), steel);
    }

    static void Rail(string name, Transform parent, Vector3 from, Vector3 to)
    {
        var rail = new GameObject(name).transform;
        rail.SetParent(parent, false);
        rail.localPosition = from;
        rail.localRotation = Quaternion.LookRotation(to - from, Vector3.up);

        float length = Vector3.Distance(from, to);
        var blocking = rail.gameObject.AddComponent<BoxCollider>();
        blocking.center = new Vector3(0f, RailHeight / 2f, length / 2f);
        blocking.size = new Vector3(0.1f, RailHeight, length);

        foreach (float height in new[] { RailHeight, RailHeight / 2f })
            Box("Bar", rail, new Vector3(0f, height, length / 2f), new Vector3(0.06f, 0.06f, length), steel, 0, false, false);

        foreach (float offset in Spaced(length, 1.5f))
            Box("Post", rail, new Vector3(0f, RailHeight / 2f, offset), new Vector3(0.05f, RailHeight, 0.05f), steel, 0, false, false);
    }

    static void PlaceHideCrate(Transform parent, GameObject prefab)
    {
        var spot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        spot.name = "Hide_Crate";
        spot.transform.SetLocalPositionAndRotation(new Vector3(27.5f, Deck, HideCrateZ), Quaternion.Euler(0f, 90f, 0f));
        PrefabUtility.RecordPrefabInstancePropertyModifications(spot);
        PrefabUtility.RecordPrefabInstancePropertyModifications(spot.transform);

        Transform body = spot.transform.Find("Body");
        body.localPosition = Vector3.up;
        body.localScale = new Vector3(2f, 2f, 2f);
        body.GetComponent<MeshRenderer>().sharedMaterial = wood;
        PrefabUtility.RecordPrefabInstancePropertyModifications(body);
        PrefabUtility.RecordPrefabInstancePropertyModifications(body.GetComponent<MeshRenderer>());

        Transform door = spot.transform.Find("Door");
        door.localPosition = new Vector3(0f, 1f, 1.01f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(door);

        Transform exit = spot.transform.Find("Exit Point");
        exit.localPosition = new Vector3(0f, 0.05f, 1.75f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(exit);

        var serialized = new SerializedObject(spot.GetComponent<HidingSpot>());
        serialized.FindProperty("reach").floatValue = 2.4f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // --- Ground floor ---

    static void BuildGroundFloor(Transform hall)
    {
        Transform crates = Group("Crates", hall);
        for (int i = 0; i < FloorCrates.Length; i++)
            Crate($"Crate_{i + 1:00}", crates, FloorCrates[i].x, FloorCrates[i].y);

        Transform pile = Group("Back Door Pile", hall);
        Crate("Crate_1", pile, 13.7f, 48.3f);
        Crate("Crate_2", pile, 16.3f, 48.3f);
        Crate("Crate_3", pile, 15f, 46f);

        // Monsters stop at the front door. Baked into the NavMesh, so the door itself can stay open.
        var doorway = new GameObject("Door_No_Monsters");
        doorway.transform.SetParent(hall, false);
        doorway.transform.localPosition = new Vector3(15f, 2f, -0.2f);
        var volume = doorway.AddComponent<NavMeshModifierVolume>();
        volume.size = new Vector3(5f, 5f, 3f);
        volume.area = NotWalkableArea;
    }

    // --- Lighting ---

    static void BuildLighting(Scene scene, GameObject lamp)
    {
        Transform lighting = NewRoot(scene, "Light Pools").transform;
        Texture2D cookie = MakeSkylightCookie();

        for (int i = 0; i < Skylights.Length; i++) SkylightPool($"S{i + 1}", lighting, Skylights[i], cookie);

        WindowPool("W1", lighting, 34.7f);
        WindowPool("W2", lighting, 22.2f);

        HangingLamp("LightPool_Front_Door", lighting, lamp, new Vector3(15f, 4f, 5.8f), 3f, 85f);
        HangingLamp("LightPool_Front_Right", lighting, lamp, new Vector3(25f, 4f, 3.3f), 2.7f, 79f);
        HangingLamp("LightPool_Set_Piece", lighting, lamp, new Vector3(5f, Deck + 3f, 7.5f), 3.3f, 107f);

        Transform posts = Root(scene, "Lamp Posts").transform;
        Transform street = posts.GetChild(0);
        street.name = "Street_Lamp";
        street.position = new Vector3(16.5f, 0f, -6.5f);
        for (int i = posts.childCount - 1; i > 0; i--) Object.DestroyImmediate(posts.GetChild(i).gameObject);
    }

    // The light shines through this square, so its pool on the floor is square too.
    static Texture2D MakeSkylightCookie()
    {
        const int size = 128;
        const float squareFraction = 0.59f;
        float centre = (size - 1) / 2f;
        float half = size / 2f * squareFraction;

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool lit = Mathf.Abs(x - centre) <= half && Mathf.Abs(y - centre) <= half;
                texture.SetPixel(x, y, lit ? Color.white : Color.clear);
            }
        }

        File.WriteAllBytes(CookiePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(CookiePath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(CookiePath);
        importer.textureType = TextureImporterType.Cookie;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(CookiePath);
    }

    static void SkylightPool(string id, Transform parent, Vector2 place, Texture2D cookie)
    {
        var position = new Vector3(place.x, 4f, place.y);
        GameObject pool = Pool($"LightPool_{id}", parent, position, Quaternion.identity, LightPoolShape.Square, 3f);

        Light light = Moonlight($"{id}_Moonlight", pool.transform, new Vector3(place.x, 14f, place.y), Quaternion.Euler(90f, 0f, 0f), 300f);
        light.spotAngle = 40f;
        light.innerSpotAngle = 36f;
        light.cookie = cookie;
    }

    static void WindowPool(string id, Transform parent, float z)
    {
        GameObject pool = Pool($"LightPool_{id}", parent, new Vector3(0f, 4f, z), Quaternion.Euler(0f, 90f, 0f), LightPoolShape.Wedge, 3f);

        // Placed outside so the real window opening shapes the light into the widening pool.
        var source = new Vector3(-8f, 14f, z);
        var target = new Vector3(3f, 0f, z);
        Light light = Moonlight($"{id}_Moonlight", pool.transform, source, Quaternion.LookRotation(target - source), 250f);
        light.spotAngle = 40f;
        light.innerSpotAngle = 30f;
    }

    static Light Moonlight(string name, Transform parent, Vector3 position, Quaternion rotation, float intensity)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.transform.SetPositionAndRotation(position, rotation);

        var light = holder.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = new Color(0.72f, 0.8f, 1f);
        light.intensity = intensity;
        light.range = 32f;
        light.shadows = LightShadows.Hard;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        return light;
    }

    static GameObject Pool(string name, Transform parent, Vector3 position, Quaternion rotation, LightPoolShape shape, float radius)
    {
        var pool = new GameObject(name);
        pool.transform.SetParent(parent, false);
        pool.transform.SetPositionAndRotation(position, rotation);

        var volume = pool.AddComponent<NavMeshModifierVolume>();
        volume.center = new Vector3(0f, -1.85f, 0f);
        volume.size = new Vector3(1f, 5f, 1f);
        volume.area = LitArea;

        var zone = pool.AddComponent<LampZone>();
        var serialized = new SerializedObject(zone);
        serialized.FindProperty("shape").enumValueIndex = (int)shape;
        serialized.FindProperty("radius").floatValue = radius;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        zone.SyncNavMeshVolume();
        return pool;
    }

    static void HangingLamp(string name, Transform parent, GameObject prefab, Vector3 position, float radius, float spotAngle)
    {
        var lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        lamp.name = name;
        lamp.transform.position = position;
        PrefabUtility.RecordPrefabInstancePropertyModifications(lamp);
        PrefabUtility.RecordPrefabInstancePropertyModifications(lamp.transform);

        var zone = lamp.GetComponent<LampZone>();
        var serialized = new SerializedObject(zone);
        serialized.FindProperty("radius").floatValue = radius;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        zone.SyncNavMeshVolume();
        PrefabUtility.RecordPrefabInstancePropertyModifications(lamp.GetComponent<NavMeshModifierVolume>());

        var light = lamp.transform.Find("Spot Light").GetComponent<Light>();
        light.spotAngle = spotAngle;
        PrefabUtility.RecordPrefabInstancePropertyModifications(light);

        float chainTop = RoofHeight(position.x);
        float chainLength = chainTop - position.y;
        Box("Chain", parent, new Vector3(position.x, position.y + chainLength / 2f, position.z), new Vector3(0.04f, chainLength, 0.04f), steel, 0, false, false);
    }

    // --- Outside ---

    static void BuildStreet(Scene scene)
    {
        Transform street = NewRoot(scene, "Street").transform;
        const float left = -10f, right = 40f, far = -16f, wall = -Thick;

        Slab("Pavement", street, left, right, -4f, wall, 0f, 0.2f, concrete);
        Slab("Road", street, left, right, far, -4f, 0f, 0.2f, asphalt);

        Transform car = Group("Car", street);
        Box("Body", car, new Vector3(4f, 0.55f, -6f), new Vector3(6f, 1.1f, 2.6f), steel, blocker);
        Box("Cabin", car, new Vector3(4f, 1.4f, -6f), new Vector3(3f, 0.6f, 2.3f), glass, blocker, true, false);

        Transform bounds = Group("Bounds", street);
        InvisibleWall("Left", bounds, new Vector3(left - 0.1f, 3f, (far + wall) / 2f), new Vector3(0.2f, 6f, wall - far));
        InvisibleWall("Right", bounds, new Vector3(right + 0.1f, 3f, (far + wall) / 2f), new Vector3(0.2f, 6f, wall - far));
        InvisibleWall("Far", bounds, new Vector3((left + right) / 2f, 3f, far - 0.1f), new Vector3(right - left, 6f, 0.2f));
        InvisibleWall("Near_Left", bounds, new Vector3((left + wall) / 2f, 3f, wall + 0.1f), new Vector3(wall - left, 6f, 0.2f));
        float rightEdge = Width + Thick;
        InvisibleWall("Near_Right", bounds, new Vector3((rightEdge + right) / 2f, 3f, wall + 0.1f), new Vector3(right - rightEdge, 6f, 0.2f));
    }

    static void InvisibleWall(string name, Transform parent, Vector3 centre, Vector3 size)
    {
        var wall = new GameObject(name);
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = centre;
        wall.AddComponent<BoxCollider>().size = size;
    }

    static void MovePlayerToStart(Scene scene)
    {
        Transform player = Root(scene, "Player").transform;
        Vector3 move = PlayerStart - player.position;

        player.position += move;
        Root(scene, "Main Camera").transform.position += move;
        Root(scene, "Player Follow Camera").transform.position += move;

        Transform checkpoint = Root(scene, "Checkpoints").transform.Find("Checkpoint_01");
        checkpoint.position = PlayerStart;
        PrefabUtility.RecordPrefabInstancePropertyModifications(checkpoint);
    }

    // The platform and end point are both tagged TestOnly, so they are easy to find and remove together.
    static void BuildTestPlatform(Scene scene)
    {
        var platform = Slab("Test Platform", null, -4.4f, -Thick, 5.5f, 9.5f, 7.5f, 0.2f, concrete);
        SceneManager.MoveGameObjectToScene(platform, scene);
        platform.tag = TestOnlyTag;

        var end = new GameObject("End Point");
        SceneManager.MoveGameObjectToScene(end, scene);
        end.transform.position = new Vector3(-2.4f, 7.55f, 7.5f);
        end.tag = TestOnlyTag;

        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Marker";
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.transform.SetParent(end.transform, false);
        marker.transform.localPosition = Vector3.up * -0.03f;
        marker.transform.localScale = new Vector3(4f, 0.01f, 4f);
        marker.GetComponent<MeshRenderer>().sharedMaterial = glow;

        var serialized = new SerializedObject(end.AddComponent<EndPoint>());
        serialized.FindProperty("player").objectReferenceValue = Root(scene, "Player").GetComponent<PlayerController>();
        serialized.FindProperty("finishedMessage").objectReferenceValue = Root(scene, "HUD").transform.Find("Finished Message").gameObject;
        serialized.FindProperty("radius").floatValue = 2.5f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // --- Monsters ---

    static void BuildMonsters(Scene scene)
    {
        Scene corridor = EditorSceneManager.OpenScene(CorridorPath, OpenSceneMode.Additive);
        GameObject source = Root(corridor, "Monster_01");
        Transform monsters = NewRoot(scene, "Monsters").transform;
        GameObject player = Root(scene, "Player");

        Monster("M1", source, monsters, player, new Vector3(20f, 0f, 30f), 0f,
            new Vector3(20f, 0f, 30f), new Vector3(13f, 0f, 31f), new Vector3(12f, 0f, 22f),
            new Vector3(16f, 0f, 18f), new Vector3(21f, 0f, 21f), new Vector3(25.5f, 0f, 28f));
        Monster("M2", source, monsters, player, new Vector3(7f, 0f, 21f), 0f,
            new Vector3(7f, 0f, 21f), new Vector3(7f, 0f, 29f), new Vector3(10f, 0f, 31f),
            new Vector3(11f, 0f, 24f), new Vector3(10f, 0f, 14f), new Vector3(8f, 0f, 12f));
        Monster("M3", source, monsters, player, new Vector3(29.25f, Deck, 23.3f), 3f,
            new Vector3(29.25f, Deck, 44f), new Vector3(29.25f, Deck, 4f));
        Monster("M4", source, monsters, player, new Vector3(9.3f, Deck, 8.3f), 0f,
            new Vector3(9.3f, Deck, 2.3f), new Vector3(9.3f, Deck, 14.2f), new Vector3(0.8f, Deck, 14.2f));

        EditorSceneManager.CloseScene(corridor, true);
    }

    static void Monster(string id, GameObject source, Transform parent, GameObject player, Vector3 start, float routeEndPause, params Vector3[] route)
    {
        var monster = Object.Instantiate(source, parent);
        monster.name = id;
        monster.transform.SetPositionAndRotation(start, Quaternion.identity);

        Transform routeGroup = Group($"{id} Route", parent);
        var enemy = new SerializedObject(monster.GetComponent<Enemy>());
        enemy.FindProperty("player").objectReferenceValue = player.GetComponent<PlayerController>();
        enemy.FindProperty("visibility").objectReferenceValue = player.GetComponent<PlayerVisibility>();
        enemy.FindProperty("lives").objectReferenceValue = player.GetComponent<PlayerLives>();
        enemy.FindProperty("routeEndPause").floatValue = routeEndPause;

        var points = enemy.FindProperty("patrolRoute");
        points.arraySize = route.Length;
        for (int i = 0; i < route.Length; i++)
        {
            var point = new GameObject($"Point_{i + 1:00}").transform;
            point.SetParent(routeGroup, false);
            point.position = route[i];
            points.GetArrayElementAtIndex(i).objectReferenceValue = point;
        }
        enemy.ApplyModifiedPropertiesWithoutUndo();
    }

    // --- NavMesh ---

    static void BakeNavMesh(Scene scene)
    {
        var surface = Root(scene, "Nav Surface").GetComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Volume;
        surface.center = new Vector3(Width / 2f, 5.5f, Length / 2f);
        surface.size = new Vector3(Width + 2f, 13f, Length + 2f);
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

        Physics.SyncTransforms();
        surface.BuildNavMesh();

        string path = $"{SceneFolder}/NavMesh.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(surface.navMeshData, path);
        EditorUtility.SetDirty(surface);
    }
}
