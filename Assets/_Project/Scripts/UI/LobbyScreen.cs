using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Mirro.Networking;

namespace Mirro.UI
{
    /// <summary>
    /// 대기실. 방장과 참가자가 함께 쓰며, NetworkSession의 상태(슬롯/준비/테마/크기)를 매 프레임 반영한다.
    /// 방장은 [게임 시작], 참가자는 [준비] 버튼을 본다.
    /// </summary>
    public class LobbyScreen
    {
        private class SlotCard
        {
            public Image background;
            public Image dot;
            public Text name;
            public Text state;
            public Text tag;
        }

        private static readonly Color CardFilled = new Color(0.17f, 0.20f, 0.23f);
        private static readonly Color CardLocal = new Color(0.23f, 0.27f, 0.31f);
        private static readonly Color CardEmpty = new Color(0.11f, 0.13f, 0.15f);
        private static readonly Color ReadyGreen = new Color(0.45f, 0.85f, 0.55f);

        public RectTransform Root { get; }

        private readonly MainMenuUI _menu;
        private readonly Text _info;
        private readonly Text _address;
        private readonly Text _hint;
        private readonly SlotCard[] _cards = new SlotCard[NetworkSession.MaxPlayers];
        private readonly UIFactory.ButtonParts _mainButton;
        private bool _isHost;
        private bool _localReady;

        public LobbyScreen(MainMenuUI menu)
        {
            _menu = menu;
            Root = menu.NewScreen("LobbyScreen");

            var title = UIFactory.AddLabel(Root, "Title", "대기실", 90, Color.white, FontStyle.Bold);
            UIFactory.SetBox(title.rectTransform, new Vector2(0f, 430f), new Vector2(1400f, 130f));

            _info = UIFactory.AddLabel(Root, "Info", string.Empty, 46, MainMenuUI.MutedText, FontStyle.Bold);
            UIFactory.SetBox(_info.rectTransform, new Vector2(0f, 340f), new Vector2(1500f, 70f));

            _address = UIFactory.AddLabel(Root, "Address", string.Empty, 32, MainMenuUI.MutedText);
            UIFactory.SetBox(_address.rectTransform, new Vector2(0f, 280f), new Vector2(1700f, 50f));

            for (int i = 0; i < _cards.Length; i++)
                _cards[i] = BuildCard(i);

            _hint = UIFactory.AddLabel(Root, "Hint", string.Empty, 36, MainMenuUI.MutedText);
            UIFactory.SetBox(_hint.rectTransform, new Vector2(0f, -140f), new Vector2(1500f, 60f));

            _mainButton = UIFactory.AddTextButton(Root, "MainButton", string.Empty, new Vector2(0f, -290f), new Vector2(580f, 120f),
                MainMenuUI.DefaultAccent, Color.white, 56, OnMainButton, 48f);

            UIFactory.AddTextButton(Root, "LeaveButton", "나가기", new Vector2(-780f, -450f), new Vector2(240f, 90f),
                new Color(0.24f, 0.27f, 0.29f), Color.white, 40, () => menu.LeaveRoomToTitle(), 36f);
        }

        private SlotCard BuildCard(int index)
        {
            float x = (index - (NetworkSession.MaxPlayers - 1) * 0.5f) * 430f;
            var rt = UIFactory.NewRect("Slot" + index, Root);
            UIFactory.SetBox(rt, new Vector2(x, 90f), new Vector2(400f, 300f));
            var card = new SlotCard { background = UIFactory.AddRounded(rt, CardEmpty, 44f) };

            var dotRt = UIFactory.NewRect("Dot", rt);
            UIFactory.SetBox(dotRt, new Vector2(0f, 70f), new Vector2(110f, 110f));
            card.dot = UIFactory.AddCircle(dotRt, CardEmpty);

            card.name = UIFactory.AddLabel(rt, "Name", string.Empty, 44, Color.white, FontStyle.Bold);
            UIFactory.SetBox(card.name.rectTransform, new Vector2(0f, -30f), new Vector2(380f, 60f));

            card.state = UIFactory.AddLabel(rt, "State", string.Empty, 34, MainMenuUI.MutedText);
            UIFactory.SetBox(card.state.rectTransform, new Vector2(0f, -85f), new Vector2(380f, 50f));

            card.tag = UIFactory.AddLabel(rt, "Tag", string.Empty, 30, MainMenuUI.DefaultAccent);
            UIFactory.SetBox(card.tag.rectTransform, new Vector2(0f, -125f), new Vector2(380f, 40f));
            return card;
        }

        public void Show()
        {
            var nm = NetworkManager.Singleton;
            _isHost = nm != null && nm.IsServer;

            if (_isHost)
            {
                var addresses = NetworkFlow.GetLocalAddresses();
                _address.text = addresses.Count > 0
                    ? "내 IP 주소: " + string.Join(", ", addresses) + "  (같은 네트워크의 친구는 이 주소로 참가해요)"
                    : "IP 주소를 찾지 못했어요. 같은 PC에서는 127.0.0.1로 참가할 수 있어요.";
            }
            else
            {
                _address.text = "방장에게 접속되었어요";
            }

            var session = NetworkSession.Instance;
            if (session != null) _menu.SetBackgroundTint(session.Theme.accentColor);
            Tick();
        }

        public void Tick()
        {
            var session = NetworkSession.Instance;
            if (!NetworkFlow.IsRunning || session == null)
            {
                string message = NetworkFlow.Instance.PendingMessage ?? "방과의 연결이 끊겼어요.";
                NetworkFlow.Instance.PendingMessage = null;
                _menu.ShowTitleScreen(message);
                return;
            }

            var theme = session.Theme;
            _info.color = theme.accentColor;
            _info.text = $"테마: {theme.displayName}   ·   맵 크기: {session.MazeSize.Value} × {session.MazeSize.Value}";

            ulong localId = NetworkManager.Singleton.LocalClientId;
            _localReady = false;

            for (int i = 0; i < _cards.Length; i++)
            {
                var card = _cards[i];
                if (i >= session.Slots.Count)
                {
                    card.background.color = CardEmpty;
                    card.dot.color = new Color(0.20f, 0.22f, 0.25f);
                    card.name.text = "빈 자리";
                    card.name.color = new Color(0.45f, 0.49f, 0.52f);
                    card.state.text = string.Empty;
                    card.tag.text = string.Empty;
                    continue;
                }

                var slot = session.Slots[i];
                bool isLocal = slot.clientId == localId;
                bool isHostSlot = slot.clientId == NetworkManager.ServerClientId;
                if (isLocal) _localReady = slot.ready;

                card.background.color = isLocal ? CardLocal : CardFilled;
                card.dot.color = PlayerColors.Get(slot.colorIndex);
                card.name.text = PlayerColors.GetName(slot.colorIndex) + " 플레이어";
                card.name.color = Color.white;
                card.state.text = slot.ready ? "준비 완료" : "대기 중";
                card.state.color = slot.ready ? ReadyGreen : MainMenuUI.MutedText;
                card.tag.color = theme.accentColor;
                card.tag.text = isHostSlot && isLocal ? "방장 · 나" : isHostSlot ? "방장" : isLocal ? "나" : string.Empty;
            }

            Color accent = theme.accentColor;
            Color accentText = UIFactory.ContrastText(accent);

            if (_isHost)
            {
                bool canStart = session.CanStart;
                _mainButton.label.text = "게임 시작";
                _mainButton.SetInteractable(canStart, accent, accentText);
                _hint.text = session.Slots.Count < NetworkSession.MinPlayersToStart
                        ? $"게임을 시작하려면 최소 {NetworkSession.MinPlayersToStart}명이 필요해요 (지금 {session.Slots.Count}명)"
                    : !canStart ? "모든 참가자가 준비하면 시작할 수 있어요"
                    : "모두 준비 완료! 게임을 시작하세요";
            }
            else
            {
                _mainButton.label.text = _localReady ? "준비 취소" : "준비";
                _mainButton.SetInteractable(true, _localReady ? ReadyGreen : accent, _localReady ? new Color(0.08f, 0.2f, 0.1f) : accentText);
                _hint.text = _localReady ? "방장이 시작하기를 기다리는 중이에요..." : "준비 버튼을 눌러주세요";
            }
        }

        private void OnMainButton()
        {
            var session = NetworkSession.Instance;
            if (session == null) return;

            if (_isHost) session.StartGame();
            else session.SetReadyRpc(!_localReady);
        }
    }
}
