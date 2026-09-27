// 운영자 페이지(도망자, 부스) 잠금. 한 번 비밀번호를 맞히면 그 기기는 계속 운영자로 인정된다
import { staffSignIn, isStaff } from './store.js';
import { $ } from './common.js';

export async function requireStaff(container) {
  if (await isStaff()) return;
  return new Promise(resolve => {
    container.innerHTML = `
      <form class="card" id="staff-form">
        <h2>운영자 확인</h2>
        <input id="staff-pw" type="password" inputmode="numeric" placeholder="개발자 비밀번호" required autocomplete="off">
        <button class="btn">들어가기</button>
        <p class="error" id="staff-err"></p>
      </form>`;
    $('#staff-form', container).addEventListener('submit', async e => {
      e.preventDefault();
      try {
        await staffSignIn('', $('#staff-pw', container).value);
        container.innerHTML = '';
        resolve();
      } catch (err) {
        $('#staff-err', container).textContent = err.message;
      }
    });
  });
}
