using UnityEngine;

namespace Mirro.Gameplay
{
    /// <summary>깃발 찾기 타임어택의 크기별 최고 기록(내 PC에만 저장).</summary>
    public static class TreasureRecords
    {
        private static string Key(int size) => "mirro.treasure.best." + size;

        /// <summary>저장된 최고 기록(초). 기록이 없으면 0.</summary>
        public static float Best(int size) => PlayerPrefs.GetFloat(Key(size), 0f);

        /// <summary>기록을 제출한다. 이전 최고 기록(없으면 0)을 previousBest로 돌려주고, 새 기록이면 저장하고 true.</summary>
        public static bool Submit(int size, float seconds, out float previousBest)
        {
            previousBest = Best(size);
            bool isNewRecord = previousBest <= 0f || seconds < previousBest;
            if (isNewRecord)
            {
                PlayerPrefs.SetFloat(Key(size), seconds);
                PlayerPrefs.Save();
            }
            return isNewRecord;
        }
    }
}
