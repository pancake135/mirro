using System;
using Unity.Netcode;
using UnityEngine;

namespace Mirro.Networking
{
    /// <summary>로비의 플레이어 한 자리. 서버가 관리하며 모든 피어에 복제된다.</summary>
    public struct PlayerSlot : INetworkSerializable, IEquatable<PlayerSlot>
    {
        public ulong clientId;
        public int colorIndex;
        public bool ready;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref colorIndex);
            serializer.SerializeValue(ref ready);
        }

        public bool Equals(PlayerSlot other) =>
            clientId == other.clientId && colorIndex == other.colorIndex && ready == other.ready;
    }

    public static class PlayerColors
    {
        public static readonly Color[] Colors =
        {
            new Color(0.90f, 0.28f, 0.28f),
            new Color(0.28f, 0.55f, 0.95f),
            new Color(0.98f, 0.82f, 0.20f),
            new Color(0.62f, 0.36f, 0.85f)
        };

        public static readonly string[] Names = { "빨강", "파랑", "노랑", "보라" };

        public static Color Get(int index) => Colors[Mathf.Abs(index) % Colors.Length];

        public static string GetName(int index) => Names[Mathf.Abs(index) % Names.Length];
    }
}
