# RankBot — automatic Roblox group ranking

Ranks players in your Roblox group **by itself**, every 5 minutes, on GitHub's
free servers. Your computer can be off. It can even be unplugged.

It does two jobs:

1. **Auto-rank new members** — anybody who joins your group gets moved from the
   joining rank up to the rank you choose.
2. **Watchlist** — a list of specific players you want at a specific rank.
   Already in the group → ranked now. **Not** joined yet → RankBot waits and
   ranks them the moment they join.

The watchlist always wins, so nobody ever gets ranked twice.

Everything is set up in one window: **RankBotSetup.exe**.

---

## The files

| File | What it is |
| --- | --- |
| `RankBotSetup.exe` | The setup window. Start here. |
| `build.bat` | Rebuilds the .exe if you change `RankBotSetup.cs`. |
| `rankbot.js` | The bot itself. GitHub runs this. |
| `config.json` | Your settings. The .exe writes this — you don't have to. |
| `watchlist.json` | Your player list. The .exe writes this too. |
| `nevertouch.json` | Players RankBot must never change. The .exe writes this too. |
| `apikey.txt` / `cookie.txt` | Your login. **Never upload these.** They go to GitHub as Secrets instead. |
| `upload-to-github/` | Safe-to-upload copy, built by the **Prepare GitHub files** button. |

---

## Step 1 — Set it up

Double-click **RankBotSetup.exe**.

1. **Group ID** → type it, press **Check group**. It shows your group's name and
   fills every rank dropdown.
2. **Login** → paste either an API key or a cookie (see below), press **Test**.
   Green text means it works.
3. **Rank people who join** → tick it, pick "They join as" and "Move them to".
4. **Watchlist** → **Add player**, type a username or user ID, pick their rank.
5. **Never touch these players** → one username or user ID per line. RankBot
   will never change their rank, not even if they are on the watchlist. Use it
   for staff, friends, or anyone you rank by hand.
6. Leave **Practice mode** ticked for now.
7. Press **Save and run now** and read the black log box.

When the log looks right, untick Practice mode, save again, and it's live.

### The 24/7 switch

The big **24/7 mode: ON / OFF** button is the master switch. Off means every
run does nothing at all, so you can stop the bot without deleting anything and
turn it back on later. It is saved into `config.json` — upload that file to
GitHub (or edit it there) to switch the online bot too.

### Which login?

**API key (better).** Made at https://create.roblox.com/dashboard/credentials —
add the **Group** API System, pick your group, tick **group:read** and
**group:write**, set Accepted IPs to `0.0.0.0/0`. It only touches your group, and
you can delete it in one click.

**Cookie (backup).** Your `.ROBLOSECURITY` value. It works, but it unlocks the
whole account it came from, so use a bot account, never your main one. If a
cookie ever gets out, log that account out of Roblox — that kills it instantly.

RankBot tries the API key first and only falls back to the cookie if the key is
missing or broken. It also falls back if the key can *read* the group but is not
allowed to *change* ranks — that is the `group:write` scope, and forgetting to
tick it is the most common setup mistake.

### What the bot account is allowed to do

Roblox only lets an account rank people **below its own rank**, and only *to*
ranks below its own. If your bot sits at rank 10, it can move people between
rank 0 and rank 9 and nothing else. Give it a higher rank with the
"Manage lower-ranked member ranks" permission if you need more reach.

---

## Step 2 — Put it on GitHub so it runs forever

1. Make a **new repository** on GitHub called `rankbot`. Choose **Public** —
   public repos get unlimited free Actions minutes. Private repos only get 2000
   a month, and running every 5 minutes needs about 8000. Your login is safe
   either way: it goes in an encrypted secret, never in the code.
2. In RankBotSetup.exe press **Prepare GitHub files**. It builds a folder called
   `upload-to-github` containing everything that is safe to send, with your key
   and cookie deliberately left out, and opens it for you.

   > **Why this matters:** `.gitignore` only protects you when you use git
   > commands. Dragging files into the GitHub **website** ignores it completely.
   > Uploading `apikey.txt` or `cookie.txt` to a public repo shows your login to
   > the whole internet. Always upload from the prepared folder.

3. Drag everything from `upload-to-github` onto GitHub. If GitHub drops the
   hidden `.github` folder, use **Add file → Create new file**, name it
   `.github/workflows/rank.yml`, and paste the contents in.
4. **Settings → Secrets and variables → Actions → New repository secret**, and
   add whichever you use. This is how your login gets there safely - encrypted,
   and never visible in the code:
   - `ROBLOX_API_KEY`
   - `ROBLOX_COOKIE`
5. **Actions** tab → **RankBot** → **Run workflow** to try it once by hand.

After that it runs on its own every 5 minutes, free, forever.

---

## Everyday use

- **Add a player:** open RankBotSetup.exe, add a row, Save. Then upload the new
  `watchlist.json` to GitHub (or edit it right on the GitHub website with the
  pencil ✏️ button). Editing it on GitHub makes RankBot run straight away
  instead of waiting for the timer.
- **See what happened:** the **Actions** tab lists every run. Green = worked,
  red = something broke, and the log says exactly what.

## Things to know

- GitHub's 5-minute timer is a *minimum*, not a promise. When GitHub is busy,
  runs can be 5–30 minutes late. Fine for ranking; not instant.
- If your repo is public and you don't commit anything for 60 days, GitHub
  pauses the schedule and emails you. One click re-enables it.
- Roblox lets two ranks share the same number. When that happens the setup app
  saves the rank's **name** instead, so the right one is always picked. It also
  warns you in the log — giving them different numbers in Roblox is tidier.
- The Open Cloud group API is still in **beta**, so Roblox may change it. If
  ranking suddenly starts failing, check that first.
- Cookies die when that account logs out of Roblox. If the log says the cookie
  failed, get a fresh one and paste it in again.
- Groups bigger than 2000 members need `maxMemberPagesToScan` raised in
  `config.json`. RankBot always says in the log when it had to stop early.

## Changing the code

If you edit `RankBotSetup.cs`, run `build.bat` to rebuild the .exe.

Before changing `rankbot.js` for real, tick **Practice mode** and press
**Save and run now**. It reports what it would do without touching your group.
