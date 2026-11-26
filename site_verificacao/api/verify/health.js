const KEYS = [
  'VERIFY_SECRET',
  'DISCORD_CLIENT_ID',
  'DISCORD_CLIENT_SECRET',
  'DISCORD_REDIRECT_URI',
  'DISCORD_BOT_TOKEN',
  'DISCORD_GUILD_ID',
  'DISCORD_MINOR_ROLE_ID',
  'DISCORD_ADULT_ROLE_ID',
  'DISCORD_INVITE_CHANNEL_ID',
  'GOOGLE_CLIENT_ID',
  'GOOGLE_CLIENT_SECRET',
  'GOOGLE_REDIRECT_URI',
  'SUPABASE_URL',
  'SUPABASE_SERVICE_ROLE_KEY',
];

export default async function handler(req, res) {
  const present = {};
  for (const key of KEYS) {
    const value = process.env[key];
    present[key] = typeof value === 'string' && value.trim().length > 0;
  }

  const missing = KEYS.filter((key) => !present[key]);

  res
    .status(200)
    .setHeader('Content-Type', 'application/json; charset=utf-8')
    .setHeader('Cache-Control', 'no-store')
    .send(
      JSON.stringify({
        ok: missing.length === 0,
        commit: (process.env.VERCEL_GIT_COMMIT_SHA || '').slice(0, 7) || null,
        node: process.version,
        missing,
        present,
      })
    );
}
