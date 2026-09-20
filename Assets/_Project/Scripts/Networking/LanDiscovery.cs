using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace Mirro.Networking
{
    /// <summary>같은 네트워크(LAN)에서 발견된 방 하나의 정보.</summary>
    public class RoomInfo
    {
        public string id;
        public string hostName;
        public string address;
        public ushort port;
        public int themeIndex;
        public int mazeSize;
        public int players;
        public int maxPlayers;
        public float lastSeen;
    }

    /// <summary>
    /// LAN 방 자동 검색. 방장은 로비에 있는 동안 1초마다 방 정보를 UDP로 알리고(각 네트워크 어댑터의 브로드캐스트 주소),
    /// 참가 화면이 열려 있는 동안 수신 소켓을 열어 발견된 방 목록을 만든다. 게임이 시작됐거나 방이 가득 차면 알림을
    /// 멈추므로 목록에서 자동으로 사라진다. 모든 소켓 작업은 메인 스레드(Update)에서 논블로킹으로 처리한다.
    /// </summary>
    public class LanDiscovery : MonoBehaviour
    {
        public const int DiscoveryPort = 47777;
        private const string Magic = "MIRRO1";
        private const float BeaconInterval = 1f;
        private const float RoomTimeout = 3.5f;

        /// <summary>false면 알림/검색을 모두 끈다(테스트에서 IP 직접 입력 경로만 검증할 때 사용).</summary>
        public static bool Enabled = true;

        /// <summary>true면 loopback으로만 주고받는다. 테스트가 방화벽 경고와 실제 LAN 트래픽을 피하기 위해 쓴다.</summary>
        public static bool LoopbackOnly;

        private static LanDiscovery _instance;

        public static LanDiscovery Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("LanDiscovery");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<LanDiscovery>();
                }
                return _instance;
            }
        }

        /// <summary>현재 발견된 방 목록(이름순). 매 프레임 갱신된다.</summary>
        public IReadOnlyList<RoomInfo> Rooms => _rooms;

        public bool IsListening => _listener != null;

        private readonly Dictionary<string, RoomInfo> _found = new Dictionary<string, RoomInfo>();
        private readonly List<RoomInfo> _rooms = new List<RoomInfo>();
        private readonly string _roomId = Guid.NewGuid().ToString("N").Substring(0, 8);

        private UdpClient _listener;
        private UdpClient _sender;
        private float _nextBeacon;
        private bool _loggedSendError;

        public void StartListening()
        {
            if (!Enabled || _listener != null) return;

            try
            {
                var udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(LoopbackOnly ? IPAddress.Loopback : IPAddress.Any, DiscoveryPort));
                _listener = udp;
            }
            catch (SocketException e)
            {
                Debug.LogWarning("[Mirro] LAN discovery listener could not start: " + e.Message);
                _listener = null;
            }
        }

        /// <summary>알림(방장) 또는 검색(참가자)을 위해 오브젝트가 살아 있도록 보장한다.</summary>
        public static void EnsureExists() => _ = Instance;

        /// <summary>인스턴스가 없으면 만들지 않고 멈춘다(종료/씬 정리 중에 오브젝트를 새로 만들지 않으려고).</summary>
        public static void StopListeningIfExists()
        {
            if (_instance != null) _instance.StopListening();
        }

        public void StopListening()
        {
            _listener?.Close();
            _listener = null;
            _found.Clear();
            _rooms.Clear();
        }

        private void Update()
        {
            if (_listener != null) ReceiveAll();
            PruneAndRebuild();
            TryAdvertise();
        }

        private void OnDestroy()
        {
            _listener?.Close();
            _sender?.Close();
        }

        // ---- 수신 ----

        private void ReceiveAll()
        {
            try
            {
                while (_listener != null && _listener.Available > 0)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _listener.Receive(ref from);
                    Parse(data, from.Address);
                }
            }
            catch (SocketException)
            {
                // 일시적인 수신 오류(예: 이전 전송에 대한 ICMP 응답)는 무시하고 다음 프레임에 계속한다.
            }
        }

        private void Parse(byte[] data, IPAddress source)
        {
            string[] parts = Encoding.UTF8.GetString(data).Split('|');
            if (parts.Length != 8 || parts[0] != Magic) return;
            if (!int.TryParse(parts[3], out int theme) || !int.TryParse(parts[4], out int size) ||
                !int.TryParse(parts[5], out int players) || !int.TryParse(parts[6], out int max) ||
                !ushort.TryParse(parts[7], out ushort port))
                return;

            string id = parts[1];
            if (!_found.TryGetValue(id, out var room))
            {
                room = new RoomInfo { id = id };
                _found[id] = room;
            }

            room.hostName = parts[2];
            room.address = source.ToString();
            room.port = port;
            room.themeIndex = theme;
            room.mazeSize = size;
            room.players = players;
            room.maxPlayers = max;
            room.lastSeen = Time.unscaledTime;
        }

        private void PruneAndRebuild()
        {
            _rooms.Clear();
            List<string> expired = null;
            foreach (var pair in _found)
            {
                if (Time.unscaledTime - pair.Value.lastSeen > RoomTimeout)
                    (expired ??= new List<string>()).Add(pair.Key);
                else
                    _rooms.Add(pair.Value);
            }
            if (expired != null)
                foreach (var id in expired) _found.Remove(id);

            _rooms.Sort((a, b) => string.CompareOrdinal(a.hostName, b.hostName));
        }

        // ---- 송신 ----

        private void TryAdvertise()
        {
            if (!Enabled || !NetworkFlow.IsRunning || !NetworkManager.Singleton.IsServer) return;

            var session = NetworkSession.Instance;
            if (session == null || session.IsSolo.Value || session.CurrentPhase != SessionPhase.Lobby ||
                session.Slots.Count >= NetworkSession.MaxPlayers)
                return;

            if (Time.unscaledTime < _nextBeacon) return;
            _nextBeacon = Time.unscaledTime + BeaconInterval;

            string hostName = Environment.MachineName.Replace('|', ' ');
            string message = string.Join("|", Magic, _roomId, hostName, session.ThemeIndex.Value, session.MazeSize.Value,
                session.Slots.Count, NetworkSession.MaxPlayers, NetworkFlow.Instance.HostPort);
            byte[] data = Encoding.UTF8.GetBytes(message);

            _sender ??= new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            foreach (var target in GetBroadcastTargets())
            {
                try
                {
                    _sender.Send(data, data.Length, new IPEndPoint(target, DiscoveryPort));
                }
                catch (SocketException e)
                {
                    if (_loggedSendError) continue;
                    _loggedSendError = true;
                    Debug.LogWarning($"[Mirro] LAN discovery beacon to {target} failed: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 비콘을 보낼 주소들. 각 활성 어댑터의 방향성 브로드캐스트(IP | ~서브넷마스크)와 255.255.255.255를 사용한다
        /// (VPN/가상 어댑터가 있어도 실제 LAN 어댑터로 나가도록 어댑터별로 보낸다).
        /// </summary>
        public static List<IPAddress> GetBroadcastTargets()
        {
            var targets = new List<IPAddress>();
            if (LoopbackOnly)
            {
                targets.Add(IPAddress.Loopback);
                return targets;
            }

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;

                        byte[] ip = unicast.Address.GetAddressBytes();
                        byte[] mask = unicast.IPv4Mask.GetAddressBytes();
                        var broadcast = new byte[4];
                        for (int i = 0; i < 4; i++)
                            broadcast[i] = (byte)(ip[i] | ~mask[i]);

                        var address = new IPAddress(broadcast);
                        if (!targets.Contains(address)) targets.Add(address);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Mirro] Could not enumerate network adapters: " + e.Message);
            }

            if (!targets.Contains(IPAddress.Broadcast)) targets.Add(IPAddress.Broadcast);
            return targets;
        }
    }
}
