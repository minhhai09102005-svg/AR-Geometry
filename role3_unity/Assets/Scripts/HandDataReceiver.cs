using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;


public class HandDataReceiver : MonoBehaviour
{
    // =====================================================
    // CONFIG
    // =====================================================

    public int port = 5052;


    // =====================================================
    // SINGLE INSTANCE
    // Tránh 2 HandDataReceiver cùng bind port 5052
    // =====================================================

    private static HandDataReceiver instance;


    // =====================================================
    // UDP
    // =====================================================

    private UdpClient udpClient;

    private Thread receiveThread;

    private volatile bool running = false;


    // =====================================================
    // DATA
    // =====================================================

    private string latestJson = "";

    private string processedJson = "";

    private readonly object dataLock = new object();


    // =====================================================
    // JSON CLASS
    // =====================================================

    [Serializable]
    public class InteractionData
    {
        public float x;

        public float y;
    }


    [Serializable]
    public class HandData
    {
        public string hand;

        public string gesture;

        public InteractionData interaction;

        public float pinch_ratio;
    }


    [Serializable]
    public class HandPacket
    {
        public string version;

        public string source;

        public int frame_id;

        public string coordinate_system;

        public HandData[] hands;
    }


    // =====================================================
    // AWAKE
    // =====================================================

    void Awake()
    {
        // Nếu đã có 1 HandDataReceiver khác
        // thì không cho object thứ 2 chạy

        if (
            instance != null
            &&
            instance != this
        )
        {
            Debug.LogError(
                "Đang có 2 HandDataReceiver trong Scene!"
            );

            enabled = false;

            return;
        }


        instance = this;
    }


    // =====================================================
    // START
    // =====================================================

    void Start()
    {
        StartReceiver();
    }


    // =====================================================
    // START UDP RECEIVER
    // =====================================================

    void StartReceiver()
    {
        // Tránh StartReceiver chạy 2 lần

        if (running)
        {
            return;
        }


        try
        {
            // Tạo UDP socket

            udpClient = new UdpClient();


            // Bind port 5052
            // IPAddress.Any = nhận từ localhost hoặc mạng LAN

            udpClient.Client.Bind(
                new IPEndPoint(
                    IPAddress.Any,
                    port
                )
            );


            running = true;


            // Tạo thread nhận dữ liệu

            receiveThread =
                new Thread(
                    ReceiveLoop
                );


            receiveThread.IsBackground = true;


            receiveThread.Start();


            Debug.Log(
                "=================================="
            );

            Debug.Log(
                "HandDataReceiver STARTED"
            );

            Debug.Log(
                "Listening UDP on port: "
                + port
            );

            Debug.Log(
                "=================================="
            );
        }


        catch (SocketException error)
        {
            Debug.LogError(
                "Không thể mở UDP port "
                + port
                + ". Port có thể đang bị chương trình khác sử dụng."
            );


            Debug.LogError(
                "Socket error: "
                + error.Message
            );


            StopReceiver();
        }


        catch (Exception error)
        {
            Debug.LogError(
                "UDP start error: "
                + error.Message
            );


            StopReceiver();
        }
    }


    // =====================================================
    // RECEIVE LOOP
    // =====================================================

    void ReceiveLoop()
    {
        IPEndPoint remoteEndPoint =
            new IPEndPoint(
                IPAddress.Any,
                0
            );


        while (running)
        {
            try
            {
                // Chờ packet từ Python

                byte[] data =
                    udpClient.Receive(
                        ref remoteEndPoint
                    );


                // bytes -> JSON string

                string json =
                    Encoding.UTF8.GetString(
                        data
                    );


                // Lưu JSON mới nhất

                lock (dataLock)
                {
                    latestJson = json;
                }
            }


            catch (SocketException)
            {
                // Khi socket bị Close()
                // Receive() sẽ thoát vào đây

                break;
            }


            catch (ObjectDisposedException)
            {
                break;
            }


            catch (Exception)
            {
                if (!running)
                {
                    break;
                }
            }
        }
    }


    // =====================================================
    // UPDATE
    // =====================================================

    void Update()
    {
        string jsonToProcess;


        lock (dataLock)
        {
            jsonToProcess = latestJson;
        }


        // Chưa có data

        if (
            string.IsNullOrEmpty(
                jsonToProcess
            )
        )
        {
            return;
        }


        // Packet này đã xử lý rồi

        if (
            jsonToProcess
            ==
            processedJson
        )
        {
            return;
        }


        processedJson =
            jsonToProcess;

        Debug.Log("UDP RECEIVED: " + jsonToProcess);
        HandPacket packet;


        // =================================================
        // JSON -> OBJECT
        // =================================================

        try
        {
            packet =
                JsonUtility
                .FromJson<HandPacket>(
                    jsonToProcess
                );
        }


        catch (Exception error)
        {
            Debug.LogWarning(
                "JSON parse error: "
                + error.Message
            );


            return;
        }


        // =================================================
        // KHÔNG CÓ HAND
        // =================================================

        if (
            packet == null
            ||
            packet.hands == null
            ||
            packet.hands.Length == 0
        )
        {
            return;
        }


        // =================================================
        // TEST HAND ĐẦU TIÊN
        // =================================================

        HandData hand =
            packet.hands[0];


        // =================================================
        // PRINT UNITY CONSOLE
        // =================================================

        Debug.Log(
            "Frame = "
            + packet.frame_id

            + " | Hand = "
            + hand.hand

            + " | Gesture = "
            + hand.gesture

            + " | x = "
            + hand.interaction.x

            + " | y = "
            + hand.interaction.y

            + " | ratio = "
            + hand.pinch_ratio
        );
    }


    // =====================================================
    // STOP RECEIVER
    // =====================================================

    void StopReceiver()
    {
        running = false;


        // Đóng UDP socket
        // sẽ làm Receive() thoát ra

        if (udpClient != null)
        {
            try
            {
                udpClient.Close();
            }
            catch
            {
            }


            udpClient = null;
        }


        // Chờ thread kết thúc

        if (
            receiveThread != null
            &&
            receiveThread.IsAlive
        )
        {
            receiveThread.Join(
                500
            );
        }


        receiveThread = null;
    }


    // =====================================================
    // KHI GAMEOBJECT BỊ DISABLE
    // =====================================================

    void OnDisable()
    {
        StopReceiver();
    }


    // =====================================================
    // KHI GAMEOBJECT BỊ DESTROY
    // =====================================================

    void OnDestroy()
    {
        StopReceiver();


        if (
            instance == this
        )
        {
            instance = null;
        }
    }


    // =====================================================
    // KHI UNITY THOÁT
    // =====================================================

    void OnApplicationQuit()
    {
        StopReceiver();
    }
}