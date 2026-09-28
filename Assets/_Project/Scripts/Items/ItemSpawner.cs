using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Mirro.Core;
using Mirro.Gameplay;
using Mirro.Maze;

namespace Mirro.Items
{
    /// <summary>서버 전용. 매치가 시작되면 아이템 지점을 만들고, 아이템이 주워지면 일정 시간 뒤 새 자리에 새 종류로 다시 세운다. MatchManager와 함께 사라진다.</summary>
    public class ItemSpawner : MonoBehaviour
    {
        /// <summary>테스트용: 0 이상이면 재생성 시간(초) 대신 이 값을 쓴다.</summary>
        public static float RespawnSecondsOverride = -1f;

        private class Spot
        {
            public Vector2Int cell;
            public ItemPickup pickup;
        }

        private readonly List<Spot> _spots = new List<Spot>();
        private readonly List<Vector2Int> _starts = new List<Vector2Int>();
        private MazeData _maze;
        private float _cellSize;
        private GameObject _prefab;
        private int _respawns;

        public void Begin(MazeData maze, float cellSize, IReadOnlyList<Vector2Int> startCells)
        {
            _maze = maze;
            _cellSize = cellSize;
            _starts.AddRange(startCells);
            _prefab = Resources.Load<GameObject>("Prefabs/ItemPickup");
            if (_prefab == null)
            {
                Debug.LogError("[Mirro] Prefabs/ItemPickup is missing - run Mirro > Setup Project.");
                return;
            }

            int count = ItemRules.SpotCount(Mathf.Min(maze.Width, maze.Height));
            var cells = SpawnPlacer.PlaceItemSpots(maze, count, _starts);
            for (int i = 0; i < cells.Length; i++)
            {
                _spots.Add(new Spot { cell = cells[i] });
                SpawnAt(i);
            }
            Debug.Log($"[Mirro] Placed {cells.Length} item pickup(s)");
        }

        private void SpawnAt(int index)
        {
            var spot = _spots[index];
            var type = (ItemType)Random.Range(1, ItemInfo.Count + 1);
            Vector3 center = SpawnPlacer.CellCenter(spot.cell, _cellSize);

            var go = Instantiate(_prefab, new Vector3(center.x, 0f, center.z), Quaternion.identity);
            var pickup = go.GetComponent<ItemPickup>();
            pickup.InitServer(type, index);
            go.GetComponent<NetworkObject>().Spawn(true);
            spot.pickup = pickup;
        }

        /// <summary>주워진 아이템을 치우고 그 지점의 재생성을 예약한다.</summary>
        public void OnPickedUp(ItemPickup pickup)
        {
            int index = pickup.SpotIndex;
            if (pickup.IsSpawned) pickup.NetworkObject.Despawn(true);
            if (index < 0 || index >= _spots.Count) return;

            _spots[index].pickup = null;
            StartCoroutine(Respawn(index));
        }

        private IEnumerator Respawn(int index)
        {
            yield return new WaitForSeconds(RespawnSecondsOverride >= 0f ? RespawnSecondsOverride : ItemRules.RespawnSeconds);

            var match = MatchManager.Instance;
            if (match == null || match.Finished.Value) yield break;

            var occupied = new List<Vector2Int>();
            for (int i = 0; i < _spots.Count; i++)
                if (i != index) occupied.Add(_spots[i].cell);

            var rng = new DeterministicRandom(_maze.Seed ^ (++_respawns * 7919) ^ 0x1F123BB5);
            _spots[index].cell = SpawnPlacer.PickItemSpot(_maze, _starts, occupied, rng);
            SpawnAt(index);
        }
    }
}
