# Medora (pharmacy): new device setup and production deployment

## بداية سريعة (عربي)

1. ثبّتي **Node.js 20+** و **.NET SDK 10**.
   - على ويندوز ثبّتي كذلك **SQL Server LocalDB**.
   - على ماك أو لينكس ثبّتي **Docker Desktop**.
2. فكّي الضغط، وافتحي الطرفية داخل المجلد، ثم شغّلي:
   - ويندوز: `powershell -ExecutionPolicy Bypass -File .\setup.ps1`
   - ماك أو لينكس: `bash setup.sh`
3. أعيدي إدخال **مفتاح Groq** بنفسك، فهو الشيء الوحيد غير الموجود في الملف المضغوط لأنه سرّي:
   `dotnet user-secrets set Ai:ApiKey <gsk_...> --project backend/src/Medora.Api`
4. شغّلي الـ API: `dotnet run --project backend/src/Medora.Api --launch-profile http`
5. في طرفية ثانية: `cd mobile` ثم `npm run web` للمتصفح، أو `npx expo start` للهاتف عبر تطبيق Expo Go.

---

## 1. Prerequisites (install once per device)

| Tool | Version | Windows | macOS / Linux |
|---|---|---|---|
| Node.js | 20+ (tested on 26) | `winget install OpenJS.NodeJS.LTS` | https://nodejs.org |
| .NET SDK | 10.0.x | `winget install Microsoft.DotNet.SDK.10` | https://dotnet.microsoft.com/download |
| SQL Server | LocalDB 2022+ (Windows) or Docker | See the note below | Docker Desktop |
| Expo Go (phone) | latest | App Store / Google Play | — |
| Git (recommended) | any | `winget install Git.Git` | preinstalled |

LocalDB needs the **Visual C++ 2015–2022 Redistributable (x64)** (`winget install Microsoft.VCRedist.2015+.x64`). It also needs at least 2 GB free on C:.

## 2. First run

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1     # Windows
bash setup.sh                                           # macOS / Linux
```

The script runs the 62 backend tests, installs the mobile packages, typechecks the app, and prepares the database. Then start the two servers:

```bash
dotnet run --project backend/src/Medora.Api --launch-profile http   # API -> http://localhost:5280  (docs: /scalar/v1)
cd mobile && npm run web                                           # app in the browser, or: npx expo start
```

**On a real phone:**
- Set `EXPO_PUBLIC_API_URL=http://<this-PC's-LAN-IP>:5280` before `npx expo start`.
- The phone and the PC must be on the same Wi-Fi.

**Demo logins** (password `Password123!`): `demo@medora.local` (customer), `pharmacist@medora.local`, and `admin@medora.local`.

## 3. Secrets: the only thing to re-enter on a new device

| Secret | Where it lived | How to set it on the new device |
|---|---|---|
| Groq API key | .NET user-secrets, in the old PC's user profile (never in the project folder) | `dotnet user-secrets set Ai:ApiKey <gsk_...> --project backend/src/Medora.Api` |

The AI provider address and model (Groq, `openai/gpt-oss-120b`) are already in `appsettings.Development.json`. If you skip the key, the app still works and the AI features answer "not available".

## 4. What is NOT in the zip

| Excluded | Why | Restored by |
|---|---|---|
| `mobile/node_modules/` | Re-downloadable (~575 MB) | `npm ci` |
| `mobile/.expo/` | Local Expo cache | Recreated automatically |
| `backend/**/bin`, `obj` | Build output | `dotnet build` |

## 5. Deploy to production

> **Free option (recommended to start):** see **[DEPLOY-FREE.md](DEPLOY-FREE.md)**. It uses the Render free tier, the Azure SQL free database and the Expo free plan, step by step on Windows. The section below is the paid/self-hosted route.

**API and database (Docker on a VPS, or Azure):**

1. `cp .env.production.example .env.production` and fill it in: the SQL password, the JWT key, and the AI provider and key.
2. `docker compose --env-file .env.production up -d --build`
   - This tests and builds the API image.
   - It starts SQL Server.
   - It migrates the database and seeds the catalogue and drug knowledge.
3. Put **HTTPS** in front of port 8080 (the phones call the API directly), for example with Caddy: `api.medora.example.com { reverse_proxy localhost:8080 }`.

**Mobile app (Android and iOS stores) through Expo EAS (cloud builds, so no Mac is needed for iOS):**

1. In `mobile/eas.json`, set `production.env.EXPO_PUBLIC_API_URL` to your HTTPS API address.
2. In `mobile/app.json`, confirm the app identifiers (`dev.medora.app`) or set your own.
3. Run these, in order:

   ```bash
   npm i -g eas-cli
   eas login
   eas build:configure
   ```

4. Build the store apps: `eas build -p android --profile production` and `eas build -p ios --profile production`.
5. Submit with `eas submit`. This needs a **Google Play Console** account (one-time fee) and an **Apple Developer** account (yearly).

A quick internal test build for Android is `eas build -p android --profile preview`, which produces an installable `.apk`.

**Before real customers:**
- Replace the mock payment gateway with Stripe.
- Store prescription photos in blob storage.
- Use the customer's paid AI provider (not a free tier).
- Keep `SEED_DEMO_DATA=false`.
- Back up the SQL volume.
- Replace the demo drug knowledge base with a licensed one.
