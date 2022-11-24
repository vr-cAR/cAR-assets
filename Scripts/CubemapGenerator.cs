using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

public class CubemapGenerator : MonoBehaviour
{
    public WebRtcHandler webRtcHandler;

    private const int LEFT = 0, RIGHT = 1, UP = 2, DOWN = 3, FRONT = 4, BACK = 5, NUM_DIRECTIONS = 6;
    public RawImage left, right, up, down, front, back;
    public string leftId = "left", rightId = "right", upId = "up", downId = "down", frontId = "front", backId = "back";

    public Canvas canvas;
    public TMP_InputField addrInputField;
    public TMP_InputField portInputField;
    public Button submitButton;
    public TMP_Text statusText;

    private RawImage[] planes;
    private Dictionary<string, int> idToIdx;

    private void onConnectionUpdate(bool connected)
    {
        if (connected)
        {
            submitButton.interactable = false;
        } else
        {
            submitButton.interactable = true;
            hidePlanes();
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
        planes = new RawImage[NUM_DIRECTIONS];

        // fill out planes array
        planes[LEFT] = left;
        planes[RIGHT] = right;
        planes[UP] = up;
        planes[DOWN] = down;
        planes[FRONT] = front;
        planes[BACK] = back;

        // create id to idx dictionary
        idToIdx = new Dictionary<string, int>()
        {
            { leftId, LEFT },
            { rightId, RIGHT },
            { upId, UP },
            { downId, DOWN },
            { frontId, FRONT },
            { backId, BACK },
        };

        // instantiate gui
        submitButton.onClick.AddListener(() =>
        {
            webRtcHandler.setControlServer(addrInputField.text, portInputField.text);
        });
        hidePlanes();

        // add webrtc callbacks
        foreach (var (id, idx) in idToIdx)
        {
            var plane = planes[idx];
            webRtcHandler.addTrackDelegate(id, (e) =>
            {
                if (e.Track is VideoStreamTrack video)
                {
                    video.OnVideoReceived += tex =>
                    {
                        Debug.Log($"Setting texture for {e.Track.Id}");
                        plane.gameObject.SetActive(true);
                        plane.texture = tex;
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

    public void hidePlanes()
    {
        foreach (var image in planes)
        {
            image?.GameObject().SetActive(false);
        }
    }
}
