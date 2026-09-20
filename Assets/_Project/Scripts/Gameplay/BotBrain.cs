using System.Collections.Generic;
using UnityEngine;
using Mirro.Maze;
using Mirro.Networking;

namespace Mirro.Gameplay
{
    /// <summary>
    /// 서버에서 봇 하나를 조종한다. 경기 시작 후 출발 지연이 지나면 BotPlanner가 정한 경로의 칸 중심을 차례로 걸어가고,
    /// 목표 깃발 칸에 도착하면 사람과 같은 시간(PullSeconds) 서 있다가 서버 검증(ServerTryPullFlag)으로 깃발을 뽑는다.
    /// </summary>
    public class BotBrain : MonoBehaviour
    {
        /// <summary>테스트용: 0 이상이면 난이도의 출발 지연 대신 이 값(초)을 쓴다.</summary>
        public static float StartDelayOverride = -1f;

        /// <summary>테스트용: 이동 속도 배율.</summary>
        public static float SpeedMultiplier = 1f;

        private const float AwarenessInterval = 0.5f;
        private const float ArriveDistance = 0.6f;
        private const float StuckCheckSeconds = 1.5f;
        private const float StuckDistance = 0.4f;
        private const float Gravity = -20f;

        private readonly Dictionary<int, ulong> _enemyFlags = new Dictionary<int, ulong>();

        private NetworkPlayer _player;
        private CharacterController _controller;
        private MazeData _maze;
        private float _cellSize;
        private BotSettings _settings;
        private BotPlanner _planner;

        private int _pathIndex;
        private float _nextAwareness;
        private ulong _pullTarget = MatchManager.NoOne;
        private float _pullTimer;
        private float _stuckTimer;
        private Vector3 _stuckAnchor;
        private float _sidestepUntil;
        private float _sidestepSign = 1f;
        private float _verticalVelocity;

        public void Init(NetworkPlayer player, MazeData maze, float cellSize, BotDifficulty difficulty, int seed)
        {
            _player = player;
            _controller = player.GetComponent<CharacterController>();
            _maze = maze;
            _cellSize = cellSize;
            _settings = BotSettings.For(difficulty);
            _planner = new BotPlanner(maze, _settings, seed);
            _stuckAnchor = transform.position;
        }

        private int CellIndexOf(Vector3 position)
        {
            var cell = SpawnPlacer.CellAt(position, _cellSize);
            return _maze.Index(Mathf.Clamp(cell.x, 0, _maze.Width - 1), Mathf.Clamp(cell.y, 0, _maze.Height - 1));
        }

        private Vector3 CenterOf(int cellIndex)
        {
            return SpawnPlacer.CellCenter(new Vector2Int(cellIndex % _maze.Width, cellIndex / _maze.Width), _cellSize);
        }

        private void Update()
        {
            var match = MatchManager.Instance;
            if (_planner == null || match == null || match.Finished.Value || !_player.IsAlive.Value) return;

            float delay = StartDelayOverride >= 0f ? StartDelayOverride : _settings.startDelay;
            if (match.Elapsed < delay) return;

            Vector3 position = transform.position;
            int cell = CellIndexOf(position);
            _planner.MarkVisited(cell);

            if (_pullTarget != MatchManager.NoOne)
            {
                UpdatePull(match);
                return;
            }

            Replan(cell);

            var path = _planner.Path;
            if (_pathIndex >= path.Count)
            {
                // 목표 깃발 칸에 도착했다. 그 자리에 서서 뽑기 시작한다.
                if (_planner.IsChasing && Flag.FindFor(_planner.TargetFlagId) != null)
                {
                    _pullTarget = _planner.TargetFlagId;
                    _pullTimer = 0f;
                }
                return;
            }

            Vector3 goal = CenterOf(path[_pathIndex]);
            Vector3 toGoal = goal - position;
            toGoal.y = 0f;
            float distance = toGoal.magnitude;
            if (distance < ArriveDistance)
            {
                _pathIndex++;
                return;
            }

            // 순간이동 등으로 경로에서 크게 벗어났으면 새로 찾는다.
            if (distance > _cellSize * 3f)
            {
                _planner.ClearTarget();
                _pathIndex = 0;
                return;
            }

            Walk(toGoal / distance);
        }

        private void Replan(int cell)
        {
            bool targetGone = _planner.IsChasing && Flag.FindFor(_planner.TargetFlagId) == null;
            bool nothingPlanned = !_planner.IsChasing && _pathIndex >= _planner.Path.Count;
            bool spotting = _settings.explorer && !_planner.IsChasing && Time.time >= _nextAwareness;
            if (!targetGone && !nothingPlanned && !spotting) return;

            _enemyFlags.Clear();
            foreach (var flag in Flag.All)
                if (flag.PlayerId != _player.PlayerId) _enemyFlags[CellIndexOf(flag.transform.position)] = flag.PlayerId;
            _nextAwareness = Time.time + AwarenessInterval;

            // 탐색 중에는 주변에 깃발이 보이는지만 살핀다. 새로 고를 필요가 있을 때만 다시 계획한다.
            if (spotting && !targetGone && !nothingPlanned)
            {
                if (_planner.TrySpotFlag(cell, _enemyFlags)) _pathIndex = 0;
                return;
            }

            _planner.Plan(cell, _enemyFlags);
            _pathIndex = 0;
        }

        private void UpdatePull(MatchManager match)
        {
            var flag = Flag.FindFor(_pullTarget);
            if (flag == null)
            {
                _pullTarget = MatchManager.NoOne;
                _planner.ClearTarget();
                return;
            }

            Vector3 look = flag.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 360f * Time.deltaTime);

            _pullTimer += Time.deltaTime;
            if (_pullTimer < MatchManager.PullSeconds) return;

            match.ServerTryPullFlag(_player.PlayerId, _pullTarget);
            _pullTarget = MatchManager.NoOne;
            _planner.ClearTarget();
        }

        private void Walk(Vector3 direction)
        {
            // 1.5초 동안 거의 못 나아갔다면(사람에게 막힘 등) 옆으로 비켜서 돌아간다.
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer >= StuckCheckSeconds)
            {
                Vector3 moved = transform.position - _stuckAnchor;
                moved.y = 0f;
                if (moved.magnitude < StuckDistance)
                {
                    _sidestepUntil = Time.time + 1f;
                    _sidestepSign = -_sidestepSign;
                }
                _stuckAnchor = transform.position;
                _stuckTimer = 0f;
            }

            Vector3 move = direction;
            if (Time.time < _sidestepUntil) move += Vector3.Cross(Vector3.up, direction) * (0.8f * _sidestepSign);
            move.Normalize();

            _verticalVelocity = _controller.isGrounded ? -1f : _verticalVelocity + Gravity * Time.deltaTime;
            Vector3 velocity = move * (_settings.speed * SpeedMultiplier) + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 540f * Time.deltaTime);
        }
    }
}
