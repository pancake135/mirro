using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Mirro.Gameplay;
using Mirro.Maze;
using Mirro.Player;

namespace Mirro.Networking
{
    /// <summary>
    /// 플레이어 프리팹의 네트워크 동작. 소유자(로컬)는 1인칭 컨트롤러/카메라를 켜고, 다른 사람의 캐릭터는
    /// 색이 입혀진 몸체(캡슐)로 보여준다. 이동은 소유자 권위(NetworkTransform Owner)이고, 깃발/아이템 같은
    /// 게임 효과는 서버가 판정한다.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        public static event Action<NetworkPlayer> LocalPlayerSpawned;

        public static NetworkPlayer Local { get; private set; }
        public static readonly List<NetworkPlayer> All = new List<NetworkPlayer>();

        public readonly NetworkVariable<int> ColorIndex = new NetworkVariable<int>();

        /// <summary>false면 탈락한 플레이어. 서버가 정하고, 탈락하면 몸체가 사라지고 본인은 관전 시점으로 바뀐다.</summary>
        public readonly NetworkVariable<bool> IsAlive = new NetworkVariable<bool>(true);

        /// <summary>이 플레이어의 상하 시선 각도. 좌우 회전은 NetworkTransform이 실어 나르지만 상하는 없어서 소유자가 따로 알린다(관전용).</summary>
        public readonly NetworkVariable<float> LookPitch = new NetworkVariable<float>(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // 소유자 권위 NetworkTransform에서는 서버가 정한 스폰 위치가 소유자 쪽 인스턴스에 반영되지 않고 원점에서
        // 생성된다. 그래서 스폰 위치/방향을 스폰 페이로드(OnSynchronize)에 실어 보내 모든 피어가 직접 맞춘다.
        private Vector3 _spawnPosition;
        private float _spawnYaw;

        public FirstPersonController controller;
        public Camera playerCamera;
        public AudioListener audioListener;

        private Renderer _bodyRenderer;
        private Renderer _noseRenderer;
        private CapsuleCollider _bodyCollider;
        private bool _hiddenBySpectator;
        private float _nextPitchSync;

        public Color PlayerColor => PlayerColors.Get(ColorIndex.Value);

        /// <summary>서버가 정한 시작 위치/방향(신의 손 등으로 "시작 지점으로 보낼 때" 쓴다).</summary>
        public Vector3 SpawnPosition => _spawnPosition;

        public float SpawnYaw => _spawnYaw;

        public static NetworkPlayer Find(ulong clientId)
        {
            foreach (var player in All)
                if (player.OwnerClientId == clientId) return player;
            return null;
        }

        /// <summary>서버 전용: Spawn 호출 전에 스폰 위치/방향을 정한다(스폰 페이로드에 함께 실린다).</summary>
        public void InitServer(Vector3 position, float yaw)
        {
            _spawnPosition = position;
            _spawnYaw = yaw;
        }

        protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
        {
            serializer.SerializeValue(ref _spawnPosition);
            serializer.SerializeValue(ref _spawnYaw);
            base.OnSynchronize(ref serializer);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            ColorIndex.OnValueChanged += OnColorChanged;
            IsAlive.OnValueChanged += OnAliveChanged;

            ApplySpawnPose();

            if (IsOwner) SetupLocal();
            else SetupRemote();
        }

        private void Update()
        {
            // 상하 시선은 자주 바뀌므로 조금이라도 달라졌을 때만, 초당 20번 이하로 보낸다.
            if (!IsSpawned || !IsOwner || Time.unscaledTime < _nextPitchSync) return;

            float pitch = controller.Pitch;
            if (Mathf.Abs(pitch - LookPitch.Value) < 0.25f) return;

            LookPitch.Value = pitch;
            _nextPitchSync = Time.unscaledTime + 0.05f;
        }

        /// <summary>깃발을 뽑겠다는 요청. 서버가 거리/생존 여부를 다시 검증한 뒤에만 탈락이 확정된다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void PullFlagRpc(ulong targetPlayerId, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            MatchManager.Instance?.ServerTryPullFlag(OwnerClientId, targetPlayerId);
        }

        /// <summary>관전 중인 사람이 이 캐릭터의 눈으로 보고 있을 때 몸체가 화면에 걸리지 않도록 숨긴다.</summary>
        public void SetSpectated(bool spectated)
        {
            _hiddenBySpectator = spectated;
            RefreshBodyVisibility();
        }

        private void OnAliveChanged(bool previous, bool alive)
        {
            RefreshBodyVisibility();
            if (!alive && IsOwner) EnterSpectator();
        }

        private void RefreshBodyVisibility()
        {
            if (_bodyRenderer == null) return;

            bool visible = IsAlive.Value && !_hiddenBySpectator;
            _bodyRenderer.enabled = visible;
            _noseRenderer.enabled = visible;
            _bodyCollider.enabled = IsAlive.Value;
        }

        /// <summary>탈락: 조작을 끄고 살아 있는 다른 플레이어의 시점으로 관전한다(처음엔 깃발을 뽑은 사람).</summary>
        private void EnterSpectator()
        {
            controller.enabled = false;

            ulong killer = MatchManager.NoOne;
            var match = MatchManager.Instance;
            if (match != null)
            {
                for (int i = 0; i < match.Eliminated.Count; i++)
                    if (match.Eliminated[i].clientId == OwnerClientId) killer = match.Eliminated[i].eliminatedBy;
            }

            if (GetComponent<SpectatorView>() == null)
                gameObject.AddComponent<SpectatorView>().Begin(this, killer);
        }

        /// <summary>
        /// 소유자 전용 순간이동. 다른 화면에서 이 캐릭터가 미로를 가로질러 미끄러지며 다른 플레이어를 밀어내지 않도록
        /// 위치 보간 대신 NetworkTransform.Teleport로 즉시 이동시킨다(신의 손 아이템 등에서 사용).
        /// </summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            var cc = GetComponent<CharacterController>();
            bool wasEnabled = cc.enabled;
            cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            GetComponent<NetworkTransform>().Teleport(position, rotation, transform.localScale);
            cc.enabled = wasEnabled;
        }

        private void ApplySpawnPose()
        {
            if (_spawnPosition == Vector3.zero) return;

            var cc = GetComponent<CharacterController>();
            bool wasEnabled = cc.enabled;
            cc.enabled = false;
            transform.SetPositionAndRotation(_spawnPosition, Quaternion.Euler(0f, _spawnYaw, 0f));
            cc.enabled = wasEnabled;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            ColorIndex.OnValueChanged -= OnColorChanged;
            IsAlive.OnValueChanged -= OnAliveChanged;
            if (Local == this) Local = null;
        }

        private void SetupLocal()
        {
            Local = this;
            gameObject.AddComponent<FlagPuller>();
            controller.enabled = true;
            playerCamera.enabled = true;
            playerCamera.tag = "MainCamera";
            audioListener.enabled = true;
            LocalPlayerSpawned?.Invoke(this);
        }

        private void SetupRemote()
        {
            // 원격 캐릭터는 CharacterController 대신 일반 콜라이더로 충돌/피격 대상이 된다.
            GetComponent<CharacterController>().enabled = false;
            _bodyCollider = gameObject.AddComponent<CapsuleCollider>();
            _bodyCollider.height = 1.8f;
            _bodyCollider.radius = 0.35f;
            _bodyCollider.center = new Vector3(0f, 0.9f, 0f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            _bodyRenderer = body.GetComponent<Renderer>();

            var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            nose.name = "Nose";
            Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.55f, 0.3f);
            nose.transform.localScale = Vector3.one * 0.26f;
            _noseRenderer = nose.GetComponent<Renderer>();

            ApplyColor();
            RefreshBodyVisibility();
        }

        private void OnColorChanged(int previous, int current) => ApplyColor();

        private void ApplyColor()
        {
            if (_bodyRenderer == null) return;
            _bodyRenderer.sharedMaterial = MazeBuilder.CreateColoredMaterial(PlayerColor);
            _noseRenderer.sharedMaterial = MazeBuilder.CreateColoredMaterial(Color.white);
        }
    }
}
