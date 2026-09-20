using UnityEngine;
using Mirro.Themes;

namespace Mirro.UI
{
    /// <summary>계절 카드 상단에 그리는 단순한 도형 엠블럼(봄=꽃, 여름=해, 가을=낙엽, 겨울=눈송이).</summary>
    public static class ThemeEmblem
    {
        public static void Build(RectTransform parent, SeasonTheme season, Color accent)
        {
            var main = new Color(1f, 1f, 1f, 0.92f);
            switch (season)
            {
                case SeasonTheme.Spring: BuildFlower(parent, main); break;
                case SeasonTheme.Summer: BuildSun(parent, main); break;
                case SeasonTheme.Autumn: BuildLeaf(parent, main, accent * 0.72f); break;
                default: BuildSnowflake(parent, main); break;
            }
        }

        private static void BuildFlower(RectTransform parent, Color color)
        {
            for (int k = 0; k < 5; k++)
            {
                float angle = (90f + k * 72f) * Mathf.Deg2Rad;
                var petal = UIFactory.NewRect("Petal", parent);
                UIFactory.SetBox(petal, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 66f, new Vector2(78f, 78f));
                UIFactory.AddCircle(petal, color);
            }
            var center = UIFactory.NewRect("Center", parent);
            UIFactory.SetBox(center, Vector2.zero, new Vector2(58f, 58f));
            UIFactory.AddCircle(center, new Color(1f, 0.86f, 0.32f));
        }

        private static void BuildSun(RectTransform parent, Color color)
        {
            var core = UIFactory.NewRect("Core", parent);
            UIFactory.SetBox(core, Vector2.zero, new Vector2(120f, 120f));
            UIFactory.AddCircle(core, color);

            for (int k = 0; k < 8; k++)
            {
                float deg = k * 45f;
                float rad = deg * Mathf.Deg2Rad;
                var ray = UIFactory.NewRect("Ray", parent);
                UIFactory.SetBox(ray, new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 98f, new Vector2(18f, 46f));
                ray.localRotation = Quaternion.Euler(0f, 0f, deg - 90f);
                UIFactory.AddRounded(ray, color, 9f);
            }
        }

        private static void BuildLeaf(RectTransform parent, Color color, Color vein)
        {
            var leaf = UIFactory.NewRect("Leaf", parent);
            UIFactory.SetBox(leaf, Vector2.zero, new Vector2(112f, 156f));
            leaf.localRotation = Quaternion.Euler(0f, 0f, 30f);
            UIFactory.AddRounded(leaf, color, 56f);

            var midrib = UIFactory.NewRect("Midrib", leaf);
            UIFactory.SetBox(midrib, Vector2.zero, new Vector2(8f, 150f));
            UIFactory.AddRounded(midrib, vein, 4f);

            var stem = UIFactory.NewRect("Stem", leaf);
            UIFactory.SetBox(stem, new Vector2(0f, -96f), new Vector2(9f, 52f));
            UIFactory.AddRounded(stem, color, 4f);
        }

        private static void BuildSnowflake(RectTransform parent, Color color)
        {
            for (int k = 0; k < 3; k++)
            {
                var arm = UIFactory.NewRect("Arm", parent);
                UIFactory.SetBox(arm, Vector2.zero, new Vector2(14f, 210f));
                arm.localRotation = Quaternion.Euler(0f, 0f, k * 60f);
                UIFactory.AddRounded(arm, color, 7f);

                for (int side = -1; side <= 1; side += 2)
                {
                    for (int end = -1; end <= 1; end += 2)
                    {
                        var twig = UIFactory.NewRect("Twig", arm);
                        UIFactory.SetBox(twig, new Vector2(side * 22f, end * 62f), new Vector2(10f, 46f));
                        twig.localRotation = Quaternion.Euler(0f, 0f, -side * end * 45f);
                        UIFactory.AddRounded(twig, color, 5f);
                    }
                }
            }

            var center = UIFactory.NewRect("Center", parent);
            UIFactory.SetBox(center, Vector2.zero, new Vector2(44f, 44f));
            UIFactory.AddCircle(center, color);
        }
    }
}
