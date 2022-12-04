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
        var thumbstickDir = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        //webRtcHandler.sendControlsMessage(controlsLabel, -wheel.transform.rotation.eulerAngles.z, thumbstickDir.y);
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
