using UnityEngine;
using UnityEngine.InputSystem;
using Mirro.Items;
using Mirro.Networking;

namespace Mirro.Gameplay
{
    /// <summary>
    /// 로컬 플레이어의 아이템 사용 입력. F로 쓰고(번개/레이더/칼은 즉시, 신의 손은 대상 목록을 열어 1~4로 고른다),
    /// 실제 효과는 서버가 다시 검증해 처리한다. 탈락/경직/일시정지 중에는 아무것도 하지 않는다.
    /// </summary>
    public class ItemUser : MonoBehaviour
    {
        private static readonly Key[] DigitKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };

        private NetworkPlayer _player;

        /// <summary>신의 손 대상 목록이 열려 있는지.</summary>
        public bool TargetMenuOpen { get; private set; }

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
        }

        /// <summary>그 색의 살아 있는 플레이어(나 포함). 없으면 null. 숫자키 1~4 = 색 0~3(관전 시점 전환과 같은 규칙).</summary>
        public static NetworkPlayer FindByColor(int colorIndex)
        {
            foreach (var player in NetworkPlayer.All)
                if (player.IsSpawned && player.IsAlive.Value && player.ColorIndex.Value == colorIndex) return player;
            return null;
        }

        private void Update()
        {
            var match = MatchManager.Instance;
            var keyboard = Keyboard.current;
            var controller = _player.controller;
            bool canAct = match != null && keyboard != null && !match.Finished.Value && _player.IsAlive.Value &&
                          controller.enabled && !controller.IsPaused && !controller.IsStunned;
            if (!canAct || _player.Held == ItemType.None)
            {
                TargetMenuOpen = false;
                return;
            }

            if (TargetMenuOpen)
            {
                HandleTargetMenu(keyboard);
                return;
            }
            if (!keyboard.fKey.wasPressedThisFrame) return;

            switch (_player.Held)
            {
                case ItemType.GodsHand:
                    TargetMenuOpen = true;
                    ItemEffects.PlayMenuSelect();
                    break;
                case ItemType.Knife:
                    var eye = _player.playerCamera.transform;
                    ItemEffects.PlayKnifeSwing(eye.position, eye.forward);
                    _player.UseItemRpc(MatchManager.NoOne);
                    break;
                default:
                    _player.UseItemRpc(MatchManager.NoOne);
                    break;
            }
        }

        private void HandleTargetMenu(Keyboard keyboard)
        {
            if (keyboard.fKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)
            {
                TargetMenuOpen = false;
                ItemEffects.PlayMenuSelect();
                return;
            }

            for (int i = 0; i < DigitKeys.Length; i++)
            {
                if (!keyboard[DigitKeys[i]].wasPressedThisFrame) continue;

                var target = FindByColor(i);
                if (target == null) continue;

                ItemEffects.PlayMenuSelect();
                _player.UseItemRpc(target.PlayerId);
                TargetMenuOpen = false;
                return;
            }
        }
    }
}
