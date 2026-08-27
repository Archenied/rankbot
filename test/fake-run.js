// Offline test: pretends to be Roblox so you can check RankBot's logic
// without a login and without touching your real group.
//   node test/fake-run.js
//
// It runs the same situation twice - once logged in with an API key, once
// logged in with a cookie - and checks that both do exactly the same thing.

const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawnSync } = require('child_process');

const SRC = path.join(__dirname, '..');
const MODE = process.argv[2]; // 'apikey', 'cookie' or 'fallback' when run as a child

// --- The pretend group ------------------------------------------------------

const ROLES = [
  { id: 1, name: 'Guest', rank: 1 },
  { id: 2, name: 'Member', rank: 10 },
  { id: 3, name: 'Admin', rank: 100 },
  { id: 4, name: 'Owner', rank: 255 },
];

// userId -> role id they are currently on
const MEMBERS = {
  111: 1, // just joined
  112: 1, // just joined
  201: 1, // AlreadyIn: just joined AND on the watchlist -> watchlist wins
  203: 2, // AlreadyCorrect
  777: 2,
  900: 3, // an admin, must not be touched
};

const NAMES = { alreadyin: 201, notjoinedyet: 202, alreadycorrect: 203 };

const WATCHLIST = [
  { username: 'AlreadyIn', rank: 100 },           // in group, wrong rank -> promote
  { username: 'NotJoinedYet', rank: 100 },        // not a member -> wait for them
  { username: 'AlreadyCorrect', rank: 'Member' }, // already right -> do nothing
  { userId: 777, rank: 250 },                     // rank does not exist -> error
  { username: 'GhostAccount', rank: 10 },         // no such account -> error
];

// 112 is a brand new member AND protected -> must be left alone.
// AlreadyCorrect (203) is protected too, even though it is on the watchlist.
const NEVER_TOUCH = [112, 'AlreadyCorrect'];

// What must happen, written as "userId -> rank number".
const EXPECTED = ['111 -> 10', '201 -> 100'];
const EXPECTED_PROBLEMS = 2;

// --- Parent: run both modes as child processes and compare -----------------

if (!MODE) {
  let allPassed = true;
  for (const mode of ['apikey', 'cookie', 'fallback', 'writefail']) {
    console.log(`\n############ logged in with: ${mode} ############\n`);
    const run = spawnSync(process.execPath, [__filename, mode], { stdio: 'inherit' });
    if (run.status !== 0) allPassed = false;
  }
  console.log(`\n############ OVERALL: ${allPassed ? 'PASS' : 'FAIL'} ############`);
  process.exitCode = allPassed ? 0 : 1;
  return;
}

// --- Child: build a throwaway copy of RankBot ------------------------------

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'rankbot-'));
fs.mkdirSync(path.join(tmp, 'src'));
fs.copyFileSync(path.join(SRC, 'src', 'rankbot.js'), path.join(tmp, 'src', 'rankbot.js'));

fs.writeFileSync(path.join(tmp, 'config.json'), JSON.stringify({
  groupId: 999,
  autoRankNewMembers: { enabled: true, fromRank: 1, toRank: 'Member' },
  watchlistEnabled: true,
  maxMemberPagesToScan: 20,
  dryRun: false,
}));
fs.writeFileSync(path.join(tmp, 'watchlist.json'), JSON.stringify(WATCHLIST));
fs.writeFileSync(path.join(tmp, 'nevertouch.json'), JSON.stringify(NEVER_TOUCH));

if (MODE === 'apikey') {
  process.env.ROBLOX_API_KEY = 'test-key';
  delete process.env.ROBLOX_COOKIE;
} else if (MODE === 'cookie') {
  delete process.env.ROBLOX_API_KEY;
  process.env.ROBLOX_COOKIE = 'test-cookie';
} else if (MODE === 'fallback') {
  // A broken API key must quietly hand over to the cookie.
  process.env.ROBLOX_API_KEY = 'wrong-key';
  process.env.ROBLOX_COOKIE = 'test-cookie';
} else {
  // A key that can READ but not WRITE: reading succeeds, so the trouble only
  // shows up on the first rank change. The cookie must then take over.
  process.env.ROBLOX_API_KEY = 'readonly-key';
  process.env.ROBLOX_COOKIE = 'test-cookie';
}

// --- The pretend Roblox server ---------------------------------------------

const rankChanges = [];
const roleById = (id) => ROLES.find((r) => String(r.id) === String(id));

function reply(body, status = 200, headers = {}) {
  return Promise.resolve({
    ok: status >= 200 && status < 300,
    status,
    headers: { get: (h) => headers[h.toLowerCase()] || null },
    text: () => Promise.resolve(typeof body === 'string' ? body : JSON.stringify(body)),
  });
}

function recordChange(userId, roleId) {
  const role = roleById(roleId);
  rankChanges.push(`${userId} -> ${role ? role.rank : '???'}`);
  MEMBERS[userId] = Number(roleId);
}

globalThis.fetch = (url, options = {}) => {
  const u = decodeURIComponent(String(url));
  const method = (options.method || 'GET').toUpperCase();
  const body = options.body ? JSON.parse(options.body) : null;

  // --- shared: username lookup (no login needed) ---
  if (u.includes('/usernames/users')) {
    return reply({
      data: body.usernames
        .filter((n) => NAMES[n.toLowerCase()])
        .map((n) => ({ requestedUsername: n, id: NAMES[n.toLowerCase()] })),
    });
  }

  // --- API key backend ---
  if (u.startsWith('https://apis.roblox.com')) {
    const key = options.headers['x-api-key'];
    if (key !== 'test-key' && key !== 'readonly-key') return reply({ message: 'bad key' }, 401);
    if (method === 'PATCH' && key === 'readonly-key') {
      return reply({ message: 'The required scope <group:write> is missing.' }, 403);
    }

    if (u.endsWith('/groups/999')) return reply({ displayName: 'Test Group' });

    if (u.includes('/roles?')) {
      return reply({
        groupRoles: ROLES.map((r) => ({
          path: `groups/999/roles/${r.id}`, id: r.id, displayName: r.name, rank: r.rank,
        })),
      });
    }

    if (u.includes('/memberships?')) {
      const one = u.match(/users\/(\d+)/);
      const ids = one ? [one[1]].filter((id) => MEMBERS[id]) : Object.keys(MEMBERS);
      return reply({
        groupMemberships: ids.map((id) => ({
          path: `groups/999/memberships/m${id}`,
          user: `users/${id}`,
          role: `groups/999/roles/${MEMBERS[id]}`,
        })),
      });
    }

    if (method === 'PATCH' && u.includes('/memberships/')) {
      const userId = u.split('/memberships/m')[1];
      recordChange(userId, String(body.role).split('/').pop());
      return reply({});
    }
  }

  // --- cookie backend ---
  if (u.startsWith('https://auth.roblox.com/v2/logout')) {
    return reply('', 403, { 'x-csrf-token': 'csrf-123' });
  }

  if (u.includes('users.roblox.com/v1/users/authenticated')) {
    const cookie = (options.headers || {}).Cookie || '';
    if (!cookie.includes('test-cookie')) return reply({ message: 'bad cookie' }, 401);
    return reply({ id: 1, name: 'BotAccount' });
  }

  if (u.startsWith('https://groups.roblox.com')) {
    if (u.endsWith('/groups/999/roles')) return reply({ roles: ROLES });

    const rolesOfUser = u.match(/\/users\/(\d+)\/groups\/roles/);
    if (rolesOfUser) {
      const roleId = MEMBERS[rolesOfUser[1]];
      if (!roleId) return reply({ data: [] });
      return reply({ data: [{ group: { id: 999 }, role: roleById(roleId) }] });
    }

    const usersAtRole = u.match(/\/groups\/999\/roles\/(\d+)\/users/);
    if (usersAtRole) {
      const wanted = Number(usersAtRole[1]);
      return reply({
        data: Object.keys(MEMBERS)
          .filter((id) => MEMBERS[id] === wanted)
          .map((id) => ({ userId: Number(id) })),
        nextPageCursor: null,
      });
    }

    const setRank = u.match(/\/groups\/999\/users\/(\d+)$/);
    if (method === 'PATCH' && setRank) {
      if (options.headers['x-csrf-token'] !== 'csrf-123') return reply('', 403);
      recordChange(setRank[1], body.roleId);
      return reply({});
    }
  }

  throw new Error(`unexpected request: ${method} ${u}`);
};

// --- Run it and check --------------------------------------------------------

require(path.join(tmp, 'src', 'rankbot.js'));

process.on('exit', () => {
  const got = rankChanges.slice().sort();
  const want = EXPECTED.slice().sort();
  const rightChanges = JSON.stringify(got) === JSON.stringify(want);
  const rightExit = process.exitCode === 1; // 1 is correct: 2 bad watchlist entries
  const untouched = MEMBERS[900] === 3;

  console.log('\n---------------- CHECKS ----------------');
  console.log('rank changes :', got.join(', ') || '(none)');
  console.log('expected     :', want.join(', '));
  console.log('admin left alone         :', untouched ? 'yes' : 'NO');
  console.log(`exit code is 1 (${EXPECTED_PROBLEMS} bad entries):`, rightExit ? 'yes' : `NO (${process.exitCode})`);
  console.log(rightChanges && rightExit && untouched ? 'PASS' : 'FAIL');

  process.exitCode = rightChanges && rightExit && untouched ? 0 : 1;
});
