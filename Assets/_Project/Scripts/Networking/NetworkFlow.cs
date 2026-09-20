using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirro.Core;
using Mirro.Themes;

namespace Mirro.Networking
{
    public enum ConnectionStatus
    {
        Idle,
        Connecting,
        Connected,
        Failed
    }

    /// <summary>
    /// 방 만들기/참가/나가기 진입점. NetworkManager는 방마다 프리팹에서 새로 만들고 나갈 때 파괴해
    /// 재사용 시의 잔여 상태 문제를 피한다. 씬 전환을 넘어 살아남도록 DontDestroyOnLoad로 유지된다.
    /// </summary>
    public class NetworkFlow : MonoBehaviour
    {
        public const ushort DefaultPort = 7777;

        /// <summary>방을 여는 수신 주소. 테스트에서는 방화벽 경고를 피하려고 127.0.0.1로 바꾼다.</summary>
        public static string ListenAddress = "0.0.0.0";

        private static NetworkFlow _instance;

        public static NetworkFlow Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("NetworkFlow");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<NetworkFlow>();
                }
                return _instance;
            }
        }

        public static bool IsRunning => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        public ConnectionStatus Status { get; private set; }
        public string FailureReason { get; private set; }

        /// <summary>메뉴 화면이 한 번 보여주고 비우는 안내 문구(예: 연결이 끊겼다는 알림).</summary>
        public string PendingMessage { get; set; }

        public bool IsLeaving { get; private set; }

        /// <summary>내가 방장으로 연 방의 게임 포트(LAN 검색 알림에 실린다).</summary>
        public ushort HostPort { get; private set; } = DefaultPort;

        public bool HostRoom(MazeThemeConfig theme, int size, ushort port = DefaultPort)
        {
            if (!PrepareManager()) return false;

            var nm = NetworkManager.Singleton;
            var transport = nm.GetComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", port, ListenAddress);
            nm.ConnectionApprovalCallback = ApproveConnection;
            HostPort = port;

            if (!nm.StartHost())
            {
                Fail("방을 만들지 못했어요. 이미 실행 중인 게임이 같은 포트를 쓰고 있을 수 있어요.");
                DestroyManager();
                return false;
            }

            var sessionPrefab = Resources.Load<GameObject>("Prefabs/NetworkSession");
            var sessionGo = Instantiate(sessionPrefab);
            sessionGo.GetComponent<NetworkObject>().Spawn(false);

            int themeIndex = 0;
            var themes = ThemeLibrary.All;
            for (int i = 0; i < themes.Count; i++)
                if (themes[i].season == theme.season) themeIndex = i;
            sessionGo.GetComponent<NetworkSession>().Configure(themeIndex, size);

            nm.OnClientDisconnectCallback += OnLocalDisconnect;
            Status = ConnectionStatus.Connected;
            FailureReason = null;

            // 방 정보를 LAN에 알리는 오브젝트는 처음 접근할 때 생성되므로 방장도 여기서 만들어 둔다.
            LanDiscovery.EnsureExists();
            return true;
        }

        public bool JoinRoom(string address, ushort port = DefaultPort)
        {
            address = (address ?? string.Empty).Trim();
            if (address.Length == 0)
            {
                Fail("IP 주소를 입력해주세요.");
                return false;
            }
            if (!PrepareManager()) return false;

            var nm = NetworkManager.Singleton;
            var transport = nm.GetComponent<UnityTransport>();
            transport.SetConnectionData(address, port);
            transport.MaxConnectAttempts = 4;
            transport.ConnectTimeoutMS = 1000;

            nm.OnClientConnectedCallback += OnLocalConnected;
            nm.OnClientDisconnectCallback += OnLocalDisconnect;

            Status = ConnectionStatus.Connecting;
            FailureReason = null;

            if (!nm.StartClient())
            {
                Fail("접속을 시작하지 못했어요. IP 주소를 확인해주세요.");
                DestroyManager();
                return false;
            }
            return true;
        }

        /// <summary>방에서 나간다. loadMenuScene이 true면 메인 메뉴 씬으로 전환한다.</summary>
        public void Leave(bool loadMenuScene, string message = null)
        {
            if (IsLeaving) return;
            StartCoroutine(LeaveRoutine(loadMenuScene, message));
        }

        private IEnumerator LeaveRoutine(bool loadMenuScene, string message)
        {
            IsLeaving = true;
            PendingMessage = message;

            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientConnectedCallback -= OnLocalConnected;
                nm.OnClientDisconnectCallback -= OnLocalDisconnect;
                nm.Shutdown();
                while (nm != null && nm.ShutdownInProgress)
                    yield return null;
            }

            DestroyManager();
            if (NetworkSession.Instance != null)
                Destroy(NetworkSession.Instance.gameObject);

            Status = ConnectionStatus.Idle;
            FailureReason = null;
            GameSession.Clear();
            IsLeaving = false;

            if (loadMenuScene)
                SceneManager.LoadScene(GameSession.MenuScene);
        }

        private bool PrepareManager()
        {
            if (IsLeaving) return false;
            if (NetworkManager.Singleton != null) DestroyManager();

            var prefab = Resources.Load<GameObject>("Prefabs/NetworkManager");
            if (prefab == null)
            {
                Fail("NetworkManager 프리팹이 없어요. 메뉴 Mirro > Setup Project를 실행해주세요.");
                return false;
            }
            Instantiate(prefab);
            return NetworkManager.Singleton != null;
        }

        private static IEnumerator DestroyManagerNextFrame()
        {
            yield return null;
            DestroyManager();
        }

        private static void DestroyManager()
        {
            if (NetworkManager.Singleton != null)
                Destroy(NetworkManager.Singleton.gameObject);
        }

        private void Fail(string reason)
        {
            Status = ConnectionStatus.Failed;
            FailureReason = reason;
        }

        private static void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;

            // 방장 자신의 연결은 거부할 수 없고(NGO가 경고를 낸다) 세션도 아직 없으므로 바로 승인한다.
            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                response.Approved = true;
                return;
            }

            var session = NetworkSession.Instance;
            if (session == null || session.CurrentPhase != SessionPhase.Lobby)
            {
                response.Approved = false;
                response.Reason = "이미 게임이 시작된 방이에요.";
            }
            else if (session.Slots.Count >= NetworkSession.MaxPlayers)
            {
                response.Approved = false;
                response.Reason = "방이 가득 찼어요 (최대 4명).";
            }
            else
            {
                response.Approved = true;
            }
        }

        private void OnLocalConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && clientId == nm.LocalClientId)
                Status = ConnectionStatus.Connected;
        }

        private void OnLocalDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || IsLeaving) return;
            // 호스트에서는 다른 클라이언트의 이탈도 이 콜백으로 오므로 클라이언트 쪽 연결 종료만 처리한다.
            if (nm.IsServer) return;

            string reason = string.IsNullOrEmpty(nm.DisconnectReason) ? null : nm.DisconnectReason;
            // 방장이 방을 닫을 때 NGO가 보내는 기본 문구(영어)는 한국어로 바꿔 보여준다.
            if (reason != null && reason.Contains("host shutting down")) reason = "방장이 방을 닫았어요.";
            bool wasConnecting = Status == ConnectionStatus.Connecting;

            if (wasConnecting)
            {
                Fail(reason ?? "방장에게 접속하지 못했어요. IP 주소와 같은 네트워크인지 확인해주세요.");
                StartCoroutine(DestroyManagerNextFrame());
            }
            else
            {
                Leave(SceneManager.GetActiveScene().name != GameSession.MenuScene,
                    reason ?? "방장과의 연결이 끊겼어요.");
            }
        }

        public static List<string> GetLocalAddresses()
        {
            var result = new List<string>();
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        result.Add(ip.ToString());
            }
            catch (Exception)
            {
                // 주소를 못 얻어도 방 자체는 동작한다.
            }
            result.Sort((a, b) => string.CompareOrdinal(a.StartsWith("192.168.") ? "0" + a : "1" + a,
                b.StartsWith("192.168.") ? "0" + b : "1" + b));
            return result;
        }
    }
}
