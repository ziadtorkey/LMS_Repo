# Runbook: protecting the Data Protection key ring

**Why.** The API encrypts secrets at rest with ASP.NET Data Protection: the client secrets of external identity providers
(`IdentityProviders.ProtectedClientSecret`) and, later, other protected values. The key ring itself is stored in the
`DataProtectionKeys` table of the same MySQL database. Unprotected, a database dump contains both the ciphertext and the keys.
Outside `Development` the key ring is therefore encrypted with an X.509 certificate that lives **outside the database**, and the API
**refuses to start** without it (`InvalidOperationException` naming `DataProtection:Certificate`).

Development and tests keep the plain key ring; nothing to configure there.

## Settings

Set through environment variables or the host's secret store. Never commit them, never put them in `appsettings*.json`.

| Setting (env var form) | Meaning |
|---|---|
| `DataProtection:Certificate:Path` (`DataProtection__Certificate__Path`) | Path of a PFX file on the host (a mounted secret). |
| `DataProtection:Certificate:Base64` | The PFX as base64, instead of a path (use whichever your secret store supports). |
| `DataProtection:Certificate:Password` | PFX password. |
| `DataProtection:PreviousCertificates:0:Path` / `:Base64` / `:Password` | Certificates the existing keys may still be protected with (rotation, below). `:1`, `:2`... for more. |

The certificate needs a **private key** (RSA 2048 or larger) and must not be expired. The same certificate must be configured on **every API node**.
The key-protection certificate is not a TLS certificate: do not reuse the site certificate.

## Provision (first deployment)

1. Create a certificate and export it with its private key as a password-protected PFX. Example for a self-managed one (valid 2 years):

   ```bash
   openssl req -x509 -newkey rsa:3072 -sha256 -days 730 -nodes -subj "/CN=lms-dataprotection" \
     -keyout dp.key -out dp.crt
   openssl pkcs12 -export -inkey dp.key -in dp.crt -out lms-dp.pfx -passout pass:"$PFX_PASSWORD"
   shred -u dp.key
   ```

2. Store the PFX and its password in the secret store (or mount the PFX as a read-only file) and expose them as the settings above.
3. Start the API on a **fresh database** (or one whose `DataProtectionKeys` table is still empty). The first start creates the
   first key and protects it with the certificate.
4. **Enabling protection on a database that already has key rows is not supported.** Turning the setting on does not create a new key:
   the existing unprotected key stays the default (up to 90 days) and every secret keeps being encrypted with it, so re-saving
   secrets or deleting rows would not help (and deleting the default key would break the stored secrets). Do it only before the
   first production data exists. If an environment already has key rows, rebuild it, or ask the platform team for a supervised
   one-off migration that creates a new default key with `IKeyManager` and then re-saves every identity-provider secret.
5. Back up the PFX and password separately from database backups. Losing the certificate makes every protected secret unrecoverable
   (identity provider secrets would have to be entered again).

## Rotate (before expiry, or on a schedule)

Data Protection decrypts a payload with the key that protected it, and that key is wrapped by the certificate that was current
when the key was created. Stored identity-provider secrets are long-lived payloads: **they stay bound to their original key, and so to
the original certificate, even after the key ring rolls to a new key.** Therefore an old certificate can only be retired after every
secret protected under it has been re-saved. Until then it must stay configured.

1. Create the new PFX as above.
2. Configure the **new** certificate as `DataProtection:Certificate:*` and move the **old** one to
   `DataProtection:PreviousCertificates:0:*` (add `:1`, `:2`... for earlier ones). Deploy to all nodes.
   An expired previous certificate is fine: it is only used to decrypt.
3. New keys are protected with the new certificate once the key ring rolls (default key lifetime 90 days). Old payloads stay readable
   through the previous certificate.
4. **Keep the previous certificate configured.** It is only safe to remove it after (a) the ring has rolled to a key protected by the new
   certificate and (b) every identity-provider client secret has been saved again, which encrypts it with the new default key
   (admin screen: edit the provider and enter the client secret again). If you are not sure all were re-saved, do not remove it: the
   cost of keeping an expired certificate configured is nothing, the cost of removing it too early is that provider sign-in stops.
5. After the removal, test one provider sign-in in each client before closing the change.

## Suspected leak of the certificate or its password

Rotating alone does not help, because the leaked certificate can still decrypt keys it protected (a copy of the database plus the
certificate reveals the secrets). Treat the secrets themselves as compromised:

1. Rotate every identity-provider **client secret at the provider** (their admin console).
2. Provision a new certificate as above (new certificate current, old one previous), enter the new client secrets in the admin screen
   (this encrypts them with the new default key), and confirm sign-in works.
3. Only after all secrets were re-entered with the new certificate in place, remove the compromised certificate from
   `PreviousCertificates` and destroy the PFX.

## Troubleshooting

| Symptom | Cause |
|---|---|
| Startup fails: "The Data Protection key ring must be protected outside the Development environment" | No certificate configured in a non-Development environment. |
| "could not be loaded" | Wrong path, wrong password, or the file is not a PFX with a private key. The message never contains the password. |
| "has expired" (current certificate) | Rotate the certificate (above). An expired certificate under `PreviousCertificates` is accepted. |
| "sets neither Path nor Base64" | An entry under `PreviousCertificates` is empty or mistyped; fix or remove it. |
| "at least 2048 bits" | The certificate has a weaker RSA key; issue a new one. |
| Provider sign-in fails after a deployment, logs show a cryptographic error | A node is using a different certificate, or the previous certificate was removed too early. Restore the setting. |
