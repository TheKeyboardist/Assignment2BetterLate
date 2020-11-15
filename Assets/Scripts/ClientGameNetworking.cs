using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using NetworkObjects;
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

public class ClientGameNetworking : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;

    public string PlayerID;

    public GameObject PlayerPrefab;

    public GameObject playerGO;
    NetInfo playerInfo;

    [SerializeField]
    List<GameObject> AllPlayersGO = new List<GameObject>();


    void Start()
    {
        Debug.Log("Initialized.");

        m_Driver = NetworkDriver.Create();
        m_Connection = default(NetworkConnection);
        var endpoint = NetworkEndPoint.Parse(serverIP, serverPort);
        m_Connection = m_Driver.Connect(endpoint);
    }

    void SendToServer(string message)
    {
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message), Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    void OnConnect()
    {
        Debug.Log("Connected to the server.");
        PlayerID = "Player " + LocalIPAddress() + " " + System.DateTime.Now.ToString() + UnityEngine.Random.value.ToString();
        SpawnPlayer();
        InvokeRepeating("HandShake", 0.0f, 2.0f);
        InvokeRepeating("UpdateStats", 0.0f, 1.0f / 30.0f);
    }

    string LocalIPAddress()
    {
        IPHostEntry host;
        string localIP = "0.0.0.0";
        host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (IPAddress ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                localIP = ip.ToString();
                break;
            }
        }
        return localIP;
    }

    void SpawnPlayer()
    {
        Debug.Log("Player spawned.");

        Vector3 pos = new Vector3(UnityEngine.Random.Range(-2.0f, 2.0f), 0.0f, 0.0f);

        playerGO = Instantiate(PlayerPrefab, pos, new Quaternion());
        playerInfo = playerGO.GetComponent<NetInfo>();
        playerInfo.localID = m_Connection.InternalId.ToString();
        playerInfo.playerID = PlayerID;
        playerInfo.ActivateCam();
        //playerGO.AddComponent<PlayerControl>();
        AllPlayersGO.Add(playerGO);

        //// Example to send a handshake message:
        PlayerSpawnMsg m = new PlayerSpawnMsg();
        m.Position = pos;
        m.ID = PlayerID;
        SendToServer(JsonUtility.ToJson(m));
    }

    void SpawnOtherPlayer(PlayerSpawnMsg msg)
    {
        if (msg.ID != PlayerID)
        {
            GameObject otherPlayerGO = Instantiate(PlayerPrefab, msg.Position, new Quaternion());
            otherPlayerGO.GetComponent<NetInfo>().playerID = msg.ID;
            AllPlayersGO.Add(otherPlayerGO);
        }
    }

    void UpdateOtherPlayer(UpdateStatsMsg msg)
    {
        if (msg.ID != PlayerID)
        {
            GameObject Obj = FindPlayerObj(msg.ID);
            if (Obj)
            {
                Obj.transform.position = msg.Position;
                //Obj.transform.rotation = msg.Rotation;
            }
        }
    }

    GameObject FindPlayerObj(string ID)
    {
        foreach (GameObject go in AllPlayersGO)
        {
            if (go.GetComponent<NetInfo>().playerID == ID)
            {
                return go;
            }
        }
        return null;
    }

    void OnData(DataStreamReader stream)
    {
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length, Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch (header.cmd)
        {
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                //Debug.Log("Handshake message received!");
                break;

            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Player update message received!");
                break;

            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("Server update message received!");
                break;

            case Commands.REQUEST_ID:
                RequestIDMsg riMsg = JsonUtility.FromJson<RequestIDMsg>(recMsg);
                playerInfo.serverID = riMsg.ID;
                Debug.Log("Request ID message received!");
                break;

            case Commands.PLAYER_SPAWN:
                PlayerSpawnMsg psMsg = JsonUtility.FromJson<PlayerSpawnMsg>(recMsg);
                SpawnOtherPlayer(psMsg);
                break;

            case Commands.UPDATE_STATS:
                UpdateStatsMsg usMsg = JsonUtility.FromJson<UpdateStatsMsg>(recMsg);
                UpdateOtherPlayer(usMsg);
                break;

            case Commands.PLAYER_DC:
                PlayerDCMsg pdMsg = JsonUtility.FromJson<PlayerDCMsg>(recMsg);
                KillPlayer(pdMsg);
                break;

            default:
                Debug.Log("Unrecognized message received!");
                break;
        }
    }

    void Disconnect()
    {
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    void OnDisconnect()
    {
        Debug.Log("Client got disconnected from server");
        m_Connection = default(NetworkConnection);
    }

    public void OnDestroy()
    {
        m_Driver.Dispose();
    }

    void HandShake()
    {
        //// Example to send a handshake message:
        HandshakeMsg m = new HandshakeMsg();
        m.player.id = m_Connection.InternalId.ToString();
        SendToServer(JsonUtility.ToJson(m));
    }

    void KillPlayer(PlayerDCMsg msg)
    {
        Destroy(FindPlayerObj(msg.PlayerID));
    }

    void DC()
    {
        //// Example to send a handshake message:
        PlayerDCMsg m = new PlayerDCMsg();
        m.PlayerID = PlayerID;
        SendToServer(JsonUtility.ToJson(m));
    }

    void UpdateStats()
    {
        UpdateStatsMsg m = new UpdateStatsMsg();
        m.ID = PlayerID;
        m.Position = playerGO.transform.position;
        //m.Rotation = playerGO.transform.rotation;
        SendToServer(JsonUtility.ToJson(m));
    }

    void Update()
    {
        m_Driver.ScheduleUpdate().Complete();

        if (!m_Connection.IsCreated)
        {
            return;
        }

        DataStreamReader stream;
        NetworkEvent.Type cmd;
        cmd = m_Connection.PopEvent(m_Driver, out stream);
        while (cmd != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                OnConnect();
            }
            else if (cmd == NetworkEvent.Type.Data)
            {
                OnData(stream);
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                DC();
                OnDisconnect();
            }

            cmd = m_Connection.PopEvent(m_Driver, out stream);
        }

        if (Input.GetKeyDown(KeyCode.Q))
        {
            DC();
            Invoke("ExitGame", 2.0f);
        }

    }

    void ExitGame()
    {
        Application.Quit();
    }
}
