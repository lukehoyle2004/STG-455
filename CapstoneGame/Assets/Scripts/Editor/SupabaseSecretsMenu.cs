using CapstoneGame.Auth;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Supabase > Create or Select Secrets File: makes (or finds) the gitignored
/// Assets/Resources/SupabaseSecrets.asset and selects it, so you can paste the project URL and anon key
/// into the Inspector.
/// </summary>
public static class SupabaseSecretsMenu
{
    public const string Folder = "Assets/Resources";
    public const string AssetPath = Folder + "/" + SupabaseSecrets.ResourcePath + ".asset";

    [MenuItem("Tools/Supabase/Create or Select Secrets File")]
    public static void CreateOrSelect()
    {
        var secrets = AssetDatabase.LoadAssetAtPath<SupabaseSecrets>(AssetPath);
        if (secrets == null)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Resources");
            secrets = ScriptableObject.CreateInstance<SupabaseSecrets>();
            AssetDatabase.CreateAsset(secrets, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Created {AssetPath}. Paste your Supabase Project URL and anon/publishable key into it. " +
                      "This file is gitignored and will not be committed.");
        }

        Selection.activeObject = secrets;
        EditorGUIUtility.PingObject(secrets);
    }
}
