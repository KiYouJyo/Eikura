import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { webcrypto } from 'node:crypto';

globalThis.crypto ??= webcrypto;
const source = await readFile(new URL('./worker.js', import.meta.url), 'utf8');
const moduleUrl = `data:text/javascript,${encodeURIComponent(source.replace("import { DurableObject } from 'cloudflare:workers';", 'class DurableObject { constructor(ctx) { this.ctx = ctx; } }'))}`;
const { default: worker, OAuthSession } = await import(moduleUrl);

function fixture() {
  const records = new Map();
  const storage = {
    get: async key => records.get(key),
    put: async (key, value) => { records.set(key, value); },
    delete: async key => records.delete(key),
    setAlarm: async () => {},
    transaction: async callback => callback(storage),
  };
  const session = new OAuthSession({ storage });
  const env = { BANGUMI_CLIENT_ID: 'client-id', BANGUMI_CLIENT_SECRET: 'secret', BANGUMI_REDIRECT_URI: 'https://relay.example/callback', OAUTH_ROLLOUT_ENABLED: 'true', AUTH_STORE: {} };
  const ctx = { exports: { OAuthSession: { getByName: () => session } } };
  return { session, env, ctx };
}

test('login redirects with state and never includes a secret', async () => {
  const { env, ctx } = fixture();
  const state = 'a'.repeat(43), challenge = 'b'.repeat(43);
  const response = await worker.fetch(new Request(`https://relay.example/login?state=${state}&challenge=${challenge}`), env, ctx);
  assert.equal(response.status, 302);
  const target = new URL(response.headers.get('location'));
  assert.equal(target.origin, 'https://bgm.tv');
  assert.equal(target.searchParams.get('state'), state);
  assert.equal(target.searchParams.get('redirect_uri'), 'https://relay.example/callback');
  assert.ok(!target.href.includes('secret'));
  assert.equal((await worker.fetch(new Request(`https://relay.example/login?state=${state}&challenge=${challenge}`), env, ctx)).status, 409);
});

test('health requires every configured runtime binding', async () => {
  const { env, ctx } = fixture();
  assert.equal((await worker.fetch(new Request('https://relay.example/health'), env, ctx)).status, 200);
  delete env.AUTH_STORE;
  assert.equal((await worker.fetch(new Request('https://relay.example/health'), env, ctx)).status, 503);
  env.AUTH_STORE = {};
  env.OAUTH_ROLLOUT_ENABLED = 'false';
  assert.equal((await worker.fetch(new Request('https://relay.example/health'), env, ctx)).status, 503);
});

test('ticket is one time and requires the desktop verifier', async () => {
  const { session, env, ctx } = fixture();
  const verifier = 'v'.repeat(43), state = 's'.repeat(43), ticket = 't'.repeat(43);
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  const challenge = Buffer.from(digest).toString('base64url');
  assert.equal(await session.begin(challenge), true);
  assert.equal(await session.reserveCallback(), true);
  assert.equal(await session.reserveCallback(), false);
  assert.equal(await session.issue(ticket, { access_token: 'access', refresh_token: 'refresh' }), true);
  const claim = value => worker.fetch(new Request('https://relay.example/claim', { method: 'POST', body: JSON.stringify({ state, ticket, verifier: value }) }), env, ctx);
  assert.equal((await claim('x'.repeat(43))).status, 410);
  const first = await claim(verifier);
  assert.equal(first.status, 200);
  assert.equal(first.headers.get('cache-control'), 'no-store');
  assert.deepEqual(await first.json(), { access_token: 'access', refresh_token: 'refresh' });
  assert.equal((await claim(verifier)).status, 410);
});

test('callback checks state, exchanges server-side, and redirects with ticket only', async () => {
  const { env, ctx } = fixture();
  const state = 's'.repeat(43), verifier = 'v'.repeat(43);
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  const challenge = Buffer.from(digest).toString('base64url');
  await worker.fetch(new Request(`https://relay.example/login?state=${state}&challenge=${challenge}`), env, ctx);
  const isolated = fixture();
  const wrong = await worker.fetch(new Request(`https://relay.example/callback?state=${'x'.repeat(43)}&code=example-code`), isolated.env, isolated.ctx);
  assert.equal(wrong.status, 400);
  const originalFetch = globalThis.fetch;
  let exchangeBody;
  globalThis.fetch = async (_url, options) => {
    exchangeBody = options.body;
    return Response.json({ access_token: 'access', refresh_token: 'refresh' });
  };
  try {
    const callback = await worker.fetch(new Request(`https://relay.example/callback?state=${state}&code=example-code`), env, ctx);
    assert.equal(callback.status, 302);
    assert.equal(exchangeBody.get('client_secret'), 'secret');
    const deepLink = new URL(callback.headers.get('location'));
    assert.equal(deepLink.origin, 'null');
    assert.equal(deepLink.protocol, 'eikura:');
    assert.equal(deepLink.searchParams.get('state'), state);
    assert.ok(deepLink.searchParams.get('ticket'));
    assert.ok(!deepLink.href.includes('access') && !deepLink.href.includes('refresh'));
    const claim = await worker.fetch(new Request('https://relay.example/claim', { method: 'POST', body: JSON.stringify({ state, ticket: deepLink.searchParams.get('ticket'), verifier }) }), env, ctx);
    assert.equal(claim.status, 200);
  } finally { globalThis.fetch = originalFetch; }
});

test('expired ticket cannot be claimed', async () => {
  const { session } = fixture();
  const verifier = 'v'.repeat(43);
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  const challenge = Buffer.from(digest).toString('base64url');
  await session.begin(challenge);
  await session.reserveCallback();
  await session.issue('t'.repeat(43), { access_token: 'access' });
  const record = await session.ctx.storage.get('session');
  record.expires = Date.now() - 1;
  await session.ctx.storage.put('session', record);
  assert.equal(await session.claim('t'.repeat(43), verifier), null);
});
