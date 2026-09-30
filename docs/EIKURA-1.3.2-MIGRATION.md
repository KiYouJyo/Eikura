# Eikura 1.3.2 external cutover checklist

This document tracks the external steps around the Eikura 1.3.2 rename. The GitHub/release migration, Cloudflare/Bangumi relay rollout, and live end-to-end OAuth acceptance are complete.

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

## 4. Cloudflare Worker — Stage A complete

The new relay source lives in:

`cloudflare/eikura-bangumi-auth/`

Target Worker:

`eikura-bangumi-auth.x2425618950.workers.dev`

Target callback:

`https://eikura-bangumi-auth.x2425618950.workers.dev/callback`

Stage A was completed successfully with `rollout_enabled=false`.

Verified from GitHub Actions:

- `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`, and `BANGUMI_CLIENT_SECRET` are all present;
- the Eikura Worker can be updated with Wrangler using the configured account token;
- `BANGUMI_CLIENT_SECRET` is written successfully as a Worker secret;
- the Worker redeploy succeeds after secret configuration;
- `/health` returned HTTP `503` with body `unavailable` while staged;
- staged `/login` requests were rejected with HTTP `503`.

The temporary Stage A CI probe was removed after verification. The permanent deployment workflow remains manual-only:

`Deploy Eikura Bangumi OAuth Worker`

## 5. Bangumi OAuth callback and Stage B — complete

The new HTTPS callback is registered in the Bangumi OAuth application:

`https://eikura-bangumi-auth.x2425618950.workers.dev/callback`

The Eikura relay was then enabled with rollout set to `true`.

Verified from GitHub Actions:

- the enabled Worker deploy succeeded;
- `/health` returned HTTP `200` with body exactly `ready`;
- `/login` returned HTTP `302` to `https://bgm.tv/oauth/authorize`;
- the authorization redirect carried the expected Eikura callback URL;
- the configured client ID and authorization-code response type were present.

The temporary Stage B CI probe was removed after verification.

Live end-to-end acceptance also passed:

1. Eikura 1.3.2 started Bangumi sign-in;
2. browser authorization completed successfully;
3. the browser returned through `eikura://bangumi-auth`;
4. Eikura received the callback and completed the token claim;
5. the connected Bangumi account loaded successfully in the installed app.

Eikura 1.3.2 still registers and accepts `eizo://bangumi-auth` for compatibility with the legacy relay. Keep the old Worker/callback online through the 1.3.2 transition even after the new relay is enabled.

## 6. Completed release acceptance

Verified:

- 1.3.1 → 1.3.2 signed-package in-place upgrade;
- settings, media catalog and playback history retention;
- unchanged GitHub/MSIX package identity and family;
- Eikura 1.3.2 package launch after upgrade;
- both `eikura://` and `eizo://` protocol registrations in the application contract;
- legacy relay fallback while the new Worker rollout was disabled;
- component restore/build from the renamed Eikura repositories;
- final v1.3.2 public Release bridge asset contract;
- GitHub Pages deployment and published release status;
- Cloudflare Stage A deployment, Worker secret configuration and staged endpoint verification;
- Bangumi callback registration and Stage B enabled-relay verification;
- live browser OAuth sign-in through `eikura://bangumi-auth` with the connected Bangumi account loading successfully.

All Eizo → Eikura 1.3.2 migration and external cutover acceptance items are complete.
