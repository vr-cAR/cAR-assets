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

public class WebRtcHandler : MonoBehaviour
{
    public delegate void OnStatusUpdate(string status, bool isError);
    public delegate void OnConnectionUpdate(bool connected);

    public string[] iceServers;

    private Channel controlPlaneChannel = null;
    private Control.ControlClient controlPlaneGrpcClient = null;
    private RTCPeerConnection videoConnection = null;
    private long seqNum = 0;

    private Dictionary<String, DelegateOnTrack> trackIdToDelegate = new Dictionary<string, DelegateOnTrack>();
    private Dictionary<String, RTCDataChannel> dataChannels = new Dictionary<string, RTCDataChannel>();

    [HideInInspector]
    public OnStatusUpdate onStatusUpdate
    {
        private get;
        set;
    }

    [HideInInspector]
    public OnConnectionUpdate onConnectionUpdate
    {
        private get;
        set;
    }

    public void setControlServer(string addr, string port) {
        Debug.Log("Button pressed; initializing webrtc connection");
        if (this.controlPlaneChannel != null) {
            closeControlPlaneConnection();
        }
        if (this.videoConnection != null)
        {
            this.videoConnection.Close();
            this.videoConnection = null;
        }
        this.controlPlaneChannel = new Channel($"{addr}:{port}", ChannelCredentials.Insecure);
        this.controlPlaneGrpcClient = new Control.ControlClient(controlPlaneChannel);
        StartCoroutine(setupRTCPeerConnection());
    }

    public bool sendControlsMessage(string chanLabel, float dx, float dy)
    {
        //Debug.Log("in sendControlsMessage");
        if (!this.dataChannels.ContainsKey(chanLabel) || this.dataChannels[chanLabel].ReadyState != RTCDataChannelState.Open)
        {
            //Debug.Log("exiting sendControlsMessage");
            return false;
        }
        Debug.Log($"Sending direction <{dx}, {dy}> to remote channel {chanLabel}. Open channels: {this.dataChannels}");
        var chan = dataChannels[chanLabel];

        ThumbstickDirection msg = new ThumbstickDirection()
        {
            Dx = dx,
            Dy = dy,
            SeqNum = seqNum,
        };

        byte[] msgAsBytes;
        using (var ms = new MemoryStream())
        {
            msg.WriteDelimitedTo(ms);
            msgAsBytes = ms.ToArray();
        }
        chan.Send(msgAsBytes);
        seqNum++;
        return true;
    }

    public void addTrackDelegate(string trackId, DelegateOnTrack del)
    {
        this.trackIdToDelegate[trackId] = del;
    }

    // Like Start() but called regardless of if the script is enabled or not
    void Awake()
    {
        // Initialize WebRTC
        WebRTC.Initialize(limitTextureSize: true, enableNativeLog: true);
    }

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {   
    }

    private IEnumerator setupRTCPeerConnection()
    {
        yield return handleInfo("Checking if control plane is running");
        var healthCheckTask = this.controlPlaneGrpcClient.HealthCheckAsync(new HealthCheckRequest());
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
        var stream = this.controlPlaneGrpcClient.SendHandshake();
        

        yield return handleInfo("Setting up RTC peer connection configuration");
        RTCConfiguration config = default;

        // register ICE servers
        var iceServers = new List<RTCIceServer>();
        foreach (var ice in this.iceServers) {
            iceServers.Add(new RTCIceServer()
            {
                urls = new[] { ice.Trim() }
            });
        }
        config.iceServers = iceServers.ToArray();

        yield return handleInfo("Creating RTC peer connection");
        this.videoConnection = new RTCPeerConnection(ref config);

        // handler for video streams
        this.videoConnection.OnTrack = e =>
        {
            Debug.Log($"Received track. ID: {e.Track.Id}, Kind: {e.Track.Kind}");

            if (!trackIdToDelegate.ContainsKey(e.Track.Id))
            {
                Debug.LogWarning($"Track ID {e.Track.Id} cannot be handled");
                return;
            }

            var del = trackIdToDelegate[e.Track.Id];
            del(e);
        };

        this.videoConnection.OnDataChannel = chn =>
        {
            Debug.Log($"Received data channel. Label: {chn.Label}");
            dataChannels[chn.Label] = chn;
        };

        this.videoConnection.OnIceCandidate = (RTCIceCandidate candidate) =>
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

        this.videoConnection.OnIceConnectionChange = (RTCIceConnectionState state) =>
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
        onConnectionUpdate(true);
        StartCoroutine(WebRTC.Update());
    }

    public void OnDestroy() {
        closeControlPlaneConnection();
        this.videoConnection?.Close();
        WebRTC.Dispose();
    }

    private IEnumerator handleInfo(string msg)
    {
        Debug.Log($"{msg}");
        onStatusUpdate(msg, false);
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
        onStatusUpdate(msg, true);
        reopenUI();
        yield break;
    }

    private void reopenUI()
    {
        closeControlPlaneConnection();
        closePeerConnection();
        onConnectionUpdate(false);
    }

    private void closeControlPlaneConnection()
    {
        this.controlPlaneChannel?.ShutdownAsync().Wait();
        this.controlPlaneChannel = null;
        this.controlPlaneGrpcClient = null;
    }

    private void closePeerConnection()
    {
        seqNum = 0;
        foreach (var (_, chn) in dataChannels)
        {
            chn.Close();
        }
        this.dataChannels.Clear();
        this.videoConnection?.Close();
        this.videoConnection = null;
    }
}