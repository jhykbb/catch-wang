// Firebase 모드 저장소: Firestore에 저장해서 모든 폰이 같은 데이터를 본다.
// 참가자는 익명 로그인(uid가 기기 id 역할), 운영자는 이메일 로그인.
// 누가 무엇을 쓸 수 있는지는 firestore.rules가 서버에서 막는다.
import { initializeApp } from 'https://www.gstatic.com/firebasejs/10.12.2/firebase-app.js';
import {
  getAuth, onAuthStateChanged, signInAnonymously,
} from 'https://www.gstatic.com/firebasejs/10.12.2/firebase-auth.js';
import {
  getFirestore, doc, collection, query, where, onSnapshot,
  getDoc, getDocs, setDoc, runTransaction, writeBatch,
} from 'https://www.gstatic.com/firebasejs/10.12.2/firebase-firestore.js';
import { CONFIG } from './config.js';
import { statusOf, randomCode, blurArea, weightedPick, newestFirst } from './common.js';

const app = initializeApp(CONFIG.firebase);
const auth = getAuth(app);
const db = getFirestore(app);

// 게임 상태와 힌트 사진을 문서 두 개로 나눈다.
// 뽑기권이 나갈 때마다 game이 바뀌는데, 사진까지 매번 다시 내려받지 않게 하려는 것
const gameRef = doc(db, 'game', 'current');
const hintRef = doc(db, 'hint', 'current');
const IDLE = { status: 'idle', roundNo: 0 };

// 처음 온 사람은 익명 로그인시킨다
const authReady = new Promise(resolve => {
  const off = onAuthStateChanged(auth, async user => {
    if (user) {
      off();
      resolve(user);
      return;
    }
    try {
      await signInAnonymously(auth);
    } catch (err) {
      console.error('익명 로그인 실패', err);
    }
  });
});

async function myUid() {
  await authReady;
  return auth.currentUser.uid;
}

// Firebase 오류를 화면에 보여줄 한국어 문장으로 바꾼다. 우리가 던진 Error는 그대로 둔다
function friendly(err, deniedMessage) {
  if (!err?.code) return err;
  if (err.code === 'permission-denied') return new Error(deniedMessage);
  console.error(err);
  return new Error('연결이 불안정해요. 잠시 후 다시 시도해 주세요.');
}

function listen(start) {
  let unsubscribe = () => {};
  let stopped = false;
  authReady.then(() => { if (!stopped) unsubscribe = start(); });
  return () => { stopped = true; unsubscribe(); };
}

const ticketsOf = snap => newestFirst(snap.docs.map(d => d.data()));
const prizesOf = snap => snap.docs.map(d => ({ id: d.id, ...d.data() })).sort((a, b) => a.order - b.order);

// ---- 구독 ----

export function watchGame(cb) {
  let game = IDLE;
  let hint = null;
  const emit = () => cb(hint && hint.roundId === game.roundId
    ? { ...game, photo: hint.photo, area: hint.area, hintAt: hint.hintAt }
    : game);
  return listen(() => {
    const offGame = onSnapshot(gameRef, s => { game = s.exists() ? s.data() : IDLE; emit(); });
    const offHint = onSnapshot(hintRef, s => { hint = s.exists() ? s.data() : null; emit(); });
    return () => { offGame(); offHint(); };
  });
}

export function watchMyTickets(cb) {
  return listen(() => onSnapshot(
    query(collection(db, 'tickets'), where('uid', '==', auth.currentUser.uid)),
    s => cb(ticketsOf(s)),
  ));
}

export function watchRoundTickets(roundId, cb) {
  return listen(() => onSnapshot(
    query(collection(db, 'tickets'), where('roundId', '==', roundId)),
    s => cb(ticketsOf(s)),
  ));
}

export function watchPrizes(cb) {
  return listen(() => onSnapshot(collection(db, 'prizes'), s => cb(prizesOf(s))));
}

// ---- 라운드 (운영자만) ----

async function currentGame() {
  const snap = await getDoc(gameRef);
  return snap.exists() ? snap.data() : IDLE;
}

export async function prepareRound() {
  const game = await currentGame();
  if (statusOf(game) === 'live') throw new Error('진행 중인 라운드가 있어요. 먼저 종료하세요.');
  const roundNo = (game.roundNo ?? 0) + 1;
  const roundId = `R${roundNo}`;
  const batch = writeBatch(db);
  batch.set(doc(db, 'roundKeys', roundId), { key: randomCode(8) });
  batch.set(gameRef, { status: 'ready', roundNo, roundId, claimed: 0, maxTickets: CONFIG.maxTicketsPerRound });
  await batch.commit();
  return roundId;
}

export async function getRoundKey(roundId) {
  const snap = await getDoc(doc(db, 'roundKeys', roundId));
  return snap.exists() ? snap.data().key : null;
}

export async function startRound({ photo, position }) {
  const game = await currentGame();
  if (game.status !== 'ready') throw new Error('부스에서 먼저 라운드를 준비해 주세요.');
  const now = Date.now();
  const batch = writeBatch(db);
  batch.set(hintRef, { roundId: game.roundId, photo, area: blurArea(position), hintAt: now });
  batch.update(gameRef, { status: 'live', startedAt: now, endsAt: now + CONFIG.roundMinutes * 60000 });
  await batch.commit();
}

export async function updateHint({ photo, position }) {
  const game = await currentGame();
  if (statusOf(game) !== 'live') throw new Error('진행 중인 라운드가 없어요.');
  await setDoc(hintRef, { roundId: game.roundId, photo, area: blurArea(position), hintAt: Date.now() });
}

export async function endRound() {
  const game = await currentGame();
  if (game.status !== 'live') return;
  await setDoc(gameRef, { ...game, status: 'ended', endsAt: Math.min(game.endsAt, Date.now()) });
}

// ---- 뽑기권 ----

// 한 트랜잭션에서 "받았음 표시(claims) + 뽑기권(tickets) + 나간 개수(game.claimed)"를 같이 쓴다.
// QR 비밀값 확인과 한 사람 1장 제한은 firestore.rules가 서버에서 검사한다
export async function claimTicket({ roundId, key, nickname }) {
  const uid = await myUid();
  const claimRef = doc(db, 'claims', `${roundId}_${uid}`);
  try {
    return await runTransaction(db, async tx => {
      const claim = await tx.get(claimRef);
      if (claim.exists()) {
        const t = await tx.get(doc(db, 'tickets', claim.data().code));
        return { ticket: t.data(), already: true };
      }
      const gameSnap = await tx.get(gameRef);
      const game = gameSnap.exists() ? gameSnap.data() : IDLE;
      if (game.roundId !== roundId || statusOf(game) !== 'live') {
        throw new Error('지금 진행 중인 라운드의 QR이 아니에요.');
      }
      if (game.claimed >= game.maxTickets) {
        throw new Error('이번 라운드 뽑기권이 모두 나갔어요. 다음 라운드를 노려 보세요!');
      }
      const code = randomCode(5);
      const ticket = {
        code, roundId, uid, nickname,
        prizeId: null, prizeName: null,
        createdAt: Date.now(), redeemed: false,
      };
      tx.set(claimRef, { roundId, uid, key, code });
      tx.set(doc(db, 'tickets', code), ticket);
      tx.update(gameRef, { claimed: game.claimed + 1 });
      return { ticket, already: false };
    });
  } catch (err) {
    throw friendly(err, '올바른 QR이 아니에요.');
  }
}

export async function drawPrize(code) {
  const uid = await myUid();
  const prizeIds = (await getDocs(collection(db, 'prizes'))).docs.map(d => d.id);
  try {
    return await runTransaction(db, async tx => {
      const ticketRef = doc(db, 'tickets', code);
      const ticketSnap = await tx.get(ticketRef);
      if (!ticketSnap.exists() || ticketSnap.data().uid !== uid) throw new Error('뽑기권을 찾을 수 없어요.');
      const ticket = ticketSnap.data();
      if (ticket.prizeId) return ticket;

      const prizeSnaps = await Promise.all(prizeIds.map(id => tx.get(doc(db, 'prizes', id))));
      const pick = weightedPick(prizeSnaps.filter(s => s.exists()).map(s => ({ id: s.id, ...s.data() })));
      const drawn = { prizeId: pick.id, prizeName: pick.name, drawnAt: Date.now() };
      tx.update(doc(db, 'prizes', pick.id), { stock: pick.stock - 1, lastTicket: code });
      tx.update(ticketRef, drawn);
      return { ...ticket, ...drawn };
    });
  } catch (err) {
    throw friendly(err, '뽑기에 실패했어요. 다시 눌러 주세요.');
  }
}

export async function getMyTicket(code) {
  const uid = await myUid();
  try {
    const snap = await getDoc(doc(db, 'tickets', String(code ?? '').toUpperCase()));
    return snap.exists() && snap.data().uid === uid ? snap.data() : null;
  } catch {
    return null;
  }
}

export async function getTicket(code) {
  const snap = await getDoc(doc(db, 'tickets', String(code ?? '').toUpperCase()));
  return snap.exists() ? snap.data() : null;
}

export async function redeemTicket(code) {
  const ref = doc(db, 'tickets', String(code ?? '').toUpperCase());
  return runTransaction(db, async tx => {
    const snap = await tx.get(ref);
    if (!snap.exists()) throw new Error('없는 코드예요.');
    const t = snap.data();
    if (!t.prizeId) throw new Error('아직 뽑기를 안 한 뽑기권이에요.');
    if (t.redeemed) throw new Error('이미 지급한 상품이에요.');
    const done = { redeemed: true, redeemedAt: Date.now() };
    tx.update(ref, done);
    return { ...t, ...done };
  });
}

// ---- 상품 (운영자만 수정) ----

export async function getPrizes() {
  await authReady;
  return prizesOf(await getDocs(collection(db, 'prizes')));
}

export async function savePrizes(prizes) {
  const existing = await getDocs(collection(db, 'prizes'));
  const keep = new Set(prizes.map(p => p.id));
  const batch = writeBatch(db);
  existing.docs.filter(d => !keep.has(d.id)).forEach(d => batch.delete(d.ref));
  prizes.forEach((p, order) => {
    batch.set(doc(db, 'prizes', p.id), { name: p.name, stock: p.stock, weight: p.weight, order });
  });
  await batch.commit();
}

// 라운드, 뽑기권, 받음 표시, 힌트를 모두 지운다. 상품 목록은 남긴다
export async function resetAll() {
  const refs = [];
  for (const name of ['tickets', 'claims', 'roundKeys']) {
    (await getDocs(collection(db, name))).docs.forEach(d => refs.push(d.ref));
  }
  refs.push(hintRef);
  for (let i = 0; i < refs.length; i += 400) {
    const batch = writeBatch(db);
    refs.slice(i, i + 400).forEach(ref => batch.delete(ref));
    if (i === 0) batch.set(gameRef, IDLE);
    await batch.commit();
  }
}

// ---- 운영자 ----

// 운영자 비밀번호를 staff/{uid} 문서로 써 본다. 비밀번호가 맞을 때만 규칙이 쓰기를 허락하고,
// 그 뒤로는 이 기기(uid)가 운영자로 인정된다. 비밀번호 자체는 사이트 코드에 없다
export async function staffSignIn(_email, password) {
  const uid = await myUid();
  try {
    await setDoc(doc(db, 'staff', uid), { pw: password });
  } catch (err) {
    throw friendly(err, '비밀번호가 틀렸어요.');
  }
}

// 운영자만 읽을 수 있는 곳을 한 번 읽어 보고 판정한다
export async function isStaff() {
  await authReady;
  try {
    await getDoc(doc(db, 'roundKeys', 'staff-check'));
    return true;
  } catch {
    return false;
  }
}
