import crypto from 'node:crypto';

const MINOR_AGE = 18;
const MIN_AGE = 13;
const MAX_AGE = 120;
const STATE_TTL_MS = 15 * 60 * 1000;

function redirect(res, location) {
  res.status(302).setHeader('Location', location);
  res.setHeader('Cache-Control', 'no-store');
  res.end();
}

function fail(res, reason) {
  redirect(res, `/?status=error&reason=${encodeURIComponent(reason)}`);
}

function openState(state, secret) {
  if (typeof state !== 'string') return null;
  const parts = state.split('.');
  if (parts.length !== 3) return null;
  try {
    const key = crypto.createHash('sha256').update(secret).digest();
    const decipher = crypto.createDecipheriv('aes-256-gcm', key, Buffer.from(parts[0], 'base64url'));
    decipher.setAuthTag(Buffer.from(parts[2], 'base64url'));
    const raw = Buffer.concat([decipher.update(Buffer.from(parts[1], 'base64url')), decipher.final()]);
    const payload = JSON.parse(raw.toString('utf8'));
    if (!payload || typeof payload.t !== 'number' || Date.now() - payload.t > STATE_TTL_MS) return null;
    if (typeof payload.b !== 'string') return null;
    return payload;
  } catch {
    return null;
  }
}

function sealState(payload, secret) {
  const key = crypto.createHash('sha256').update(secret).digest();
  const iv = crypto.randomBytes(12);
  const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
  const raw = Buffer.concat([cipher.update(JSON.stringify(payload), 'utf8'), cipher.final()]);
  const tag = cipher.getAuthTag();
  return `${iv.toString('base64url')}.${raw.toString('base64url')}.${tag.toString('base64url')}`;
}

function ageFrom(birthdate) {
  const parts = birthdate.split('-').map(Number);
  if (parts.length !== 3 || parts.some((n) => !Number.isFinite(n))) return null;
  const [year, month, day] = parts;
  const date = new Date(Date.UTC(year, month - 1, day));
  if (
    date.getUTCFullYear() !== year ||
    date.getUTCMonth() !== month - 1 ||
    date.getUTCDate() !== day
  ) return null;
  const now = new Date();
  let age = now.getUTCFullYear() - year;
  const monthDiff = now.getUTCMonth() + 1 - month;
  if (monthDiff < 0 || (monthDiff === 0 && now.getUTCDate() < day)) age -= 1;
  if (age < 0 || age > MAX_AGE) return null;
  return age;
}

async function discordJson(url, options = {}) {
  const response = await fetch(url, {
    ...options,
    headers: {
      'User-Agent': 'CapriVerify/1.0',
      ...(options.headers || {}),
    },
  });
  const text = await response.text();
  let data = null;
  if (text) {
    try { data = JSON.parse(text); } catch { data = null; }
  }
  return { response, data, text };
}

async function saveVerification(record) {
  const url = process.env.SUPABASE_URL;
  const key = process.env.SUPABASE_SERVICE_ROLE_KEY;
  if (!url || !key) return { ok: false, error: 'Supabase não configurado.' };

  const response = await fetch(`${url.replace(/\/$/, '')}/rest/v1/discord_verifications?on_conflict=discord_id`, {
    method: 'POST',
    headers: {
      apikey: key,
      Authorization: `Bearer ${key}`,
      'Content-Type': 'application/json',
      Prefer: 'resolution=merge-duplicates,return=minimal',
    },
    body: JSON.stringify(record),
  });

  if (!response.ok) {
    const text = await response.text().catch(() => '');
    return { ok: false, error: `Supabase ${response.status}: ${text.slice(0, 300)}` };
  }
  return { ok: true };
}

async function addToGuild({ botToken, guildId, userId, accessToken }) {
  const { response } = await discordJson(`https://discord.com/api/v10/guilds/${guildId}/members/${userId}`, {
    method: 'PUT',
    headers: {
      Authorization: `Bot ${botToken}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ access_token: accessToken }),
  });
  // 201 = added, 204 = already a member / updated. 403 = bot can't add (missing Create Invite).
  return response.ok || response.status === 204;
}

async function applyRole({ botToken, guildId, userId, roleId }) {
  const { response, text } = await discordJson(`https://discord.com/api/v10/guilds/${guildId}/members/${userId}/roles/${roleId}`, {
    method: 'PUT',
    headers: { Authorization: `Bot ${botToken}` },
  });
  if (response.ok || response.status === 204) return { ok: true };
  return { ok: false, error: `Discord ${response.status}: ${text.slice(0, 200)}` };
}

async function removeRole({ botToken, guildId, userId, roleId }) {
  const { response } = await discordJson(`https://discord.com/api/v10/guilds/${guildId}/members/${userId}/roles/${roleId}`, {
    method: 'DELETE',
    headers: { Authorization: `Bot ${botToken}` },
  });
  // 404 = the member doesn't have that role; that's fine.
  return response.ok || response.status === 204 || response.status === 404;
}

export default async function handler(req, res) {
  try {
    const url = new URL(req.url, `https://${req.headers.host || 'localhost'}`);
    const code = url.searchParams.get('code');
    const state = url.searchParams.get('state');
    const discordError = url.searchParams.get('error');

    if (discordError || !code) return fail(res, 'oauth');

    const secret = process.env.VERIFY_SECRET;
    const clientId = process.env.DISCORD_CLIENT_ID;
    const clientSecret = process.env.DISCORD_CLIENT_SECRET;
    const botToken = process.env.DISCORD_BOT_TOKEN;
    const guildId = process.env.DISCORD_GUILD_ID || '1287190286925303858';
    const minorRoleId = process.env.DISCORD_MINOR_ROLE_ID;
    const adultRoleId = process.env.DISCORD_ADULT_ROLE_ID;

    if (!secret || !clientId || !clientSecret) return fail(res, 'config');

    const payload = openState(state, secret);
    if (!payload) return fail(res, 'state');

    const typedAge = ageFrom(payload.b);
    if (typedAge === null || typedAge < MIN_AGE) return fail(res, 'age');

    // Google's birthday (when available) is the cross-check for what the user typed.
    const googleAge = Number.isInteger(payload.g) ? payload.g : null;
    const googleMatch = typeof payload.gm === 'boolean' ? payload.gm : null;
    const mismatch = googleMatch === false;

    const effectiveAge = googleAge !== null ? googleAge : typedAge;
    const isMinor = effectiveAge < MINOR_AGE;

    const host = req.headers['x-forwarded-host'] || req.headers.host;
    const proto = req.headers['x-forwarded-proto'] || 'https';
    const redirectUri = process.env.DISCORD_REDIRECT_URI || `${proto}://${host}/api/verify/callback`;

    const tokenBody = new URLSearchParams({
      client_id: clientId,
      client_secret: clientSecret,
      grant_type: 'authorization_code',
      code,
      redirect_uri: redirectUri,
    });

    const tokenResult = await discordJson('https://discord.com/api/v10/oauth2/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: tokenBody.toString(),
    });

    if (!tokenResult.response.ok || !tokenResult.data?.access_token) return fail(res, 'oauth');
    const accessToken = tokenResult.data.access_token;

    const userResult = await discordJson('https://discord.com/api/v10/users/@me', {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    if (!userResult.response.ok || !userResult.data?.id) return fail(res, 'discord');
    const user = userResult.data;

    let roleApplied = null;
    let roleError = null;
    let joinToken = null;

    if (botToken) {
      if (mismatch) {
        // Data informada ≠ data do Google: não entra no servidor nem recebe cargo.
        roleError = 'A data informada não confere com a data da conta Google.';
      } else {
        const joined = await addToGuild({ botToken, guildId, userId: user.id, accessToken });
        if (!joined) roleError = 'Não foi possível adicionar ao servidor (o bot precisa da permissão Create Invite).';

        const roleId = isMinor ? minorRoleId : adultRoleId;
        const oppositeRoleId = isMinor ? adultRoleId : minorRoleId;
        if (roleId) {
          const applied = await applyRole({ botToken, guildId, userId: user.id, roleId });
          if (applied.ok) roleApplied = isMinor ? 'minor' : 'adult';
          else roleError = applied.error;
        }
        if (oppositeRoleId) await removeRole({ botToken, guildId, userId: user.id, roleId: oppositeRoleId });

        // Token curto que libera um convite de uso único só para quem verificou.
        joinToken = sealState(
          {
            d: user.id,
            g: guildId,
            t: Date.now(),
            n: crypto.randomBytes(12).toString('base64url'),
          },
          secret
        );
      }
    } else {
      roleError = 'Bot do Discord não configurado.';
    }

    const ip = (req.headers['x-forwarded-for'] || '').split(',')[0].trim();
    const ipHash = ip ? crypto.createHash('sha256').update(`${ip}:${secret}`).digest('hex').slice(0, 32) : null;

    const saved = await saveVerification({
      discord_id: user.id,
      username: user.username || null,
      global_name: user.global_name || null,
      age_years: effectiveAge,
      is_minor: isMinor,
      self_age: typedAge,
      google_age: googleAge,
      google_match: googleMatch,
      guild_id: guildId,
      role_applied: roleApplied,
      role_error: roleError,
      source: googleAge !== null ? 'google' : 'site',
      ip_hash: ipHash,
    });

    if (!saved.ok) {
      // A verificação em si funcionou; só o registro no Supabase falhou.
      console.error('Supabase save failed:', saved.error);
    }

    if (mismatch) return redirect(res, '/?status=error&reason=mismatch');
    const vt = joinToken ? `&vt=${encodeURIComponent(joinToken)}` : '';
    return redirect(res, `/?status=ok&role=${roleApplied || 'none'}${vt}`);
  } catch (error) {
    console.error('verify callback error:', error);
    return fail(res, 'discord');
  }
}
