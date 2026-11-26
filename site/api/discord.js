const GUILD_ID = '1287190286925303858';

export default async function handler(req, res) {
  try {
    const guildId = process.env.DISCORD_GUILD_ID || GUILD_ID;
    const botToken = process.env.DISCORD_BOT_TOKEN;

    // Com o bot configurado, o contador não depende de nenhum convite público.
    const headers = { 'User-Agent': 'CapriSite/1.0' };
    let url;
    if (botToken) {
      url = `https://discord.com/api/v10/guilds/${guildId}?with_counts=true`;
      headers.Authorization = `Bot ${botToken}`;
    } else {
      url = 'https://discord.com/api/v10/invites/zHmfDrqxmz?with_counts=true';
    }

    const response = await fetch(url, { headers });
    if (!response.ok) {
      res.status(502).setHeader('Content-Type', 'application/json; charset=utf-8').setHeader('Cache-Control', 's-maxage=60, stale-while-revalidate=300').send(JSON.stringify({ error: 'Discord request failed' }));
      return;
    }

    const data = await response.json();
    const guild = data.guild || data;
    const profile = data.profile || {};
    const icon = guild.icon ? `https://cdn.discordapp.com/icons/${guild.id || guildId}/${guild.icon}.png?size=128` : null;

    res.status(200).setHeader('Content-Type', 'application/json; charset=utf-8').setHeader('Cache-Control', 's-maxage=60, stale-while-revalidate=300').send(JSON.stringify({
      name: guild.name || profile.name || 'Discord',
      members: data.approximate_member_count ?? profile.member_count ?? null,
      online: data.approximate_presence_count ?? profile.online_count ?? null,
      icon
    }));
  } catch (error) {
    res.status(500).setHeader('Content-Type', 'application/json; charset=utf-8').send(JSON.stringify({ error: 'Unexpected error' }));
  }
}
