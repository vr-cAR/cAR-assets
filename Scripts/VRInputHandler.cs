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
    public Transform wheel;

    // TMP Wheel + objects to control with wheel
    public GameObject Vehicle; // represents the car
    private Rigidbody VehicleRigidBody;

    public string controlsLabel = "controls";

    // Start is called before the first frame update
    void Start()
    {
        VehicleRigidBody = Vehicle.GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        //Debug.Log("in update!");
        var thumbstickDir = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        //Debug.LogAssertion(webRtcHandler != null);
        if (thumbstickDir != null)
        {
            var angle = wheel.transform.rotation.eulerAngles.z;
            float x = 0;
            if (0 <= angle && angle <= 180)
            {
                x = -angle / 180.0f;
            } else if (180 < angle && angle <= 360)
            {
                x = (360 - angle) / 180.0f;
            }
            
            webRtcHandler?.sendControlsMessage(controlsLabel, x, thumbstickDir.y);
        }
        TurnVehicle();
    }

    private void TurnVehicle()
    {
        // Turn wheels compared to the steering wheel
        var turn = -wheel.transform.rotation.eulerAngles.z;
        if (turn < -350)
        {
            turn = turn + 360;
        }
        Debug.Log("Turning: " + turn);

        VehicleRigidBody.MoveRotation(Quaternion.RotateTowards(Vehicle.transform.rotation, Quaternion.Euler(0, turn, 0), Time.deltaTime * 9999));
    }
}
