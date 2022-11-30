using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public struct VideoSink
{
    public string id;
    public Renderer sink;
}

public class WorldBuilder : MonoBehaviour
{
    public WebRtcHandler webRtcHandler;

    public List<VideoSink> videoSinks;

    public TMP_InputField addrInputField;
    public TMP_InputField portInputField;
    public Canvas UI;
    public Button submitButton;
    public TMP_Text statusText;

    private void onConnectionUpdate(bool connected)
    {
        if (connected)
        {
            Debug.Log("Enabling UI");
            UI.enabled = false;
        } else
        {
            Debug.Log("Disabling UI");
            UI.enabled = true;
        }
    }

    private void onStatusUpdate(string status, bool isError)
    {
        statusText.color = isError ? Color.red : Color.green;
        statusText.text = status;
    }

    // Start is called before the first frame update
    void Start()
    {
        // instantiate gui
        submitButton.onClick.AddListener(() =>
        {
            webRtcHandler.setControlServer(addrInputField.text, portInputField.text);
        });

        // add webrtc callbacks
        foreach (var videoSink in videoSinks)
        {
            webRtcHandler.addTrackDelegate(videoSink.id, (e) =>
            {
                if (e.Track is VideoStreamTrack video)
                {
                    video.OnVideoReceived += tex =>
                    {
                        Debug.Log($"Setting texture for {e.Track.Id}");
                        videoSink.sink.gameObject.SetActive(true);
                        videoSink.sink.material.mainTexture = tex;
                        Debug.Log($"Displaying video for {e.Track.Id}");
                    };
                } else
                {
                    Debug.Log($"Got incompatible media format for track {e.Track.Id}");
                }
            });
        }

        webRtcHandler.onStatusUpdate = onStatusUpdate;
        webRtcHandler.onConnectionUpdate = onConnectionUpdate;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
