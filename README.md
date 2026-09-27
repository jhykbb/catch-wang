# 왕성혁을 잡아라! 홈페이지

교내 술래잡기 챌린지 홈페이지. 지금은 **프로토타입** 단계라 데이터가 브라우저(localStorage)에만 저장된다.

## 페이지

| 파일 | 누가 | 하는 일 |
| --- | --- | --- |
| `index.html` | 참가자 | 라운드 상태, 힌트 사진, 대략적인 위치(지도), 내 뽑기권 |
| `claim.html` | 참가자 | 도망자 등의 QR을 찍으면 열림. 뽑기권 받기 → 뽑기 → 교환 코드 |
| `runner.html` | 도망자 | 사진 + GPS 올려서 라운드 시작, 힌트 갱신, 조기 종료 |
| `booth.html` | 보상 부스 | 라운드 준비와 QR 인쇄, 상품 교환, 기록, 재고 관리 |

게임 이름, 라운드 길이, 뽑기권 수량, 기본 상품은 `js/config.js`에서 바꾼다.
운영자 비밀번호(프로토타입용)도 `js/config.js`에 있다.

## 한 라운드 흐름

1. 부스: "새 라운드 준비" → QR 인쇄 → 도망자 등에 붙이기
2. 도망자: 숨을 곳에서 `runner.html` → 사진 찍기 + 위치 확인 → "라운드 시작!"
3. 참가자: 힌트 보고 찾아가서 등의 QR 찍기 → 뽑기 → 교환 코드
4. 부스: 교환 코드 조회 → 상품 주고 "지급 완료"
5. 30분이 지나거나 도망자가 끝내면 종료 → QR 떼기

## 로컬 실행

```
powershell -ExecutionPolicy Bypass -File server.ps1
```

그 다음 http://localhost:5500/booth.html 을 연다.

## 저장소 두 가지

`js/config.js`의 `firebase`가 `null`이면 **로컬 모드**(`js/store.local.js`, 이 브라우저에만 저장),
값이 있으면 **Firebase 모드**(`js/store.firebase.js`, 모든 폰이 같은 데이터)로 돈다.
페이지 코드는 `js/store.js`만 쓰기 때문에 모드를 바꿔도 고칠 곳이 없다.

## Firebase 설정 순서

1. https://console.firebase.google.com 에서 프로젝트 추가 (애널리틱스는 꺼도 됨)
2. Authentication > 로그인 방법: **익명** 사용 설정
3. 운영자는 개발자 비밀번호로 들어간다. 한 번 맞힌 기기는 계속 운영자로 인정된다
4. Firestore Database > 데이터베이스 만들기: 위치 `asia-northeast3 (서울)`, 프로덕션 모드
5. Firestore > 규칙: `firestore.rules` 내용을 붙여넣고, `CHANGE_ME_SHA256`을 개발자 비밀번호의 SHA-256 값으로 바꾼 뒤 게시 (이 파일은 공개되니 실제 값은 콘솔에만)
6. 프로젝트 설정 > 내 앱 > 웹 앱 추가 → 나오는 `firebaseConfig`를 `js/config.js`의 `firebase:`에 붙여넣기
7. 사이트를 GitHub Pages에 올린 뒤, Authentication > 설정 > 승인된 도메인에 `아이디.github.io` 추가

## 알고 있는 한계

- 뽑기는 참가자 폰에서 돌린다. 개발자 도구를 다룰 줄 아는 사람은 원하는 상품을 고를 수 있다.
  재고 차감과 "뽑기권 1장당 1번"은 서버 규칙이 막지만, 어떤 상품이 나올지는 못 막는다.
- 힌트 사진은 Firestore 문서에 글자로 넣는다(900px로 줄여서). Firebase Storage는 유료 요금제가 필요해서 쓰지 않았다.

## 다음 할 일

- [x] 저장소를 Firebase Firestore로 (여러 폰에서 같이 보이게)
- [x] 운영자 비밀번호를 Firebase 로그인으로
- [ ] Firebase 프로젝트 만들고 설정값 넣기
- [ ] GitHub Pages에 올리기 (폰에서 GPS를 쓰려면 HTTPS 필요)
- [ ] 교내에서 실제 폰으로 테스트
