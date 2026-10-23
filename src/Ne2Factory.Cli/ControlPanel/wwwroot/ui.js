// Tiny DOM helpers shared by the views. Children are appended as text nodes,
// never parsed as HTML, so ticket titles and log lines are safe to render as-is.

export function h(tag, props = {}, ...children) {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(props ?? {})) {
    if (value == null || value === false) continue;
    if (key.startsWith('on')) el.addEventListener(key.slice(2).toLowerCase(), value);
    else if (key === 'class') el.className = value;
    else el.setAttribute(key, value === true ? '' : value);
  }
  el.append(...children.flat().filter((c) => c != null && c !== false));
  return el;
}

// Calls fn now and every `ms` until the returned stop function is called.
// Never overlaps calls: the next one waits for the previous to settle.
export function poll(fn, ms) {
  let stopped = false;
  let timer;
  const tick = async () => {
    try { await fn(); } catch (err) { console.error(err); }
    if (!stopped) timer = setTimeout(tick, ms);
  };
  tick();
  return () => { stopped = true; clearTimeout(timer); };
}

export function formatDate(value) {
  return value ? new Date(value).toLocaleString('es-ES') : '—';
}

// Runs an async action from a button: disables it meanwhile and reports a
// failure in `errorEl` (cleared on success).
export async function act(button, errorEl, action) {
  button.disabled = true;
  errorEl.textContent = '';
  try {
    await action();
  } catch (err) {
    errorEl.textContent = err.message;
  } finally {
    button.disabled = false;
  }
}
