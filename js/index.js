// 참가자 메인 화면: 라운드 상태, 힌트 사진, 대략적인 위치, 내 뽑기권
import { CONFIG } from './config.js';
import { watchGame, statusOf, watchMyTickets } from './store.js';
import { $, escapeHtml, applyTitle, formatRemaining, formatClock } from './common.js';

applyTitle();
const statusEl = $('#status');
const target = escapeHtml(CONFIG.targetName);

let game = null;
let map = null;
let shownKey = null;

// 상태가 바뀔 때만 카드 전체를 다시 그리고, 1초마다는 타이머만 갱신한다
function render() {
  if (!game) return;
  const st = statusOf(game);
  const key = `${st}|${game.roundId}|${game.hintAt}`;
  if (key !== shownKey) {
    shownKey = key;
    drawStatus(st);
  }
  const timer = $('#timer');
  if (timer) timer.textContent = formatRemaining(game.endsAt - Date.now());
}

function drawStatus(st) {
  if (map) { map.remove(); map = null; }
  statusEl.className = st === 'live' ? 'card live' : 'card';

  if (st === 'live') {
    statusEl.innerHTML = `
      <p class="badge">${escapeHtml(game.roundId)} 진행 중</p>
      <h2>${target}이(가) 나타났습니다!</h2>
      <p class="timer-line">남은 시간 <strong id="timer"></strong></p>
      <img class="hint" id="hint-photo" alt="${target} 주변 힌트 사진">
      <p class="muted">힌트 사진 · ${formatClock(game.hintAt)}에 올라옴</p>
      <div id="map" class="map"></div>
      <p class="muted">빨간 원 안 어딘가에 있어요. 정확한 위치는 아니에요.</p>`;
    $('#hint-photo').src = game.photo;
    drawMap();
  } else if (st === 'ready') {
    statusEl.innerHTML = `
      <h2>곧 ${target}이(가) 나타나요</h2>
      <p class="muted">도망자가 숨을 곳으로 이동하는 중이에요. 이 화면을 켜 두세요.</p>`;
  } else if (st === 'ended') {
    statusEl.innerHTML = `
      <h2>${escapeHtml(game.roundId)} 라운드 종료!</h2>
      <p class="muted">다음 라운드를 기다려 주세요.</p>`;
  } else {
    statusEl.innerHTML = `
      <h2>아직 게임 전이에요</h2>
      <p class="muted">라운드가 시작되면 여기에 힌트가 올라와요.</p>`;
  }
}

function drawMap() {
  if (!window.L || !game.area) return;
  map = L.map('map', { scrollWheelZoom: false }).setView([game.area.lat, game.area.lng], 17);
  L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; OpenStreetMap',
  }).addTo(map);
  const circle = L.circle([game.area.lat, game.area.lng], {
    radius: game.area.radius,
    color: '#e0342b',
    fillOpacity: 0.2,
  }).addTo(map);
  map.fitBounds(circle.getBounds(), { padding: [24, 24] });
}

function renderTickets(list) {
  $('#tickets').innerHTML = list.length
    ? list.map(t => `
        <li>
          <span class="code">${escapeHtml(t.code)}</span>
          <span>${t.prizeName
            ? escapeHtml(t.prizeName)
            : `<a href="claim.html?t=${encodeURIComponent(t.code)}">뽑기 하러 가기</a>`}</span>
          <span class="muted">${t.redeemed ? '수령 완료' : t.prizeName ? '부스에서 받기' : ''}</span>
        </li>`).join('')
    : `<li class="muted">아직 없어요. ${target}을(를) 찾아보세요!</li>`;
}

watchGame(g => { game = g; render(); });
watchMyTickets(renderTickets);
setInterval(render, 1000);
