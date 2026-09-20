using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Mirro.Maze;
using Mirro.Networking;

namespace Mirro.Gameplay
{
    /// <summary>
    /// 플레이어의 시작 칸에 서 있는 깃발. 서버가 스폰하고, 다른 플레이어가 뽑으면 주인이 탈락하며 사라진다.
    /// 어느 플레이어의 깃발인지(id/색)는 바뀌지 않으므로 스폰 페이로드(OnSynchronize)로 보낸다.
    /// 모양은 프로그래머 아트(기둥 + 천 + 바닥 표시)로 실행 중에 만든다.
    /// </summary>
    public class Flag : NetworkBehaviour
    {
        public static readonly List<Flag> All = new List<Flag>();

        /// <summary>주인 없는 깃발(깃발 찾기 모드)의 색.</summary>
        public static readonly Color TreasureColor = new Color(1f, 0.82f, 0.2f);

        private const float PoleHeight = 2f;
        private const float ClothWidth = 0.65f;

        private ulong _playerId;
        private int _colorIndex;
        private Transform _cloth;
        private float _wavePhase;

        /// <summary>이 깃발을 가진 플레이어의 clientId.</summary>
        public ulong PlayerId => _playerId;

        public int ColorIndex => _colorIndex;

        /// <summary>안내 문구에 쓰는 깃발 이름(플레이어 색 이름, 금색 깃발은 "금색").</summary>
        public string DisplayName => MatchManager.IsTreasureId(_playerId) ? "금색" : PlayerColors.GetName(_colorIndex);

        public static Flag FindFor(ulong playerId)
        {
            foreach (var flag in All)
                if (flag._playerId == playerId) return flag;
            return null;
        }

        /// <summary>서버 전용: Spawn 호출 전에 주인을 정한다.</summary>
        public void InitServer(ulong playerId, int colorIndex)
        {
            _playerId = playerId;
            _colorIndex = colorIndex;
        }

        protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
        {
            serializer.SerializeValue(ref _playerId);
            serializer.SerializeValue(ref _colorIndex);
            base.OnSynchronize(ref serializer);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            _wavePhase = _playerId * 1.7f;
            BuildVisual();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
        }

        private void Update()
        {
            if (_cloth == null) return;
            // 천이 살랑이도록 기둥을 축으로 조금씩 흔든다.
            _cloth.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 2.2f + _wavePhase) * 9f, 0f);
        }

        private void BuildVisual()
        {
            Color color = MatchManager.IsTreasureId(_playerId) ? TreasureColor : PlayerColors.Get(_colorIndex);
            var colored = MazeBuilder.CreateColoredMaterial(color);
            var pole = MazeBuilder.CreateColoredMaterial(new Color(0.85f, 0.85f, 0.82f));
            var baseMaterial = MazeBuilder.CreateColoredMaterial(Color.Lerp(color, Color.black, 0.25f));

            AddPart(PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.02f, 0f), new Vector3(0.9f, 0.02f, 0.9f), baseMaterial);
            AddPart(PrimitiveType.Cylinder, "Pole", new Vector3(0f, PoleHeight * 0.5f, 0f), new Vector3(0.07f, PoleHeight * 0.5f, 0.07f), pole);
            AddPart(PrimitiveType.Sphere, "Tip", new Vector3(0f, PoleHeight + 0.04f, 0f), Vector3.one * 0.15f, pole);

            // 천은 기둥 끝에 붙은 피벗의 자식이라 피벗만 돌리면 기둥 쪽을 축으로 흔들린다.
            var pivot = new GameObject("ClothPivot").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(0f, PoleHeight - 0.42f, 0f);
            _cloth = pivot;

            var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cloth.name = "Cloth";
            Destroy(cloth.GetComponent<Collider>());
            cloth.transform.SetParent(pivot, false);
            cloth.transform.localPosition = new Vector3(ClothWidth * 0.5f + 0.04f, 0f, 0f);
            cloth.transform.localScale = new Vector3(ClothWidth, 0.42f, 0.03f);
            cloth.GetComponent<Renderer>().sharedMaterial = colored;
        }

        private void AddPart(PrimitiveType type, string partName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = partName;
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
