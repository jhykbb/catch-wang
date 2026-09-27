import { CONFIG } from './config.js';

export const $ = (sel, root = document) => root.querySelector(sel);

export function escapeHtml(s) {
  return String(s ?? '').replace(/[&<>"']/g, c => (
    { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
  ));
}

function storageGet(k) { try { return localStorage.getItem(k); } catch { return null; } }
function storageSet(k, v) { try { localStorage.setItem(k, v); } catch {} }

export function getNickname() { return storageGet('catchwang-nick') || ''; }

// 닉네임이 없으면 입력 폼을 띄우고, 입력될 때까지 기다린다
export function ensureNickname(container) {
  const existing = getNickname();
  if (existing) return Promise.resolve(existing);
  return new Promise(resolve => {
    container.innerHTML = `
      <form class="card" id="nick-form">
        <h2>닉네임을 정해 주세요</h2>
        <p class="muted">이름이나 학번은 적지 마세요. 상품 받을 때 확인용이에요.</p>
        <input id="nick-input" maxlength="12" required placeholder="예: 번개토끼" autocomplete="off">
        <button class="btn">참가하기</button>
      </form>`;
    $('#nick-form', container).addEventListener('submit', e => {
      e.preventDefault();
      const nick = $('#nick-input', container).value.trim();
      if (!nick) return;
      storageSet('catchwang-nick', nick);
      container.innerHTML = '';
      resolve(nick);
    });
  });
}

export function applyTitle() {
  document.querySelectorAll('[data-title]').forEach(el => { el.textContent = CONFIG.gameTitle; });
  document.title = document.title.replace('{title}', CONFIG.gameTitle);
}

export function formatRemaining(ms) {
  if (ms <= 0) return '0:00';
  const s = Math.ceil(ms / 1000);
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

export function formatClock(ts) {
  return new Date(ts).toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit' });
}

// 사진을 긴 변 900px JPEG로 줄인다 (저장 용량과 데이터 사용량 절약)
export async function compressImage(file, maxSide = 900, quality = 0.65) {
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, maxSide / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  return canvas.toDataURL('image/jpeg', quality);
}

export function getPosition() {
  return new Promise((resolve, reject) => {
    if (!navigator.geolocation) return reject(new Error('이 브라우저는 위치 기능을 지원하지 않아요.'));
    navigator.geolocation.getCurrentPosition(
      p => resolve({ lat: p.coords.latitude, lng: p.coords.longitude, accuracy: p.coords.accuracy }),
      err => reject(new Error(err.code === 1
        ? '위치 권한이 거부됐어요. 브라우저 설정에서 허용해 주세요.'
        : '위치를 가져오지 못했어요.')),
      { enableHighAccuracy: true, timeout: 15000, maximumAge: 0 },
    );
  });
}

// ---- 두 저장소(로컬, Firebase)가 같이 쓰는 게임 규칙 ----

// 시간이 다 되면 저장된 값이 live여도 끝난 것으로 본다
export function statusOf(game, now = Date.now()) {
  if (game.status === 'live' && now >= game.endsAt) return 'ended';
  return game.status;
}

const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // 헷갈리는 0/O, 1/I 제외

export function randomCode(len) {
  const nums = crypto.getRandomValues(new Uint32Array(len));
  return Array.from(nums, n => CODE_CHARS[n % CODE_CHARS.length]).join('');
}

// 실제 좌표 대신 반경 안에서 무작위로 옮긴 원만 저장한다. 실제 위치는 항상 원 안에 있다.
export function blurArea({ lat, lng, accuracy }) {
  const radius = Math.max(CONFIG.mapBlurMeters, Math.min(accuracy ?? 0, 150));
  const angle = Math.random() * 2 * Math.PI;
  const dist = Math.random() * radius * 0.6;
  return {
    lat: lat + (dist * Math.cos(angle)) / 111320,
    lng: lng + (dist * Math.sin(angle)) / (111320 * Math.cos(lat * Math.PI / 180)),
    radius,
  };
}

// 재고가 남은 상품 중에서 weight 비율로 하나 고른다
export function weightedPick(prizes) {
  const pool = prizes.filter(p => p.stock > 0 && p.weight > 0);
  if (!pool.length) throw new Error('상품이 모두 소진됐어요. 부스에 문의해 주세요.');
  let r = Math.random() * pool.reduce((sum, p) => sum + p.weight, 0);
  for (const p of pool) {
    r -= p.weight;
    if (r < 0) return p;
  }
  return pool[pool.length - 1];
}

export function newestFirst(tickets) {
  return [...tickets].sort((a, b) => b.createdAt - a.createdAt);
}
