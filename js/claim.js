// 도망자 등의 QR을 찍으면 오는 페이지: 뽑기권 받기 -> 뽑기 -> 교환 코드
// QR 주소: claim.html?r=라운드&k=비밀값 / 받은 뒤에는 claim.html?t=교환코드 로 바뀐다
import { claimTicket, getMyTicket, drawPrize, getPrizes } from './store.js';
import { $, escapeHtml, applyTitle, ensureNickname } from './common.js';

applyTitle();
const app = $('#app');
const params = new URLSearchParams(location.search);

function showError(message) {
  app.innerHTML = `
    <section class="card">
      <h2>앗!</h2>
      <p>${escapeHtml(message)}</p>
      <a class="btn secondary" href="index.html">메인으로</a>
    </section>`;
}

function showResult(ticket) {
  app.innerHTML = `
    <section class="card draw">
      <p class="badge">${escapeHtml(ticket.roundId)} · ${escapeHtml(ticket.nickname)}</p>
      <div class="slot">${escapeHtml(ticket.prizeName)}</div>
      <p>교환 코드</p>
      <div class="big-code">${escapeHtml(ticket.code)}</div>
      <p class="muted">${ticket.redeemed
        ? '이미 상품을 받았어요.'
        : '보상 부스에 가서 이 화면을 보여 주세요.'}</p>
      <a class="btn secondary" href="index.html">메인으로</a>
    </section>`;
}

async function showDraw(ticket, notice) {
  app.innerHTML = `
    <section class="card draw">
      <h2>뽑기권 획득!</h2>
      ${notice ? `<p class="muted">${escapeHtml(notice)}</p>` : ''}
      <div class="slot" id="slot">?</div>
      <button class="btn" id="draw-btn">뽑기!</button>
      <p class="error" id="draw-err"></p>
    </section>`;

  const names = (await getPrizes()).map(p => p.name);
  $('#draw-btn').addEventListener('click', async e => {
    e.target.disabled = true;
    const slot = $('#slot');
    let i = 0;
    const spin = setInterval(() => { slot.textContent = names[i++ % names.length]; }, 80);
    try {
      const [result] = await Promise.all([
        drawPrize(ticket.code),
        new Promise(r => setTimeout(r, 1600)),
      ]);
      clearInterval(spin);
      showResult(result);
    } catch (err) {
      clearInterval(spin);
      slot.textContent = '?';
      $('#draw-err').textContent = err.message;
      e.target.disabled = false;
    }
  });
}

async function main() {
  let code = params.get('t');
  let notice = '';

  if (!code) {
    const roundId = params.get('r');
    const key = params.get('k');
    if (!roundId || !key) return showError('QR 정보가 없어요. 도망자 등의 QR을 다시 찍어 주세요.');
    const nickname = await ensureNickname(app);
    try {
      const { ticket, already } = await claimTicket({ roundId, key, nickname });
      code = ticket.code;
      if (already) notice = '이번 라운드 뽑기권은 이미 받았어요.';
    } catch (err) {
      return showError(err.message);
    }
    // 새로고침해도 다시 받지 않게, 그리고 주소창에서 비밀값이 보이지 않게 바꿔 둔다
    history.replaceState(null, '', `claim.html?t=${encodeURIComponent(code)}`);
  }

  const ticket = await getMyTicket(code);
  if (!ticket) return showError('이 기기의 뽑기권이 아니에요.');
  if (ticket.prizeName) showResult(ticket);
  else showDraw(ticket, notice);
}

main();
