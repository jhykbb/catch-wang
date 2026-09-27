// 로컬 모드 저장소: 모든 데이터를 이 브라우저의 localStorage에 둔다.
// Firebase 설정 전 화면 테스트용. 같은 브라우저의 탭끼리만 공유된다.
import { CONFIG, DEFAULT_PRIZES } from './config.js';
import { statusOf, randomCode, blurArea, weightedPick, newestFirst } from './common.js';

const DB_KEY = 'catchwang-db-v2';
const IDLE = { status: 'idle', roundNo: 0 };

function emptyDb() {
  return {
    game: IDLE,
    hint: null,
    roundKeys: {},   // 라운드별 QR 비밀값. 참가자 화면에는 절대 내보내지 않는다
    tickets: {},     // 교환 코드 -> 뽑기권
    prizes: structuredClone(DEFAULT_PRIZES),
  };
}

function load() {
  try {
    const raw = localStorage.getItem(DB_KEY);
    if (raw) return JSON.parse(raw);
  } catch {}
  return emptyDb();
}

const listeners = new Set();
function notify() { listeners.forEach(cb => cb()); }
function save(db) {
  localStorage.setItem(DB_KEY, JSON.stringify(db));
  notify();
}
// 다른 탭에서 바뀐 것도 알려준다
window.addEventListener('storage', e => { if (e.key === DB_KEY) notify(); });

function watch(select, cb) {
  const fire = () => cb(select(load()));
  listeners.add(fire);
  fire();
  return () => listeners.delete(fire);
}

// Firebase 모드의 익명 로그인 uid 대신 쓰는 기기 id
function myUid() {
  let id = localStorage.getItem('catchwang-device');
  if (!id) {
    id = crypto.randomUUID();
    localStorage.setItem('catchwang-device', id);
  }
  return id;
}

function publicGame({ game, hint }) {
  return hint && hint.roundId === game.roundId
    ? { ...game, photo: hint.photo, area: hint.area, hintAt: hint.hintAt }
    : game;
}

// ---- 구독 ----

export const watchGame = cb => watch(publicGame, cb);
export const watchMyTickets = cb =>
  watch(db => newestFirst(Object.values(db.tickets).filter(t => t.uid === myUid())), cb);
export const watchRoundTickets = (roundId, cb) =>
  watch(db => newestFirst(Object.values(db.tickets).filter(t => t.roundId === roundId)), cb);
export const watchPrizes = cb => watch(db => db.prizes, cb);

// ---- 라운드 ----

export async function prepareRound() {
  const db = load();
  if (statusOf(db.game) === 'live') throw new Error('진행 중인 라운드가 있어요. 먼저 종료하세요.');
  const roundNo = db.game.roundNo + 1;
  const roundId = `R${roundNo}`;
  db.roundKeys[roundId] = randomCode(8);
  db.game = { status: 'ready', roundNo, roundId, claimed: 0, maxTickets: CONFIG.maxTicketsPerRound };
  save(db);
  return roundId;
}

export async function getRoundKey(roundId) {
  return load().roundKeys[roundId] ?? null;
}

export async function startRound({ photo, position }) {
  const db = load();
  if (db.game.status !== 'ready') throw new Error('부스에서 먼저 라운드를 준비해 주세요.');
  const now = Date.now();
  db.hint = { roundId: db.game.roundId, photo, area: blurArea(position), hintAt: now };
  db.game = { ...db.game, status: 'live', startedAt: now, endsAt: now + CONFIG.roundMinutes * 60000 };
  save(db);
}

export async function updateHint({ photo, position }) {
  const db = load();
  if (statusOf(db.game) !== 'live') throw new Error('진행 중인 라운드가 없어요.');
  db.hint = { roundId: db.game.roundId, photo, area: blurArea(position), hintAt: Date.now() };
  save(db);
}

export async function endRound() {
  const db = load();
  if (db.game.status !== 'live') return;
  db.game = { ...db.game, status: 'ended', endsAt: Math.min(db.game.endsAt, Date.now()) };
  save(db);
}

// ---- 뽑기권 ----

export async function claimTicket({ roundId, key, nickname }) {
  const db = load();
  const uid = myUid();
  const existing = Object.values(db.tickets).find(t => t.roundId === roundId && t.uid === uid);
  if (existing) return { ticket: existing, already: true };

  if (db.game.roundId !== roundId || statusOf(db.game) !== 'live') {
    throw new Error('지금 진행 중인 라운드의 QR이 아니에요.');
  }
  if (db.roundKeys[roundId] !== key) throw new Error('올바른 QR이 아니에요.');
  if (db.game.claimed >= db.game.maxTickets) {
    throw new Error('이번 라운드 뽑기권이 모두 나갔어요. 다음 라운드를 노려 보세요!');
  }

  let code;
  do { code = randomCode(5); } while (db.tickets[code]);
  const ticket = {
    code, roundId, uid, nickname,
    prizeId: null, prizeName: null,
    createdAt: Date.now(), redeemed: false,
  };
  db.tickets[code] = ticket;
  db.game.claimed += 1;
  save(db);
  return { ticket, already: false };
}

export async function drawPrize(code) {
  const db = load();
  const t = db.tickets[code];
  if (!t || t.uid !== myUid()) throw new Error('뽑기권을 찾을 수 없어요.');
  if (t.prizeId) return t;
  const pick = weightedPick(db.prizes);
  pick.stock -= 1;
  t.prizeId = pick.id;
  t.prizeName = pick.name;
  t.drawnAt = Date.now();
  save(db);
  return t;
}

export async function getMyTicket(code) {
  const t = load().tickets[String(code ?? '').toUpperCase()];
  return t && t.uid === myUid() ? t : null;
}

export async function getTicket(code) {
  return load().tickets[String(code ?? '').toUpperCase()] ?? null;
}

export async function redeemTicket(code) {
  const db = load();
  const t = db.tickets[String(code ?? '').toUpperCase()];
  if (!t) throw new Error('없는 코드예요.');
  if (!t.prizeId) throw new Error('아직 뽑기를 안 한 뽑기권이에요.');
  if (t.redeemed) throw new Error('이미 지급한 상품이에요.');
  t.redeemed = true;
  t.redeemedAt = Date.now();
  save(db);
  return t;
}

// ---- 상품 ----

export async function getPrizes() {
  return load().prizes;
}

export async function savePrizes(prizes) {
  const db = load();
  db.prizes = prizes;
  save(db);
}

export async function resetAll() {
  localStorage.removeItem(DB_KEY);
  notify();
}

// ---- 운영자 ----

export async function staffSignIn(_email, password) {
  if (password !== CONFIG.staffPassword) throw new Error('비밀번호가 틀렸어요.');
  try { sessionStorage.setItem('catchwang-staff', '1'); } catch {}
}

export async function isStaff() {
  try { return sessionStorage.getItem('catchwang-staff') === '1'; } catch { return false; }
}
