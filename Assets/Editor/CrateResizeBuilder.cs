using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LastLamp;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off: makes every plain crate in both levels 1.75 m, the middle of the corridor's 1.5 m and the warehouse's
/// 2 m. Each crate keeps its centre on the ground and its bottom on its floor. Crate lockers (hiding spots) keep their
/// size. Then it rebakes each level's NavMesh, warns if a monster's patrol route no longer joins up outside the light,
/// and reruns Tools > Build Wooden Crates so the crates get a 1.75 m wooden mesh.
/// Can run from the menu or in batch mode: -executeMethod CrateResizeBuilder.RunBatch
/// Delete this script once it has run (it stays in git history).
/// </summary>
static class CrateResizeBuilder
{
    const float Size = 1.75f;
    const string OldMaterialPath = "Assets/Materials/Crate_Brown.mat";
    const string LitArea = "Lit";

    static readonly string[] ScenePaths =
    {
        "Assets/Scenes/Corridor_01.unity",
        "Assets/Scenes/Warehouse_01.unity",
    };

    [MenuItem("Tools/Resize Crates")]
    static void RunFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Run();
    }

    public static void RunBatch()
    {
        bool ok = false;
        try { ok = Run(); }
        catch (Exception e) { Debug.LogException(e); }
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Run()
    {
        var crateMaterial = AssetDatabase.LoadAssetAtPath<Material>(OldMaterialPath);
        if (crateMaterial == null)
        {
            Debug.LogError($"Resize Crates: {OldMaterialPath} not found.");
            return false;
        }

        var log = new StringBuilder("Resize Crates:\n");
        bool ok = true;

        foreach (string path in ScenePaths)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int resized = 0;

            foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!IsPlainCrate(renderer, crateMaterial)) continue;

                Transform crate = renderer.transform;
                var box = crate.GetComponent<BoxCollider>();
                float bottom = box != null ? box.bounds.min.y : crate.position.y - crate.lossyScale.y / 2f;
                Vector3 before = crate.lossyScale;

                Vector3 parentScale = crate.parent != null ? crate.parent.lossyScale : Vector3.one;
                crate.localScale = new Vector3(Size / parentScale.x, Size / parentScale.y, Size / parentScale.z);
                Vector3 position = crate.position;
                // The crate's collider is the unit cube around its centre, so the centre sits half a crate up.
                position.y = bottom + Size / 2f;
                crate.position = position;

                EditorUtility.SetDirty(crate);
                resized++;
                log.AppendLine($"  {path}: {PathOf(crate)} {before.x:0.##} m to {Size} m, bottom stays at {bottom:0.##}");
            }

            Physics.SyncTransforms();
            ok &= Rebake(scene, log);
            CheckRoutes(path, log);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                ok = false;
                log.AppendLine($"  {path}: COULD NOT SAVE");
            }
            log.AppendLine($"  {path}: {resized} crates resized.");
        }

        // Give the resized crates their wooden mesh at the new size, and drop the meshes no crate uses any more.
        var buildCrates = Type.GetType("CrateBuilder")?.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (buildCrates == null)
        {
            ok = false;
            log.AppendLine("  Could not find CrateBuilder.Run, so the wooden meshes still have the old sizes. Run Tools > Build Wooden Crates.");
        }
        else
        {
            buildCrates.Invoke(null, null);
        }

        AssetDatabase.SaveAssets();
        if (ok) Debug.Log(log.ToString());
        else Debug.LogError(log.ToString());
        return ok;
    }

    // A plain crate: the built-in cube with the brown crate material, not part of a hiding spot.
    static bool IsPlainCrate(MeshRenderer renderer, Material crateMaterial)
    {
        if (Array.IndexOf(renderer.sharedMaterials, crateMaterial) < 0) return false;
        var filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null || filter.sharedMesh.name != "Cube") return false;
        return renderer.GetComponentInParent<HidingSpot>(true) == null;
    }

    static bool Rebake(Scene scene, StringBuilder log)
    {
        bool ok = true;
        foreach (var surface in UnityEngine.Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None))
        {
            string assetPath = surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : "";
            if (string.IsNullOrEmpty(assetPath))
            {
                ok = false;
                log.AppendLine($"  {scene.path}: {surface.name} has no saved NavMesh asset, so it was not rebaked.");
                continue;
            }

            surface.BuildNavMesh();
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
            EditorUtility.SetDirty(surface);
            log.AppendLine($"  {scene.path}: rebaked {surface.name} into {assetPath}.");
        }
        return ok;
    }

    // Every monster must still be able to walk its route without crossing a lit pool.
    static void CheckRoutes(string scenePath, StringBuilder log)
    {
        int lit = NavMesh.GetAreaFromName(LitArea);
        int mask = lit >= 0 ? NavMesh.AllAreas & ~(1 << lit) : NavMesh.AllAreas;
        var path = new NavMeshPath();

        foreach (var enemy in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
        {
            var route = new SerializedObject(enemy).FindProperty("patrolRoute");
            var points = new List<Vector3> { enemy.transform.position };
            for (int i = 0; i < route.arraySize; i++)
            {
                var point = route.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (point != null) points.Add(point.position);
            }

            for (int i = 0; i + 1 < points.Count; i++)
            {
                bool joined = NavMesh.SamplePosition(points[i], out NavMeshHit from, 1f, mask)
                    && NavMesh.SamplePosition(points[i + 1], out NavMeshHit to, 1f, mask)
                    && NavMesh.CalculatePath(from.position, to.position, mask, path)
                    && path.status == NavMeshPathStatus.PathComplete;
                if (!joined) Debug.LogWarning($"Resize Crates: in {scenePath}, {enemy.name}'s route no longer joins up outside the light between point {i} and {i + 1}.");
            }
            log.AppendLine($"  {scenePath}: checked {enemy.name}'s route ({points.Count} points).");
        }
    }

    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
}
