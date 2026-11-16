# Free deployment: Medora (pharmacy)

**Cost: $0/month.** The only paid step is publishing to the app stores (Google Play has a one-time fee, Apple a yearly one), and that step is optional. The web version and the Android `.apk` are free.

| Part | Free service | Address you get |
|---|---|---|
| Database | Azure SQL Database, free offer | `<name>.database.windows.net` |
| API | Render free web service (Docker) | `https://medora-api.onrender.com` |
| App on the web (any phone or PC browser) | Render free static site | `https://medora-app.onrender.com` |
| Android app file (`.apk`) | Expo EAS, free plan | A download link |
| AI (staff) | Groq free tier | — |

> **Limits of free tiers.**
> - The API **sleeps after 15 minutes without visitors**, so the first request after that takes about 30–60 seconds while it wakes.
> - The database has a monthly compute allowance and pauses if it is used up (it resets on the 1st).
> - Groq has rate limits.
>
> This is perfect for demos, testing and showing clients. Real customers need paid tiers and real payments.

## خطوات مختصرة (عربي)

1. **GitHub:** أنشئي حسابًا مجانيًا، وثبّتي **GitHub Desktop**، وارفعي مجلد `pharmacy-app` كمستودع خاص (Private).
2. **Azure:** أنشئي حسابًا مجانيًا. سيطلب بطاقة للتحقق فقط، ولا خصم مع الإيقاف التلقائي. ثم أنشئي **Azure SQL Database** واختاري **Apply free offer**.
3. **Render:** سجّلي الدخول بحساب GitHub، واختاري **New → Blueprint**، ثم اختاري المستودع. سيقرأ الملف `render.yaml` وينشئ الخدمتين.
4. املئي القيم السرية التي يطلبها Render، وهي رابط قاعدة البيانات ومفتاح Groq والعناوين، وانتظري حتى يكتمل النشر.
5. نسخة أندرويد (اختياري): `eas build -p android --profile preview`، وستحصلين على رابط لتنزيل التطبيق.

---

## Step 1: Put the code on GitHub (free, private)

1. Create an account at https://github.com.
2. On Windows, install **GitHub Desktop** (`winget install GitHub.GitHubDesktop`) and sign in.
3. **File → Add local repository →** choose the `pharmacy-app` folder → **Create a repository**. Then **Publish repository**, keeping "Keep this code private" ticked.

`.gitignore` already keeps secrets and `node_modules` out.

## Step 2: Free SQL database on Azure

1. Create an account at https://azure.microsoft.com/free. A card is required only for identity checks.
2. In the portal, search **SQL databases → Create**.
3. At the top, click **Apply offer** for the free database.
4. **Server:** create a new one, using SQL authentication with an admin user and a strong password. Save them.
5. **Database name:** `Medora`.
6. **When the free limit is reached:** keep **"Auto-pause the database until next month"**, so you can never be charged.
7. **Networking:**
   - Choose **Public endpoint**, then turn on **"Allow Azure services and resources to access this server"**.
   - Add a firewall rule from `0.0.0.0` to `255.255.255.255`. Render's addresses change, so it needs access from anywhere; the strong password protects the database.
8. Your connection string (one line):

```
Server=tcp:<your-server>.database.windows.net,1433;Initial Catalog=Medora;User ID=<admin>;Password=<password>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;
```

`Connection Timeout=60` gives a paused free database time to wake up.

## Step 3: Deploy on Render (free)

1. Sign up at https://render.com **with your GitHub account**.
2. **New → Blueprint →** choose the `pharmacy-app` repository. Render reads `render.yaml` and proposes **medora-api** and **medora-app**.
3. Fill in the values it asks for:

| Setting | Value |
|---|---|
| `ConnectionStrings__Medora` | The connection string from step 2 |
| `Ai__ApiKey` | Your Groq key (`gsk_...`) |
| `Cors__Origins__0` | `https://medora-app.onrender.com` (the app's address) |
| `EXPO_PUBLIC_API_URL` | `https://medora-api.onrender.com` (the API's address) |

If Render gives slightly different addresses (for example with a suffix), put the real ones here, then click **Manual Deploy → Clear build cache & deploy** on **medora-app**.

4. Wait for both services to show **Live**. The first build takes about 5–10 minutes, because the image builds and runs all 62 tests first.
5. Open `https://medora-app.onrender.com` and log in with a demo account (password `Password123!`):
   - `demo@medora.local`
   - `pharmacist@medora.local`
   - `admin@medora.local`

   The first visit after a quiet period takes up to a minute while the API wakes up.

## Step 4 (optional): Android app file, free

```powershell
cd mobile
npm i -g eas-cli
eas login                          # free Expo account
```

1. In `eas.json`, set `preview.env.EXPO_PUBLIC_API_URL` to your API address.
2. Build the Android app:

   ```powershell
   eas build -p android --profile preview
   ```

3. When the cloud build finishes (free builds can queue), you get a link to install the `.apk` on any Android phone.
4. **iPhone users** can simply use the web address. A native iOS app needs an Apple Developer account, which is paid.

## Moving to paid tiers later

Nothing in the code changes, only settings:
- On Render, change the plan from Free to Starter so the API never sleeps.
- On Azure, choose "continue using with charges" for the database.
- Replace the Groq key with the customer's AI provider.
