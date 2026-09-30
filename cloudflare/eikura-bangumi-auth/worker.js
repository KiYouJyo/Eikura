import { DurableObject } from 'cloudflare:workers';

const AUTH_ORIGIN = 'https://bgm.tv';
const TICKET_TTL_MS = 90_000;
const LOGIN_TTL_MS = 5 * 60_000;
const OAUTH_VALUE = /^[A-Za-z0-9_-]{32,128}$/;

function reply(body, status = 200) {
  return new Response(body, {
    status,
    headers: {
      'Cache-Control': 'no-store',
      'Referrer-Policy': 'no-referrer',
      'X-Content-Type-Options': 'nosniff',
    },
  });
}

function randomValue() {
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  return btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

function clientId(env) {
  return env.BANGUMI_CLIENT_ID;
}

function clientSecret(env) {
  return env.BANGUMI_CLIENT_SECRET;
}

function callbackUrl(env) {
  return env.BANGUMI_REDIRECT_URI;
}

function store(ctx, state) {
  return ctx.exports.OAuthSession.getByName(state);
}

export class OAuthSession extends DurableObject {
  async begin(challenge) {
    const expires = Date.now() + LOGIN_TTL_MS;
    const created = await this.ctx.storage.transaction(async (txn) => {
      if (await txn.get('session')) return false;
      await txn.put('session', { challenge, expires, phase: 'pending' });
      return true;
    });
    if (!created) return false;
    await this.ctx.storage.setAlarm(expires);
    return true;
  }

  async reserveCallback() {
    return this.ctx.storage.transaction(async (txn) => {
      const session = await txn.get('session');
      if (!session || session.phase !== 'pending' || session.expires < Date.now()) return false;
      session.phase = 'exchanging';
      await txn.put('session', session);
      return true;
    });
  }

  async issue(ticket, tokens) {
    const session = await this.ctx.storage.get('session');
    if (!session || session.phase !== 'exchanging') return false;
    session.phase = 'issued';
    session.ticket = ticket;
    session.tokens = tokens;
    session.expires = Date.now() + TICKET_TTL_MS;
    await this.ctx.storage.put('session', session);
    await this.ctx.storage.setAlarm(session.expires);
    return true;
  }

  async claim(ticket, verifier) {
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
    const challenge = btoa(String.fromCharCode(...new Uint8Array(digest))).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
    return this.ctx.storage.transaction(async (txn) => {
      const session = await txn.get('session');
      if (!session || session.phase !== 'issued' || session.expires < Date.now() ||
          session.ticket !== ticket || session.challenge !== challenge) return null;
      await txn.delete('session');
      return session.tokens;
    });
  }

  async discard() {
    await this.ctx.storage.delete('session');
  }

  async alarm() {
    const session = await this.ctx.storage.get('session');
    if (session?.expires <= Date.now()) await this.ctx.storage.delete('session');
  }
}

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    const configured = Boolean(env.OAUTH_ROLLOUT_ENABLED === 'true' &&
      ctx?.exports?.OAuthSession && env.AUTH_STORE &&
      clientId(env) && clientSecret(env) &&
      callbackUrl(env) === new URL('/callback', request.url).toString());
    if (url.pathname === '/health' && request.method === 'GET')
      return reply(configured ? 'ready' : 'unavailable', configured ? 200 : 503);
    if (!configured) return reply('OAuth relay is not configured.', 503);

    if (url.pathname === '/login' && request.method === 'GET') {
      const state = url.searchParams.get('state');
      const challenge = url.searchParams.get('challenge');
      if (!OAUTH_VALUE.test(state || '') || !OAUTH_VALUE.test(challenge || '')) return reply('Invalid login request.', 400);
      if (!await store(ctx, state).begin(challenge)) return reply('Login state already used.', 409);
      const target = new URL('/oauth/authorize', AUTH_ORIGIN);
      target.searchParams.set('client_id', clientId(env));
      target.searchParams.set('response_type', 'code');
      target.searchParams.set('redirect_uri', callbackUrl(env));
      target.searchParams.set('state', state);
      return new Response(null, { status: 302, headers: { Location: target.toString(), 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer' } });
    }

    if (url.pathname === '/callback' && request.method === 'GET') {
      const state = url.searchParams.get('state');
      const code = url.searchParams.get('code');
      if (!OAUTH_VALUE.test(state || '') || !code || code.length > 1024 || url.searchParams.has('error')) return reply('Authorization failed.', 400);
      const session = store(ctx, state);
      if (!await session.reserveCallback()) return reply('Invalid or expired login state.', 400);
      const body = new URLSearchParams({ grant_type: 'authorization_code', client_id: clientId(env), client_secret: clientSecret(env), code, redirect_uri: callbackUrl(env) });
      let tokenResponse;
      try {
        tokenResponse = await fetch(new URL('/oauth/access_token', AUTH_ORIGIN), {
          method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', Accept: 'application/json' }, body,
        });
        if (!tokenResponse.ok) throw new Error('Token exchange failed');
        const payload = await tokenResponse.json();
        if (typeof payload.access_token !== 'string' || !payload.access_token) throw new Error('Invalid token response');
        const ticket = randomValue();
        if (!await session.issue(ticket, { access_token: payload.access_token, refresh_token: payload.refresh_token || null })) throw new Error('Session expired');
        const deepLink = new URL('eikura://bangumi-auth');
        deepLink.searchParams.set('state', state);
        deepLink.searchParams.set('ticket', ticket);
        return new Response(null, { status: 302, headers: { Location: deepLink.toString(), 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer' } });
      } catch {
        await session.discard();
        return reply('Authorization could not be completed. Please retry from Eikura.', 502);
      }
    }

    if (url.pathname === '/claim' && request.method === 'POST') {
      if (Number(request.headers.get('content-length') || 0) > 4096) return reply('Invalid request.', 413);
      let input;
      try { input = await request.json(); } catch { return reply('Invalid request.', 400); }
      if (!OAUTH_VALUE.test(input?.state || '') || !OAUTH_VALUE.test(input?.ticket || '') || !OAUTH_VALUE.test(input?.verifier || '')) return reply('Invalid request.', 400);
      const tokens = await store(ctx, input.state).claim(input.ticket, input.verifier);
      if (!tokens) return reply('Ticket expired or already claimed.', 410);
      return new Response(JSON.stringify(tokens), { status: 200, headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer', 'X-Content-Type-Options': 'nosniff' } });
    }
    return reply('Not found.', 404);
  },
};
