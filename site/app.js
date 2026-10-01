// TaskPack 홈페이지: 가방 미리보기와 최신 릴리스 다운로드 연결
(() => {
  const REPO = 'pomeloEater/TaskPack';

  // ───────── 가방 미리보기 ─────────
  const bag = document.getElementById('bag');
  const grid = document.getElementById('grid');
  const drawer = document.getElementById('drawer');
  const pin = document.getElementById('pin');
  const toast = document.getElementById('toast');

  // 탭(테마)마다 들어 있는 아이템: [이름, 그림 문자, 배경색]. 빈칸은 null
  const contents = {
    dark:   [['메모', '📝', '#F5C451'], ['브라우저', '🌐', '#5BA8F5'], null, ['터미널', '⌨️', '#3B3F45'], ['폴더', '📁', '#F2A541'], null, null, ['메일', '✉️', '#7C8CF8'], null],
    custom: [['게임', '🎮', '#F76B15'], null, ['음악', '🎵', '#E5484D'], null, ['채팅', '💬', '#30A46C'], null, ['녹화', '🎥', '#8E4EC6'], null, ['스크린샷', '📸', '#0090FF']],
    light:  [['사전', '📖', '#AD7F58'], ['필기', '✏️', '#FFC53D'], ['계산기', '🧮', '#8B8D98'], null, null, ['타이머', '⏱️', '#12A594'], null, null, null],
  };

  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  function renderSlots(theme) {
    grid.replaceChildren(...contents[theme].map((item) => {
      const slot = document.createElement('button');
      slot.className = 'slot';
      if (!item) {
        slot.classList.add('empty');
        slot.tabIndex = -1;
        slot.setAttribute('aria-hidden', 'true');
        return slot;
      }
      const [name, emoji, color] = item;
      const ico = document.createElement('span');
      ico.className = 'ico';
      ico.style.background = color;
      ico.textContent = emoji;
      slot.append(ico, name);
      slot.addEventListener('click', () => launch(slot, name));
      return slot;
    }));
  }

  function setOpen(open) {
    bag.classList.toggle('open', open);
    bag.setAttribute('aria-hidden', String(!open));
    drawer.classList.toggle('running', open);
  }

  let toastTimer;
  function showToast(text) {
    toast.textContent = text;
    toast.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => toast.classList.remove('show'), 1400);
  }

  // 칸을 누르면 "실행"하고, 고정(📌)이 꺼져 있으면 가방이 닫힌다 — 앱과 같은 동작
  function launch(slot, name) {
    slot.classList.remove('pop');
    void slot.offsetWidth;
    slot.classList.add('pop');
    showToast(`${name} 실행!`);
    if (pin.getAttribute('aria-pressed') !== 'true')
      setTimeout(() => setOpen(false), reduceMotion ? 0 : 180);
  }

  drawer.addEventListener('click', () => {
    drawer.classList.remove('nudge');
    setOpen(!bag.classList.contains('open'));
  });

  pin.addEventListener('click', () => {
    const on = pin.getAttribute('aria-pressed') !== 'true';
    pin.setAttribute('aria-pressed', String(on));
    showToast(on ? '고정됨: 바깥을 눌러도 안 닫혀요' : '고정 해제');
  });

  bag.querySelectorAll('.tab[data-theme]').forEach((tab) => {
    tab.addEventListener('click', () => {
      bag.querySelectorAll('.tab').forEach((t) => t.classList.toggle('active', t === tab));
      bag.dataset.theme = tab.dataset.theme;
      renderSlots(tab.dataset.theme);
    });
  });

  // 가방 바깥(바탕화면)을 누르면 닫힌다
  document.querySelector('.desk').addEventListener('click', (e) => {
    if (!bag.contains(e.target) && !drawer.contains(e.target) && pin.getAttribute('aria-pressed') !== 'true')
      setOpen(false);
  });

  renderSlots(bag.dataset.theme);
  // 처음엔 한 번 열어 보여 주고, 가방 아이콘을 눌러 보라고 살짝 두근거리게 한다
  setTimeout(() => setOpen(true), reduceMotion ? 0 : 900);
  setTimeout(() => drawer.classList.add('nudge'), 2600);

  // ───────── 최신 릴리스 다운로드 연결 ─────────
  // 설치 파일은 두 가지: TaskPack-Setup-<버전>.exe (.NET 포함, 기본)과 TaskPack-Setup-<버전>-lite.exe (.NET 8을 따로 설치).
  // 파일 이름의 "-lite"로 둘을 가른다. 실패하거나 못 찾으면 링크는 GitHub 릴리스 페이지를 그대로 가리킨다
  const mb = (bytes) => (bytes / 1024 / 1024).toFixed(1);
  fetch(`https://api.github.com/repos/${REPO}/releases/latest`)
    .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
    .then((release) => {
      const exes = (release.assets || []).filter((a) => /\.exe$/i.test(a.name));
      const lite = exes.find((a) => /-lite\.exe$/i.test(a.name));
      const full = exes.find((a) => !/-lite\.exe$/i.test(a.name));
      const version = String(release.tag_name || '').replace(/^v/, '');

      if (full) {
        document.querySelectorAll('#download, .inline-download').forEach((a) => { a.href = full.browser_download_url; });
        document.getElementById('download-meta').textContent =
          `v${version} · ${mb(full.size)}MB · 무료 · MIT 라이선스 · Windows 10/11 64비트`;
      }
      if (lite) {
        document.querySelectorAll('.lite-download').forEach((a) => { a.href = lite.browser_download_url; });
        document.querySelectorAll('.lite-size').forEach((el) => { el.textContent = `약 ${mb(lite.size)}MB`; });
      }
    })
    .catch(() => {});
})();
