namespace Mirro.Core
{
    /// <summary>
    /// xorshift32 기반 PRNG. 플랫폼/스크립팅 백엔드(Mono, IL2CPP)에 관계없이
    /// 동일 seed에 대해 항상 동일한 결과를 내야 하므로 System.Random 대신 사용한다.
    /// 호스트가 seed만 전송하면 모든 클라이언트가 동일한 미로를 재생성할 수 있다.
    /// </summary>
    public class DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(int seed)
        {
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        private uint NextUInt()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        /// <summary>[minInclusive, maxExclusive) 범위의 정수 반환.</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        public float NextFloat01()
        {
            return (NextUInt() & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
