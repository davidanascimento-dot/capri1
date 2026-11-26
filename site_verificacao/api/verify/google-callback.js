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

function sealState(payload, secret) {
  const key = crypto.createHash('sha256').update(secret).digest();
  const iv = crypto.randomBytes(12);
  const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
  const raw = Buffer.concat([cipher.update(JSON.stringify(payload), 'utf8'), cipher.final()]);
  const tag = cipher.getAuthTag();
  return `${iv.toString('base64url')}.${raw.toString('base64url')}.${tag.toString('base64url')}`;
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

function ageFrom(birthdate) {
  if (typeof birthdate !== 'string') return null;
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

async function requestJson(url, options = {}) {
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

// Picks the birthday that belongs to the account itself (not to a contact).
function birthdayFromPeople(data) {
  const list = Array.isArray(data && data.birthdays) ? data.birthdays : [];
  const usable = list.filter((item) => item && item.date && Number.isInteger(item.date.year));
  if (!usable.length) return null;

  const account = usable.find((item) => {
    const source = item.metadata && item.metadata.source;
    return source && source.type === 'ACCOUNT';
  });
  const picked = account || usable[0];
  const { year, month, day } = picked.date;
  if (!Number.isInteger(month) || !Number.isInteger(day)) return null;

  const mm = String(month).padStart(2, '0');
  const dd = String(day).padStart(2, '0');
  return `${year}-${mm}-${dd}`;
}

export default async function handler(req, res) {
  try {
    const url = new URL(req.url, `https://${req.headers.host || 'localhost'}`);
    const code = url.searchParams.get('code');
    const state = url.searchParams.get('state');
    const googleError = url.searchParams.get('error');

    if (googleError || !code) return fail(res, 'google-oauth');

    const secret = process.env.VERIFY_SECRET;
    const googleClientId = process.env.GOOGLE_CLIENT_ID;
    const googleClientSecret = process.env.GOOGLE_CLIENT_SECRET;
    const discordClientId = process.env.DISCORD_CLIENT_ID;

    if (!secret || !googleClientId || !googleClientSecret || !discordClientId) {
      return fail(res, 'config');
    }

    const payload = openState(state, secret);
    if (!payload) return fail(res, 'state');

    const typedAge = ageFrom(payload.b);
    if (typedAge === null || typedAge < MIN_AGE) return fail(res, 'age');

    const host = req.headers['x-forwarded-host'] || req.headers.host;
    const proto = req.headers['x-forwarded-proto'] || 'https';
    const googleRedirectUri = process.env.GOOGLE_REDIRECT_URI || `${proto}://${host}/api/verify/google-callback`;

    const tokenResult = await requestJson('https://oauth2.googleapis.com/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        client_id: googleClientId,
        client_secret: googleClientSecret,
        grant_type: 'authorization_code',
        code,
        redirect_uri: googleRedirectUri,
      }).toString(),
    });

    if (!tokenResult.response.ok || !tokenResult.data?.access_token) {
      return fail(res, 'google-oauth');
    }

    const people = await requestJson(
      'https://people.googleapis.com/v1/people/me?personFields=birthdays',
      { headers: { Authorization: `Bearer ${tokenResult.data.access_token}` } }
    );

    const googleBirthdate = people.response.ok ? birthdayFromPeople(people.data) : null;
    const googleAge = googleBirthdate ? ageFrom(googleBirthdate) : null;

    if (googleAge !== null && googleAge < MIN_AGE) return fail(res, 'age');

    const googleMatch = googleAge === null ? null : googleAge === typedAge;

    // Google has no birthday on file: let the user continue with what they typed,
    // using a short-lived sealed ticket so the fallback can't be abused directly.
    if (googleAge === null) {
      const ticket = sealState(
        { b: payload.b, t: Date.now(), n: crypto.randomBytes(12).toString('base64url') },
        secret
      );
      return redirect(res, `/?status=no-birthday&ticket=${encodeURIComponent(ticket)}`);
    }

    const discordRedirectUri = process.env.DISCORD_REDIRECT_URI || `${proto}://${host}/api/verify/callback`;

    const discordState = sealState(
      {
        b: payload.b,
        g: googleAge,
        gm: googleMatch,
        n: crypto.randomBytes(12).toString('base64url'),
        t: Date.now(),
      },
      secret
    );

    const params = new URLSearchParams({
      client_id: discordClientId,
      redirect_uri: discordRedirectUri,
      response_type: 'code',
      scope: 'identify guilds.join',
      state: discordState,
      prompt: 'consent',
    });

    return redirect(res, `https://discord.com/api/oauth2/authorize?${params.toString()}`);
  } catch (error) {
    console.error('google callback error:', error);
    return fail(res, 'google-oauth');
  }
}
