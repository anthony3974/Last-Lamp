using System.Collections.Generic;
using System.Linq;
using LastLamp;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: builds Rowhouse_01, the two-storey rowhouse joined to the warehouse's left wall, and joins it to the
/// warehouse. The player drops from the warehouse's escape window onto its flat roof, where the old test platform was,
/// goes down through a roof opening to the first floor, crosses it, goes down a second stair to the ground floor, and
/// crosses that to the back door, where the level ends.
///
/// Same world coordinates as the warehouse: the rowhouse's inside runs X -20 to -0.8 and Z 0 to 17, its walls 0.4 thick,
/// its right wall's outer face against the warehouse's left wall at X -0.4. Floors at 0 and 3.75, the roof at 7.5.
///
/// In Warehouse_01 it removes the test platform and end point, adds the Area Link that loads the rowhouse from the
/// upper level's front, narrows the warehouse's area so it stops at its own left wall, and makes the front door close
/// behind the player. Delete once checked.
/// </summary>
public static class RowhouseBuilder
{
    const string ScenePath = "Assets/Scenes/Rowhouse_01.unity";
    const string SceneFolder = "Assets/Scenes/Rowhouse_01";
    const string WarehousePath = "Assets/Scenes/Warehouse_01.unity";
    const string SceneName = "Rowhouse_01";

    const float Left = -20f, Right = -0.8f, Front = 0f, Back = 17f, Thick = 0.4f;
    const float Ground = 0f, First = 3.75f, Roof = 7.5f, SlabThickness = 0.2f;
    const float ParapetTop = Roof + 1.1f;
    const float RailHeight = 1.1f;
    const float DoorTop = 2.6f;

    // Stair A, roof down to the first floor, against the left wall. Stair B, first floor down to the ground, against the back wall.
    const float StairAx0 = Left, StairAx1 = -17.8f, StairAFoot = 9.4f, StairATop = 15f;
    const float StairBz0 = 14.8f, StairBz1 = Back, StairBFoot = -2.8f, StairBTop = -8.4f;

    static Material brick, concrete, steel, roofMetal, glass, glow;
    static int blocker;
    static GameObject lampPrefab, checkpointPrefab, hidingSpotPrefab, canPrefab, wickPrefab;

    [MenuItem("Tools/Build Rowhouse")]
    public static void Run()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            Debug.LogError($"Build Rowhouse: {ScenePath} already exists, so this has already been run.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        brick = Material("Assets/Materials/Warehouse_Brick.mat");
        concrete = Material("Assets/Materials/Warehouse_Concrete.mat");
        steel = Material("Assets/Materials/Warehouse_Rusted_Steel.mat");
        roofMetal = Material("Assets/Materials/Warehouse_Roof_Metal.mat");
        glass = Material("Assets/Materials/Warehouse_Glass.mat");
        glow = Material("Assets/Materials/Lamp_Glow.mat");
        lampPrefab = Prefab("Assets/Prefabs/Lamp.prefab");
        checkpointPrefab = Prefab("Assets/Prefabs/Checkpoint.prefab");
        hidingSpotPrefab = Prefab("Assets/Prefabs/HidingSpot.prefab");
        canPrefab = Prefab("Assets/Prefabs/Can.prefab");
        wickPrefab = Prefab("Assets/Prefabs/Wick.prefab");
        blocker = LayerMask.NameToLayer("VisionBlocker");

        // A copy of the warehouse, so the sky, ambient light and NavMesh settings match exactly. Its own objects go,
        // after a crate, a monster and the end point are copied from them.
        if (!AssetDatabase.CopyAsset(WarehousePath, ScenePath))
        {
            Debug.LogError("Build Rowhouse: couldn't copy the warehouse scene.");
            return;
        }
        if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets/Scenes", SceneName);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        BuildRowhouse(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        CheckRoutes(scene);

        JoinWarehouse();
        AddToBuildList();
        AssetDatabase.SaveAssets();

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Debug.Log("Build Rowhouse: finished.");
    }

    static Material Material(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);
    static GameObject Prefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
    static GameObject Root(Scene scene, string name) => scene.GetRootGameObjects().First(root => root.name == name);

    static void Set(Object target, string property, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(property).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetFloat(Object target, string property, float value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(property).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // --- The rowhouse ---

    static void BuildRowhouse(Scene scene)
    {
        // Templates from the warehouse copy.
        GameObject crateTemplate = Root(scene, "Warehouse").transform.Find("Crates/Crate_01").gameObject;
        GameObject monsterTemplate = Root(scene, "Monsters").transform.Find("M1").gameObject;
        GameObject endTemplate = Root(scene, "End Point");
        GameObject level = Root(scene, "Level");
        GameObject navSurface = Root(scene, "Nav Surface");
        var keep = new HashSet<GameObject> { level, navSurface };

        var house = NewRoot("Rowhouse").transform;
        BuildShell(house);
        BuildFloors(house);
        BuildStairs(house);
        BuildRoof(house);
        BuildFirstFloor(house, crateTemplate);
        BuildGroundFloor(house, crateTemplate);

        BuildLights();
        Checkpoint first = BuildCheckpoints();
        BuildHidingSpots();
        BuildCans();
        BuildMonsters(monsterTemplate);
        BuildEndPoint(endTemplate);
        BuildAreaLink();
        SetUpLevel(level, first);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (keep.Contains(root) || NewRoots.Contains(root)) continue;
            Object.DestroyImmediate(root);
        }
        Root(scene, "End Point (new)").name = "End Point";

        BakeNavMesh(navSurface.GetComponent<NavMeshSurface>());
    }

    // Everything this tool adds at the root. The rest of the warehouse copy goes.
    static readonly HashSet<GameObject> NewRoots = new HashSet<GameObject>();

    static GameObject NewRoot(string name)
    {
        var root = new GameObject(name);
        NewRoots.Add(root);
        return root;
    }

    static Transform Group(string name, Transform parent)
    {
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        return group;
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

    static void InvisibleWall(string name, Transform parent, Vector3 min, Vector3 max)
    {
        var wall = new GameObject(name);
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = (min + max) / 2f;
        wall.AddComponent<BoxCollider>().size = max - min;
    }

    // --- Walls ---

    struct Opening
    {
        public float From, To, Bottom, Top;
        public bool Door;
        public Opening(float from, float to, float bottom, float top, bool door = false)
        {
            From = from; To = to; Bottom = bottom; Top = top; Door = door;
        }
    }

    // A wall along X (alongZ false) or Z (alongZ true) on the given line, from one end to the other, with gaps.
    static Transform Wall(string name, Transform parent, bool alongZ, float line, float from, float to, float bottom, float top,
        Material material, params Opening[] openings)
    {
        Transform wall = Group(name, parent);
        float cursor = from;
        int piece = 1;

        void Piece(float a0, float a1, float y0, float y1)
        {
            if (a1 - a0 <= 0.001f || y1 - y0 <= 0.001f) return;
            float a = (a0 + a1) / 2f;
            float y = (y0 + y1) / 2f;
            Vector3 position = alongZ ? new Vector3(line, y, a) : new Vector3(a, y, line);
            Vector3 size = alongZ ? new Vector3(Thick, y1 - y0, a1 - a0) : new Vector3(a1 - a0, y1 - y0, Thick);
            Box($"Piece_{piece++:00}", wall, position, size, material, blocker);
        }

        foreach (var opening in openings.OrderBy(o => o.From))
        {
            Piece(cursor, opening.From, bottom, top);
            Piece(opening.From, opening.To, bottom, opening.Bottom);
            Piece(opening.From, opening.To, opening.Top, top);
            cursor = opening.To;
        }
        Piece(cursor, to, bottom, top);
        return wall;
    }

    // Fills an outside opening: a window of the warehouse's dark glass with a cross of bars, or a shut steel door.
    static void Fill(Transform parent, bool alongZ, float line, Opening opening, int number)
    {
        float middle = (opening.From + opening.To) / 2f;
        float width = opening.To - opening.From;
        float height = opening.Top - opening.Bottom;
        float midY = (opening.Bottom + opening.Top) / 2f;
        Vector3 Point(float a, float y) => alongZ ? new Vector3(line, y, a) : new Vector3(a, y, line);
        Vector3 Size(float across, float up, float depth) => alongZ ? new Vector3(depth, up, across) : new Vector3(across, up, depth);

        if (opening.Door)
        {
            Box($"Door_{number:00}", parent, Point(middle, midY), Size(width, height, 0.2f), steel, blocker);
            return;
        }

        Transform window = Group($"Window_{number:00}", parent);
        Box("Glass", window, Point(middle, midY), Size(width, height, 0.04f), glass, 0, true, false);
        Box("Bar_Upright", window, Point(middle, midY), Size(0.06f, height, 0.08f), steel, 0, false);
        Box("Bar_Across", window, Point(middle, midY), Size(width, 0.06f, 0.08f), steel, 0, false);
    }

    // An outside wall in two bands, the ground floor's and everything above it, so each can have its own row of openings.
    static void OutsideWall(string name, Transform parent, bool alongZ, float line, float from, float to, float top,
        Opening[] ground, Opening[] upper)
    {
        Transform wall = Group(name, parent);
        Transform lower = Wall("Ground_Floor", wall, alongZ, line, from, to, Ground, First, brick, ground);
        Transform higher = Wall("Above", wall, alongZ, line, from, to, First, top, brick, upper);
        for (int i = 0; i < ground.Length; i++) Fill(lower, alongZ, line, ground[i], i + 1);
        for (int i = 0; i < upper.Length; i++) Fill(higher, alongZ, line, upper[i], i + 1);
    }

    static Opening[] Openings(params Opening[] openings) => openings;

    static void BuildShell(Transform house)
    {
        Transform walls = Group("Walls", house);
        float x0 = Left - Thick, x1 = Right + Thick, z0 = Front - Thick, z1 = Back + Thick;
        const float window = 1f, windowTop = 2.6f, upperWindow = First + 1.05f, upperWindowTop = First + 2.65f;

        // The street side, in line with the warehouse's front wall.
        OutsideWall("Wall_Front", walls, false, Front - Thick / 2f, x0, x1, ParapetTop,
            Openings(new Opening(-17.5f, -15f, window, windowTop), new Opening(-13f, -10.5f, window, windowTop),
                new Opening(-6.6f, -4.8f, Ground, DoorTop, true), new Opening(-3.5f, -1.5f, window, windowTop)),
            Openings(new Opening(-17.5f, -15f, upperWindow, upperWindowTop), new Opening(-13.5f, -11f, upperWindow, upperWindowTop),
                new Opening(-7.5f, -5f, upperWindow, upperWindowTop), new Opening(-3.5f, -1.5f, upperWindow, upperWindowTop)));

        // The back door is where the level ends.
        OutsideWall("Wall_Back", walls, false, Back + Thick / 2f, x0, x1, ParapetTop,
            Openings(new Opening(-19f, -16.6f, Ground, DoorTop, true), new Opening(-14f, -11.5f, window, windowTop)),
            Openings(new Opening(-12f, -9.5f, upperWindow, upperWindowTop)));

        OutsideWall("Wall_Left", walls, true, Left - Thick / 2f, z0, z1, ParapetTop,
            Openings(new Opening(2f, 4.5f, window, windowTop)),
            Openings(new Opening(3f, 5.5f, upperWindow, upperWindowTop)));

        // Against the warehouse, up to the roof. The warehouse's wall stands above it.
        OutsideWall("Wall_Right", walls, true, Right + Thick / 2f, z0, z1, Roof, Openings(), Openings());
    }

    static void BuildFloors(Transform house)
    {
        Transform floors = Group("Floors", house);
        Slab("Ground_Floor", floors, Left - Thick, Right + Thick, Front - Thick, Back + Thick, Ground, SlabThickness, concrete);

        // The first floor, open over stair B.
        Slab("First_Floor_Main", floors, Left, Right, Front, StairBz0, First, SlabThickness, concrete);
        Slab("First_Floor_Back_Left", floors, Left, StairBTop, StairBz0, Back, First, SlabThickness, concrete);
        Slab("First_Floor_Back_Right", floors, StairBFoot, Right, StairBz0, Back, First, SlabThickness, concrete);
    }

    // --- Stairs ---

    static void BuildStairs(Transform house)
    {
        Transform stairs = Group("Stairs", house);

        // Stair A: from the roof at the back down toward the front, against the left wall, landing in the front-left room.
        float aMiddle = (StairAx0 + StairAx1) / 2f;
        Transform a = Stair("Stair_A", stairs, new Vector3(aMiddle, First, StairAFoot), new Vector3(aMiddle, Roof, StairATop), StairAx1 - StairAx0);
        // Its right side on the first floor: a wall up to the roof, which also closes off the space under the steps.
        Wall("Stair_A_Wall", a, true, StairAx1 + Thick / 2f, StairAFoot, Back, First, Roof - SlabThickness, concrete);

        // Stair B: from the first floor in the middle of the back hall down toward the right, against the back wall.
        float bMiddle = (StairBz0 + StairBz1) / 2f;
        Transform b = Stair("Stair_B", stairs, new Vector3(StairBFoot, Ground, bMiddle), new Vector3(StairBTop, First, bMiddle), StairBz1 - StairBz0);
        Rail("Rail_Front", b, new Vector3(StairBTop, First, StairBz0 + 0.05f), new Vector3(StairBFoot, First, StairBz0 + 0.05f));
        Rail("Rail_End", b, new Vector3(StairBFoot - 0.05f, First, StairBz0), new Vector3(StairBFoot - 0.05f, First, Back));
        // On the ground floor, walls close off the space under the steps. The foot opens toward the front.
        Wall("Under_Front", b, false, StairBz0 - Thick / 2f, StairBTop - Thick, StairBFoot, Ground, First - SlabThickness, concrete);
        Wall("Under_End", b, true, StairBTop - Thick / 2f, StairBz0, Back, Ground, First - SlabThickness, concrete);
    }

    // Steps like the warehouse's, with one smooth collider under them, so the player and the NavMesh treat it as a ramp.
    static Transform Stair(string name, Transform parent, Vector3 foot, Vector3 top, float width)
    {
        Transform stair = Group(name, parent);
        Vector3 flat = top - foot;
        float rise = flat.y;
        flat.y = 0f;
        float run = flat.magnitude;
        Quaternion facing = Quaternion.LookRotation(flat);
        int count = Mathf.CeilToInt(rise / 0.18f);
        float stepRise = rise / count;
        float stepRun = run / count;

        for (int i = 0; i < count; i++)
        {
            float tread = foot.y + stepRise * i + 0.1f;
            Vector3 along = foot + flat.normalized * (stepRun * (i + 0.5f));
            Box($"Step_{i + 1:00}", stair, new Vector3(along.x, tread - stepRise / 2f, along.z), new Vector3(width, stepRise, stepRun), steel, 0, false, true, facing);
        }

        const float rampThickness = 0.2f;
        Quaternion slope = Quaternion.LookRotation(top - foot);
        var ramp = new GameObject("Ramp_Collider").transform;
        ramp.SetParent(stair, false);
        ramp.localRotation = slope;
        ramp.localPosition = (foot + top) / 2f - slope * Vector3.up * (rampThickness / 2f);
        ramp.gameObject.AddComponent<BoxCollider>().size = new Vector3(width, rampThickness, Vector3.Distance(foot, top));
        return stair;
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

        int posts = Mathf.Max(1, Mathf.RoundToInt(length / 1.5f));
        for (int i = 0; i <= posts; i++)
            Box("Post", rail, new Vector3(0f, RailHeight / 2f, length * i / posts), new Vector3(0.05f, RailHeight, 0.05f), steel, 0, false, false);
    }

    // --- Roof ---

    static void BuildRoof(Transform house)
    {
        Transform roof = Group("Roof", house);
        Slab("Roof_Main", roof, StairAx1, Right, Front, Back, Roof, SlabThickness, roofMetal);
        Slab("Roof_Front_Left", roof, Left, StairAx1, Front, StairAFoot, Roof, SlabThickness, roofMetal);
        Slab("Roof_Back_Left", roof, Left, StairAx1, StairATop, Back, Roof, SlabThickness, roofMetal);

        // Around the opening over stair A. Its back end is the way down.
        Rail("Rail_Opening_Side", roof, new Vector3(StairAx1 + 0.05f, Roof, StairAFoot), new Vector3(StairAx1 + 0.05f, Roof, StairATop));
        Rail("Rail_Opening_Front", roof, new Vector3(StairAx0, Roof, StairAFoot + 0.05f), new Vector3(StairAx1, Roof, StairAFoot + 0.05f));

        // The two houses' chimneys stand on their shared wall, and a vent on each.
        Box("Chimney_Front", roof, new Vector3(-10f, Roof + 1.1f, 1.0f), new Vector3(1.4f, 2.2f, 0.9f), brick, blocker);
        Box("Chimney_Back", roof, new Vector3(-10f, Roof + 1.1f, 16.2f), new Vector3(1.4f, 2.2f, 0.9f), brick, blocker);
        Box("Vent_Left", roof, new Vector3(-14.5f, Roof + 0.35f, 4.5f), new Vector3(0.9f, 0.7f, 0.9f), steel, blocker);
        Box("Vent_Right", roof, new Vector3(-6.5f, Roof + 0.35f, 13.5f), new Vector3(0.9f, 0.7f, 0.9f), steel, blocker);

        // Nobody can jump the parapet. Along the warehouse, only the escape window is open.
        Transform bounds = Group("Bounds", roof);
        float top = ParapetTop + 2f;
        InvisibleWall("Front", bounds, new Vector3(Left - Thick, ParapetTop, Front - Thick), new Vector3(Right + Thick, top, Front));
        InvisibleWall("Back", bounds, new Vector3(Left - Thick, ParapetTop, Back), new Vector3(Right + Thick, top, Back + Thick));
        InvisibleWall("Left", bounds, new Vector3(Left - Thick, ParapetTop, Front - Thick), new Vector3(Left, top, Back + Thick));
        InvisibleWall("Right_Front", bounds, new Vector3(Right, Roof, Front - Thick), new Vector3(Right + 0.2f, top, 5.6f));
        InvisibleWall("Right_Back", bounds, new Vector3(Right, Roof, 9.4f), new Vector3(Right + 0.2f, top, Back + Thick));
    }

    // --- First floor: two rooms at the front, a hall at the back ---

    static void BuildFirstFloor(Transform house, GameObject crate)
    {
        Transform floor = Group("First Floor", house);
        float top = Roof - SlabThickness;
        float doorTop = First + DoorTop;

        // Between the front rooms and the back hall.
        Wall("Wall_Hall", floor, false, 9.6f, StairAx1 + Thick, Right, First, top,
            concrete, new Opening(-15f, -13f, First, doorTop), new Opening(-7f, -5f, First, doorTop));
        // Between the two front rooms.
        Wall("Wall_Rooms", floor, true, -10f, Front, 9.4f, First, top, concrete, new Opening(3f, 5f, First, doorTop));

        Transform crates = Group("Crates", floor);
        Crate(crate, crates, "Crate_01", -17.6f, First, 1.3f);
        Crate(crate, crates, "Crate_02", -12.3f, First, 7.6f);
        Crate(crate, crates, "Crate_03", -7.6f, First, 1.3f);
        Crate(crate, crates, "Crate_04", -3.6f, First, 8.0f);
    }

    // --- Ground floor: an open workshop with shelves ---

    static void BuildGroundFloor(Transform house, GameObject crate)
    {
        Transform floor = Group("Ground Floor", house);

        Transform columns = Group("Columns", floor);
        foreach (float x in new[] { -15f, -10f, -5f })
        foreach (float z in new[] { 4.8f, 9.6f })
            Box("Column", columns, new Vector3(x, (First - SlabThickness) / 2f, z), new Vector3(0.3f, First - SlabThickness, 0.3f), steel);

        Transform shelves = Group("Shelves", floor);
        // Shelf_A splits off the strip along the back: the short way to the back door, and the one the monster walks.
        Shelf("Shelf_A", shelves, -16f, -9.5f, 11.6f, 12.4f);
        Shelf("Shelf_B", shelves, -14f, -11f, 2.6f, 3.4f);

        Transform crates = Group("Crates", floor);
        Crate(crate, crates, "Crate_01", -17.5f, Ground, 1.3f);
        Crate(crate, crates, "Crate_02", -12.4f, Ground, 6.6f);
        Crate(crate, crates, "Crate_03", -7.6f, Ground, 1.3f);
        Crate(crate, crates, "Crate_04", -15.6f, Ground, 15.9f);
    }

    static void Crate(GameObject template, Transform parent, string name, float x, float floorY, float z)
    {
        var copy = Object.Instantiate(template, parent);
        copy.name = name;
        copy.transform.position = new Vector3(x, floorY + template.transform.localScale.y / 2f, z);
        copy.transform.rotation = Quaternion.identity;
    }

    // Steel shelving, 2.4 m tall, with a panel down the middle so it can't be seen through. One collider blocks sight.
    static void Shelf(string name, Transform parent, float x0, float x1, float z0, float z1)
    {
        const float height = 2.4f;
        var shelf = new GameObject(name) { layer = blocker }.transform;
        shelf.SetParent(parent, false);
        shelf.localPosition = new Vector3((x0 + x1) / 2f, 0f, (z0 + z1) / 2f);
        float length = x1 - x0, depth = z1 - z0;

        var solid = shelf.gameObject.AddComponent<BoxCollider>();
        solid.center = new Vector3(0f, height / 2f, 0f);
        solid.size = new Vector3(length, height, depth);

        foreach (float y in new[] { 0.15f, 0.9f, 1.65f, 2.37f })
            Box("Board", shelf, new Vector3(0f, y, 0f), new Vector3(length, 0.05f, depth), steel, 0, false);
        foreach (float x in new[] { -length / 2f + 0.03f, length / 2f - 0.03f })
        foreach (float z in new[] { -depth / 2f + 0.03f, depth / 2f - 0.03f })
            Box("Post", shelf, new Vector3(x, height / 2f, z), new Vector3(0.06f, height, 0.06f), steel, 0, false);
        Box("Panel", shelf, new Vector3(0f, height / 2f, 0f), new Vector3(length, height, 0.03f), steel, 0, false);
    }

    // --- Lights ---

    static void BuildLights()
    {
        Transform lights = NewRoot("Light Pools").transform;
        float groundLamp = First - SlabThickness - 0.7f;
        float firstLamp = First + groundLamp;

        // The roof: a street lamp by the landing from the window, where the player starts.
        Transform post = Group("Lamp_Post", lights);
        post.position = new Vector3(-3.4f, Roof, 9.4f);
        Box("Pole", post, new Vector3(0f, 1.85f, 0f), new Vector3(0.15f, 3.7f, 0.15f), steel);
        Lamp("Lamp", post, new Vector3(-3.4f, Roof + 3.7f, 9.4f), 2.6f, 80f, 25f, 0f);

        // First floor: where stair A comes down, and halfway along the back hall.
        Lamp("LightPool_Stair_A", lights, new Vector3(-18.4f, firstLamp, 7.8f), 1.9f, 77f, 15f, Roof - SlabThickness);
        Lamp("LightPool_Hall", lights, new Vector3(-13f, firstLamp, 15.2f), 1.9f, 77f, 15f, Roof - SlabThickness);

        // Ground floor: where stair B comes down, and one in the middle of the workshop.
        Lamp("LightPool_Stair_B", lights, new Vector3(-2.2f, groundLamp, 12.6f), 2.2f, 85f, 15f, First - SlabThickness);
        Lamp("LightPool_Workshop", lights, new Vector3(-4f, groundLamp, 6.5f), 2.2f, 85f, 15f, First - SlabThickness);
    }

    // A Lamp prefab hung from the ceiling on a chain (ceiling 0 for one on a post).
    static void Lamp(string name, Transform parent, Vector3 position, float radius, float spotAngle, float intensity, float ceiling)
    {
        var lamp = (GameObject)PrefabUtility.InstantiatePrefab(lampPrefab, parent);
        lamp.name = name;
        lamp.transform.position = position;

        var zone = lamp.GetComponent<LampZone>();
        SetFloat(zone, "radius", radius);
        zone.SyncNavMeshVolume();

        var light = lamp.transform.Find("Spot Light").GetComponent<Light>();
        light.spotAngle = spotAngle;
        light.intensity = intensity;

        foreach (Object part in new Object[] { lamp, lamp.transform, zone, lamp.GetComponent<NavMeshModifierVolume>(), light })
            PrefabUtility.RecordPrefabInstancePropertyModifications(part);

        if (ceiling > position.y)
        {
            float length = ceiling - position.y;
            Box("Chain", lamp.transform, new Vector3(0f, length / 2f, 0f), new Vector3(0.04f, length, 0.04f), steel, 0, false, false);
        }
    }

    // --- Checkpoints, hiding spots, cans ---

    static Checkpoint BuildCheckpoints()
    {
        Transform checkpoints = NewRoot("Checkpoints").transform;
        Checkpoint first = Place(checkpointPrefab, checkpoints, "Checkpoint_01", new Vector3(-2.4f, Roof + 0.05f, 7.5f), 0f).GetComponent<Checkpoint>();
        Place(checkpointPrefab, checkpoints, "Checkpoint_02", new Vector3(-2f, Ground + 0.05f, 12.6f), 0f);
        return first;
    }

    static void BuildHidingSpots()
    {
        Transform spots = NewRoot("Hiding Spots").transform;
        // Their doors face into the room (the prefab's +Z).
        Place(hidingSpotPrefab, spots, "Hiding_Spot_01", new Vector3(-1.6f, First, 3f), -90f);
        Place(hidingSpotPrefab, spots, "Hiding_Spot_02", new Vector3(-19.4f, Ground, 6.5f), 90f);
    }

    static void BuildCans()
    {
        Transform cans = NewRoot("Cans").transform;
        // Off the monsters' routes, or a monster walking into one would make a noise and alert the others.
        Place(canPrefab, cans, "Can_01", new Vector3(-10.5f, First + 0.13f, 13.4f), 0f);
        Place(canPrefab, cans, "Can_02", new Vector3(-8.6f, First + 0.13f, 7.8f), 0f);
        Place(canPrefab, cans, "Can_03", new Vector3(-11.5f, Ground + 0.13f, 15.8f), 0f);
        Place(canPrefab, cans, "Can_04", new Vector3(-6.8f, Ground + 0.13f, 4.4f), 0f);
        Place(canPrefab, cans, "Can_05", new Vector3(-16.2f, Ground + 0.13f, 10.4f), 0f);
    }

    static GameObject Place(GameObject prefab, Transform parent, string name, Vector3 position, float turn)
    {
        var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        placed.name = name;
        placed.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, turn, 0f));
        PrefabUtility.RecordPrefabInstancePropertyModifications(placed);
        PrefabUtility.RecordPrefabInstancePropertyModifications(placed.transform);
        return placed;
    }

    // --- Monsters ---

    static void BuildMonsters(GameObject template)
    {
        Transform monsters = NewRoot("Monsters").transform;

        // First floor: round the back hall and both front rooms, through every doorway.
        Monster("M1", template, monsters, 0f,
            new Vector3(-16f, First, 11.3f), new Vector3(-9.5f, First, 11.3f), new Vector3(-6f, First, 11.3f),
            new Vector3(-6f, First, 8.4f), new Vector3(-3.5f, First, 5.5f), new Vector3(-8.5f, First, 4f),
            new Vector3(-11.5f, First, 4f), new Vector3(-14f, First, 4.5f), new Vector3(-14f, First, 8.6f));

        // Ground floor: the strip behind Shelf_A, the back door, and back through the middle.
        Monster("M2", template, monsters, 1.5f,
            new Vector3(-7.5f, Ground, 13.5f), new Vector3(-14f, Ground, 13.6f), new Vector3(-18.5f, Ground, 13.5f),
            new Vector3(-18.5f, Ground, 8.5f), new Vector3(-12.5f, Ground, 8.5f), new Vector3(-7.5f, Ground, 8.5f));
    }

    // A copy of the warehouse's monster, with its Wick put back as a prefab instance so it follows Wick.prefab.
    static void Monster(string id, GameObject template, Transform parent, float routeEndPause, params Vector3[] route)
    {
        var enemy = template.GetComponent<Enemy>();
        var oldAlert = (Renderer)new SerializedObject(enemy).FindProperty("alertLight").objectReferenceValue;

        var monster = Object.Instantiate(template, parent);
        monster.name = id;
        Vector3 heading = route[1] - route[0];
        monster.transform.SetPositionAndRotation(route[0], Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.z)));

        Object.DestroyImmediate(monster.transform.Find("Wick").gameObject);
        var wick = (GameObject)PrefabUtility.InstantiatePrefab(wickPrefab, monster.transform);
        wick.name = "Wick";
        wick.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        Renderer alert = wick.GetComponentsInChildren<Renderer>(true).First(r => r.name == oldAlert.name);

        Transform routeGroup = Group($"{id} Route", parent);
        var serialized = new SerializedObject(monster.GetComponent<Enemy>());
        serialized.FindProperty("alertLight").objectReferenceValue = alert;
        serialized.FindProperty("routeEndPause").floatValue = routeEndPause;
        var points = serialized.FindProperty("patrolRoute");
        points.arraySize = route.Length;
        for (int i = 0; i < route.Length; i++)
        {
            var point = new GameObject($"Point_{i + 1:00}").transform;
            point.SetParent(routeGroup, false);
            point.position = route[i];
            points.GetArrayElementAtIndex(i).objectReferenceValue = point;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // --- End, link, level ---

    static void BuildEndPoint(GameObject template)
    {
        var end = Object.Instantiate(template);
        end.name = "End Point (new)";
        NewRoots.Add(end);
        end.tag = "Untagged";
        end.transform.position = new Vector3(-17.8f, Ground + 0.05f, 15.6f);
        const float radius = 1.8f;
        end.transform.Find("Marker").localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);

        var serialized = new SerializedObject(end.GetComponent<EndPoint>());
        serialized.FindProperty("radius").floatValue = radius;
        serialized.FindProperty("finishedText").stringValue = "YOU MADE IT\n\nE  Restart";
        serialized.FindProperty("nextLevel").stringValue = "";
        serialized.FindProperty("restartLevel").stringValue = "Warehouse_01";
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // Keeps the warehouse loaded while the player is on the roof or the first floor, where the roof opening shows the sky
    // and the warehouse beside it. It unloads once they are down on the ground floor, which has no view out.
    static void BuildAreaLink()
    {
        Transform links = NewRoot("Area Links").transform;
        AreaLinkBox(links, "To Warehouse", "Warehouse_01", new Vector3(Left - 1.2f, First - 0.75f, Front - 1f), new Vector3(0.2f, 14f, Back + 1f));
    }

    static void AreaLinkBox(Transform parent, string name, string area, Vector3 min, Vector3 max)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.transform.position = (min + max) / 2f;
        var link = new SerializedObject(holder.AddComponent<AreaLink>());
        link.FindProperty("area").stringValue = area;
        link.FindProperty("size").vector3Value = max - min;
        link.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetUpLevel(GameObject level, Checkpoint first)
    {
        Transform start = level.transform.Find("Player Start");
        // Where the old test platform's end point was, just below the window, facing away from the warehouse.
        start.SetPositionAndRotation(new Vector3(-2.4f, Roof + 0.05f, 7.5f), Quaternion.Euler(0f, -90f, 0f));

        var serialized = new SerializedObject(level.GetComponent<Level>());
        serialized.FindProperty("firstCheckpoint").objectReferenceValue = first;
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(Left - Thick - 0.4f, -1f, Front - Thick), new Vector3(-0.2f, 12f, Back + 1f));
        serialized.FindProperty("bounds").boundsValue = bounds;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // --- NavMesh ---

    static void BakeNavMesh(NavMeshSurface surface)
    {
        surface.collectObjects = CollectObjects.Volume;
        surface.center = new Vector3((Left + Right) / 2f, 5f, (Front + Back) / 2f);
        surface.size = new Vector3(Right - Left + 2f, 12f, Back - Front + 2f);

        Physics.SyncTransforms();
        surface.navMeshData = null;
        surface.BuildNavMesh();

        string path = $"{SceneFolder}/NavMesh.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(surface.navMeshData, path);
        EditorUtility.SetDirty(surface);
    }

    // Each patrol route must join up outside the light, and the way from the roof to the back door must exist.
    static void CheckRoutes(Scene scene)
    {
        var surface = Root(scene, "Nav Surface").GetComponent<NavMeshSurface>();
        surface.enabled = false;
        surface.enabled = true;

        int lit = 1 << NavMesh.GetAreaFromName("Lit");
        int dark = NavMesh.AllAreas & ~lit;
        var path = new NavMeshPath();
        bool allGood = true;

        foreach (Enemy enemy in Root(scene, "Monsters").GetComponentsInChildren<Enemy>())
        {
            var route = (Transform[])typeof(Enemy).GetField("patrolRoute", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(enemy);
            for (int i = 0; i + 1 < route.Length; i++)
            {
                bool joined = Snap(route[i].position, out Vector3 from) && Snap(route[i + 1].position, out Vector3 to)
                    && NavMesh.CalculatePath(from, to, dark, path) && path.status == NavMeshPathStatus.PathComplete;
                if (!joined)
                {
                    allGood = false;
                    Debug.LogWarning($"Build Rowhouse: {enemy.name}'s route doesn't join up outside the light from point {i + 1} to {i + 2}.");
                }
            }
        }

        Vector3 roofStart = new Vector3(-4f, Roof, 6f);
        Vector3 backDoor = new Vector3(-17.8f, Ground, 15f);
        bool through = Snap(roofStart, out Vector3 a) && Snap(backDoor, out Vector3 b)
            && NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        if (!through) allGood = false;
        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        Debug.Log($"Build Rowhouse: roof to back door {(through ? $"joins up, {length:0} m" : "DOES NOT join up")}. Routes {(allGood ? "all join up outside the light" : "have problems, see warnings")}.");
    }

    static bool Snap(Vector3 point, out Vector3 snapped)
    {
        bool found = NavMesh.SamplePosition(point, out NavMeshHit hit, 1f, NavMesh.AllAreas);
        snapped = hit.position;
        if (!found) Debug.LogWarning($"Build Rowhouse: no NavMesh near {point}.");
        return found;
    }

    // --- The warehouse ---

    static void JoinWarehouse()
    {
        Scene scene = EditorSceneManager.OpenScene(WarehousePath, OpenSceneMode.Single);

        // The rowhouse's roof replaces the test platform, and its back door the test end point.
        Object.DestroyImmediate(Root(scene, "Test Platform"));
        Object.DestroyImmediate(Root(scene, "End Point"));

        // The rowhouse loads from the upper level's front: the front catwalk, the set piece and the last stretch of the
        // right catwalk, and stays loaded out through the escape window onto its roof. Never from the ground floor.
        var links = new GameObject("Area Links").transform;
        AreaLinkBox(links, "To Rowhouse", SceneName, new Vector3(Left - 1f, 7f, -0.5f), new Vector3(30.5f, 14f, 15.5f));

        // The warehouse's area stops at its own left wall, where the rowhouse's begins.
        var level = new SerializedObject(Root(scene, "Level").GetComponent<Level>());
        var boundsProperty = level.FindProperty("bounds");
        Bounds bounds = boundsProperty.boundsValue;
        bounds.SetMinMax(new Vector3(-0.6f, bounds.min.y, bounds.min.z), bounds.max);
        boundsProperty.boundsValue = bounds;
        level.ApplyModifiedPropertiesWithoutUndo();

        var door = new SerializedObject(Root(scene, "Warehouse").transform.Find("Front_Door").GetComponent<Door>());
        door.FindProperty("closeBehindPlayer").boolValue = true;
        door.FindProperty("insideSide").intValue = 1;
        door.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Build Rowhouse: Warehouse_01 joined. Its area is now {bounds.min} to {bounds.max}.");
    }

    static void AddToBuildList()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ScenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
