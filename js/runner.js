// 도망자 화면: 숨을 곳에서 사진 + GPS를 올려 라운드를 시작하고, 끝나면 QR을 뗀다
import { watchGame, statusOf, startRound, updateHint, endRound } from './store.js';
import { $, escapeHtml, applyTitle, formatRemaining, compressImage, getPosition } from './common.js';
import { requireStaff } from './staff.js';

applyTitle();
const app = $('#app');
await requireStaff(app);

let game = null;
let shownKey = null;

// 사진 찍기 + 위치 확인 + 올리기 버튼 묶음. 둘 다 준비돼야 버튼이 켜진다
function hintForm(label, onSubmit) {
  const wrap = document.createElement('div');
  wrap.innerHTML = `
    <label class="field">1. 주변 사진 찍기
      <input type="file" accept="image/*" capture="environment" id="photo-input">
    </label>
    <p class="muted">얼굴 말고 바닥, 벽, 사물 위주로 찍어 주세요.</p>
    <img id="photo-preview" class="hint" hidden alt="찍은 사진 미리보기">
    <p id="gps-line" class="field"></p>
    <button class="btn" id="hint-submit" disabled>${escapeHtml(label)}</button>
    <p class="error" id="hint-err"></p>`;

  let photo = null;
  let position = null;
  const submit = $('#hint-submit', wrap);
  const updateButton = () => { submit.disabled = !(photo && position); };

  $('#photo-input', wrap).addEventListener('change', async e => {
    const file = e.target.files[0];
    if (!file) return;
    try {
      photo = await compressImage(file);
      const img = $('#photo-preview', wrap);
      img.src = photo;
      img.hidden = false;
      $('#hint-err', wrap).textContent = '';
    } catch {
      photo = null;
      $('#hint-err', wrap).textContent = '사진을 읽지 못했어요. 다시 찍어 주세요.';
    }
    updateButton();
  });

  const locate = async () => {
    const line = $('#gps-line', wrap);
    line.textContent = '2. 위치 확인 중...';
    try {
      position = await getPosition();
      line.textContent = `2. 위치 확인 완료 (오차 약 ${Math.round(position.accuracy)}m)`;
    } catch (err) {
      position = null;
      line.innerHTML = `2. ${escapeHtml(err.message)} <button type="button" class="link">다시 시도</button>`;
      $('button', line).addEventListener('click', locate);
    }
    updateButton();
  };
  locate();

  submit.addEventListener('click', async () => {
    submit.disabled = true;
    try {
      await onSubmit({ photo, position });
    } catch (err) {
      $('#hint-err', wrap).textContent = err.message;
      updateButton();
    }
  });
  return wrap;
}

function render() {
  if (!game) return;
  const st = statusOf(game);
  const key = `${st}|${game.roundId}`;
  if (key !== shownKey) {
    shownKey = key;
    draw(st);
  }
  const timer = $('#timer');
  if (timer) timer.textContent = formatRemaining(game.endsAt - Date.now());
  const claimed = $('#claimed');
  if (claimed) claimed.textContent = `${game.claimed} / ${game.maxTickets}`;
  const photo = $('#current-photo');
  if (photo && photo.dataset.at !== String(game.hintAt)) {
    photo.src = game.photo;
    photo.dataset.at = String(game.hintAt);
  }
}

function draw(st) {
  const round = escapeHtml(game.roundId ?? '');

  if (st === 'ready') {
    app.innerHTML = `
      <section class="card">
        <p class="badge">${round} 준비 완료</p>
        <h2>숨을 곳에 도착하면 시작하세요</h2>
        <p>등에 <strong>${round}</strong> QR이 붙어 있는지 먼저 확인하세요.</p>
        <div id="form-slot"></div>
      </section>`;
    $('#form-slot').append(hintForm('라운드 시작!', startRound));
  } else if (st === 'live') {
    app.innerHTML = `
      <section class="card live">
        <p class="badge">${round} 진행 중</p>
        <p class="timer-line">남은 시간 <strong id="timer"></strong></p>
        <p>나간 뽑기권 <strong id="claimed"></strong></p>
        <p>참가자가 찾아오면 등을 돌려 QR을 찍게 해 주세요.</p>
        <img class="hint" id="current-photo" alt="지금 공개된 힌트 사진">
        <p class="muted">지금 참가자에게 보이는 힌트 사진</p>
      </section>
      <section class="card">
        <h2>자리를 옮겼나요?</h2>
        <p class="muted">새 장소에서 사진과 위치를 다시 올리세요.</p>
        <div id="form-slot"></div>
      </section>
      <section class="card">
        <button class="btn secondary" id="end-btn">라운드 일찍 끝내기</button>
      </section>`;
    $('#form-slot').append(hintForm('힌트 다시 올리기', async data => {
      await updateHint(data);
      shownKey = null; // 폼을 새로 그려 다음 이동에 대비
      render();
    }));
    const endBtn = $('#end-btn');
    endBtn.addEventListener('click', async () => {
      if (endBtn.dataset.armed) return endRound();
      endBtn.dataset.armed = '1';
      endBtn.textContent = '정말 끝낼까요? 한 번 더 누르세요';
    });
  } else if (st === 'ended') {
    app.innerHTML = `
      <section class="card">
        <h2>${round} 라운드 끝! 수고했어요</h2>
        <p>등의 QR을 떼고 보상 부스로 돌아와 주세요.</p>
        <p class="muted">다음 라운드는 부스에서 준비하면 이 화면에 나타나요.</p>
      </section>`;
  } else {
    app.innerHTML = `
      <section class="card">
        <h2>대기 중</h2>
        <p>부스에서 "새 라운드 준비"를 누르면 시작할 수 있어요.</p>
      </section>`;
  }
}

watchGame(g => { game = g; render(); });
setInterval(render, 1000);
