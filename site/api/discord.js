export default async function handler() {
  try {
    const res = await fetch('https://discord.com/api/v9/invites/zHmfDrqxmz?with_counts=true', {
      headers: { 'User-Agent': 'CapriSite/1.0' }
    });
    if (!res.ok) {
      return new Response(JSON.stringify({ error: 'Discord request failed' }), {
        status: 502,
        headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 's-maxage=60, stale-while-revalidate=300' }
      });
    }
    const data = await res.json();
    const guild = data.guild || {};
    const profile = data.profile || {};
    const icon = guild.icon ? `https://cdn.discordapp.com/icons/${guild.id}/${guild.icon}.png?size=128` : null;
    return new Response(JSON.stringify({
      name: guild.name || profile.name || 'Discord',
      members: data.approximate_member_count ?? profile.member_count ?? null,
      online: data.approximate_presence_count ?? profile.online_count ?? null,
      icon
    }), {
      status: 200,
      headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 's-maxage=60, stale-while-revalidate=300' }
    });
  } catch (err) {
    return new Response(JSON.stringify({ error: 'Internal error' }), {
      status: 500,
      headers: { 'content-type': 'application/json; charset=utf-8' }
    });
  }
}
