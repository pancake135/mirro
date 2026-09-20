using UnityEngine;
using UnityEngine.InputSystem;
using Mirro.Core;
using Mirro.Networking;

namespace Mirro.Gameplay
{
    public enum SpectatorMode
    {
        /// <summary>살아 있는 플레이어의 눈으로 그 사람이 보는 그대로(상하 시선 포함) 본다.</summary>
        Follow,

        /// <summary>어디에도 묶이지 않은 자유 카메라. 마우스로 둘러보고 WASD/Space/Ctrl로 날아다닌다.</summary>
        Free
    }

    /// <summary>
    /// 탈락한 로컬 플레이어의 관전 시점.
    /// 플레이어 시점: 좌클릭/우클릭으로 다음/이전 생존자, 1~4로 그 색의 플레이어를 바로 선택.
    /// 자유 시점: 마우스로 상하좌우 시선, WASD 이동, Space 위 / Ctrl 아래, Shift 빠르게.
    /// Tab으로 두 시점을 오간다(자유 시점으로 나갈 땐 보고 있던 자리에서 출발한다).
    /// </summary>
    public class SpectatorView : MonoBehaviour
    {
        private const float EyeHeight = 1.6f;
        private const float FollowSharpness = 14f;
        private const float FlySpeed = 10f;
        private const float FastFlyMultiplier = 3f;
        private const float MaxPitch = 89f;
        private const float MinHeight = 0.3f;
        private const float MaxHeight = 80f;

        private NetworkPlayer _self;
        private NetworkPlayer _target;
        private NetworkPlayer _hiddenBody;
        private Camera _camera;
        private float _yaw;
        private float _pitch;
        private Vector3 _freePosition;

        public SpectatorMode Mode { get; private set; } = SpectatorMode.Follow;

        /// <summary>플레이어 시점일 때 지금 따라가는 사람(자유 시점이면 null).</summary>
        public NetworkPlayer Target => Mode == SpectatorMode.Follow ? _target : null;

        /// <summary>자유 시점 카메라의 위치(테스트용).</summary>
        public Vector3 FreePosition => _freePosition;

        public void Begin(NetworkPlayer self, ulong preferredTargetId)
        {
            _self = self;
            _camera = self.playerCamera;

            var preferred = NetworkPlayer.Find(preferredTargetId);
            var first = IsWatchable(preferred) ? preferred : NextTarget(null, 1);
            if (first != null) Follow(first);
            else EnterFree();
        }

        private bool IsWatchable(NetworkPlayer player) => player != null && player != _self && player.IsSpawned && player.IsAlive.Value;

        // ---- 입력 ----

        private void Update()
        {
            if (_self == null || _self.controller.IsPaused) return;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null) HandleKeys(keyboard);

            if (Mode == SpectatorMode.Follow)
            {
                if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
                    Follow(NextTarget(_target, mouse.leftButton.wasPressedThisFrame ? 1 : -1));
            }
            else
            {
                Fly(keyboard, mouse);
            }
        }

        private void HandleKeys(Keyboard keyboard)
        {
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                if (Mode == SpectatorMode.Follow) EnterFree();
                else Follow(IsWatchable(_target) ? _target : NearestWatchable(_freePosition));
            }

            var digits = new[] { keyboard.digit1Key, keyboard.digit2Key, keyboard.digit3Key, keyboard.digit4Key };
            for (int i = 0; i < digits.Length; i++)
            {
                if (!digits[i].wasPressedThisFrame) continue;

                // 1~4는 플레이어 색(빨강/파랑/노랑/보라)과 같다. 탈락했거나 나간 사람은 고를 수 없다.
                foreach (var player in NetworkPlayer.All)
                {
                    if (player.ColorIndex.Value != i || !IsWatchable(player)) continue;
                    Follow(player);
                    break;
                }
            }
        }

        private void Fly(Keyboard keyboard, Mouse mouse)
        {
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * _self.controller.lookSensitivity;
                _yaw += delta.x;
                _pitch = Mathf.Clamp(_pitch - delta.y, -MaxPitch, MaxPitch);
            }
            if (keyboard == null) return;

            float sideways = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float forward = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            float vertical = (keyboard.spaceKey.isPressed ? 1f : 0f) - (keyboard.leftCtrlKey.isPressed ? 1f : 0f);

            // 바라보는 방향(위아래 포함)으로 날아가고, Space/Ctrl은 시선과 관계없이 수직으로 오르내린다.
            Vector3 direction = Quaternion.Euler(_pitch, _yaw, 0f) * new Vector3(sideways, 0f, forward) + Vector3.up * vertical;
            if (direction.sqrMagnitude > 1f) direction.Normalize();

            float speed = FlySpeed * (keyboard.leftShiftKey.isPressed ? FastFlyMultiplier : 1f);
            _freePosition = ClampToArena(_freePosition + direction * (speed * Time.unscaledDeltaTime));
        }

        private static Vector3 ClampToArena(Vector3 position)
        {
            var bootstrap = MazeGameBootstrap.Instance;
            if (bootstrap != null && bootstrap.Maze != null)
            {
                float extent = bootstrap.Maze.Width * bootstrap.CellSize;
                position.x = Mathf.Clamp(position.x, -4f, extent + 4f);
                position.z = Mathf.Clamp(position.z, -4f, extent + 4f);
            }
            position.y = Mathf.Clamp(position.y, MinHeight, MaxHeight);
            return position;
        }

        // ---- 시점 전환 ----

        private void EnterFree()
        {
            if (_camera == null) return;

            // 지금 보고 있던 자리와 방향에서 그대로 출발한다.
            Vector3 euler = _camera.transform.eulerAngles;
            _yaw = euler.y;
            _pitch = Mathf.DeltaAngle(0f, euler.x);
            _freePosition = ClampToArena(_camera.transform.position);
            Mode = SpectatorMode.Free;
            SetHiddenBody(null);
        }

        private void Follow(NetworkPlayer target)
        {
            if (target == null) return;
            _target = target;
            Mode = SpectatorMode.Follow;
            SetHiddenBody(target);
        }

        /// <summary>그 사람의 눈 속에서 볼 때는 몸체(코 구슬 등)가 화면에 걸리지 않게 그 사람의 몸체를 숨긴다.</summary>
        private void SetHiddenBody(NetworkPlayer player)
        {
            if (_hiddenBody != null && _hiddenBody != player) _hiddenBody.SetSpectated(false);
            _hiddenBody = player;
            if (player != null) player.SetSpectated(true);
        }

        private void LateUpdate()
        {
            if (_camera == null) return;

            if (Mode == SpectatorMode.Free)
            {
                _camera.transform.SetPositionAndRotation(_freePosition, Quaternion.Euler(_pitch, _yaw, 0f));
                return;
            }

            // 보던 사람이 탈락하거나 나가면 다른 생존자로 넘어가고, 아무도 없으면 자유 시점으로 바뀐다.
            if (!IsWatchable(_target))
            {
                var next = NextTarget(_target, 1);
                if (next == null) { EnterFree(); return; }
                Follow(next);
            }

            Vector3 position = _target.transform.position + Vector3.up * EyeHeight;
            Quaternion rotation = Quaternion.Euler(_target.LookPitch.Value, _target.transform.eulerAngles.y, 0f);
            float t = 1f - Mathf.Exp(-FollowSharpness * Time.unscaledDeltaTime);
            _camera.transform.SetPositionAndRotation(
                Vector3.Lerp(_camera.transform.position, position, t),
                Quaternion.Slerp(_camera.transform.rotation, rotation, t));
        }

        private void OnDestroy()
        {
            SetHiddenBody(null);
        }

        // ---- 대상 찾기 ----

        /// <summary>from 다음(direction=1) 또는 이전(-1)의 볼 수 있는 플레이어. from이 없어졌으면 처음부터 찾는다.</summary>
        private NetworkPlayer NextTarget(NetworkPlayer from, int direction)
        {
            var all = NetworkPlayer.All;
            int count = all.Count;
            if (count == 0) return null;

            int start = from != null ? all.IndexOf(from) : (direction > 0 ? -1 : 0);
            for (int step = 1; step <= count; step++)
            {
                int index = ((start + direction * step) % count + count) % count;
                if (IsWatchable(all[index])) return all[index];
            }
            return null;
        }

        private NetworkPlayer NearestWatchable(Vector3 position)
        {
            NetworkPlayer best = null;
            float bestDistance = float.MaxValue;
            foreach (var player in NetworkPlayer.All)
            {
                if (!IsWatchable(player)) continue;
                float distance = (player.transform.position - position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = player;
                }
            }
            return best;
        }
    }
}
