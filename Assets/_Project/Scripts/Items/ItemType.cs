using UnityEngine;

namespace Mirro.Items
{
    public enum ItemType
    {
        None = 0,
        GodsHand = 1,
        Lightning = 2,
        Radar = 3,
        Knife = 4
    }

    public static class ItemInfo
    {
        /// <summary>None을 뺀 아이템 종류 수.</summary>
        public const int Count = 4;

        public static string DisplayName(ItemType type)
        {
            switch (type)
            {
                case ItemType.GodsHand: return "신의 손";
                case ItemType.Lightning: return "번개";
                case ItemType.Radar: return "레이더";
                case ItemType.Knife: return "칼";
                default: return string.Empty;
            }
        }

        public static Color ColorOf(ItemType type)
        {
            switch (type)
            {
                case ItemType.GodsHand: return new Color(0.75f, 0.50f, 1.00f);
                case ItemType.Lightning: return new Color(1.00f, 0.90f, 0.20f);
                case ItemType.Radar: return new Color(0.30f, 0.90f, 0.55f);
                case ItemType.Knife: return new Color(0.95f, 0.40f, 0.40f);
                default: return Color.white;
            }
        }
    }
}
