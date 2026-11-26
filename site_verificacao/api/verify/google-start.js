import crypto from 'node:crypto';

const MIN_AGE = 13;
const MAX_AGE = 120;

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

// AES-GCM keeps the typed birthdate opaque in the OAuth state.
function sealState(payload, secret) {
  const key = crypto.createHash('sha256').update(secret).digest();
  const iv = crypto.randomBytes(12);
  const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
  const raw = Buffer.concat([cipher.update(JSON.stringify(payload), 'utf8'), cipher.final()]);
  const tag = cipher.getAuthTag();
  return `${iv.toString('base64url')}.${raw.toString('base64url')}.${tag.toString('base64url')}`;
}

export default async function handler(req, res) {
  if (req.method !== 'POST') {
    res.setHeader('Allow', 'POST');
    return json(res, 405, { error: 'Método não permitido.' });
  }

  const clientId = process.env.GOOGLE_CLIENT_ID;
  const secret = process.env.VERIFY_SECRET;
  if (!clientId || !secret) {
    const missing = [];
    if (!clientId) missing.push('GOOGLE_CLIENT_ID');
    if (!secret) missing.push('VERIFY_SECRET');
    return json(res, 500, { error: 'Verificação com Google não configurada no servidor.', missing });
  }

  let body = req.body;
  if (typeof body === 'string') {
    try { body = JSON.parse(body); } catch { body = null; }
  }

  const birthdate = body && body.birthdate;
  const age = ageFrom(birthdate);
  if (age === null) {
    return json(res, 400, { error: 'Data de nascimento inválida.' });
  }
  if (age < MIN_AGE) {
    return json(res, 400, { error: 'É necessário ter pelo menos 13 anos para usar o Discord.' });
  }

  const host = req.headers['x-forwarded-host'] || req.headers.host;
  const proto = req.headers['x-forwarded-proto'] || 'https';
  const redirectUri = process.env.GOOGLE_REDIRECT_URI || `${proto}://${host}/api/verify/google-callback`;

  const state = sealState(
    {
      b: birthdate,
      t: Date.now(),
      n: crypto.randomBytes(12).toString('base64url'),
    },
    secret
  );

  const params = new URLSearchParams({
    client_id: clientId,
    redirect_uri: redirectUri,
    response_type: 'code',
    scope: 'openid email profile https://www.googleapis.com/auth/user.birthday.read',
    access_type: 'online',
    include_granted_scopes: 'true',
    prompt: 'consent',
    state,
  });

  return json(res, 200, {
    url: `https://accounts.google.com/o/oauth2/v2/auth?${params.toString()}`,
  });
}
