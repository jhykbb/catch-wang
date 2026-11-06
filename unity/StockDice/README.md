# StockDice (가제: 떡상 다이스): Unity 프로젝트

주사위로 보드를 돌며 땅을 사고 주식에 투자하는 경제 보드게임. 규칙서는 [`game-design/RULES.md`](../../game-design/RULES.md)에 있다.

- 엔진: **Unity 6 LTS (6000.x)**, URP (URP가 없으면 Built-in으로 자동 대체)
- 1단계: 로컬 핫시트 + AI 상대 (2~4인). 온라인(Steamworks)은 로직이 안정된 뒤에 붙인다.

## 여는 방법

### 방법 A (추천): 템플릿 프로젝트에 복사

1. Unity Hub → New project → **Universal 3D** 템플릿으로 새 프로젝트를 만든다.
2. 이 폴더의 `Assets/_Project` 폴더를 새 프로젝트의 `Assets/` 아래에 복사한다.
3. 에디터가 스크립트를 컴파일하면 `Assets/_Project/Scenes/Main.unity`가 **자동으로 생성되고** 빌드 설정에도 추가된다.
   (안 생기면 메뉴 **StockDice → 메인 씬 만들기 + 열기**)
4. Main 씬을 열고 **Play**.

### 방법 B: 이 폴더를 그대로 열기

1. Unity Hub → Add → Add project from disk → `unity/StockDice` 선택 (에디터 버전이 다르면 Hub가 맞춰서 열어 준다).
2. 처음 열면 URP 패키지는 설치되지만 파이프라인 에셋이 없어서 **Built-in 렌더러로 동작**한다. 게임은 정상으로 돌아가고 포스트 프로세싱만 빠진다.
3. URP로 보려면 메뉴 **StockDice → URP 렌더 파이프라인 자동 설정 (실험적)**을 누른다.
   실패하면 `Assets > Create > Rendering > URP Asset (with Universal Renderer)`로 에셋을 만들어 `Project Settings > Graphics`에 지정하고, **StockDice → 기본 재질 다시 만들기**를 누른다.

> 처음 열면 `.meta` 파일과 `ProjectSettings/*.asset`이 생긴다. 이 파일들도 커밋한다.
> (`Library/`, `Temp/`, `Logs/`, `UserSettings/`는 `.gitignore`로 제외)

### 빈 씬에서 직접

빈 씬에 GameObject 하나를 만들고 `GameBootstrap` 컴포넌트만 붙이면 된다. 카메라, 조명, UI, 보드는 전부 코드로 만든다.

## 한글 폰트

- 개발 중에는 OS에 설치된 한글 폰트(맑은 고딕, Apple SD 산돌고딕 Neo, Noto Sans CJK KR, 나눔고딕 순)를 자동으로 쓴다.
- **출시 빌드에는 반드시 폰트를 넣는다.** OFL 라이선스 한글 폰트(예: Pretendard, Noto Sans KR)의 TTF를
  `Assets/_Project/Resources/Fonts/MainFont.ttf`로 넣으면 그 폰트를 1순위로 쓴다.
- UI는 uGUI(레거시 Text)라서 동적 폰트로 한글 전체가 바로 나온다. 나중에 TextMeshPro로 바꿀 때는 한글 SDF 아틀라스를 만들어야 한다.

## 조작

- 마우스 클릭으로 모든 조작. 마우스 휠로 카메라 확대/축소
- 상단 **속도 x1/x2/x4** 버튼으로 연출 속도 조절 (AI 차례는 자동으로 1.6배 빠르게)
- 오른쪽 주식 시장에서 종목을 누르고, 아래 매매 상자에서 수량을 정해 매수/매도
- 핫시트: 여러 명이 한 화면에서 차례대로 한다. 내부자 정보는 그 사람에게만 팝업으로 보여준다.

## 구조

```
Assets/_Project/
  Scripts/
    Core/      순수 C# 게임 로직 (asmdef: noEngineReferences = true, UnityEngine 사용 불가)
      Model/     GameConfig(규칙 숫자), Board(보드 32칸, 가상 지명/회사), GameState, Items, Rng(PCG32)
      Engine/    GameEngine(행동 → 이벤트), GameAction, GameEvents, StockMarket(주가/총자산), GameRunner
      AI/        AiAgent (존버형 / 단타형 / 땅부자형 / 개미형)
    View/      Unity 화면 (asmdef: StockDice.View)
      Flow/      GameBootstrap(씬 진입점), GameSession(진행 루프), EventPlayer(이벤트 → 애니메이션), UrpSetup
      Visual/    BoardView, TokenView, DiceView, FxManager, CameraRig, MeshFactory(둥근 로우폴리 메시), MaterialLibrary, Tween
      UI/        StartScreen, HudView, StockPanel, ActionPanel, Popup, ResultScreen, UIKit, FontProvider
    Editor/    ProjectSetup (메인 씬/재질 자동 생성, URP 설정 메뉴)
  Tests/EditMode/  규칙 테스트 (NUnit, Unity Test Runner와 dotnet test 겸용)
```

### 로직과 화면의 분리

```
사람(HUD 버튼) / AI(AiAgent) ──GameAction──▶ GameEngine.Apply() ──List<GameEvent>──▶ EventPlayer ──▶ 애니메이션
                                                  │
                                              GameState (직렬화 가능, 난수 상태 포함)
```

- 로직은 행동(주사위 굴리기, 매수, 턴 종료 등)을 받아 **이벤트 목록**을 내놓는다. 화면은 그 목록을 **순서대로 재생**만 한다.
- 같은 로직을 AI, 밸런스 시뮬레이터, (나중에) 온라인 호스트가 그대로 쓴다.
- 난수는 자체 PCG32라서 같은 시드면 어느 플랫폼에서나 같은 판이 나온다 (리플레이, 동기화 검증용).
- 이벤트에는 `VisibleTo`가 있어서 비밀 정보(내부자 정보)는 그 플레이어에게만 보낸다.
- 인원수는 하드코딩하지 않는다. `GameConfig.MaxPlayers`만 올리면 5~6인이 된다 (말 배치, 카드 높이, 확률표 보간이 자동으로 따라감).

### 새 이벤트 추가하는 법

1. `GameEvents.cs`에 이벤트 클래스를 추가하고 `Describe()`(로그 문구)를 쓴다.
2. `GameEngine`에서 `Emit(new ...Event { ... })`.
3. `EventPlayer.Play()`의 switch에 case를 하나 추가해 연출을 붙인다.

## 테스트

- Unity: `Window > General > Test Runner > EditMode > Run All`
- 명령줄 (Unity 없이):

```bash
cd tools/Core.Tests && dotnet test
```

## 밸런스 시뮬레이터

AI끼리 수천 판을 돌려 평균 게임 시간, 땅/주식 유불리, 역전 빈도, 성향별 승률, 투자자 수별 수익률을 본다.
결과는 [`game-design/SIMULATION.md`](../../game-design/SIMULATION.md)에 있다.

```bash
cd tools/Simulator && dotnet run -c Release -- --games 3000 --out ../../game-design/SIMULATION.md
```

옵션: `--players 2,3,4`, `--rounds 20`, `--cash 10000`, `--seed 1`, `--toll 0.2,0.6,1.2,2.0`, `--build 0.5`, `--turnend-build`

## 다음 단계

- 온라인: Steamworks(Steam 로비 + P2P). 호스트가 `GameEngine`을 돌리고 손님은 `GameAction`만 보낸다. `GameSession.Submit`과 이벤트 큐 부분을 네트워크로 바꾸면 된다.
- 효과음과 배경음, 출시용 한글 폰트, TextMeshPro 전환
- 설정 화면 (해상도, 음량, 연출 속도 기본값)
- 세이브/로드 (`GameState` 직렬화)

## 알려진 한계 (이 커밋 기준)

- 이 코드는 Unity 에디터가 없는 환경에서 작성했다.
  - 게임 로직(Core)은 .NET에서 규칙 테스트 25개와 시뮬레이션 수만 판으로 검증했다.
  - 화면 코드(View, Editor)는 Unity 2021.3 참조 어셈블리로 **컴파일만** 확인했다 (오류 0, 경고 0). Unity 6에서 실제로 돌려 보면 배치나 연출 타이밍을 다듬어야 할 수 있다.
- URP 포스트 프로세싱(`UrpSetup.cs`)은 URP 패키지가 있을 때만 컴파일되는 코드라서 위 컴파일 확인에 포함되지 않았다.
