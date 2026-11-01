// Direction C, "guide": the only script. The theme toggle and the expanders; nothing else.
(function () {
  var root = document.documentElement;
  var order = ['system', 'light', 'dark'];
  var labels = { system: 'Theme: follows the system', light: 'Theme: light', dark: 'Theme: dark' };
  var icons = { system: 'monitor', light: 'sun', dark: 'moon' };

  function current() { return root.getAttribute('data-theme') || 'system'; }
  function apply(mode) {
    if (mode === 'system') root.removeAttribute('data-theme'); else root.setAttribute('data-theme', mode);
    try { localStorage.setItem('si-guide-theme', mode); } catch (e) { /* storage unavailable: the choice lasts for this page */ }
    document.querySelectorAll('[data-theme-toggle]').forEach(function (btn) {
      btn.setAttribute('aria-label', labels[mode] + '. Change theme');
      btn.setAttribute('title', labels[mode]);
      var use = btn.querySelector('use');
      if (use) use.setAttribute('href', '#i-' + icons[mode]);
    });
  }

  document.addEventListener('click', function (event) {
    var toggle = event.target.closest('[data-theme-toggle]');
    if (toggle) {
      apply(order[(order.indexOf(current()) + 1) % order.length]);
      return;
    }
    var expander = event.target.closest('[aria-controls][aria-expanded]');
    if (expander) {
      var open = expander.getAttribute('aria-expanded') !== 'true';
      expander.setAttribute('aria-expanded', String(open));
      expander.getAttribute('aria-controls').split(' ').forEach(function (id) {
        document.querySelectorAll('[data-group="' + id + '"]').forEach(function (row) { row.hidden = !open; });
      });
    }
  });

  apply(current());
})();
