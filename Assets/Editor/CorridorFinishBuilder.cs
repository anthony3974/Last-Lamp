using LastLamp;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-off tool for PLAN.md task 12: gives each monster its own access to lamp light through a Lit NavMesh area,
/// adds the second monster and hiding spot, splits the patrol routes, and adds the end point.
/// Everything it makes is a normal object that can be edited by hand. Delete once the corridor is confirmed.
/// </summary>
public static class CorridorFinishBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const string LampPrefabPath = "Assets/Prefabs/Lamp.prefab";
    const string HidingSpotPrefabPath = "Assets/Prefabs/HidingSpot.prefab";
    const string MarkerMaterialPath = "Assets/Materials/Lamp_Glow.mat";
    const string NavMeshPath = "Assets/Scenes/Corridor_01/NavMesh.asset";
    const string AreaSettingsPath = "ProjectSettings/NavMeshAreas.asset";
    const string LitAreaName = "Lit";
    const int LitArea = 3;
    const float LampSafeRadius = 2.6f;
    const float EnemyBodyRadius = 0.45f;

    static readonly Vector3[] SecondRoute =
    {
        new Vector3(0f, 0f, 50f),
        new Vector3(-1f, 0f, 40f),
        new Vector3(2f, 0f, 35f),
    };

    [MenuItem("Tools/Finish Corridor")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        var monster = GameObject.Find("Monster");
        var player = GameObject.Find("Player");
        var hud = GameObject.Find("HUD");
        var surface = Object.FindAnyObjectByType<NavMeshSurface>();
        var spots = GameObject.Find("Hiding Spots");
        var route = GameObject.Find("Patrol Route");

        if (scene.path != ScenePath || monster == null || player == null || hud == null || surface == null || spots == null || route == null)
        {
            Debug.LogError($"Finish Corridor: open {ScenePath} first, or this has already been built.");
            return;
        }

        var markerMaterial = AssetDatabase.LoadAssetAtPath<Material>(MarkerMaterialPath);
        var hidingSpot = AssetDatabase.LoadAssetAtPath<GameObject>(HidingSpotPrefabPath);
        if (markerMaterial == null || hidingSpot == null)
        {
            Debug.LogError("Finish Corridor: the marker material or hiding spot prefab was not found.");
            return;
        }

        NameLitArea();
        MarkLampPoolsAsLit();
        if (!LampsAreMarked()) return;

        PlaceSecondHidingSpot(hidingSpot, spots.transform);
        SplitPatrolRoutes(monster, route.transform);
        BuildEndPoint(markerMaterial, player, hud);
        Rebake(surface);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Finish Corridor: finished.");
    }

    static void NameLitArea()
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(AreaSettingsPath)[0]);
        settings.FindProperty("areas").GetArrayElementAtIndex(LitArea).FindPropertyRelative("name").stringValue = LitAreaName;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    static void MarkLampPoolsAsLit()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(LampPrefabPath);

        var obstacle = contents.GetComponent<NavMeshObstacle>();
        if (obstacle != null) Object.DestroyImmediate(obstacle);

        var volume = contents.GetComponent<NavMeshModifierVolume>();
        if (volume == null) volume = contents.AddComponent<NavMeshModifierVolume>();

        float width = (LampSafeRadius + EnemyBodyRadius) * 2f;
        volume.center = new Vector3(0f, -1.85f, 0f);
        volume.size = new Vector3(width, 5f, width);
        volume.area = LitArea;

        PrefabUtility.SaveAsPrefabAsset(contents, LampPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);

        foreach (var zone in Object.FindObjectsByType<LampZone>(FindObjectsSortMode.None))
            PrefabUtility.MergePrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(zone.gameObject));
    }

    static bool LampsAreMarked()
    {
        foreach (var zone in Object.FindObjectsByType<LampZone>(FindObjectsSortMode.None))
        {
            if (zone.GetComponent<NavMeshModifierVolume>() != null && zone.GetComponent<NavMeshObstacle>() == null) continue;

            Debug.LogError("Finish Corridor: the lamps in the scene have not picked up the prefab change. Run this again.");
            return false;
        }
        return true;
    }

    static void PlaceSecondHidingSpot(GameObject prefab, Transform group)
    {
        var spot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
        spot.name = "Hiding_Spot_02";
        spot.transform.SetPositionAndRotation(new Vector3(-2.6f, 0f, 50f), Quaternion.Euler(0f, 90f, 0f));
    }

    // The first monster keeps the near half of the corridor and the second takes the far half.
    static void SplitPatrolRoutes(GameObject monster, Transform firstRoute)
    {
        var second = Object.Instantiate(monster, SecondRoute[0], monster.transform.rotation);
        second.name = "Monster_02";
        monster.name = "Monster_01";

        var firstEnemy = new SerializedObject(monster.GetComponent<Enemy>());
        firstEnemy.FindProperty("patrolRoute").arraySize = 3;
        firstEnemy.ApplyModifiedPropertiesWithoutUndo();

        Object.DestroyImmediate(firstRoute.Find("Point_04").gameObject);
        Object.DestroyImmediate(firstRoute.Find("Point_05").gameObject);
        firstRoute.name = "Patrol Route 01";

        var secondRoute = new GameObject("Patrol Route 02").transform;
        var secondEnemy = new SerializedObject(second.GetComponent<Enemy>());
        var points = secondEnemy.FindProperty("patrolRoute");
        points.arraySize = SecondRoute.Length;

        for (int i = 0; i < SecondRoute.Length; i++)
        {
            var point = new GameObject($"Point_{i + 1:00}").transform;
            point.SetParent(secondRoute);
            point.position = SecondRoute[i];
            points.GetArrayElementAtIndex(i).objectReferenceValue = point;
        }
        secondEnemy.ApplyModifiedPropertiesWithoutUndo();
    }

    static void BuildEndPoint(Material markerMaterial, GameObject player, GameObject hud)
    {
        var message = new GameObject("Finished Message", typeof(RectTransform));
        message.layer = LayerMask.NameToLayer("UI");

        var rect = message.GetComponent<RectTransform>();
        rect.SetParent(hud.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(1000f, 220f);

        var label = message.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 60;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.85f, 0.85f, 0.85f);
        label.raycastTarget = false;
        label.text = "YOU MADE IT\nE  Restart";
        message.SetActive(false);

        var end = new GameObject("End Point");
        end.transform.position = new Vector3(0f, 0.05f, 66f);

        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Marker";
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.transform.SetParent(end.transform, false);
        marker.transform.localPosition = Vector3.up * -0.03f;
        marker.transform.localScale = new Vector3(4f, 0.01f, 4f);
        marker.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;

        var serialized = new SerializedObject(end.AddComponent<EndPoint>());
        serialized.FindProperty("player").objectReferenceValue = player.GetComponent<PlayerController>();
        serialized.FindProperty("finishedMessage").objectReferenceValue = message;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Rebake(NavMeshSurface surface)
    {
        surface.BuildNavMesh();
        AssetDatabase.DeleteAsset(NavMeshPath);
        AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
        EditorUtility.SetDirty(surface);
    }
}
