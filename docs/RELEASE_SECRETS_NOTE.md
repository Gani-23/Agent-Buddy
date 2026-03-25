# Release Secrets Note (Fill + Paste into GitHub Secrets)

This note helps you generate the **exact values** needed by the GitHub Actions workflow.
Copy the outputs from the commands below and paste them into **GitHub → Settings → Secrets and variables → Actions**.

---

## Windows code signing (optional)

### 1) Base64 for your .pfx

**macOS / Linux**
```bash
base64 -i /path/to/your_certificate.pfx | pbcopy
```
Paste the clipboard into secret:
- `WINDOWS_PFX_BASE64 = <PASTE HERE>`

**Windows (PowerShell)**
```powershell
certutil -encode C:\path\to\your_certificate.pfx C:\temp\pfx.b64
```
Open `C:\temp\pfx.b64` and copy all contents into:
- `WINDOWS_PFX_BASE64 = <PASTE HERE>`

### 2) PFX password
- `WINDOWS_PFX_PASSWORD = <YOUR_PFX_PASSWORD>`

---

## macOS signing + notarization (optional)

### 1) Export Developer ID Application cert to .p12

**Keychain Access GUI**
- Find: `Developer ID Application: Your Name (TEAMID)`
- Right‑click → Export → Save as `agentbuddy.p12`
- Set an export password (you will use it below)

### 2) Base64 the .p12
```bash
base64 -i /path/to/agentbuddy.p12 | pbcopy
```
Paste into:
- `MAC_CERT_P12_BASE64 = <PASTE HERE>`

### 3) Certificate password
- `MAC_CERT_PASSWORD = <YOUR_P12_PASSWORD>`

### 4) Certificate name (exact string)
```bash
security find-identity -v -p codesigning
```
Copy the full certificate name. Example:
```
Developer ID Application: Your Name (TEAMID)
```
Paste into:
- `MAC_CERT_NAME = <EXACT CERT NAME>`

### 5) Notarization credentials
- `MAC_NOTARY_APPLE_ID = <YOUR_APPLE_ID_EMAIL>`
- `MAC_NOTARY_PASSWORD = <APP_SPECIFIC_PASSWORD>`
- `MAC_NOTARY_TEAM_ID = <YOUR_TEAM_ID>`

How to generate app‑specific password:
1. Go to https://appleid.apple.com
2. Sign in → App‑Specific Passwords → Generate
3. Name it “AgentBuddy Notary”

---

## Quick checklist of all secrets

- `WINDOWS_PFX_BASE64`
- `WINDOWS_PFX_PASSWORD`
- `MAC_CERT_P12_BASE64`
- `MAC_CERT_PASSWORD`
- `MAC_CERT_NAME`
- `MAC_NOTARY_APPLE_ID`
- `MAC_NOTARY_PASSWORD`
- `MAC_NOTARY_TEAM_ID`

---

## Optional: store your outputs here (local only)

```
WINDOWS_PFX_BASE64=
WINDOWS_PFX_PASSWORD=
MAC_CERT_P12_BASE64=
MAC_CERT_PASSWORD=
MAC_CERT_NAME=
MAC_NOTARY_APPLE_ID=
MAC_NOTARY_PASSWORD=
MAC_NOTARY_TEAM_ID=
```
