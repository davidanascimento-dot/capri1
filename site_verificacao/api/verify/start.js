import crypto from 'node:crypto';

const MINOR_AGE = 18;
const MIN_AGE = 13;
const MAX_AGE = 120;
const STATE_TTL_MS = 15 * 60 * 1000;

function json(res, status, body) {
  res.status(status).setHeader('Content-Type', 'application/json; charset=utf-8');
  res.setHeader('Cache-Control', 'no-store');
  res.send(JSON.stringify(body));
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

// AES-GCM keeps the birthdate opaque in the OAuth state instead of only signing it.
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

export default async function handler(req, res) {
  if (req.method !== 'POST') {
    res.setHeader('Allow', 'POST');
    return json(res, 405, { error: 'Método não permitido.' });
  }

  const clientId = process.env.DISCORD_CLIENT_ID;
  const secret = process.env.VERIFY_SECRET;
  if (!clientId || !secret) {
    return json(res, 500, { error: 'Verificação não configurada no servidor.' });
  }

  let body = req.body;
  if (typeof body === 'string') {
    try { body = JSON.parse(body); } catch { body = null; }
  }

  // When Google verification is enabled, the manual path only accepts the
  // short-lived ticket issued by the Google callback (Google had no birthday).
  const googleEnabled = Boolean(process.env.GOOGLE_CLIENT_ID);
  let birthdate = body && body.birthdate;

  if (googleEnabled) {
    const ticket = body && body.ticket;
    const opened = ticket ? openState(ticket, secret) : null;
    if (!opened) {
      return json(res, 403, { error: 'Use a verificação com Google para continuar.' });
    }
    birthdate = opened.b;
  }

  const age = ageFrom(birthdate);
  if (age === null) {
    return json(res, 400, { error: 'Data de nascimento inválida.' });
  }
  if (age < MIN_AGE) {
    return json(res, 400, { error: 'É necessário ter pelo menos 13 anos para usar o Discord.' });
  }

  const host = req.headers['x-forwarded-host'] || req.headers.host;
  const proto = req.headers['x-forwarded-proto'] || 'https';
  const redirectUri = process.env.DISCORD_REDIRECT_URI || `${proto}://${host}/api/verify/callback`;

  const state = sealState(
    {
      b: birthdate,
      a: age,
      m: age < MINOR_AGE,
      n: crypto.randomBytes(12).toString('base64url'),
      t: Date.now(),
    },
    secret
  );

  const params = new URLSearchParams({
    client_id: clientId,
    redirect_uri: redirectUri,
    response_type: 'code',
    scope: 'identify guilds.join',
    state,
    prompt: 'consent',
  });

  return json(res, 200, {
    url: `https://discord.com/api/oauth2/authorize?${params.toString()}`,
  });
}
