using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VRCenterer : MonoBehaviour
{
    public OVRCameraRig rig;
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
        if (OVRInput.Get(OVRInput.Button.PrimaryThumbstick))
        {
            Debug.Log($"Recentering with angular velocity {currentSpeed}");
            transform.rotation = Quaternion.RotateTowards(transform.rotation, rig.centerEyeAnchor.transform.rotation, (float)(currentSpeed / 180.0 * Math.PI));
            currentSpeed += followAccelerationInDegrees;
        }
        else
        {
            currentSpeed = followInitialSpeedInDegrees;
        }
    }
}
