import crypto from 'node:crypto';

const TOKEN_TTL_MS = 10 * 60 * 1000;

function json(res, status, body) {
  res.status(status).setHeader('Content-Type', 'application/json; charset=utf-8');
  res.setHeader('Cache-Control', 'no-store');
  res.send(JSON.stringify(body));
}

function openToken(token, secret) {
  if (typeof token !== 'string') return null;
  const parts = token.split('.');
  if (parts.length !== 3) return null;
  try {
    const key = crypto.createHash('sha256').update(secret).digest();
    const decipher = crypto.createDecipheriv('aes-256-gcm', key, Buffer.from(parts[0], 'base64url'));
    decipher.setAuthTag(Buffer.from(parts[2], 'base64url'));
    const raw = Buffer.concat([decipher.update(Buffer.from(parts[1], 'base64url')), decipher.final()]);
    const payload = JSON.parse(raw.toString('utf8'));
    if (!payload || typeof payload.t !== 'number' || Date.now() - payload.t > TOKEN_TTL_MS) return null;
    if (typeof payload.d !== 'string') return null;
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

  const secret = process.env.VERIFY_SECRET;
  const botToken = process.env.DISCORD_BOT_TOKEN;
  const channelId = process.env.DISCORD_INVITE_CHANNEL_ID;

  if (!secret || !botToken || !channelId) {
    return json(res, 500, { error: 'Convite automático não configurado no servidor.' });
  }

  let body = req.body;
  if (typeof body === 'string') {
    try { body = JSON.parse(body); } catch { body = null; }
  }

  const payload = openToken(body && body.vt, secret);
  if (!payload) {
    return json(res, 403, { error: 'Verificação expirada. Faça a verificação de novo.' });
  }

  const response = await fetch(`https://discord.com/api/v10/channels/${channelId}/invites`, {
    method: 'POST',
    headers: {
      Authorization: `Bot ${botToken}`,
      'Content-Type': 'application/json',
      'User-Agent': 'CapriVerify/1.0',
    },
    body: JSON.stringify({
      max_age: 3600,
      max_uses: 1,
      unique: true,
      reason: 'Verificação de idade concluída',
    }),
  });

  if (!response.ok) {
    const text = await response.text().catch(() => '');
    return json(res, 502, { error: `Não foi possível gerar o convite (${response.status}). ${text.slice(0, 160)}` });
  }

  const data = await response.json().catch(() => null);
  if (!data || !data.code) {
    return json(res, 502, { error: 'O Discord não devolveu um convite válido.' });
  }

  return json(res, 200, { url: `https://discord.gg/${data.code}` });
}
