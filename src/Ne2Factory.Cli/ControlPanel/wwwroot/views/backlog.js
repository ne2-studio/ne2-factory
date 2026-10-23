import { api } from '../api.js';
import { act, h } from '../ui.js';

// `backlog list` and `backlog requeue`.
const sections = {
  Unrefined: 'En cola, sin refinar',
  Refined: 'En cola, lista para implementar',
  MissingData: 'Sin datos suficientes (esperando al humano)',
  InReview: 'En revisión (pull request abierto)',
  Done: 'Hechos',
  Failed: 'Fallidos/bloqueados',
};

// Tickets `backlog requeue` can move back into the queue.
const requeueable = new Set(['Failed', 'MissingData', 'InReview']);

export default {
  id: 'backlog',
  title: 'Backlog',
  mount(root) {
    const refresh = h('button', { onclick: () => act(refresh, error, load) }, 'Refrescar');
    const error = h('p', { class: 'error' });
    const board = h('div', { class: 'board' }, h('p', { class: 'muted' }, 'Cargando…'));

    root.append(
      h('div', { class: 'row' }, h('h2', {}, 'Backlog'), h('span', { class: 'spacer' }), refresh),
      error,
      board);

    async function load() {
      const result = await api('/backlog');
      board.replaceChildren(...result.map(renderSection));
    }

    function renderSection(section) {
      return h('section', { class: 'card column' },
        h('h3', {}, sections[section.state] ?? section.state, h('span', { class: 'count' }, section.items.length)),
        section.error && h('p', { class: 'error' }, `No se pudo listar: ${section.error}`),
        h('ul', { class: 'tickets' }, section.items.map((item) => renderItem(section.state, item))));
    }

    function renderItem(state, item) {
      const requeue = requeueable.has(state) && h('button', {
        class: 'small',
        title: 'Devolver a la cola',
        onclick: () => act(requeue, error, async () => { await api(`/backlog/${item.number}/requeue`, { method: 'POST' }); await load(); }),
      }, 'Reencolar');
      return h('li', {}, h('span', { class: 'number' }, `#${item.number}`), h('span', { class: 'title' }, item.title), requeue);
    }

    load().catch((err) => { error.textContent = err.message; board.replaceChildren(); });
  },
};
