using System;
using Unity.Netcode;

namespace Mirro.Gameplay
{
    /// <summary>탈락한 플레이어 한 명의 기록. 탈락한 순서대로 MatchManager.Eliminated에 쌓인다.</summary>
    public struct MatchEntry : INetworkSerializable, IEquatable<MatchEntry>
    {
        public ulong clientId;
        public int colorIndex;

        /// <summary>깃발을 뽑은 플레이어. 아무도 뽑지 않고 탈락했다면(연결 끊김) MatchManager.NoOne.</summary>
        public ulong eliminatedBy;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref colorIndex);
            serializer.SerializeValue(ref eliminatedBy);
        }

        public bool Equals(MatchEntry other) =>
            clientId == other.clientId && colorIndex == other.colorIndex && eliminatedBy == other.eliminatedBy;
    }
}
