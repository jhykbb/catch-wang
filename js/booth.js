// 보상 부스 화면: 라운드 준비와 QR 인쇄, 상품 교환, 기록, 재고 관리
import { DEFAULT_PRIZES } from './config.js';
import {
  watchGame, watchRoundTickets, watchPrizes, statusOf, prepareRound, endRound, getRoundKey,
  getTicket, redeemTicket, savePrizes, resetAll,
} from './store.js';
import { $, escapeHtml, applyTitle, formatRemaining } from './common.js';
import { requireStaff } from './staff.js';

applyTitle();
const app = $('#app');
await requireStaff(app);
app.replaceChildren($('#page').content.cloneNode(true));

let game = null;
let qrRound = null;
let prizeDirty = false;

// ---- 라운드 ----

function makeQr(el, text, size) {
  el.innerHTML = '';
  new QRCode(el, { text, width: size, height: size, correctLevel: QRCode.CorrectLevel.M });
}

async function renderRound() {
  if (!game) return;
  const st = statusOf(game);
  const labels = {
    idle: '아직 라운드가 없어요.',
    ready: `${game.roundId} 준비됨 · 도망자가 시작 버튼을 누르면 시작돼요.`,
    live: `${game.roundId} 진행 중 · 남은 시간 ${formatRemaining(game.endsAt - Date.now())} · 뽑기권 ${game.claimed}/${game.maxTickets}`,
    ended: `${game.roundId} 종료됨`,
  };
  $('#round-status').textContent = labels[st];
  $('#prepare-btn').disabled = st === 'live';
  $('#end-btn').disabled = st !== 'live';

  const showQr = st === 'ready' || st === 'live';
  $('#qr-wrap').hidden = !showQr;
  if (showQr && qrRound !== game.roundId) {
    qrRound = game.roundId;
    const key = await getRoundKey(game.roundId);
    const url = new URL('claim.html', location.href);
    url.search = new URLSearchParams({ r: game.roundId, k: key }).toString();
    makeQr($('#qr'), url.href, 200);
    makeQr($('#print-qr'), url.href, 460);
    $('#qr-url').textContent = url.href;
    $('#claim-link').href = url.href;
    $('#print-round').textContent = `${game.roundId} 라운드 전용`;
  }
}

$('#prepare-btn').addEventListener('click', async () => {
  try { await prepareRound(); } catch (err) { alert(err.message); }
});
$('#end-btn').addEventListener('click', () => endRound());
$('#print-btn').addEventListener('click', () => window.print());

// ---- 상품 교환 ----

function showRedeem(t) {
  const box = $('#redeem-result');
  if (!t) {
    box.innerHTML = '<p class="error">없는 코드예요.</p>';
    return;
  }
  box.innerHTML = `
    <p><span class="code">${escapeHtml(t.code)}</span> ${escapeHtml(t.nickname)} · ${escapeHtml(t.roundId)}</p>
    <p style="font-size: 1.4rem; font-weight: 800; margin: 4px 0">${escapeHtml(t.prizeName ?? '아직 뽑기 전')}</p>
    ${t.redeemed
      ? '<p class="ok">지급 완료된 뽑기권이에요.</p>'
      : t.prizeName ? '<button class="btn" id="give-btn">상품 주고 지급 완료로 표시</button>' : ''}
    <p class="error" id="give-err"></p>`;
  $('#give-btn')?.addEventListener('click', async () => {
    try {
      showRedeem(await redeemTicket(t.code));
      $('#redeem-code').value = '';
    } catch (err) {
      $('#give-err').textContent = err.message;
    }
  });
}

$('#redeem-form').addEventListener('submit', async e => {
  e.preventDefault();
  showRedeem(await getTicket($('#redeem-code').value.trim()));
});

// ---- 기록 ----

let ticketsRound = null;
let stopTickets = () => {};

// 라운드가 바뀔 때만 구독을 새로 건다
function followRoundTickets() {
  const roundId = game?.roundId ?? null;
  if (roundId === ticketsRound) return;
  ticketsRound = roundId;
  stopTickets();
  if (roundId) stopTickets = watchRoundTickets(roundId, renderTickets);
  else renderTickets([]);
}

function renderTickets(rows) {
  $('#ticket-rows').innerHTML = rows.length
    ? rows.map(t => `
        <tr>
          <td>${escapeHtml(t.nickname)}</td>
          <td><span class="code">${escapeHtml(t.code)}</span></td>
          <td>${escapeHtml(t.prizeName ?? '뽑기 전')}</td>
          <td>${t.redeemed ? '<span class="ok">완료</span>' : '-'}</td>
        </tr>`).join('')
    : '<tr><td colspan="4" class="muted">아직 없어요.</td></tr>';
}

// ---- 상품 재고 ----

const prizeRow = p => `
  <tr data-id="${escapeHtml(p.id)}">
    <td><input class="p-name" value="${escapeHtml(p.name)}" placeholder="상품 이름"></td>
    <td><input class="p-stock" type="number" min="0" value="${p.stock}"></td>
    <td><input class="p-weight" type="number" min="0" value="${p.weight}"></td>
    <td><button type="button" class="link p-del">삭제</button></td>
  </tr>`;

function markDirty() {
  prizeDirty = true;
  $('#prize-msg').textContent = '저장하지 않은 변경이 있어요.';
}

let latestPrizes = [];

// 편집 중에는 다른 곳의 변경(뽑기로 재고 감소)이 와도 덮어쓰지 않는다
function renderPrizes(force = false) {
  if (prizeDirty && !force) return;
  if (!latestPrizes.length) {
    // 처음 쓰는 Firebase에는 상품이 없으니 기본 상품을 채워 두고 저장을 기다린다
    $('#prize-rows').innerHTML = DEFAULT_PRIZES.map(prizeRow).join('');
    prizeDirty = true;
    $('#prize-msg').textContent = '기본 상품이에요. 확인하고 저장을 눌러야 적용돼요.';
    return;
  }
  $('#prize-rows').innerHTML = latestPrizes.map(prizeRow).join('');
}

$('#prize-rows').addEventListener('input', markDirty);
$('#prize-rows').addEventListener('click', e => {
  if (!e.target.classList.contains('p-del')) return;
  e.target.closest('tr').remove();
  markDirty();
});
$('#add-prize').addEventListener('click', () => {
  $('#prize-rows').insertAdjacentHTML('beforeend',
    prizeRow({ id: `p${Date.now().toString(36)}`, name: '', stock: 0, weight: 1 }));
  markDirty();
});
$('#save-prizes').addEventListener('click', async () => {
  const prizes = [...$('#prize-rows').querySelectorAll('tr')]
    .map(tr => ({
      id: tr.dataset.id,
      name: tr.querySelector('.p-name').value.trim(),
      stock: Math.max(0, parseInt(tr.querySelector('.p-stock').value, 10) || 0),
      weight: Math.max(0, Number(tr.querySelector('.p-weight').value) || 0),
    }))
    .filter(p => p.name);
  await savePrizes(prizes);
  prizeDirty = false;
  $('#prize-msg').textContent = '저장했어요.';
  renderPrizes(true);
});

// ---- 테스트용 ----

$('#reset-btn').addEventListener('click', async () => {
  if (!confirm('라운드와 뽑기권 기록이 모두 지워져요. 계속할까요?')) return;
  await resetAll();
  qrRound = null;
});

watchGame(g => { game = g; renderRound(); followRoundTickets(); });
watchPrizes(prizes => { latestPrizes = prizes; renderPrizes(); });
setInterval(renderRound, 1000);
