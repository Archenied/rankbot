// RankBot - automatic Roblox group ranking.
// Runs on GitHub Actions (free) so it keeps working while your computer is off.
//
// Two ways to log in:
//   1. Open Cloud API key  (safe, official, tried first)
//   2. .ROBLOSECURITY cookie  (backup, only used if the key is missing/broken)
//
// Set up everything with RankBotSetup.exe instead of editing files by hand.

const fs = require('fs');
const path = require('path');

const ROOT = __dirname;

const config = readJson('config.json');
const watchlist = config.watchlistEnabled ? readJson('watchlist.json') : [];

// Players RankBot must never change, whatever else the settings say.
// A plain list of usernames and/or user IDs. Missing file just means "nobody".
const neverTouch = readOptionalJson('nevertouch.json', []);

const API_KEY = (process.env.ROBLOX_API_KEY || readSecretFile('apikey.txt')).trim();
const COOKIE = (process.env.ROBLOX_COOKIE || readSecretFile('cookie.txt')).trim();

const problems = [];
let changes = 0;

// Set when Roblox refuses a rank change because the login is not ALLOWED to,
// rather than because something was wrong with the request. An API key with
// only group:read hits this, and the cookie can usually take over.
let permissionProblem = false;

function readJson(name) {
  try {
    return JSON.parse(fs.readFileSync(path.join(ROOT, name), 'utf8'));
  } catch (err) {
    console.error(`Could not read ${name}: ${err.message}`);
    process.exit(1);
  }
}

function readOptionalJson(name, fallback) {
  try {
    if (!fs.existsSync(path.join(ROOT, name))) return fallback;
    return JSON.parse(fs.readFileSync(path.join(ROOT, name), 'utf8'));
  } catch (err) {
    console.error(`Could not read ${name}, ignoring it: ${err.message}`);
    return fallback;
  }
}

// On GitHub the secrets come from environment variables. On your own computer
// they come from these files, which .gitignore keeps off GitHub.
function readSecretFile(name) {
  try {
    return fs.readFileSync(path.join(ROOT, name), 'utf8');
  } catch {
    return '';
  }
}

function fail(msg) {
  problems.push(msg);
  console.error(`  !! ${msg}`);
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ---------------------------------------------------------------------------
// Shared request helper. Retries when Roblox is busy or says "slow down".
// ---------------------------------------------------------------------------

async function request(url, options = {}, attempt = 1) {
  const res = await fetch(url, options);

  if ((res.status === 429 || res.status >= 500) && attempt <= 4) {
    const wait = 2000 * attempt;
    console.log(`  ...Roblox said "slow down" (${res.status}). Waiting ${wait / 1000}s, then trying again.`);
    await sleep(wait);
    return request(url, options, attempt + 1);
  }

  const text = await res.text();
  let body = {};
  if (text) {
    try {
      body = JSON.parse(text);
    } catch {
      body = text;
    }
  }
  return { ok: res.ok, status: res.status, body, headers: res.headers };
}

function describeError(result) {
  const b = result.body;
  let detail = '';
  if (typeof b === 'string') {
    detail = b.slice(0, 200);
  } else if (b) {
    detail = b.message || b.error ||
      (Array.isArray(b.errors) && b.errors[0] && b.errors[0].message) || '';
  }
  return `HTTP ${result.status}${detail ? ` - ${detail}` : ''}`;
}

// ---------------------------------------------------------------------------
// Backend 1: Open Cloud API key  (preferred)
//
// Every backend returns the same shapes, so the ranking logic below does not
// care which one is in use:
//   role   = { id, name, rank, ref }
//   member = { userId, roleId, ref }
// ---------------------------------------------------------------------------

function openCloudBackend(groupId, apiKey) {
  const API = 'https://apis.roblox.com/cloud/v2';
  const headers = { 'x-api-key': apiKey, 'Content-Type': 'application/json' };
  const lastSegment = (s) => String(s || '').split('/').pop();

  async function get(url) {
    return request(url, { headers });
  }

  return {
    name: 'API key',

    async verify() {
      const result = await get(`${API}/groups/${groupId}`);
      if (!result.ok) return { ok: false, why: describeError(result) };
      return { ok: true, who: result.body.displayName || result.body.name || `group ${groupId}` };
    },

    async getRoles() {
      const roles = [];
      let pageToken = '';
      do {
        const url = `${API}/groups/${groupId}/roles?maxPageSize=100` +
          (pageToken ? `&pageToken=${encodeURIComponent(pageToken)}` : '');
        const result = await get(url);
        if (!result.ok) {
          fail(`Could not read the group's ranks. ${describeError(result)}`);
          return null;
        }
        for (const r of result.body.groupRoles || result.body.roles || []) {
          roles.push({ id: String(r.id), name: r.displayName || r.name, rank: Number(r.rank), ref: r.path });
        }
        pageToken = result.body.nextPageToken || '';
      } while (pageToken);
      return roles;
    },

    async getMember(userId) {
      const filter = encodeURIComponent(`user == 'users/${userId}'`);
      const result = await get(`${API}/groups/${groupId}/memberships?maxPageSize=10&filter=${filter}`);
      if (!result.ok) {
        fail(`Could not look up user ${userId} in the group. ${describeError(result)}`);
        return undefined;
      }
      const found = (result.body.groupMemberships || result.body.memberships || [])[0];
      if (!found) return null;
      return { userId: String(userId), roleId: lastSegment(found.role), ref: found.path };
    },

    // Open Cloud cannot filter members by rank, so the whole list is walked.
    async listMembersAtRole(role, maxPages) {
      const members = [];
      let pageToken = '';
      let pages = 0;
      let scanned = 0;
      do {
        const url = `${API}/groups/${groupId}/memberships?maxPageSize=100` +
          (pageToken ? `&pageToken=${encodeURIComponent(pageToken)}` : '');
        const result = await get(url);
        if (!result.ok) {
          fail(`Could not read the member list. ${describeError(result)}`);
          return { members, scanned, truncated: true };
        }
        for (const m of result.body.groupMemberships || result.body.memberships || []) {
          scanned++;
          if (lastSegment(m.role) === role.id) {
            members.push({ userId: lastSegment(m.user), roleId: role.id, ref: m.path });
          }
        }
        pageToken = result.body.nextPageToken || '';
        pages++;
        if (pageToken && pages >= maxPages) return { members, scanned, truncated: true };
      } while (pageToken);
      return { members, scanned, truncated: false };
    },

    async setRank(member, role) {
      const id = lastSegment(member.ref);
      const body = JSON.stringify({ role: role.ref });
      let result = await request(`${API}/groups/${groupId}/memberships/${id}`, {
        method: 'PATCH', headers, body,
      });
      // The group API is still in beta. If PATCH is ever turned off, use the
      // named :assignRole action instead of giving up.
      if (!result.ok && [404, 405, 501].includes(result.status)) {
        result = await request(`${API}/groups/${groupId}/memberships/${id}:assignRole`, {
          method: 'POST', headers, body,
        });
      }
      return result;
    },
  };
}

// ---------------------------------------------------------------------------
// Backend 2: .ROBLOSECURITY cookie  (backup)
// ---------------------------------------------------------------------------

function cookieBackend(groupId, cookie) {
  const GROUPS = 'https://groups.roblox.com/v1';
  const auth = { Cookie: `.ROBLOSECURITY=${cookie}` };
  let csrfToken = '';

  // Roblox hands back a refreshed cookie now and then. Saving it keeps the
  // login alive instead of letting it go stale and start failing.
  //
  // This only helps when cookie.txt can be written, i.e. on your own computer.
  // On GitHub the cookie comes from a secret and the folder is thrown away
  // after every run, so there it will still expire eventually. The API key
  // never expires, which is why it is the better choice for GitHub.
  function absorbRefreshedCookie(headers) {
    if (!headers) return;
    let sent = [];
    try {
      sent = typeof headers.getSetCookie === 'function' ? headers.getSetCookie() : [];
    } catch { sent = []; }
    if (sent.length === 0) {
      const single = headers.get ? headers.get('set-cookie') : null;
      if (single) sent = [single];
    }

    for (const line of sent) {
      const match = /\.ROBLOSECURITY=([^;]+)/.exec(line);
      if (!match) continue;
      const fresh = match[1];
      // Roblox sends a short dummy value when it is clearing the cookie.
      if (fresh.length < 100 || fresh === cookie) continue;

      cookie = fresh;
      auth.Cookie = `.ROBLOSECURITY=${fresh}`;
      try {
        fs.writeFileSync(path.join(ROOT, 'cookie.txt'), fresh);
        console.log('  (Roblox refreshed the login - saved the new cookie.)');
      } catch (err) {
        console.log(`  (Roblox refreshed the login, but cookie.txt could not be saved: ${err.message})`);
      }
    }
  }

  // Roblox hands out the anti-forgery token by rejecting a write and putting
  // the token in the response header.
  async function getCsrf() {
    const res = await fetch('https://auth.roblox.com/v2/logout', { method: 'POST', headers: auth });
    return res.headers.get('x-csrf-token') || '';
  }

  async function write(url, payload, retried = false) {
    if (!csrfToken) csrfToken = await getCsrf();
    const result = await request(url, {
      method: 'PATCH',
      headers: { ...auth, 'Content-Type': 'application/json', 'x-csrf-token': csrfToken },
      body: JSON.stringify(payload),
    });
    absorbRefreshedCookie(result.headers);
    // A stale token comes back as 403 with a fresh one attached. Try once more.
    if (result.status === 403 && !retried) {
      const fresh = result.headers.get('x-csrf-token');
      if (fresh) {
        csrfToken = fresh;
        return write(url, payload, true);
      }
    }
    return result;
  }

  return {
    name: 'cookie',

    async verify() {
      const result = await request('https://users.roblox.com/v1/users/authenticated', { headers: auth });
      absorbRefreshedCookie(result.headers);
      if (!result.ok) return { ok: false, why: describeError(result) };
      return { ok: true, who: result.body.name || 'unknown account' };
    },

    async getRoles() {
      const result = await request(`${GROUPS}/groups/${groupId}/roles`);
      if (!result.ok) {
        fail(`Could not read the group's ranks. ${describeError(result)}`);
        return null;
      }
      return (result.body.roles || []).map((r) => ({
        id: String(r.id), name: r.name, rank: Number(r.rank), ref: r.id,
      }));
    },

    async getMember(userId) {
      const result = await request(`${GROUPS}/users/${userId}/groups/roles`);
      if (!result.ok) {
        fail(`Could not look up user ${userId} in the group. ${describeError(result)}`);
        return undefined;
      }
      const entry = (result.body.data || []).find((g) => g.group && Number(g.group.id) === Number(groupId));
      if (!entry) return null;
      return { userId: String(userId), roleId: String(entry.role.id), ref: String(userId) };
    },

    // The cookie API can ask for one rank directly, so this is much cheaper
    // than the Open Cloud version above.
    async listMembersAtRole(role, maxPages) {
      const members = [];
      let cursor = '';
      let pages = 0;
      do {
        const url = `${GROUPS}/groups/${groupId}/roles/${role.ref}/users?limit=100&sortOrder=Asc` +
          (cursor ? `&cursor=${encodeURIComponent(cursor)}` : '');
        const result = await request(url, { headers: auth });
        if (!result.ok) {
          fail(`Could not read the member list. ${describeError(result)}`);
          return { members, scanned: members.length, truncated: true };
        }
        for (const u of result.body.data || []) {
          members.push({ userId: String(u.userId), roleId: role.id, ref: String(u.userId) });
        }
        cursor = result.body.nextPageCursor || '';
        pages++;
        if (cursor && pages >= maxPages) {
          return { members, scanned: members.length, truncated: true };
        }
      } while (cursor);
      return { members, scanned: members.length, truncated: false };
    },

    async setRank(member, role) {
      return write(`${GROUPS}/groups/${groupId}/users/${member.userId}`, { roleId: Number(role.ref) });
    },
  };
}

// ---------------------------------------------------------------------------
// Picking a backend: API key first, cookie only if the key cannot be used.
// ---------------------------------------------------------------------------

async function chooseBackend(groupId) {
  if (API_KEY) {
    const backend = openCloudBackend(groupId, API_KEY);
    const check = await backend.verify();
    if (check.ok) {
      console.log(`Logged in with the API key (group: ${check.who}).`);
      return backend;
    }
    console.log(`API key did not work: ${check.why}`);
    if (!COOKIE) {
      fail('The API key failed and there is no cookie to fall back on.');
      return null;
    }
    console.log('Falling back to the cookie.');
  } else if (COOKIE) {
    console.log('No API key set, using the cookie.');
  } else {
    fail('No API key and no cookie. Open RankBotSetup.exe and add one.');
    return null;
  }

  const backend = cookieBackend(groupId, COOKIE);
  const check = await backend.verify();
  if (!check.ok) {
    fail(`The cookie did not work either: ${check.why}. Cookies expire when you log out of Roblox - get a fresh one.`);
    return null;
  }
  console.log(`Logged in with the cookie as ${check.who}.`);
  return backend;
}

// ---------------------------------------------------------------------------
// Ranking
// ---------------------------------------------------------------------------

// Accepts a rank number (0-255) or a rank name.
function findRole(roles, wanted) {
  if (typeof wanted === 'number') {
    return roles.find((r) => r.rank === wanted) || null;
  }
  const needle = String(wanted).trim().toLowerCase();
  return roles.find((r) => String(r.name).trim().toLowerCase() === needle) || null;
}

function roleLabel(role) {
  return `${role.name} (rank ${role.rank})`;
}

async function rankTo(backend, member, role, who) {
  if (config.dryRun) {
    console.log(`  [practice run] would rank ${who} -> ${roleLabel(role)}`);
    changes++;
    return;
  }
  const result = await backend.setRank(member, role);
  if (result.ok) {
    console.log(`  ranked ${who} -> ${roleLabel(role)}`);
    changes++;
  } else {
    if (result.status === 401 || result.status === 403) permissionProblem = true;
    fail(`Could not rank ${who}. ${describeError(result)}`);
  }
  await sleep(600); // be polite to Roblox
}

// --- Usernames -> user ids (public endpoint, no login needed) --------------

async function resolveUsernames(names) {
  const found = new Map();
  for (let i = 0; i < names.length; i += 100) {
    const result = await request('https://users.roblox.com/v1/usernames/users', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ usernames: names.slice(i, i + 100), excludeBannedUsers: false }),
    });
    if (!result.ok) {
      fail(`Could not turn usernames into ids. ${describeError(result)}`);
      continue;
    }
    for (const user of result.body.data || []) {
      found.set(String(user.requestedUsername).toLowerCase(), user.id);
    }
  }
  return found;
}

// --- The protected list ----------------------------------------------------

// Turns the names and IDs in nevertouch.json into a set of user IDs.
async function resolveNeverTouch() {
  const guarded = new Set();
  if (!Array.isArray(neverTouch) || neverTouch.length === 0) return guarded;

  const names = [];
  for (const entry of neverTouch) {
    const text = String(entry == null ? '' : entry).trim();
    if (text.length === 0) continue;
    if (/^\d+$/.test(text)) guarded.add(text);
    else names.push(text);
  }

  if (names.length > 0) {
    const ids = await resolveUsernames(names);
    for (const name of names) {
      const id = ids.get(name.toLowerCase());
      if (id) guarded.add(String(id));
      else fail(`Never-touch list: no Roblox account named "${name}".`);
    }
  }

  console.log(`\nNever touch: ${guarded.size} player(s) protected`);
  return guarded;
}

// --- Job 1: the watchlist (runs first, because it is more specific) --------

async function rankWatchlist(backend, roles, guarded) {
  const claimed = new Set();

  if (!config.watchlistEnabled) {
    console.log('\nWatchlist: turned off');
    return claimed;
  }
  if (!Array.isArray(watchlist) || watchlist.length === 0) {
    console.log('\nWatchlist: empty');
    return claimed;
  }

  console.log(`\nWatchlist: ${watchlist.length} player(s)`);

  const namesNeeded = watchlist.filter((e) => !e.userId && e.username).map((e) => e.username);
  const ids = namesNeeded.length ? await resolveUsernames(namesNeeded) : new Map();

  for (const entry of watchlist) {
    const who = entry.username || `user ${entry.userId}`;
    const userId = entry.userId || ids.get(String(entry.username || '').toLowerCase());

    if (!userId) {
      fail(`No Roblox account named "${entry.username}". Check the spelling.`);
      continue;
    }

    claimed.add(String(userId));

    // Protection beats everything, including an explicit watchlist entry.
    if (guarded.has(String(userId))) {
      console.log(`  ${who} is on the never-touch list - skipped`);
      continue;
    }

    const wantedRole = findRole(roles, entry.rank);
    if (!wantedRole) {
      fail(`"${entry.rank}" (for ${who}) is not a rank in your group.`);
      continue;
    }

    const member = await backend.getMember(userId);
    if (member === undefined) continue; // lookup failed, already reported
    if (member === null) {
      console.log(`  ${who} has not joined the group yet - will rank them once they do`);
      continue;
    }
    if (member.roleId === wantedRole.id) {
      console.log(`  ${who} is already ${roleLabel(wantedRole)}`);
      continue;
    }

    await rankTo(backend, member, wantedRole, who);
  }

  return claimed;
}

// --- Job 2: everyone who just joined ---------------------------------------

// `claimed` holds the user ids the watchlist owns. They are skipped here,
// otherwise a new member who is also on the watchlist would be ranked twice.
async function autoRankNewMembers(backend, roles, claimed, guarded) {
  const settings = config.autoRankNewMembers || {};
  if (!settings.enabled) {
    console.log('\nAuto-rank new members: turned off');
    return;
  }

  const fromRole = findRole(roles, settings.fromRank);
  const toRole = findRole(roles, settings.toRank);

  if (!fromRole) return fail(`The joining rank "${settings.fromRank}" is not a rank in your group.`);
  if (!toRole) return fail(`The target rank "${settings.toRank}" is not a rank in your group.`);
  if (fromRole.id === toRole.id) return fail('The joining rank and the target rank are the same.');

  console.log(`\nAuto-rank new members: ${roleLabel(fromRole)} -> ${roleLabel(toRole)}`);

  const maxPages = config.maxMemberPagesToScan || 20;
  const { members, scanned, truncated } = await backend.listMembersAtRole(fromRole, maxPages);
  const protectedHere = members.filter((m) => guarded.has(m.userId)).length;
  const waiting = members.filter((m) => !claimed.has(m.userId) && !guarded.has(m.userId));

  console.log(`  checked ${scanned} member(s), ${waiting.length} waiting to be ranked`);
  const byWatchlist = members.length - waiting.length - protectedHere;
  if (byWatchlist > 0) console.log(`  (${byWatchlist} skipped - the watchlist handles them)`);
  if (protectedHere > 0) console.log(`  (${protectedHere} skipped - on the never-touch list)`);

  if (truncated) {
    console.log(`  NOTE: stopped after ${maxPages} pages. Some members were NOT checked this run.`);
    console.log('        Raise "maxMemberPagesToScan" if your group is bigger than this.');
  }

  // Roblox's "list the players on rank X" endpoint cannot be trusted: for some
  // groups it hands back members of OTHER ranks too, including people ranked
  // far above the joining rank. Ranking straight from that list would try to
  // demote owners and staff, and only Roblox's own rank rule would stop it.
  // So every single player is checked again before being touched.
  let wrongRank = 0;
  for (const member of waiting) {
    const confirmed = await backend.getMember(member.userId);
    if (confirmed === undefined) continue; // lookup failed, already reported

    if (confirmed === null || confirmed.roleId !== fromRole.id) {
      wrongRank++;
      continue;
    }

    await rankTo(backend, confirmed, toRole, `user ${member.userId}`);
  }

  if (wrongRank > 0) {
    console.log(`  (${wrongRank} skipped - Roblox listed them under ${fromRole.name}`);
    console.log('   but they are actually on a different rank)');
  }
}

// ---------------------------------------------------------------------------

// Note: process.exitCode is set instead of calling process.exit(), because
// exiting while a request is still open crashes Node on Windows and buries
// the real error message.
// One full pass. Everything it does is idempotent - anyone already on the right
// rank is skipped - so it is safe to run twice with a different login.
async function runOnce(backend, guarded) {
  const roles = await backend.getRoles();
  if (!roles || roles.length === 0) {
    console.log('\nStopping: without the rank list there is nothing RankBot can do.');
    console.log('Check that the group ID is right and that your login can see this group.');
    return false;
  }
  console.log(`Found ${roles.length} ranks: ${roles.map(roleLabel).join(', ')}`);

  const claimed = await rankWatchlist(backend, roles, guarded);
  await autoRankNewMembers(backend, roles, claimed, guarded);
  return true;
}

async function main() {
  if (!config.groupId) {
    console.error('No group ID set. Open RankBotSetup.exe and put your group ID in.');
    process.exitCode = 1;
    return;
  }

  // The on/off switch. Turning RankBot off leaves the GitHub timer running but
  // makes every run do nothing, so it can be switched back on at any time.
  if (config.active === false) {
    console.log('RankBot is switched OFF. Nothing will be done.');
    console.log('Turn it back on with the big button in RankBotSetup.exe.');
    return;
  }

  console.log(`RankBot starting for group ${config.groupId}`);
  if (config.dryRun) console.log('PRACTICE RUN: nothing will actually be changed.');

  const backend = await chooseBackend(config.groupId);
  if (!backend) {
    console.log('\nStopping: could not log in to Roblox.');
    process.exitCode = 1;
    return;
  }

  const guarded = await resolveNeverTouch();

  if (!(await runOnce(backend, guarded))) {
    process.exitCode = 1;
    return;
  }

  // An API key can be allowed to READ the group but not to change ranks (that
  // is the group:write scope). Reading works, so the failure only shows up on
  // the first rank change. When that happens, hand over to the cookie and redo
  // the whole pass.
  if (permissionProblem && backend.name === 'API key' && COOKIE) {
    console.log('\nThe API key is not allowed to change ranks (it is missing group:write).');
    console.log('Trying again with the cookie instead.');

    const spare = cookieBackend(config.groupId, COOKIE);
    const check = await spare.verify();
    if (check.ok) {
      console.log(`Logged in with the cookie as ${check.who}.`);
      problems.length = 0;
      changes = 0;
      permissionProblem = false;
      await runOnce(spare, guarded);
    } else {
      fail(`The cookie could not take over either: ${check.why}`);
    }
  }

  if (permissionProblem) {
    console.log('\nThis login is not allowed to change ranks in your group. Check that:');
    console.log('  - an API key has BOTH group:read and group:write ticked, and');
    console.log('  - the account is ranked high enough to manage the ranks you chose.');
  }

  console.log(`\nDone. ${changes} rank change(s), ${problems.length} problem(s).`);
  if (problems.length) {
    console.log('\nProblems:');
    problems.forEach((p) => console.log(` - ${p}`));
    process.exitCode = 1;
  }
}

main().catch((err) => {
  console.error('RankBot crashed:', err);
  process.exitCode = 1;
});
