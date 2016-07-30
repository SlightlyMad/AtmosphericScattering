using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AtmosphericScattering))]
class AtmosphericScatteringEditor : Editor
{
    private SerializedProperty generalSettingsFoldout;
    private SerializedProperty scatteringFoldout;
    private SerializedProperty sunFoldout;
    private SerializedProperty lightShaftsFoldout;
    private SerializedProperty ambientFoldout;
    private SerializedProperty dirLightFoldout;
    private SerializedProperty reflectionProbeFoldout;

    SerializedProperty RenderingMode;
    SerializedProperty ScatteringComputeShader;
    SerializedProperty Sun;
    SerializedProperty RenderAtmosphericFog;
    SerializedProperty IncomingLight;
    SerializedProperty RayleighScatterCoef;
    SerializedProperty MieScatterCoef;
    SerializedProperty MieG;
    SerializedProperty RenderSun;
    SerializedProperty SunIntensity;
    SerializedProperty UpdateLightColor;
    SerializedProperty LightColorIntensity;
    SerializedProperty UpdateAmbientColor;
    SerializedProperty AmbientColorIntensity;
    SerializedProperty RenderLightShafts;
    SerializedProperty LightShaftQuality;
    SerializedProperty SampleCount;
    SerializedProperty ReflectionProbe;
    SerializedProperty ReflectionProbeResolution;
    SerializedProperty DistanceScale;

    string[] ResolutionNames = { "32", "64", "128", "256" };
    int[] Resolutions = { 32, 64, 128, 256 };

    int GetResolutionIndex(int resolution)
    {
        for (int i = 0; i < Resolutions.Length; ++i)
            if (Resolutions[i] == resolution)
                return i;
        return -1;
    }

    int GetResolution(int index)
    {
        return Resolutions[index];
    }

    void OnEnable()
    {
        generalSettingsFoldout = serializedObject.FindProperty("GeneralSettingsFoldout");
        scatteringFoldout = serializedObject.FindProperty("ScatteringFoldout");
        sunFoldout = serializedObject.FindProperty("SunFoldout");
        lightShaftsFoldout = serializedObject.FindProperty("LightShaftsFoldout");
        ambientFoldout = serializedObject.FindProperty("AmbientFoldout");
        dirLightFoldout = serializedObject.FindProperty("DirLightFoldout");
        reflectionProbeFoldout = serializedObject.FindProperty("ReflectionProbeFoldout");
        RenderingMode = serializedObject.FindProperty("RenderingMode");
        ScatteringComputeShader = serializedObject.FindProperty("ScatteringComputeShader");
        Sun = serializedObject.FindProperty("Sun");
        RenderAtmosphericFog = serializedObject.FindProperty("RenderAtmosphericFog");
        IncomingLight = serializedObject.FindProperty("IncomingLight");
        RayleighScatterCoef = serializedObject.FindProperty("RayleighScatterCoef");
        MieScatterCoef = serializedObject.FindProperty("MieScatterCoef");
        MieG = serializedObject.FindProperty("MieG");
        RenderSun = serializedObject.FindProperty("RenderSun");
        SunIntensity = serializedObject.FindProperty("SunIntensity");
        UpdateLightColor = serializedObject.FindProperty("UpdateLightColor");
        LightColorIntensity = serializedObject.FindProperty("LightColorIntensity");
        UpdateAmbientColor = serializedObject.FindProperty("UpdateAmbientColor");
        AmbientColorIntensity = serializedObject.FindProperty("AmbientColorIntensity");
        RenderLightShafts = serializedObject.FindProperty("RenderLightShafts");
        LightShaftQuality = serializedObject.FindProperty("LightShaftQuality");
        SampleCount = serializedObject.FindProperty("SampleCount");
        ReflectionProbe = serializedObject.FindProperty("ReflectionProbe");
        ReflectionProbeResolution = serializedObject.FindProperty("ReflectionProbeResolution");
        DistanceScale = serializedObject.FindProperty("DistanceScale");
    }

    public override void OnInspectorGUI()
    {
        //DrawDefaultInspector();
        serializedObject.Update();

        AtmosphericScattering a = (AtmosphericScattering)target;

        GUIStyle s = new GUIStyle(EditorStyles.label);
        s.normal.textColor = Color.red;
        string errors = a.Validate().TrimEnd();
        if (errors != "")
            GUILayout.Label(errors, s);

        GUIStyle style = EditorStyles.foldout;
        FontStyle previousStyle = style.fontStyle;
        style.fontStyle = FontStyle.Bold;

        a.GeneralSettingsFoldout = EditorGUILayout.Foldout(a.GeneralSettingsFoldout, "General Settings", style);
        if (a.GeneralSettingsFoldout)
        {
            AtmosphericScattering.RenderMode rm = (AtmosphericScattering.RenderMode)EditorGUILayout.EnumPopup("Rendering Mode", (AtmosphericScattering.RenderMode)RenderingMode.enumValueIndex);
            RenderingMode.enumValueIndex = (int)rm;
            ScatteringComputeShader.objectReferenceValue = (ComputeShader)EditorGUILayout.ObjectField("Compute Shader", ScatteringComputeShader.objectReferenceValue, typeof(ComputeShader));
            Sun.objectReferenceValue = (Light)EditorGUILayout.ObjectField("Sun", Sun.objectReferenceValue, typeof(Light));
        }

        a.ScatteringFoldout = EditorGUILayout.Foldout(a.ScatteringFoldout, "Atmospheric Scattering");
        if (a.ScatteringFoldout)
        {
            RenderAtmosphericFog.boolValue = EditorGUILayout.Toggle("Render Atm Fog", RenderAtmosphericFog.boolValue);
            IncomingLight.colorValue = EditorGUILayout.ColorField(new GUIContent("Incoming Light (*)"), IncomingLight.colorValue, false, false, true, new ColorPickerHDRConfig(0, 10, 0, 10));            
            RayleighScatterCoef.floatValue = EditorGUILayout.Slider("Rayleigh Coef (*)", RayleighScatterCoef.floatValue, 0, 4);
            MieScatterCoef.floatValue = EditorGUILayout.Slider("Mie Coef (*)", MieScatterCoef.floatValue, 0, 4);
            MieG.floatValue = EditorGUILayout.Slider("MieG", MieG.floatValue, 0, 0.999f);
            DistanceScale.floatValue = EditorGUILayout.FloatField("Distance Scale", DistanceScale.floatValue);
            GUILayout.Label("* - Change requires LookUp table update");
            if (GUILayout.Button("Update LookUp Tables") && a.IsInitialized())
                ((AtmosphericScattering)target).CalculateLightLUTs();
        }

        a.SunFoldout = EditorGUILayout.Foldout(a.SunFoldout, "Sun");
        if (a.SunFoldout)
        {
            RenderSun.boolValue = EditorGUILayout.Toggle("Render Sun", RenderSun.boolValue);
            SunIntensity.floatValue = EditorGUILayout.Slider("Sun Intensity", SunIntensity.floatValue, 0, 10);
        }

        a.DirLightFoldout = EditorGUILayout.Foldout(a.DirLightFoldout, "Directional Light");
        if (a.DirLightFoldout)
        {
            UpdateLightColor.boolValue = EditorGUILayout.Toggle("Update Color", UpdateLightColor.boolValue);
            LightColorIntensity.floatValue = EditorGUILayout.Slider("Intensity", LightColorIntensity.floatValue, 0, 4);
        }
        
        a.AmbientFoldout = EditorGUILayout.Foldout(a.AmbientFoldout, "Ambient Light");
        if (a.AmbientFoldout)
        {
            UpdateAmbientColor.boolValue = EditorGUILayout.Toggle("Update Color", UpdateAmbientColor.boolValue);
            AmbientColorIntensity.floatValue = EditorGUILayout.Slider("Intensity", AmbientColorIntensity.floatValue, 0, 4);
        }

        a.LightShaftsFoldout = EditorGUILayout.Foldout(a.LightShaftsFoldout, "Light Shafts");
        if (a.LightShaftsFoldout)
        {
            bool renderLightShafts = RenderLightShafts.boolValue;
            RenderLightShafts.boolValue = EditorGUILayout.Toggle("Enable Light Shafts", RenderLightShafts.boolValue);
            if (renderLightShafts != RenderLightShafts.boolValue && a.IsInitialized()) // change
            {
                if (RenderLightShafts.boolValue)
                    a.EnableLightShafts();
                else
                    a.DisableLightShafts();
            }

            AtmosphericScattering.LightShaftsQuality quality = (AtmosphericScattering.LightShaftsQuality)LightShaftQuality.enumValueIndex;
            AtmosphericScattering.LightShaftsQuality currentQuality = (AtmosphericScattering.LightShaftsQuality)EditorGUILayout.EnumPopup("Quality", (AtmosphericScattering.LightShaftsQuality)LightShaftQuality.enumValueIndex);
            LightShaftQuality.enumValueIndex = (int)currentQuality;
            if (quality != currentQuality && a.IsInitialized())
            {
                serializedObject.ApplyModifiedProperties();
                a.InitializeLightShafts();
            }

            SampleCount.intValue = EditorGUILayout.IntSlider("Sample Count", SampleCount.intValue, 1, 64);
            //maxRayLengthProp.floatValue = EditorGUILayout.FloatField("Max Ray Length", maxRayLengthProp.floatValue);
        }

        a.ReflectionProbeFoldout = EditorGUILayout.Foldout(a.ReflectionProbeFoldout, "Reflection Probe");
        if (a.ReflectionProbeFoldout)
        {
            bool reflectionProbe = ReflectionProbe.boolValue;
            ReflectionProbe.boolValue = EditorGUILayout.Toggle("Enable Reflection Probe", ReflectionProbe.boolValue);
            if(reflectionProbe != ReflectionProbe.boolValue && a.IsInitialized())
            {
                if (ReflectionProbe.boolValue)
                    a.EnableReflectionProbe();
                else
                    a.DisableReflectionProbe();
            }

            int resolution = ReflectionProbeResolution.intValue;
            ReflectionProbeResolution.intValue = GetResolution(EditorGUILayout.Popup("Resolution", GetResolutionIndex(ReflectionProbeResolution.intValue), ResolutionNames));
            if (resolution != ReflectionProbeResolution.intValue && a.IsInitialized())
            {
                serializedObject.ApplyModifiedProperties();
                a.ChangeReflectionProbeResolution();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}