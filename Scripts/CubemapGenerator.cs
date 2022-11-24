using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using Grpc.Core;
using CAr;
using Unity.WebRTC;
using Unity.VisualScripting;
using TMPro;
using UnityEngine.UI;
using System.Threading.Tasks;
using Google.Protobuf;
using CArControls;
using System.IO;

public class CubemapGenerator : MonoBehaviour
{
    private const int LEFT = 0, RIGHT = 1, UP = 2, DOWN = 3, FRONT = 4, BACK = 5, NUM_DIRECTIONS = 6;

    public OVRCameraRig rig;

    public string controlsLabel;

    public RawImage left, right, up, down, front, back;
    public string leftId = "left", rightId = "right", upId = "up", downId = "down", frontId = "front", backId = "back";

    public Canvas canvas;
    public TMP_InputField addrInputField;
    public TMP_InputField portInputField;
    public TMP_InputField iceInputField;
    public Button submitButton;
    public TMP_Text statusText;

    public float followSpeedInDegrees;

    private RawImage[] planes;
    private Dictionary<string, int> idToIdx;

    private Channel controlPlaneChannel;
    private Control.ControlClient controlPlaneGrpcClient;
    private RTCPeerConnection videoConnection;
    private RTCDataChannel controlsChannel;
    private bool streaming;
    private long seqNum;

    private void Awake()
    {
        // Initialize WebRTC
        WebRTC.Initialize();
    }

    public void setControlServer(string addr, string port) {
        Debug.Log("Button pressed; initializing webrtc connection");
        submitButton.interactable = false;
        if (controlPlaneChannel != null) {
            closeControlPlaneConnection();
        }
        if (videoConnection != null)
        {
            videoConnection.Close();
            videoConnection = null;
        }
        controlPlaneChannel = new Channel($"{addr}:{port}", ChannelCredentials.Insecure);
        controlPlaneGrpcClient = new Control.ControlClient(controlPlaneChannel);
        StartCoroutine(setupRTCPeerConnection());
    }

    // Start is called before the first frame update
    void Start()
    {
        streaming = false;
        seqNum = 0;
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

        hidePlanes();

        // set up button
        submitButton.onClick.AddListener(() =>
        {
            setControlServer(addrInputField.text, portInputField.text);
        });

    }

    // Update is called once per frame
    void Update()
    {

        if (rig != null)
        {
            if (streaming && controlPlaneChannel != null && controlsChannel != null && controlsChannel.ReadyState == RTCDataChannelState.Open)
            {
                var thumbstickDir = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
                ThumbstickDirection msg = new ThumbstickDirection()
                {
                    Dx = thumbstickDir.x,
                    Dy = thumbstickDir.y,
                    SeqNum = seqNum,
                };
                byte[] msgAsBytes;
                using (var ms = new MemoryStream())
                {
                    msg.WriteDelimitedTo(ms);
                    msgAsBytes = ms.ToArray();
                }
                controlsChannel.Send(msgAsBytes);
                seqNum++;
            }


            if (OVRInput.Get(OVRInput.Button.PrimaryThumbstick))
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, rig.centerEyeAnchor.transform.rotation, (float)(followSpeedInDegrees / 180.0 * Math.PI));
            }
        }
    }

    private IEnumerator setupRTCPeerConnection()
    {
        yield return handleInfo("Checking if control plane is running");
        var healthCheckTask = controlPlaneGrpcClient.HealthCheckAsync(new HealthCheckRequest());
        while (!healthCheckTask.ResponseAsync.IsCompleted)
        {
            yield return new WaitForNextFrameUnit();
        }
        if (healthCheckTask.ResponseAsync.IsFaulted)
        {
            yield return handleError("Could not ping control plane", healthCheckTask.ResponseAsync.Exception);
            yield break;
        }

        yield return handleInfo("Initiating handshake request");
        var stream = controlPlaneGrpcClient.SendHandshake();
        

        yield return handleInfo("Setting up RTC peer connection configuration");
        RTCConfiguration config = default;

        // register ICE servers
        var iceServers = new List<RTCIceServer>();
        if (!string.IsNullOrWhiteSpace(iceInputField.text))
        {
            iceServers.Add(new RTCIceServer()
            {
                urls = new[] { iceInputField.text }
            });
        }
        config.iceServers = iceServers.ToArray();

        yield return handleInfo("Creating RTC peer connection");
        videoConnection = new RTCPeerConnection(ref config);

        // handler for video streams
        videoConnection.OnTrack = e =>
        {
            Debug.Log($"Received track. ID: {e.Track.Id}, Kind: {e.Track.Kind}");

            if (!idToIdx.ContainsKey(e.Track.Id))
            {
                Debug.LogWarning($"Track ID {e.Track.Id} cannot be handled");
                return;
            }

            int idx = idToIdx[e.Track.Id];
            if (e.Track is VideoStreamTrack video)
            {
                RawImage plane = planes[idx];
                video.OnVideoReceived += tex =>
                {
                    Debug.Log($"Setting texture for {e.Track.Id}");
                    plane.GameObject().SetActive(true);
                    plane.texture = tex;
                    Debug.Log($"Displaying video for {e.Track.Id}");
                };
            }
        };

        videoConnection.OnDataChannel = chn =>
        {
            Debug.Log($"Received data channel. Label: {chn.Label}");
            if (chn.Label == controlsLabel)
            {
                controlsChannel = chn;
            }
            else
            {
                Debug.LogWarning($"Data channel has unknown label. Label: {chn.Label}");
            }
        };

        videoConnection.OnIceCandidate = (RTCIceCandidate candidate) =>
        {
            Debug.Log($"ICE candidate: {candidate.Candidate}");
            RTCIceCandidateInit init = new RTCIceCandidateInit();
            init.candidate = candidate.Candidate;
            init.sdpMid = candidate.SdpMid;
            init.sdpMLineIndex = candidate.SdpMLineIndex;
            string json = JsonUtility.ToJson(init);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            string jsonBase64 = System.Convert.ToBase64String(bytes);
            Task.Run(async () =>
            {
                HandshakeMessage msg = new HandshakeMessage()
                {
                    Ice = new NotifyIce()
                    {
                        JsonBase64 = jsonBase64
                    }
                };
                await stream.RequestStream.WriteAsync(msg);
            });
        };

        videoConnection.OnIceConnectionChange = (RTCIceConnectionState state) =>
        {
            Debug.Log($"Connection switched to state {state}");
            switch (state)
            {
                case RTCIceConnectionState.Failed:
                    StartCoroutine(handleError("RTC Ice Connection failed", state));
                    reopenUI();
                    break;
                case RTCIceConnectionState.Closed:
                    reopenUI();
                    break;
            }
        };

        Task.Run(async () =>
        {
            while (await stream.ResponseStream.MoveNext())
            {
                var msg = stream.ResponseStream.Current;
                switch (msg.MsgCase)
                {
                    case HandshakeMessage.MsgOneofCase.Description:
                        RTCSdpType sdpType = RTCSdpType.Rollback;
                        switch (msg.Description.SdpType)
                        {
                            case SdpType.Offer:
                                sdpType = RTCSdpType.Offer;
                                break;
                            case SdpType.Pranswer:
                                sdpType = RTCSdpType.Pranswer;
                                break;
                            case SdpType.Answer:
                                sdpType = RTCSdpType.Answer;
                                break;
                            case SdpType.Rollback:
                                sdpType = RTCSdpType.Rollback;
                                break;
                        }
                        RTCSessionDescription remoteDescription = new RTCSessionDescription()
                        {
                            type = sdpType,
                            sdp = msg.Description.Sdp,
                        };
                        StartCoroutine(HandleOffer(remoteDescription, stream.RequestStream));
                        break;
                    case HandshakeMessage.MsgOneofCase.Ice:
                        RTCIceCandidateInit init = JsonUtility.FromJson<RTCIceCandidateInit>(
                                System.Text.Encoding.UTF8.GetString(
                                    System.Convert.FromBase64String(msg.Ice.JsonBase64
                                )
                            )
                        );
                        videoConnection.AddIceCandidate(new RTCIceCandidate(init));
                        break;
                    case HandshakeMessage.MsgOneofCase.None:
                        Debug.LogWarning("Got an empty message from server");
                        break;
                }
            }
            StartCoroutine(handleError("Control connection closed", null));
        });

    }

    private IEnumerator HandleOffer(RTCSessionDescription description, IAsyncStreamWriter<HandshakeMessage> writer)
    {
        yield return handleInfo("Got offer from server");
        var setRemoteDescriptionOp = videoConnection.SetRemoteDescription(ref description);
        if (setRemoteDescriptionOp.IsError)
        {
            StartCoroutine("Failed to set remote description", setRemoteDescriptionOp.Error);
            yield break;
        }
        yield return handleInfo("Succeeded in setting remote description");

        var answerOp = videoConnection.CreateAnswer();
        yield return answerOp;
        if (answerOp.IsError)
        {
            yield return handleError("Failed to create answer", answerOp.Error.message);
            yield break;
        }

        var localDescription = answerOp.Desc;
        var setLocalDescriptionOp = videoConnection.SetLocalDescription(ref localDescription);
        yield return setLocalDescriptionOp;
        if (setLocalDescriptionOp.IsError)
        {
            StartCoroutine("Failed to set local description", setLocalDescriptionOp.Error);
            yield break;
        }

        yield return handleInfo("Succeeded in setting local description");
        yield return setLocalDescriptionOp;

        // create msg
        SdpType sdpType = SdpType.Unspecified;
        switch (localDescription.type)
        {
            case RTCSdpType.Offer:
                sdpType = SdpType.Offer;
                break;
            case RTCSdpType.Pranswer:
                sdpType = SdpType.Pranswer;
                break;
            case RTCSdpType.Answer:
                sdpType = SdpType.Answer;
                break;
            case RTCSdpType.Rollback:
                sdpType = SdpType.Rollback;
                break;
        }
        Task sendAnswerTask = writer.WriteAsync(new HandshakeMessage()
        {
            Description = new NotifyDescription()
            {
                SdpType = sdpType,
                Sdp = localDescription.sdp,
            }
        });

        var numSecondsWaited = 0;
        while (!sendAnswerTask.IsCompleted)
        {
            yield return new WaitForSeconds(1);
            numSecondsWaited += 1;
            yield return handleInfo($"Sending answer for {numSecondsWaited} seconds");
        }

        yield return handleInfo("Succeeded in sending answer");
        streaming = true;
        StartCoroutine(WebRTC.Update());
    }

    public void OnDestroy() {
        closeControlPlaneConnection();
        videoConnection?.Close();
        WebRTC.Dispose();
    }

    private IEnumerator handleInfo(string msg)
    {
        Debug.Log($"{msg}");
        statusText.color = Color.green;
        statusText.text = msg;
        yield break;
    }

    private IEnumerator handleError(string msg, object err)
    {
        if (err != null)
        {
            Debug.LogError($"{msg}. Error: {err}");
        } else
        {
            Debug.LogError($"{msg}.");
        }
        statusText.color = Color.red;
        statusText.text = msg;
        reopenUI();
        yield break;
    }

    private void reopenUI()
    {
        closeControlPlaneConnection();
        closePeerConnection();
        submitButton.interactable = true;
        hidePlanes();
    }

    private void closeControlPlaneConnection()
    {
        controlPlaneChannel?.ShutdownAsync().Wait();
        controlPlaneChannel = null;
        controlPlaneGrpcClient = null;
    }

    private void hidePlanes()
    {
        foreach (var image in planes)
        {
            image?.GameObject().SetActive(false);
        }
    }

    private void closePeerConnection()
    {
        streaming = false;
        seqNum = 0;
        controlsChannel?.Close();
        videoConnection?.Close();
        controlsChannel = null;
        videoConnection = null;
    }
}