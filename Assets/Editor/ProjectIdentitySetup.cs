using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// One-off tool for PLAN.md task 1: replaces the template's name, company and package.
/// Delete once the settings are confirmed.
/// </summary>
public static class ProjectIdentitySetup
{
    const string Company = "Tony3974";
    const string Product = "Last Lamp";
    const string Package = "com.Tony3974.LastLamp";

    [MenuItem("Tools/Project Identity Setup")]
    static void Run()
    {
        PlayerSettings.companyName = Company;
        PlayerSettings.productName = Product;

        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, Package);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, Package);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, Package);

        AssetDatabase.SaveAssets();

        Debug.Log($"Project Identity Setup: {PlayerSettings.companyName} / {PlayerSettings.productName} / " +
                  PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone));
    }
}
