export default async function handler(req, res) {
  try {
    const response = await fetch('https://discord.com/api/v9/invites/zHmfDrqxmz?with_counts=true', {
      headers: { 'User-Agent': 'CapriSite/1.0' }
    });
    if (!response.ok) {
      res.status(502).setHeader('Content-Type', 'application/json; charset=utf-8').setHeader('Cache-Control', 's-maxage=60, stale-while-revalidate=300').send(JSON.stringify({ error: 'Discord request failed' }));
      return;
    }
    const data = await response.json();
    const guild = data.guild || {};
    const profile = data.profile || {};
    const icon = guild.icon ? `https://cdn.discordapp.com/icons/${guild.id}/${guild.icon}.png?size=128` : null;
    res.status(200).setHeader('Content-Type', 'application/json; charset=utf-8').setHeader('Cache-Control', 's-maxage=60, stale-while-revalidate=300').send(JSON.stringify({
      name: guild.name || profile.name || 'Discord',
      members: data.approximate_member_count ?? profile.member_count ?? null,
      online: data.approximate_presence_count ?? profile.online_count ?? null,
      icon
    }));
  } catch (err) {
    res.status(500).setHeader('Content-Type', 'application/json; charset=utf-8').send(JSON.stringify({ error: 'Internal error' }));
  }
}
