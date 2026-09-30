# Eikura 1.3.2 external cutover checklist

This document tracks the external steps that must happen around the Eikura 1.3.2 rename. The application code is designed so these steps can be staged without breaking Eizo 1.3.1 users.

## 1. GitHub repositories

Rename in this order:

1. `KiYouJyo/Eizo.Playback` → `KiYouJyo/Eikura.Playback`
2. `KiYouJyo/Eizo.Metadata` → `KiYouJyo/Eikura.Metadata`
3. `KiYouJyo/Eizo` → `KiYouJyo/Eikura`

After each rename, verify the old GitHub URL redirects to the new repository.

The 1.3.2 branch already points current user-facing repository/update links at the Eikura names. Legacy package IDs, assemblies and namespaces remain unchanged for compatibility.

## 2. GitHub Pages

After the main repository rename:

- confirm Pages publishes at `https://kiyoujyo.github.io/Eikura/`;
- verify the home, support and privacy pages;
- verify download links resolve to the Eikura repository releases;
- update the repository homepage field from the legacy `/Eizo/` URL to `/Eikura/`.

## 3. Cloudflare Worker

The new relay source lives in:

`cloudflare/eikura-bangumi-auth/`

Target Worker:

`eikura-bangumi-auth.x2425618950.workers.dev`

Target callback:

`https://eikura-bangumi-auth.x2425618950.workers.dev/callback`

The new Worker intentionally ships with:

`OAUTH_ROLLOUT_ENABLED=false`

This makes `/health` return unavailable, causing Eikura 1.3.2 to fall back to the legacy relay automatically.

Before enabling rollout:

- deploy the new Worker;
- configure `BANGUMI_CLIENT_SECRET` for the new Worker;
- confirm the Durable Object/runtime bindings are present;
- keep the old `eizo-bangumi-auth` Worker online.

## 4. Bangumi OAuth callback

Register/allow the new HTTPS callback:

`https://eikura-bangumi-auth.x2425618950.workers.dev/callback`

Do not remove the legacy callback yet.

After the new callback is confirmed:

1. set `OAUTH_ROLLOUT_ENABLED=true` for the Eikura Worker;
2. redeploy;
3. verify `/health` returns HTTP 200 / `ready`;
4. test a complete browser sign-in ending at `eikura://bangumi-auth`;
5. verify token claim and account loading in Eikura.

Eikura 1.3.2 still registers and accepts `eizo://bangumi-auth` for compatibility with the legacy relay.

## 5. Merge order

Only after the external names exist:

1. merge Eikura.Playback PR;
2. merge Eikura.Metadata PR;
3. merge main Eikura 1.3.2 PR;
4. verify Pages and all CI again on `main`;
5. publish v1.3.2.

## 6. Release acceptance

Before v1.3.2 publication verify:

- 1.3.1 installs can upgrade in place;
- settings, media catalog and playback history remain available;
- GitHub package identity remains upgrade-compatible;
- both `eikura://` and `eizo://` protocol activation launch the same installed app;
- Bangumi OAuth works through the Eikura relay;
- legacy relay fallback still works while retained;
- component update checks resolve after the repository renames;
- release assets use `Eikura_1.3.2.0_x64.msixbundle` and `Eikura-v1.3.2-x64-one-click.zip`.
