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
3. Start the API. The first start protects every **newly created** key with the certificate.
4. **Existing key rows** written before protection was enabled stay readable but unencrypted until Data Protection rolls to a new key
   (90 days by default). To protect immediately, after the deployment is healthy, retire them: delete the old rows of the
   `DataProtectionKeys` table only if no secret depends on them, otherwise re-save the identity provider secrets (an admin PUT with a
   new client secret re-encrypts with the new key). A fresh production database has nothing to migrate.
5. Back up the PFX and password separately from database backups. Losing the certificate makes every protected secret unrecoverable
   (identity provider secrets would have to be entered again).

## Rotate (before expiry, or after a suspected leak)

1. Create the new PFX as above.
2. Configure the **new** certificate as `DataProtection:Certificate:*` and move the **old** one to
   `DataProtection:PreviousCertificates:0:*`. Deploy to all nodes.
3. New keys are protected with the new certificate; old keys stay readable through the previous one.
4. After the key ring has rolled (at least 90 days, or when no row of `DataProtectionKeys` created before the rotation is still in use), remove
   the previous certificate setting and destroy that PFX. After a suspected leak, also rotate every identity provider client secret at the provider
   and save it again in the admin screen.

## Troubleshooting

| Symptom | Cause |
|---|---|
| Startup fails: "The Data Protection key ring must be protected outside the Development environment" | No certificate configured in a non-Development environment. |
| "could not be loaded" | Wrong path, wrong password, or the file is not a PFX with a private key. The message never contains the password. |
| "has expired" | Rotate the certificate (above). |
| Provider sign-in fails after a deployment, logs show a cryptographic error | A node is using a different certificate, or the previous certificate was removed too early. Restore the setting. |
