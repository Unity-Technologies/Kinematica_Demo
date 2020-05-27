using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ControlsMenu : MonoBehaviour
{
    public float pressStartMessageDuration = 15.0f;
    public Texture controlsTexture;
    float textureWidth = 1980.0f;
    float textureHeight = 1080.0f;

    enum State
    {
        Idle,
        DisplayMessage,
        DisplayControls,
    }

    State m_State;
    float m_DisplayMessageTimer;

    // Start is called before the first frame update
    void Start()
    {
        m_State = State.DisplayMessage;
        m_DisplayMessageTimer = pressStartMessageDuration;
    }

    private void Update()
    {
        if (m_State == State.DisplayMessage)
        {
            if (m_DisplayMessageTimer <= 0.0f)
            {
                m_State = State.Idle;
            }
            else
            {
                m_DisplayMessageTimer -= Time.deltaTime;
            }
        }

        bool bPressedMenuButton = Input.GetButtonDown("ControlsMenu") || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (bPressedMenuButton)
        {
            m_State = m_State == State.DisplayControls ? State.Idle : State.DisplayControls;
        }
    }

    // Update is called once per frame
    void OnGUI()
    {
        if (m_State == State.DisplayMessage)
        {
            string displayMessage = "Press start or enter to display controls";
            float labelWidth = GUI.skin.GetStyle("label").CalcSize(new GUIContent(displayMessage)).x;
            GUI.Label(new Rect((Screen.width - labelWidth) * 0.5f, 0, labelWidth, 20), displayMessage);
        }
        else if (m_State == State.DisplayControls && controlsTexture != null)
        {
            float width = textureWidth;
            float height = textureHeight;

            if (width > Screen.width)
            {
                height = height * (Screen.width / width);
                width = Screen.width;
            }
            if (height > Screen.height)
            {
                width = width * (Screen.height / height);
                height = Screen.height;
            }

            GUI.DrawTexture(new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height), controlsTexture, ScaleMode.ScaleToFit, true, width / height);
        }
    }
}
