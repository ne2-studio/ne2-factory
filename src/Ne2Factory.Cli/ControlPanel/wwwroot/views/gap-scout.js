import { api } from '../api.js';
import { act, h } from '../ui.js';

// `gap-scout scan <scope|all> [--yolo]`.
export default {
  id: 'gap-scout',
  title: 'Gap-scout',
  mount(root) {
    const target = h('select', {}, h('option', { value: 'all' }, 'todos los scopes'));
    const yolo = h('input', { type: 'checkbox' });
    const scan = h('button', { class: 'primary', onclick: () => act(scan, error, enqueue) }, 'Encolar scan');
    const error = h('p', { class: 'error' });
    const results = h('ul', { class: 'results' });

    root.append(
      h('section', { class: 'card' },
        h('h2', {}, 'Gap-scout'),
        h('p', { class: 'muted' },
          'Encola un job por scope que lanza el agente architecture-gap-scout y archiva sus hallazgos como issues (label gap-scout), pendientes de aprobación. ',
          'Los ejecuta el worker: sin él en marcha, quedan pendientes hasta que arranque.'),
        h('div', { class: 'row' },
          h('label', {}, 'Scope ', target),
          h('label', { title: 'claude --dangerously-skip-permissions en vez de una lista de tools permitidas' }, yolo, ' --yolo'),
          scan),
        h('p', { class: 'muted small' },
          'Sin --yolo, un comando fuera de la lista permitida (gh issue/label) espera una aprobación que nadie puede dar y el job se cuelga hasta su timeout. ',
          'architecture-gap-scout no toca git ni edita código.'),
        error,
        results));

    api('/gap-scout/scopes')
      .then((scopes) => target.append(...scopes.map((s) => h('option', { value: s }, s))))
      .catch((err) => { error.textContent = err.message; });

    async function enqueue() {
      const queued = await api('/gap-scout/scans', { method: 'POST', body: { target: target.value, yolo: yolo.checked } });
      results.replaceChildren(...queued.map((q) => h('li', {}, q.jobId
        ? `Encolado: job ${q.jobId} (scope: ${q.scope})`
        : `Saltado: ya hay un scan encolado o en curso para ${q.scope}`)));
    }
  },
};
