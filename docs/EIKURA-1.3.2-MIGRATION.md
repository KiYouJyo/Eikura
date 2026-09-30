# Eikura 1.3.2 external cutover checklist

This document tracks the external steps around the Eikura 1.3.2 rename. The GitHub/release migration is complete; Cloudflare/Bangumi rollout remains staged so Eizo 1.3.1 users are not interrupted.

## 1. GitHub repositories — complete

Canonical repositories:

1. `KiYouJyo/Eikura.Playback`
2. `KiYouJyo/Eikura.Metadata`
3. `KiYouJyo/Eikura`

The previous `Eizo*` repository URLs remain useful only as GitHub redirects for legacy clients. Current application/component repository references use the Eikura names. Legacy package IDs, assemblies and namespaces remain unchanged where required for runtime compatibility.

## 2. GitHub Pages — complete

Pages is published at:

`https://kiyoujyo.github.io/Eikura/`

The home, support, privacy and release status surfaces use Eikura branding and the repository homepage points at `/Eikura/`.

## 3. v1.3.2 release / legacy updater bridge — complete

Eikura v1.3.2 is published as a stable GitHub Release.

Eizo 1.3.1 hard-codes the expected update bundle name as:

`Eizo_1.3.2.0_x64.msixbundle`

Therefore v1.3.2 intentionally exposes exactly one MSIXBundle under that legacy **technical** filename. This is a one-version bridge only; the installed app, Release title and one-click package remain branded Eikura.

Final v1.3.2 assets:

- `Eizo_1.3.2.0_x64.msixbundle` — legacy updater bridge name; binary bytes are unchanged from the originally validated Eikura-named bundle;
- `Eikura-v1.3.2-x64-one-click.zip`;
- `SHA256SUMS.txt`.

A real signed-package CI acceptance test installs published Eizo 1.3.1, seeds `%LOCALAPPDATA%\Eizo` settings/catalog/playback-history data, upgrades in place to the final published Eikura 1.3.2 package, verifies package identity/family and retained data, validates the public bridge asset contract and launches Eikura successfully.

**Do not manually rerun the ordinary `Publish GitHub Release` workflow for v1.3.2 after this reconciliation.** The standard publication workflow is intended for subsequent releases and returns to Eikura MSIXBundle naming. For v1.3.2, the bridged Release state above is the canonical final state.

## 4. Cloudflare Worker — pending external cutover

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

## 5. Bangumi OAuth callback — pending external cutover

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

## 6. Completed release acceptance

Verified:

- 1.3.1 → 1.3.2 signed-package in-place upgrade;
- settings, media catalog and playback history retention;
- unchanged GitHub/MSIX package identity and family;
- Eikura 1.3.2 package launch after upgrade;
- both `eikura://` and `eizo://` protocol registrations in the application contract;
- legacy relay fallback while the new Worker rollout is disabled;
- component restore/build from the renamed Eikura repositories;
- final v1.3.2 public Release bridge asset contract;
- GitHub Pages deployment and published release status.

Still pending only because they require external Cloudflare/Bangumi account changes:

- deploy/configure the new Eikura Worker;
- register the new Bangumi HTTPS callback;
- enable Worker rollout;
- complete one live browser OAuth sign-in through `eikura://bangumi-auth`.
