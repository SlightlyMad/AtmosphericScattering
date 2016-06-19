using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AtmosphericScattering))]
class AtmosphericScatteringEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (GUILayout.Button("Update LookUp Tables"))
            ((AtmosphericScattering)target).CalculateLightLUTs();
    }
}