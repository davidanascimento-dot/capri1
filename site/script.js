(() => {
  const slides = Array.from(document.querySelectorAll('.bg-slide[data-bg]'));
  if (!slides.length) return;

  const triggers = Array.from(document.querySelectorAll('[data-bg-trigger], section[id], footer[data-bg-trigger]'));
  const slideByName = new Map(slides.map(s => [s.dataset.bg, s]));

  function activate(name) {
    const target = slideByName.get(name);
    if (!target) return;
    slides.forEach(s => s.classList.toggle('active', s === target));
  }

  // Map trigger elements to bg names
  const entries = [];
  triggers.forEach(el => {
    const name = el.dataset.bgTrigger || el.id;
    if (name && slideByName.has(name)) entries.push({ el, name });
  });

  if (!entries.length) return;

  // Activate based on which trigger is closest to viewport center
  let ticking = false;
  function update() {
    ticking = false;
    const center = window.innerHeight * 0.45;
    let best = null;
    let bestDist = Infinity;
    for (const { el, name } of entries) {
      const r = el.getBoundingClientRect();
      // only consider elements that are around viewport
      if (r.bottom < 0 || r.top > window.innerHeight) continue;
      const elCenter = r.top + Math.min(r.height, window.innerHeight) / 2;
      const dist = Math.abs(elCenter - center);
      if (dist < bestDist) { bestDist = dist; best = name; }
    }
    if (best) activate(best);
  }

  function onScroll() {
    if (!ticking) {
      ticking = true;
      requestAnimationFrame(update);
    }
  }

  window.addEventListener('scroll', onScroll, { passive: true });
  window.addEventListener('resize', onScroll);
  update();
})();

// Galeria de personagens: clique fixa, hover só pré-visualiza
(() => {
  const cards = Array.from(document.querySelectorAll('.char-card[data-char]'));
  const img = document.getElementById('detail-img');
  const nameEl = document.getElementById('detail-name');
  const roleEl = document.getElementById('detail-role');
  const descEl = document.getElementById('detail-desc');
  if (!cards.length || !img || !nameEl || !roleEl || !descEl) return;

  const data = {
    david: {
      name: "David Martinez",
      role: "Protagonista / Mercenário",
      desc: "Jovem que se junta aos mercenários após uma tragédia familiar.",
      img: "assets/David-Martinez.webp"
    },
    maine: {
      name: "Maine",
      role: "Líder / Ex-soldado",
      desc: "Líder veterano e ex-soldado.",
      img: "assets/Maine.webp"
    },
    lucy: {
      name: "Lucy (Lucyna Kushinada)",
      role: "Netrunner",
      desc: "Netrunner misteriosa e interesse amoroso de David.",
      img: "assets/Lucy.webp"
    },
    rebecca: {
      name: "Rebecca",
      role: "Solo",
      desc: "Solo agressiva e rápida no gatilho.",
      img: "assets/Rebecca.webp"
    },
    pilar: {
      name: "Pilar",
      role: "Técnico",
      desc: "Irmão de Rebecca, técnico cômico e grosseiro.",
      img: "assets/Pilar.webp"
    },
    kiwi: {
      name: "Kiwi",
      role: "Netrunner",
      desc: "Netrunner enigmática e sarcástica.",
      img: "assets/Kiwi.webp"
    },
    dorio: {
      name: "Dorio",
      role: "Veterana / Braço direito",
      desc: "Braço direito de Maine e veterana da equipe.",
      img: "assets/Dorio.webp"
    },
    falco: {
      name: "Falco",
      role: "Condutor nômade",
      desc: "Condutor nômade da equipe.",
      img: "assets/Falco.webp"
    },
    faraday: {
      name: "Faraday",
      role: "Fixer corporativo",
      desc: "Fixer corporativo que atua como antagonista principal.",
      img: "assets/Faraday_Infobox_CPEDGE.webp"
    },
    doc: {
      name: "Doc (Ripperdoc)",
      role: "Ripperdoc",
      desc: "Cirurgião do mercado negro que fornece implantes ilegais.",
      img: "assets/Doc_Infobox_CPEDGE.webp"
    },
    gloria: {
      name: "Gloria Martinez",
      role: "Paramédica do Trauma Team",
      desc: "Mãe de David e paramédica do Trauma Team.",
      img: "assets/Gloria_Infobox_CPEDGE.webp"
    },
    'adam-smasher': {
      name: "Adam Smasher",
      role: "Ciborgue corporativo",
      desc: "Ciborgue corporativo e antagonista final.",
      img: "assets/Adam_Smasher_Infobox_CPEDGE.webp"
    },
  };

  let selected = cards.find(c => c.classList.contains('active'))?.dataset.char || cards[0].dataset.char;

  function render(key) {
    const d = data[key];
    if (!d) return;
    img.src = d.img;
    img.alt = d.name;
    nameEl.textContent = d.name;
    roleEl.textContent = d.role;
    descEl.textContent = d.desc;
    cards.forEach(c => c.classList.toggle('active', c.dataset.char === key));
  }

  cards.forEach(card => {
    card.addEventListener('click', () => {
      selected = card.dataset.char;
      render(selected);
    });
    card.addEventListener('mouseenter', () => {
      render(card.dataset.char);
    });
    card.addEventListener('mouseleave', () => {
      render(selected);
    });
    // keyboard support
    card.setAttribute('tabindex', '0');
    card.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        selected = card.dataset.char;
        render(selected);
      }
    });
  });

  render(selected);
})();

// Intro em tela cheia (legacy-intro)
(() => {
  const intro = document.getElementById('capri-intro');
  const img = document.getElementById('capri-intro-img');
  const btn = document.getElementById('capri-intro-start');
  const audio = document.getElementById('capri-intro-audio');
  if (!intro || !img) return;

  const total = 43;
  const frames = Array.from({length: total}, (_, i) => `assets/capri/legacy-intro/${i + 1}.png`);
  frames.forEach(src => { const im = new Image(); im.src = src; });

  let i = 0;
  const fps = 14;
  const interval = Math.round(1000 / fps);
  document.body.style.overflow = 'hidden';

  function finish() {
    intro.classList.add('hide');
    document.body.style.overflow = '';
    setTimeout(() => intro.remove(), 800);
  }

  function step() {
    img.src = frames[i];
    i++;
    if (i < total) setTimeout(step, interval);
    else setTimeout(finish, 500);
  }

  function start() {
    if (btn) { btn.disabled = true; btn.textContent = 'Entrando...'; }
    if (audio) { audio.volume = 0.7; audio.play().catch(() => {}); }
    setTimeout(step, 100);
  }

  if (btn) btn.addEventListener('click', start, { once: true });
  else setTimeout(start, 150);
})();


// Contador Discord
(() => {
  const nameEl = document.getElementById('discord-community-name');
  const membersEl = document.getElementById('discord-member-count');
  const onlineEl = document.getElementById('discord-online-count');
  const iconEl = document.getElementById('discord-community-icon');
  if (!membersEl || !onlineEl) return;

  async function fetchDiscord() {
    const endpoints = [
      '/api/discord',
      'https://discord.com/api/v9/invites/zHmfDrqxmz?with_counts=true'
    ];
    for (const url of endpoints) {
      try {
        const res = await fetch(url, { cache: 'no-store' });
        if (!res.ok) continue;
        const data = await res.json();
        const members = data.members ?? data.approximate_member_count ?? data.profile?.member_count;
        const online = data.online ?? data.approximate_presence_count ?? data.profile?.online_count;
        const name = data.name ?? data.guild?.name;
        const icon = data.icon ?? (data.guild?.id && data.guild?.icon ? `https://cdn.discordapp.com/icons/${data.guild.id}/${data.guild.icon}.png?size=128` : null);
        if (typeof members === 'number') membersEl.textContent = members.toLocaleString('pt-BR');
        if (typeof online === 'number') onlineEl.textContent = online.toLocaleString('pt-BR');
        if (name && nameEl) nameEl.textContent = name;
        if (icon && iconEl) iconEl.src = icon;
        return;
      } catch (_) {}
    }
  }

  fetchDiscord();
  setInterval(fetchDiscord, 60000);
})();
