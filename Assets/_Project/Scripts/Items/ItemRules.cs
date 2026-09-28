using UnityEngine;

namespace Mirro.Items
{
    public static class ItemRules
    {
        public const float RespawnSeconds = 20f;
        public const float LightningStunSeconds = 1f;
        public const float KnifeStunSeconds = 1f;
        public const float KnifeRange = 3f;
        public const float KnifeServerSlack = 0.5f;
        public const float KnifeCosHalfAngle = 0.6428f;
        public const float RadarSeconds = 1f;
        public const float PickupServerRange = 4f;
        public const float BotPickupRange = 2f;
        public const float BotEngageRange = 6f;
        public const float BotGodsHandTimeout = 15f;

        public static int SpotCount(int mazeSize) => 4 + Mathf.Max(0, mazeSize - 10) / 20;
    }
}
