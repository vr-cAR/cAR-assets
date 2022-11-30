using System;
using System.Collections;
using System.Collections.Generic;
using CArControls;
using System.IO;
using Unity.WebRTC;
using UnityEngine;

public class VRInputHandler : MonoBehaviour
{
    public WebRtcHandler webRtcHandler;

    public string controlsLabel = "controls";

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        var thumbstickDir = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        webRtcHandler.sendControlsMessage(controlsLabel, thumbstickDir.x, thumbstickDir.y);
    }
}
