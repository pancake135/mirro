using System.Collections.Generic;
using UnityEngine;
using Mirro.Maze;
using Mirro.Networking;

namespace Mirro.Items
{
    /// <summary>
    /// 아이템 효과음/파티클. 모든 피어가 로컬에서 재생하고 네트워크는 쓰지 않는다. 클립은 Resources/Audio/Items에서 이름으로 불러오고,
    /// 파티클은 코드로 만든 1회성 ParticleSystem이라 재생 뒤 스스로 파괴된다.
    /// </summary>
    public static class ItemEffects
    {
        public const string Pickup = "item_pickup";
        public const string GodsHand = "item_godshand";
        public const string MenuSelect = "item_menu_select";
        public const string Lightning = "item_lightning";
        public const string Radar = "item_radar";
        public const string Knife = "item_knife";

        /// <summary>이름별 재생 횟수. 테스트가 어느 화면에서 어떤 효과가 재생됐는지 확인하는 데 쓴다.</summary>
        public static readonly Dictionary<string, int> PlayCounts = new Dictionary<string, int>();

        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();

        public static void PlayPickup(Vector3 position)
        {
            Play(Pickup, position, 0.9f);
            Burst(position + Vector3.up * 0.6f, ItemInfo.ColorOf(ItemType.Lightning), 16, 2.5f, 0.12f, 0.55f);
        }

        public static void PlayStunned(Vector3 position)
        {
            Play(Lightning, position, 1f);
            Burst(position, ItemInfo.ColorOf(ItemType.Lightning), 24, 4f, 0.1f, 0.45f, radius: 0.4f);
        }

        public static void PlayTeleport(Vector3 position)
        {
            Play(GodsHand, position, 1f);
            Burst(position, ItemInfo.ColorOf(ItemType.GodsHand), 40, 3f, 0.14f, 0.6f, radius: 0.5f);
        }

        public static void PlayRadar(Vector3 position)
        {
            Play(Radar, position, 0.9f);
            Burst(position, ItemInfo.ColorOf(ItemType.Radar), 48, 7f, 0.16f, 0.6f,
                ParticleSystemShapeType.Circle, 0.3f, Quaternion.Euler(90f, 0f, 0f), ringOnly: true);
        }

        public static void PlayKnifeSwing(Vector3 origin, Vector3 forward)
        {
            Play(Knife, origin, 1f);
            Burst(origin + forward.normalized * 1.2f, new Color(0.92f, 0.94f, 0.96f), 12, 5f, 0.08f, 0.25f);
        }

        public static void PlayMenuSelect()
        {
            var local = NetworkPlayer.Local;
            Vector3 at = local != null ? local.transform.position + Vector3.up * 1.6f : Vector3.zero;
            Play(MenuSelect, at, 0.8f);
        }

        private static void Play(string clipName, Vector3 position, float volume)
        {
            PlayCounts.TryGetValue(clipName, out int count);
            PlayCounts[clipName] = count + 1;

            if (!Clips.TryGetValue(clipName, out var clip) || clip == null)
            {
                clip = Resources.Load<AudioClip>("Audio/Items/" + clipName);
                Clips[clipName] = clip;
            }
            if (clip != null) AudioSource.PlayClipAtPoint(clip, position, volume);
        }

        private static void Burst(Vector3 position, Color color, int count, float speed, float size, float life,
            ParticleSystemShapeType shape = ParticleSystemShapeType.Sphere, float radius = 0.1f,
            Quaternion? rotation = null, bool ringOnly = false)
        {
            // 비활성 상태에서 설정한 뒤 켜야 기본 설정으로 먼저 재생되지 않는다.
            var go = new GameObject("ItemBurst");
            go.SetActive(false);
            go.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);

            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.duration = life;
            main.loop = false;
            main.startLifetime = life;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shapeModule = system.shape;
            shapeModule.shapeType = shape;
            shapeModule.radius = radius;
            if (ringOnly) shapeModule.radiusThickness = 0f;

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = GlowMaterial(color);
            go.SetActive(true);
            Object.Destroy(go, life + 1f);
        }

        private static Material GlowMaterial(Color color)
        {
            if (Materials.TryGetValue(color, out var cached) && cached != null) return cached;

            var material = MazeBuilder.CreateColoredMaterial(color);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.6f);
            Materials[color] = material;
            return material;
        }
    }
}
