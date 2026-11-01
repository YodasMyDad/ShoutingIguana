// Small behaviour for the static mockups: the theme switch, the density switch and tabs.
// ?theme=light|dark and ?density=compact in the address override what's saved, for screenshots.
(() => {
  const root = document.documentElement;
  const store = {
    get(key) { try { return localStorage.getItem(key); } catch { return null; } },
    set(key, value) { try { localStorage.setItem(key, value); } catch { /* storage blocked: the page still works */ } },
  };
  const params = new URLSearchParams(location.search);

  function applyTheme(choice) {
    if (choice === 'light' || choice === 'dark') root.dataset.theme = choice;
    else delete root.dataset.theme;
    document.querySelectorAll('[data-theme-choice]').forEach(b =>
      b.setAttribute('aria-checked', String(b.dataset.themeChoice === (choice || 'system'))));
  }
  function applyDensity(density) {
    root.dataset.density = density === 'compact' ? 'compact' : 'comfortable';
    document.querySelectorAll('[data-density-choice]').forEach(b =>
      b.setAttribute('aria-pressed', String(b.dataset.densityChoice === root.dataset.density)));
  }

  applyTheme(params.get('theme') || store.get('si-theme') || 'system');
  applyDensity(params.get('density') || store.get('si-density') || root.dataset.density);

  document.addEventListener('click', e => {
    const theme = e.target.closest('[data-theme-choice]');
    if (theme) { applyTheme(theme.dataset.themeChoice); store.set('si-theme', theme.dataset.themeChoice); return; }
    const density = e.target.closest('[data-density-choice]');
    if (density) { applyDensity(density.dataset.densityChoice); store.set('si-density', density.dataset.densityChoice); return; }
    const tab = e.target.closest('[role="tab"]');
    if (tab) {
      tab.closest('[role="tablist"]').querySelectorAll('[role="tab"]').forEach(t => {
        const on = t === tab;
        t.setAttribute('aria-selected', String(on));
        t.tabIndex = on ? 0 : -1;
      });
    }
  });

  // A grid reopens with its selected row in view, as the app restores it.
  document.querySelectorAll('.grid-scroll tr.is-selected').forEach(row => {
    const box = row.closest('.grid-scroll');
    const head = box.querySelector('thead')?.offsetHeight ?? 0;
    if (row.offsetTop + row.offsetHeight > box.scrollTop + box.clientHeight) box.scrollTop = row.offsetTop - head - row.offsetHeight * 3;
  });

  // Arrow keys move between tabs, as the ARIA tabs pattern expects.
  document.addEventListener('keydown', e => {
    const tab = e.target.closest?.('[role="tab"]');
    if (!tab || !['ArrowLeft', 'ArrowRight'].includes(e.key)) return;
    const tabs = [...tab.closest('[role="tablist"]').querySelectorAll('[role="tab"]')];
    const next = tabs[(tabs.indexOf(tab) + (e.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length];
    next.focus();
    next.click();
    e.preventDefault();
  });
})();
