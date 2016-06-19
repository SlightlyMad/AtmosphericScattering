using System;
using System.Collections.Generic;
using UnityEngine;

class SunController : MonoBehaviour
{
    [Range(5, 40000)]
    public float Height = 5;
    public Transform DirLightTransform;
    public bool ShowHelp = true;

    private Vector3 prevMousePos;

    public void Start()
    {
        prevMousePos = Input.mousePosition;
        Height = transform.position.y;
        //Cursor.visible = false;
    }

    public void Update()
    {
        Vector3 pos = transform.position;
        pos.y = Height;
        transform.position = pos;

        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            prevMousePos = Input.mousePosition;
        }

        Vector3 curMousePos = Input.mousePosition;
        Vector3 mouseDelta = curMousePos - prevMousePos;
        prevMousePos = curMousePos;

        if (Input.GetMouseButton(0))
        {
            DirLightTransform.Rotate(0, mouseDelta.x * 0.1f, 0, Space.World);
            DirLightTransform.Rotate(mouseDelta.y * 0.1f, 0, 0, Space.Self);
        }

        if (Input.GetMouseButton(1))
        {
            transform.Rotate(0, mouseDelta.x * 0.1f, 0, Space.World);
            transform.Rotate(-mouseDelta.y * 0.1f, 0, 0, Space.Self);
        }
        if (Input.GetKey(KeyCode.A))
            Height += 2000 * Time.deltaTime;
        if (Input.GetKey(KeyCode.Z))
            Height -= 2000 * Time.deltaTime;
        if (Input.GetKeyDown(KeyCode.BackQuote))
            ShowHelp = !ShowHelp;
    }

    void OnGUI()
    {
        if (ShowHelp)
        {
            GUILayout.Label("~ - Toggle help");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Camera Height", GUILayout.ExpandWidth(false));
            Height = GUILayout.HorizontalSlider(Height, 10, 40000, GUILayout.Width(400));
            GUILayout.EndHorizontal();

            GUILayout.Label("LMB - Rotate Sun");
            GUILayout.Label("RMB - Rotate Camera");
            GUILayout.Label("A/Z - Move Camera Up/Down");
        }
    }
}
