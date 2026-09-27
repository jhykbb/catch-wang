// 게임 설정. 코스프레에 맞춰 이름을 바꾸려면 gameTitle과 targetName만 고치면 된다.
export const CONFIG = {
  gameTitle: '왕성혁을 잡아라!',
  targetName: '왕성혁',
  roundMinutes: 30,
  maxTicketsPerRound: 20,   // 라운드당 뽑기권 최대 개수 (QR 사진 퍼짐 대비)
  mapBlurMeters: 40,        // 지도에 보여줄 원의 최소 반경. 실제 위치는 원 안 어딘가

  // Firebase 콘솔 > 프로젝트 설정 > 내 앱 > SDK 설정의 firebaseConfig 값을 붙여넣는다.
  // null이면 로컬 모드(이 브라우저에만 저장)로 돈다.
  firebase: {
    apiKey: 'AIzaSyBA9do4Q7KI2YckLw9hdfyfy4e6TBts7yA',
    authDomain: 'kingcatch-91288.firebaseapp.com',
    projectId: 'kingcatch-91288',
    storageBucket: 'kingcatch-91288.firebasestorage.app',
    messagingSenderId: '1054008597238',
    appId: '1:1054008597238:web:6485afea978ac7d732ead7',
  },

  staffPassword: '0000',    // 로컬 모드 전용 운영자 비밀번호. Firebase 모드에서는 운영자 계정으로 로그인
};

// weight가 클수록 잘 나온다. stock이 0이 되면 더 이상 안 나온다.
export const DEFAULT_PRIZES = [
  { id: 'big', name: '대상품', stock: 3, weight: 1 },
  { id: 'snack', name: '과자', stock: 60, weight: 10 },
  { id: 'candy', name: '사탕', stock: 150, weight: 25 },
];
