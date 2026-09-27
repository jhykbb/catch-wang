// 저장소 선택: config.js에 firebase 설정이 있으면 Firebase, 없으면 로컬 모드.
// 페이지들은 항상 이 파일에서만 가져다 쓴다. 두 저장소는 같은 함수들을 export한다.
import { CONFIG } from './config.js';

const impl = CONFIG.firebase
  ? await import('./store.firebase.js')
  : await import('./store.local.js');

export { statusOf } from './common.js';
export const {
  watchGame, watchMyTickets, watchRoundTickets, watchPrizes,
  prepareRound, getRoundKey, startRound, updateHint, endRound,
  claimTicket, drawPrize, getMyTicket, getTicket, redeemTicket,
  getPrizes, savePrizes, resetAll,
  staffSignIn, isStaff,
} = impl;
