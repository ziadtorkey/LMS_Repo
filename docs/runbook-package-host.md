# Runbook: package content host (M4b T9a, ADR 0005, D56)

The content host serves untrusted SCORM and H5P files. It runs inside the API process but answers only on its own origin and only `GET/HEAD /p/{token}/{path}` and `OPTIONS`.

## Configuration
| Key | Meaning |
|---|---|
| `PackageHost:Origin` | The content origin, for example `https://lms-content.example`. **A separate registrable domain**, never a subdomain of the app (D56). Missing, equal to the app origin or on the app's registrable domain: the host is not mapped, upload/attach/launch/preview answer 409 `package_host_not_configured`, and enabling `Features.ScormContent` is refused with the same code. |
| `PackageHost:AppOrigin` | The app origin (defaults to `App:PublicBaseUrl`); used for `frame-ancestors` on the content host. |
| `PackageHost:RevocationCacheSeconds` | Per-instance cache of revocation markers (default 30). This is the revocation bound on every node. |

Dev and CI use `http://localhost:<app>` and `http://127.0.0.1:<content>` (guaranteed cross-site).

## DNS, TLS, ingress (infrastructure prerequisite, owner)
1. A second registrable domain with an A/AAAA record to the same ingress as the API.
2. A certificate for it (no wildcard needed).
3. Route the content host to the API pods unchanged; forward `Host` and `X-Forwarded-*` so the pipeline matches the origin's host and port.
4. Do not add cookies, auth or caching at the proxy for this host: the API sends `Cache-Control: private, no-store`.

## Log scrubbing at the proxy
Tokens travel in the path. The API redacts `/p/{token}` from its own logs; the proxy must too. nginx sample:

```
map $request_uri $scrubbed_uri { ~^(?<head>/p/)[^/?]+(?<tail>.*)$ "${head}[redacted]${tail}"; default $request_uri; }
log_format lms_content '$remote_addr "$request_method $scrubbed_uri" $status $body_bytes_sent';
access_log /var/log/nginx/content.log lms_content;
```

## What the host guarantees
Every response (errors included) carries `Content-Security-Policy: sandbox allow-scripts; ...`, `nosniff`, `Referrer-Policy: no-referrer`, `Cache-Control: private, no-store`, CORS `*` without credentials, and never `Set-Cookie`, `X-Frame-Options` or CORP/COEP/COOP. Every failure is the same empty 404. App responses add exactly `frame-src <content origin>` to their CSP.

## Revocation
Markers (`PackageRevocations`) refuse tokens issued before them. They are written when a package is deleted, when a SCORM/H5P module is hidden (visibility update through the module handlers or `IModuleWriter`), and when a student's course enrollment ends (the enrollment engine's drop). A node picks a marker up within `RevocationCacheSeconds` (30 s, per instance, no shared cache). Whether the package is still `Ready` and not deleted is read per file request (one small query), which makes a package delete effective at once; if this becomes a cost, cache readiness in the revocation cache.

## Tokens
Tokens are authenticated encrypted payloads from ASP.NET Data Protection (purpose `Lms.PackageHost.Token.v1`), not a hand-rolled HMAC: equivalent integrity, the key ring is shared by all nodes and protected by the deployment's key protection outside Development. ADR 0005 section 5 says "HMAC"; read it as this.

## Behind a TLS-terminating proxy
The host matches `PackageHost:Origin` by host and port, and derives the default port from the request scheme. Configure `ForwardedHeaders` (`KnownProxies`/`KnownNetworks`) so `X-Forwarded-Proto: https` is honoured; otherwise `https://content.example` (443) never matches and the host answers 404 for everything (it fails closed).
