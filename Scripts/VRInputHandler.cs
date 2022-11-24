using System;
using System.Collections;
using System.Collections.Generic;
using CArControls;
using System.IO;
using Unity.WebRTC;
using UnityEngine;

public class VRInputHandler : MonoBehaviour
{
    public OVRCameraRig rig;
    public WebRtcHandler webRtcHandler;

    public string controlsLabel = "controls";
    public float followInitialSpeedInDegrees = 40;
    public float followAccelerationInDegrees = 10;

    private float currentSpeed;

    // Start is called before the first frame update
    void Start()
    {
        currentSpeed = followInitialSpeedInDegrees;
    }

    // Update is called once per frame
    void Update()
    {
        if (rig != null)
        {
            var thumbstickDir = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
            webRtcHandler.sendControlsMessage(controlsLabel, thumbstickDir.x, thumbstickDir.y);

            if (OVRInput.Get(OVRInput.Button.PrimaryThumbstick))
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, rig.centerEyeAnchor.transform.rotation, (float)(currentSpeed / 180.0 * Math.PI));
                currentSpeed += followAccelerationInDegrees;
            } else
            {
                currentSpeed = followInitialSpeedInDegrees;
            }
        }
    }
}
