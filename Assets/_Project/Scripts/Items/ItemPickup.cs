using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Mirro.Core;
using Mirro.Maze;
using Mirro.Networking;

namespace Mirro.Items
{
    /// <summary>
    /// 바닥에 떠 있는 아이템 하나. 서버가 스폰하고(종류/지점 번호는 스폰 페이로드), 바라보고 E를 누르면 줍는다(실제 판정은 서버).
    /// 모양은 종류마다 다른 프로그래머 아트 도형을 실행 중에 만든다.
    /// </summary>
    public class ItemPickup : NetworkBehaviour, IInteractable
    {
        public static readonly List<ItemPickup> All = new List<ItemPickup>();

        private const float HoverHeight = 1.3f;

        private int _type;
        private int _spot;
        private Transform _visual;

        public ItemType Type => (ItemType)_type;

        public int SpotIndex => _spot;

        public string DisplayName => ItemInfo.DisplayName(Type);

        public static ItemPickup FindByObjectId(ulong objectId)
        {
            foreach (var pickup in All)
                if (pickup != null && pickup.NetworkObjectId == objectId) return pickup;
            return null;
        }

        /// <summary>서버 전용: Spawn 호출 전에 종류와 지점 번호를 정한다.</summary>
        public void InitServer(ItemType type, int spotIndex)
        {
            _type = (int)type;
            _spot = spotIndex;
        }

        protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
        {
            serializer.SerializeValue(ref _type);
            serializer.SerializeValue(ref _spot);
            base.OnSynchronize(ref serializer);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            BuildVisual();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
        }

        private void Update()
        {
            if (_visual == null) return;
            _visual.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
            _visual.localPosition = new Vector3(0f, HoverHeight + Mathf.Sin(Time.time * 2.4f + _spot) * 0.08f, 0f);
        }

        public void Interact(GameObject instigator)
        {
            var player = instigator != null ? instigator.GetComponent<NetworkPlayer>() : null;
            if (player != null && player.IsLocalHuman) player.RequestPickupItemRpc(NetworkObjectId);
        }

        private void BuildVisual()
        {
            Color color = ItemInfo.ColorOf(Type);
            var theme = GameSession.Theme;
            Color tinted = Color.Lerp(color, theme != null ? theme.accentColor : color, 0.2f);
            var main = MazeBuilder.CreateColoredMaterial(tinted);
            var dark = MazeBuilder.CreateColoredMaterial(Color.Lerp(tinted, Color.black, 0.45f));
            var light = MazeBuilder.CreateColoredMaterial(new Color(0.92f, 0.94f, 0.96f));

            AddPart(transform, PrimitiveType.Cylinder, "Marker", new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(1.1f, 0.02f, 1.1f), dark);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            visual.localPosition = new Vector3(0f, HoverHeight, 0f);
            _visual = visual;

            switch (Type)
            {
                case ItemType.GodsHand:
                    AddPart(visual, PrimitiveType.Sphere, "Palm", Vector3.zero, Vector3.zero, Vector3.one * 0.42f, main);
                    AddPart(visual, PrimitiveType.Cylinder, "Ring", Vector3.zero, new Vector3(20f, 0f, 20f), new Vector3(0.78f, 0.015f, 0.78f), light);
                    break;
                case ItemType.Lightning:
                    AddPart(visual, PrimitiveType.Cube, "Bolt1", new Vector3(0.08f, 0.2f, 0f), new Vector3(0f, 0f, -30f), new Vector3(0.12f, 0.4f, 0.12f), main);
                    AddPart(visual, PrimitiveType.Cube, "Bolt2", new Vector3(-0.05f, -0.05f, 0f), new Vector3(0f, 0f, 30f), new Vector3(0.12f, 0.36f, 0.12f), main);
                    AddPart(visual, PrimitiveType.Cube, "Bolt3", new Vector3(0.06f, -0.3f, 0f), new Vector3(0f, 0f, -30f), new Vector3(0.12f, 0.3f, 0.12f), main);
                    break;
                case ItemType.Radar:
                    AddPart(visual, PrimitiveType.Cylinder, "Dish", new Vector3(0f, -0.1f, 0f), Vector3.zero, new Vector3(0.6f, 0.03f, 0.6f), main);
                    AddPart(visual, PrimitiveType.Cylinder, "Mast", new Vector3(0f, 0.08f, 0f), Vector3.zero, new Vector3(0.05f, 0.18f, 0.05f), light);
                    AddPart(visual, PrimitiveType.Sphere, "Tip", new Vector3(0f, 0.28f, 0f), Vector3.zero, Vector3.one * 0.12f, main);
                    break;
                default:
                    AddPart(visual, PrimitiveType.Cube, "Blade", new Vector3(0f, 0.16f, 0f), new Vector3(0f, 0f, 12f), new Vector3(0.09f, 0.56f, 0.03f), light);
                    AddPart(visual, PrimitiveType.Cube, "Handle", new Vector3(-0.04f, -0.22f, 0f), new Vector3(0f, 0f, 12f), new Vector3(0.11f, 0.2f, 0.06f), main);
                    break;
            }

            var trigger = gameObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.8f;
            trigger.center = new Vector3(0f, HoverHeight, 0f);
        }

        private static void AddPart(Transform parent, PrimitiveType type, string partName, Vector3 localPosition,
            Vector3 localEuler, Vector3 localScale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = partName;
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localEulerAngles = localEuler;
            part.transform.localScale = localScale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
