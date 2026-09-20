using UnityEngine;
using UnityEngine.InputSystem;
using Mirro.Networking;

namespace Mirro.Gameplay
{
    /// <summary>
    /// 로컬 플레이어의 깃발 뽑기 입력. 다른 플레이어의 깃발에 닿을 만큼 다가가 E를 MatchManager.PullSeconds 동안
    /// 누르고 있으면 서버에 뽑기를 요청한다(실제 판정은 서버가 다시 검증한다). 도중에 손을 떼거나 멀어지면 처음부터 다시.
    /// </summary>
    public class FlagPuller : MonoBehaviour
    {
        private NetworkPlayer _player;
        private bool _requested;

        /// <summary>지금 뽑을 수 있는 가장 가까운 남의 깃발(없으면 null).</summary>
        public Flag Candidate { get; private set; }

        /// <summary>E를 누른 채 채워지는 진행도(0~1).</summary>
        public float Progress { get; private set; }

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
        }

        private void Update()
        {
            var match = MatchManager.Instance;
            var keyboard = Keyboard.current;
            var controller = _player.controller;
            bool canAct = match != null && keyboard != null && !match.Finished.Value && _player.IsAlive.Value &&
                          controller.enabled && !controller.IsPaused && !controller.IsStunned;
            if (!canAct)
            {
                Candidate = null;
                Reset();
                return;
            }

            Candidate = FindCandidate();
            if (Candidate == null || !keyboard.eKey.isPressed)
            {
                Reset();
                return;
            }

            Progress = Mathf.Min(1f, Progress + Time.deltaTime / MatchManager.PullSeconds);
            if (Progress >= 1f && !_requested)
            {
                _requested = true;
                _player.PullFlagRpc(Candidate.PlayerId);
            }
        }

        private void Reset()
        {
            Progress = 0f;
            _requested = false;
        }

        private Flag FindCandidate()
        {
            Flag best = null;
            float bestDistance = float.MaxValue;
            Vector3 position = transform.position;

            foreach (var flag in Flag.All)
            {
                if (flag.PlayerId == _player.OwnerClientId || !MatchManager.CanReach(position, flag, 0f)) continue;

                Vector3 delta = flag.transform.position - position;
                delta.y = 0f;
                if (delta.sqrMagnitude < bestDistance)
                {
                    bestDistance = delta.sqrMagnitude;
                    best = flag;
                }
            }
            return best;
        }
    }
}
