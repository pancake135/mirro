using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Mirro.Networking;
using Mirro.Themes;

namespace Mirro.UI
{
    /// <summary>
    /// 참가 화면. 같은 네트워크에서 자동으로 발견된 방을 카드로 보여주고(클릭 한 번으로 참가),
    /// 아래에는 방장의 IP 주소를 직접 입력하는 칸을 남겨둔다(브로드캐스트가 막힌 네트워크용).
    /// </summary>
    public class JoinScreen
    {
        private const string LastIpKey = "mirro.lastIp";
        private const int VisibleRooms = 4;

        private class RoomRow
        {
            public RectTransform root;
            public Image dot;
            public Text name;
            public Text info;
            public Text count;
            public RoomInfo room;
        }

        public RectTransform Root { get; }

        private readonly MainMenuUI _menu;
        private readonly InputField _input;
        private readonly Text _subtitle;
        private readonly Text _status;
        private readonly Text _emptyText;
        private readonly Text _moreText;
        private readonly UIFactory.ButtonParts _connectButton;
        private readonly RoomRow[] _rows = new RoomRow[VisibleRooms];
        private bool _connecting;

        public JoinScreen(MainMenuUI menu)
        {
            _menu = menu;
            Root = menu.NewScreen("JoinScreen");

            var title = UIFactory.AddLabel(Root, "Title", "참가하기", 90, Color.white, FontStyle.Bold);
            UIFactory.SetBox(title.rectTransform, new Vector2(0f, 430f), new Vector2(1400f, 130f));

            _subtitle = UIFactory.AddLabel(Root, "Subtitle", string.Empty, 38, MainMenuUI.MutedText);
            UIFactory.SetBox(_subtitle.rectTransform, new Vector2(0f, 345f), new Vector2(1500f, 60f));

            for (int i = 0; i < _rows.Length; i++)
                _rows[i] = BuildRow(i);

            _emptyText = UIFactory.AddLabel(Root, "EmptyText",
                "발견된 방이 없어요. 같은 네트워크에서 방장이 방을 만들면 여기에 나타나요.", 34, MainMenuUI.MutedText);
            UIFactory.SetBox(_emptyText.rectTransform, new Vector2(0f, 140f), new Vector2(1600f, 60f));

            _moreText = UIFactory.AddLabel(Root, "MoreText", string.Empty, 30, MainMenuUI.MutedText);
            UIFactory.SetBox(_moreText.rectTransform, new Vector2(0f, -170f), new Vector2(1000f, 40f));

            var manualLabel = UIFactory.AddLabel(Root, "ManualLabel", "방이 안 보이면 방장의 IP 주소를 직접 입력하세요", 30, MainMenuUI.MutedText);
            UIFactory.SetBox(manualLabel.rectTransform, new Vector2(0f, -235f), new Vector2(1500f, 40f));

            var inputRt = UIFactory.NewRect("AddressInput", Root);
            UIFactory.SetBox(inputRt, new Vector2(-230f, -320f), new Vector2(800f, 100f));
            _input = UIFactory.AddInputField(inputRt, "127.0.0.1", "예: 192.168.0.12", 44,
                new Color(0.88f, 0.89f, 0.91f), new Color(0.16f, 0.18f, 0.20f));

            _connectButton = UIFactory.AddTextButton(Root, "ConnectButton", "접속", new Vector2(380f, -320f), new Vector2(280f, 100f),
                MainMenuUI.DefaultAccent, UIFactory.ContrastText(MainMenuUI.DefaultAccent), 48, ConnectManual, 44f);

            _status = UIFactory.AddLabel(Root, "Status", string.Empty, 36, MainMenuUI.MutedText);
            UIFactory.SetBox(_status.rectTransform, new Vector2(0f, -415f), new Vector2(1500f, 60f));

            UIFactory.AddTextButton(Root, "BackButton", "뒤로", new Vector2(-780f, -450f), new Vector2(240f, 90f),
                new Color(0.24f, 0.27f, 0.29f), Color.white, 40, Back, 36f);
        }

        private RoomRow BuildRow(int index)
        {
            var rt = UIFactory.NewRect("Room" + index, Root);
            UIFactory.SetBox(rt, new Vector2(0f, 235f - index * 115f), new Vector2(1100f, 100f));
            var baseImage = UIFactory.AddRounded(rt, new Color(0.88f, 0.89f, 0.91f), 40f);
            var row = new RoomRow { root = rt };
            UIFactory.MakeButton(rt, baseImage, () => ConnectTo(row.room), 1.03f);

            var dotRt = UIFactory.NewRect("Dot", rt);
            UIFactory.SetBox(dotRt, new Vector2(-500f, 0f), new Vector2(60f, 60f));
            row.dot = UIFactory.AddCircle(dotRt, Color.gray);

            Color dark = new Color(0.16f, 0.18f, 0.20f);
            row.name = UIFactory.AddLabel(rt, "Name", string.Empty, 40, dark, FontStyle.Bold);
            row.name.alignment = TextAnchor.MiddleLeft;
            UIFactory.SetBox(row.name.rectTransform, new Vector2(-205f, 0f), new Vector2(470f, 100f));

            row.info = UIFactory.AddLabel(rt, "Info", string.Empty, 34, new Color(dark.r, dark.g, dark.b, 0.75f));
            UIFactory.SetBox(row.info.rectTransform, new Vector2(200f, 0f), new Vector2(340f, 100f));

            row.count = UIFactory.AddLabel(rt, "Count", string.Empty, 36, dark, FontStyle.Bold);
            row.count.alignment = TextAnchor.MiddleRight;
            UIFactory.SetBox(row.count.rectTransform, new Vector2(450f, 0f), new Vector2(160f, 100f));

            rt.gameObject.SetActive(false);
            return row;
        }

        public void Show()
        {
            _connecting = false;
            _status.text = string.Empty;
            _input.text = PlayerPrefs.GetString(LastIpKey, "127.0.0.1");
            SetConnectEnabled(true);
            RefreshRooms();
        }

        private void Back()
        {
            if (_connecting)
                NetworkFlow.Instance.Leave(false);
            _connecting = false;
            _menu.ShowTitleScreen();
        }

        private void ConnectManual()
        {
            PlayerPrefs.SetString(LastIpKey, _input.text.Trim());
            Connect(_input.text, NetworkFlow.DefaultPort);
        }

        private void ConnectTo(RoomInfo room)
        {
            if (room == null) return;
            _input.text = room.address;
            Connect(room.address, room.port);
        }

        private void Connect(string address, ushort port)
        {
            if (_connecting) return;

            _connecting = NetworkFlow.Instance.JoinRoom(address, port);
            if (_connecting)
            {
                _status.color = MainMenuUI.MutedText;
                _status.text = "접속 중...";
                SetConnectEnabled(false);
            }
            else
            {
                ShowFailure();
            }
        }

        public void Tick()
        {
            RefreshRooms();

            if (!_connecting) return;

            var flow = NetworkFlow.Instance;
            if (flow.Status == ConnectionStatus.Failed)
            {
                _connecting = false;
                ShowFailure();
            }
            else if (flow.Status == ConnectionStatus.Connected && NetworkSession.Instance != null)
            {
                _connecting = false;
                _menu.ShowLobbyScreen();
            }
        }

        private void RefreshRooms()
        {
            IReadOnlyList<RoomInfo> rooms = LanDiscovery.Instance.Rooms;
            var themes = ThemeLibrary.All;

            for (int i = 0; i < _rows.Length; i++)
            {
                var row = _rows[i];
                if (i >= rooms.Count)
                {
                    row.room = null;
                    row.root.gameObject.SetActive(false);
                    continue;
                }

                var room = rooms[i];
                var theme = themes[Mathf.Clamp(room.themeIndex, 0, themes.Count - 1)];
                row.room = room;
                row.root.gameObject.SetActive(true);
                row.dot.color = theme.accentColor;
                row.name.text = room.hostName + "의 방";
                row.info.text = $"{theme.displayName} · {room.mazeSize}×{room.mazeSize}";
                row.count.text = $"{room.players}/{room.maxPlayers}명";
            }

            bool any = rooms.Count > 0;
            _emptyText.gameObject.SetActive(!any);
            _moreText.text = rooms.Count > VisibleRooms ? $"외 {rooms.Count - VisibleRooms}개의 방이 더 있어요" : string.Empty;
            _subtitle.text = !LanDiscovery.Enabled ? "IP 주소로 참가하세요"
                : any ? "참가할 방을 골라주세요"
                : "같은 네트워크의 방을 찾는 중...";
        }

        private void ShowFailure()
        {
            _status.color = MainMenuUI.WarningText;
            _status.text = NetworkFlow.Instance.FailureReason ?? "접속하지 못했어요.";
            SetConnectEnabled(true);
        }

        private void SetConnectEnabled(bool enabled)
        {
            _connectButton.SetInteractable(enabled, MainMenuUI.DefaultAccent, UIFactory.ContrastText(MainMenuUI.DefaultAccent));
        }
    }
}
