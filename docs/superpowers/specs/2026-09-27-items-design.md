# M6 — 아이템 시스템 설계 (신의 손 / 번개 / 레이더 / 칼)

승인일: 2026-09-27 · 상태: 승인됨(구현 대기). 초기 개념은 `~/.claude/plans/iridescent-stirring-stallman.md`의 M6 섹션.

## 1. 목표와 범위

4인 대결(Versus)과 혼자 하기의 봇과 대결(Bots)에 아이템 4종을 추가한다. 깃발 뽑기가 유일한 탈락 수단이라는 핵심 규칙은 바꾸지 않고, 아이템은 그 과정을 돕거나 방해하는 보조 수단이다(직접 탈락시키는 아이템은 없음).

- **등장 모드:** Versus·Bots만. 깃발 찾기(Treasure)·자유 연습(Practice)에는 상대가 없으므로 등장하지 않는다.
- **인벤토리:** 슬롯 1개, 습득 시 기존 아이템 자동 교체.
- **봇:** 쉬움/보통 봇은 아이템을 완전히 무시한다. **어려움 봇만** 지나가다 자동으로 줍고 상황에 맞춰 쓴다. 모든 봇은 사람이 아이템으로 걸면(번개/신의 손/칼) 그대로 영향을 받는다.

## 2. 습득 & 재생성

- 매치 시작 시 미로에 아이템 지점을 `count = 4 + (크기-10) / 20`개 놓는다 → 10×10=4, 30×30=5, 50×50=6.
- 배치 규칙은 금색 깃발(`SpawnPlacer.PlaceTreasures`)과 같다: 서로 미로 한 변의 16% 이상, 모든 플레이어 시작 칸에서 15% 이상 떨어진 무작위 칸(자리가 부족하면 조건을 80%씩 낮춤). 새 함수 `SpawnPlacer.PlaceItemSpots(maze, count, avoidStartCells)`로 초기 배치는 순수 함수(결정적, 셀프테스트 가능)로 만든다.
- 각 지점은 4종 중 무작위 하나(동일한 확률 25%씩)를 들고 있다(초기 배치와 재생성 모두 무작위, 결정적일 필요 없음).
- 플레이어가 아이템을 바라보고 **E**를 짧게 누르면 습득한다(기존 `IInteractable`/`FirstPersonController.HandleInteract` 라이캐스트 구조 재사용). 이미 아이템을 들고 있으면 자동 교체.
- 습득되면 그 지점은 비고, **20초** 뒤 새 무작위 자리(같은 배치 규칙, 이번엔 다른 활성 아이템 지점들과도 같은 간격 유지)에 새 아이템(무작위 종류)으로 다시 생긴다.
- 탈락했거나(`IsAlive == false`) 경직 중(`IsStunnedNow`)인 플레이어는 줍기/쓰기 모두 할 수 없다(기존 `HandleInteract`의 `IsStunned || IsPaused` 조기 반환에 자연히 포함).

## 3. 공통 사용 규칙

- 새 키 **F = 아이템 사용**(E는 그대로 줍기/깃발 뽑기). 아이템이 없으면 F는 아무 효과가 없다.
- 사용하는 순간 슬롯이 빈다 — 대상에게 맞았는지와 무관하게 소모된다(헛스윙도 소모).
- 경직(`StunnedUntil`)은 서버가 정하고 모든 피어에 복제된다. 경직 중에는 이동·시야·상호작용·아이템 사용이 모두 막힌다 — 기존 `FirstPersonController.IsStunned`를 그대로 쓴다. 봇은 `BotBrain.Update`에서 같은 조건으로 멈춘다.

## 4. 아이템 4종

### 신의 손
- F를 누르면 화면 중앙에 **살아있는 플레이어 목록**이 뜬다(관전 시점 전환과 같은 1~4 숫자키 방식 — 번호는 플레이어 색과 같다). 나 자신도 목록에 포함된다.
- 숫자키로 대상을 확정하면 서버가 그 대상을 **그 대상 자신의 시작 지점**(`NetworkPlayer.SpawnPosition`/`SpawnYaw`)으로 순간이동시킨다(`NetworkPlayer.TeleportTo`). 나를 고르면 내가 내 시작 지점으로 돌아간다(탈출/귀환 용도).
- **사거리 제한 없음**(조준이 아니라 목록에서 고르므로 미로 어디에 있든 선택 가능).
- F를 다시 누르거나 Esc를 누르면 목록을 닫는다(아이템 소모 안 함). 목록이 열려 있어도 이동/시야는 그대로 유지된다.

### 번개
- F를 누르면 즉시 **나를 제외한** 모든 생존 플레이어(사람+봇)가 1초간 경직된다. 조준/대상 선택 없음.

### 레이더
- F를 누르면 즉시 나에게만 1초간 미니맵이 뜬다(모든 생존 플레이어의 상대 위치, 북쪽 고정, 화면 중앙 = 내 위치). 서버는 사용 여부만 검증하고 소모하며, 위치 자체는 이미 모든 클라이언트에 복제되어 있으므로 서버가 따로 계산할 게 없다.

### 칼
- F를 누르면 서버가 **캐스터의 현재 위치·정면 방향** 기준으로 짧은 사거리(3 m) · 정면 각도(±50°) 안에서 가장 가까운 살아있는 상대 한 명을 찾아 경직을 준다.
- 이미 경직 중인 상대에게 맞으면 경직이 끝나는 시점에서 **+1초씩 누적**된다(연속으로 맞으면 경직 시간이 길어짐).
- 맞을 상대가 없으면 그냥 헛스윙(소모는 그대로 됨).

## 5. 어려움 봇의 아이템 사용

- `BotSettings`에 `usesItems`(어려움만 true)를 추가한다.
- **습득:** 매 틱, 들고 있는 아이템이 없고 근처(도보 경로 위, 픽업 사거리 안)에 아이템 지점이 있으면 자동으로 줍는다(봇은 키 입력이 없으므로 근접 시 자동 — 경로를 일부러 바꾸지는 않고, 원래 가던 길에 있을 때만).
- **사용:**
  - **번개/칼**: 살아있는 상대가 가까이(6 m 이내) 있으면 즉시 사용한다.
  - **신의 손**: 내 깃발 6 m 안에 상대가 있으면 그 상대를 지목한다(내 깃발 지키기). 없으면 지금 쫓고 있는 상대를 지목한다. 15초 넘게 못 쓰면 가장 가까운 상대에게라도 쓴다(썩히지 않기).
  - **레이더**: 어려움 봇은 이미 경로를 다 알아서 쓸모가 없다 — 줍는 즉시 사용해 슬롯을 비운다.

## 6. HUD

- 상단 알약 옆에 소지 아이템 표시(아이콘 대신 색 배지 + 한글 이름, 텍스트 기반 — 프로그래머 아트 원칙 유지). 없으면 숨김. 들고 있으면 "F: 사용" 힌트.
- 아이템을 바라보면 "E: OO 줍기" 프롬프트(깃발 프롬프트와 같은 자리 스타일, 진행 막대 없음 — 탭 한 번).
- 신의 손 사용 시 화면 중앙에 번호+색 목록 오버레이.
- 레이더 사용 시 1초짜리 작은 미니맵(우상단, 점으로 상대 표시).
- 아이템 비주얼은 4종 각각 다른 저폴리 도형(구/원뿔/상자/쐐기 등, `Flag`의 절차적 모델과 같은 방식), 테마 강조색으로 살짝 물들인다.

## 7. 기술 구조

- **`NetworkPlayer` 추가:** `NetworkVariable<int> HeldItem`(Everyone/Server), `NetworkVariable<double> StunnedUntil`(Everyone/Server), `IsStunnedNow` 헬퍼. `Update()`에서 로컬 사람이면 `controller.IsStunned = IsStunnedNow`로 다리를 놓는다. 새 RPC `RequestPickupItemRpc(ulong pickupNetworkObjectId)`, `UseItemRpc(ulong targetPlayerId = MatchManager.NoOne)` — `PullFlagRpc`와 같은 `[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]` 패턴, 봇 여부/발신자 검증 포함. 레이더 확인용 `[Rpc(SendTo.Owner)] RadarPulseRpc()`.
- **`MatchManager` 추가:** `ServerRequestPickup(playerId, pickupId)`(거리 검증 후 습득 처리 — 라이캐스트가 이미 벽을 막으므로 "같은 칸" 조건은 불필요, 단순 거리 검증), `ServerUseItem(playerId, targetPlayerId)`(종류별 분기·소모). `ServerBegin`에서 모드가 Versus/Bots면 `ItemSpawner`를 만들어 초기 배치를 시작한다(미로/칸 크기는 `MazeGameBootstrap.Instance`에서 읽는다, `CanReach`가 이미 하듯).
- **`Scripts/Items/ItemPickup.cs`(신규):** `NetworkBehaviour, IInteractable`. `NetworkVariable<int> ItemType`(서버가 스폰 전에 정함), `DisplayName`. `Interact(instigator)` → `instigator.GetComponent<NetworkPlayer>().RequestPickupItemRpc(NetworkObjectId)`. 정적 목록 `ItemPickup.All`(Flag.All과 같은 패턴) — `NetworkSession.DespawnGameObjects()`가 매치 종료/재시작 시 이 목록도 정리하도록 그 메서드에 한 줄 추가해야 한다.
- **`Scripts/Items/ItemSpawner.cs`(신규, MatchManager의 GameObject에 컴포넌트로 붙음, 서버 전용):** 슬롯별 상태(칸, 현재 픽업) 관리, 습득되면 20초 코루틴 뒤 새 자리/종류로 재생성. MatchManager와 함께 파괴되면 코루틴도 자동 정리된다.
- **`SpawnPlacer` 추가:** `PlaceItemSpots(maze, count, avoidStartCells)` — `PlaceTreasures`와 같은 간격 규칙(순수 함수, 셀프테스트 대상). 재생성용 단일 칸 재추첨은 `ItemSpawner` 내부에서 같은 규칙으로 (결정적일 필요 없음, `UnityEngine.Random` 사용 가능).
- **`FirstPersonController` 변경:** 상호작용 라이캐스트를 E를 누른 순간뿐 아니라 매 프레임 갱신해 `public IInteractable LookTarget`을 노출한다(HUD가 줍기 프롬프트를 미리 보여줄 수 있도록). `Interact()` 호출은 여전히 `wasPressedThisFrame`일 때만.
- **`Scripts/Gameplay/ItemUser.cs`(신규, `FlagPuller`처럼 로컬 플레이어에만 붙음):** F 입력, 즉시형(번개/레이더/칼)은 바로 `UseItemRpc(NoOne)`, 신의 손은 목록 열기/숫자키 확정/닫기 처리. `IsAlive`/`IsStunned`/`IsPaused` 조건은 `FlagPuller`와 동일하게 검사.
- **`BotSettings`에 `usesItems` 추가(어려움만 true). `BotBrain`에 습득/사용 로직 추가**(위 5절), 내부적으로 `MatchManager.Instance.ServerRequestPickup`/`ServerUseItem`을 사람과 똑같이 호출(RPC 우회, 봇은 서버 객체이므로 직접 호출).
- **`MatchHud` 추가:** 소지 아이템 배지/프롬프트/신의 손 목록 오버레이/레이더 미니맵(6절).

## 8. 테스트

- 셀프 테스트: `PlaceItemSpots` — 개수/중복 없음/간격/결정적(기존 `PlaceTreasures` 테스트와 같은 형식).
- 플레이테스트(4인 대결 + 솔로 봇 대결):
  - 습득 → 슬롯에 반영 → 다른 아이템 습득 시 자동 교체.
  - 번개: 사용 즉시 나 제외 전원 1초 경직(이동 불가, 깃발 못 뽑음), 나는 그대로 움직임.
  - 신의 손: 목록에 살아있는 전원(나 포함) 표시, 숫자키로 고른 대상이 정확히 자기 시작 지점으로 이동.
  - 칼: 사거리/각도 안의 상대만 맞고, 연속 히트 시 경직 시간이 누적됨. 아무도 없으면 소모만 되고 아무 일 없음.
  - 레이더: 사용 시 로컬에만 1초간 미니맵이 뜨고 서버 상태엔 부작용 없음.
  - 재생성: 습득 후 20초 뒤 새 자리에 새 아이템이 생김(자리는 기존 시작 칸/다른 아이템과 간격 유지).
  - 모드 제한: 깃발 찾기/자유 연습에는 아이템 지점이 하나도 없음.
  - 어려움 봇: 지나가다 아이템을 줍고, 사람이 가까이 가면 번개/칼을 쓰거나 신의 손으로 쫓아냄, 레이더는 즉시 써서 버림.
- 기존 회귀(멀티 4/3/2, 솔로 3모드, 셀프테스트)는 그대로 통과해야 한다.

## 9. 이번 범위에서 제외

- 쉬움/보통 봇의 아이템 사용.
- 아이템 관련 사운드/파티클(BGM처럼 빈 슬롯 원칙 유지, 필요하면 나중에).
- 레이더 미니맵에 벽/미로 형태 표시(이번엔 점만, 벽 지도는 범위 밖).
