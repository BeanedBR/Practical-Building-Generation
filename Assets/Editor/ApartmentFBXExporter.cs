#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// Use a CustomEditor to draw a button on the existing ProceduralApartment script
[CustomEditor(typeof(ProceduralApartment))]
public class ApartmentFBXExporter : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the normal inspector
        DrawDefaultInspector();

        GUILayout.Space(20);

        // Style a big, bold button
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontStyle = FontStyle.Bold,
            fixedHeight = 35
        };

        if (GUILayout.Button("Export Apartment to FBX", buttonStyle))
        {
            ExportToFBX();
        }
    }

    private void ExportToFBX()
    {
        ProceduralApartment apartment = (ProceduralApartment)target;

        // Use a reflection to safely check if the FBX Exporter is actually installed
        // preventing compiler errors if this script moves to a new project:
        var exporterType = System.Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");

        if (exporterType == null)
        {
            EditorUtility.DisplayDialog("Missing Package",
                "The FBX Exporter package is not installed.\n\nPlease go to Window -> Package Manager, search for 'FBX Exporter' in the Unity Registry, and install it.",
                "OK");
            return;
        }

        // Open a Save Dialog box so user can choose where to put it
        string defaultName = "ProceduralApartment_Seed_" + apartment.seed + ".fbx";
        string path = EditorUtility.SaveFilePanel("Export FBX", "Assets", defaultName, "fbx");

        if (!string.IsNullOrEmpty(path))
        {
            // Invoke the FBX Exporter API to package the GameObject
            var exportMethod = exporterType.GetMethod("ExportObject", new System.Type[] { typeof(string), typeof(UnityEngine.Object) });

            if (exportMethod != null)
            {
                exportMethod.Invoke(null, new object[] { path, apartment.gameObject });

                Debug.Log($"<color=green><b>Success!</b></color> Exported FBX to: {path}");

                // Refresh the asset database so the new FBX instantly appears in your project window
                AssetDatabase.Refresh();
            }
            else
            {
                Debug.LogError("Failed to find the ExportObject method in the FBX Exporter.");
            }
        }
    }
}
#endif