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

public class CubemapGenerator : MonoBehaviour
{
    private const int LEFT = 0, RIGHT = 1, UP = 2, DOWN = 3, FRONT = 4, BACK = 5, NUM_DIRECTIONS = 6;

    public RawImage left, right, up, down, front, back;
    public string leftId, rightId, upId, downId, frontId, backId;

    public Canvas canvas;
    public TMP_InputField addrInputField;
    public TMP_InputField portInputField;
    public TMP_InputField iceInputField;
    public Button submitButton;
    public TMP_Text statusText;

    private RawImage[] planes;
    private Dictionary<string, int> idToIdx;

    private Channel controlChannel;
    private Control.ControlClient controlClient;
    private RTCPeerConnection videoConnection;

    private void Awake()
    {
        // Initialize WebRTC
        WebRTC.Initialize();
    }

    public void setControlServer(string addr, string port) {
        Debug.Log("Button pressed; initializing webrtc connection");
        submitButton.interactable = false;
        if (controlChannel != null) {
            closeControlServiceConnection();
        }
        if (videoConnection != null)
        {
            videoConnection.Close();
            videoConnection = null;
        }
        controlChannel = new Channel($"{addr}:{port}", ChannelCredentials.Insecure);
        controlClient = new Control.ControlClient(controlChannel);
        StartCoroutine(setupRTCPeerConnection());
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
    }

    private IEnumerator setupRTCPeerConnection()
    {

        yield return handleInfo("Initiating handshake request");
        var stream = controlClient.SendHandshake();
        

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
        videoConnection.OnTrack = e =>
        {
            Debug.Log($"Received track id: {e.Track.Id}, kind: {e.Track.Kind}");
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
        StartCoroutine(WebRTC.Update());
    }

    private void closeControlServiceConnection() {
        controlChannel?.ShutdownAsync().Wait();
        controlChannel = null;
    }

    public void OnDestroy() {
        closeControlServiceConnection();
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
        closeControlServiceConnection();
        submitButton.interactable = true;
        hidePlanes();
    }

    private void hidePlanes()
    {
        foreach (var image in planes)
        {
            image?.GameObject().SetActive(false);
        }
    }
}