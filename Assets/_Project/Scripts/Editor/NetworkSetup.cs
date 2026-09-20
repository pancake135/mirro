using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Editor.Configuration;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using Mirro.Gameplay;
using Mirro.Networking;
using Mirro.Player;

namespace Mirro.EditorTools
{
    /// <summary>
    /// 네트워크 프리팹(NetworkManager / NetworkSession / Player)과 프리팹 목록을 생성한다.
    /// 네트워크 프리팹은 에셋으로 존재해야 모든 피어가 같은 해시로 스폰할 수 있으므로 에디터에서 만든다.
    /// ProjectSetup.SetupAll에서 호출되며 여러 번 실행해도 안전하다(프리팹을 덮어쓴다).
    /// </summary>
    public static class NetworkSetup
    {
        private const string PrefabDir = "Assets/_Project/Resources/Prefabs";
        private const string SessionPath = PrefabDir + "/NetworkSession.prefab";
        private const string PlayerPath = PrefabDir + "/Player.prefab";
        private const string FlagPath = PrefabDir + "/Flag.prefab";
        private const string MatchPath = PrefabDir + "/MatchManager.prefab";
        private const string ManagerPath = PrefabDir + "/NetworkManager.prefab";

        public static void CreateAll()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets/_Project/Resources", "Prefabs");

            var session = CreateSessionPrefab();
            var player = CreatePlayerPrefab();
            var flag = CreateFlagPrefab();
            var match = CreateMatchPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var list = EnsurePrefabList(session, player, flag, match);
            CreateManagerPrefab(list);
            AssetDatabase.SaveAssets();
        }

        private static GameObject CreateFlagPrefab()
        {
            var go = new GameObject("Flag");
            go.AddComponent<NetworkObject>();
            go.AddComponent<Flag>();
            return SavePrefab(go, FlagPath);
        }

        private static GameObject CreateMatchPrefab()
        {
            var go = new GameObject("MatchManager");
            go.AddComponent<NetworkObject>();
            go.AddComponent<MatchManager>();
            return SavePrefab(go, MatchPath);
        }

        private static GameObject CreateSessionPrefab()
        {
            var go = new GameObject("NetworkSession");
            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkSession>();
            return SavePrefab(go, SessionPath);
        }

        private static GameObject CreatePlayerPrefab()
        {
            var root = new GameObject("Player");
            root.AddComponent<NetworkObject>();

            var transform = root.AddComponent<NetworkTransform>();
            transform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            transform.SyncRotAngleX = false;
            transform.SyncRotAngleZ = false;
            transform.SyncScaleX = false;
            transform.SyncScaleY = false;
            transform.SyncScaleZ = false;

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.enabled = false;
            var listener = camGo.AddComponent<AudioListener>();
            listener.enabled = false;

            var controller = root.AddComponent<FirstPersonController>();
            controller.playerCamera = cam;
            controller.enabled = false;

            var player = root.AddComponent<NetworkPlayer>();
            player.controller = controller;
            player.playerCamera = cam;
            player.audioListener = listener;

            return SavePrefab(root, PlayerPath);
        }

        private static NetworkPrefabsList EnsurePrefabList(params GameObject[] prefabs)
        {
            string path = NetworkPrefabProcessor.DefaultNetworkPrefabsPath;
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(path);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, path);
            }

            foreach (var prefab in prefabs)
                if (!list.Contains(prefab))
                    list.Add(new NetworkPrefab { Prefab = prefab });

            EditorUtility.SetDirty(list);
            return list;
        }

        private static void CreateManagerPrefab(NetworkPrefabsList list)
        {
            var go = new GameObject("NetworkManager");
            var manager = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();

            if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.EnableSceneManagement = false;
            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.PlayerPrefab = null;
            manager.NetworkConfig.TickRate = 30;
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            manager.RunInBackground = true;

            SavePrefab(go, ManagerPath);
        }

        private static GameObject SavePrefab(GameObject go, string path)
        {
            var asset = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            // NetworkObject의 프리팹 해시는 OnValidate에서 계산되는데, 스크립트로 만든 프리팹은 그 호출이 빠질 수 있어 직접 갱신한다.
            var networkObject = asset.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(networkObject, null);
                EditorUtility.SetDirty(asset);
                var hash = typeof(NetworkObject).GetProperty("GlobalObjectIdHash",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(networkObject);
                Debug.Log($"[Mirro] Network prefab {asset.name}: GlobalObjectIdHash={hash}");
            }
            return asset;
        }
    }
}
