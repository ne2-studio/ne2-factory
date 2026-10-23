// Thin client for the panel's JSON API (/api). Throws Error(message) on any
// non-2xx response, with the server's { error } message when it sent one.
export async function api(path, { method = 'GET', body } = {}) {
  const headers = { 'X-Ne2-Factory': '1' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  const res = await fetch(`/api${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  const data = res.status === 204 ? null : await res.json().catch(() => null);
  if (!res.ok) throw new Error(data?.error ?? `${res.status} ${res.statusText}`);
  return data;
}
