using UnityEngine;

namespace Mirro.Core
{
    /// <summary>씬 로드 전에 한 번 적용되는 전역 런타임 설정.</summary>
    public static class RuntimeSettings
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            // 템플릿 기본값은 vSync 꺼짐(프레임 무제한)이라 GPU를 불필요하게 100% 쓴다.
            QualitySettings.vSyncCount = 1;
        }
    }
}
