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

  const fandomEl = document.getElementById('detail-fandom');
  const data = {
    david: {
      name: "David Martinez",
      role: "Protagonista / Mercenário",
      desc: "Jovem que se junta aos mercenários após uma tragédia familiar.",
      img: "assets/David-Martinez.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/David_Martinez"
    },
    maine: {
      name: "Maine",
      role: "Líder / Ex-soldado",
      desc: "Líder veterano e ex-soldado.",
      img: "assets/Maine.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/Maine_(Edgerunners)"
    },
    lucy: {
      name: "Lucy (Lucyna Kushinada)",
      role: "Netrunner",
      desc: "Netrunner misteriosa e interesse amoroso de David.",
      img: "assets/Lucy.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/Lucyna_Kushinada"
    },
    rebecca: {
      name: "Rebecca",
      role: "Solo",
      desc: "Solo agressiva e rápida no gatilho.",
      img: "assets/Rebecca.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/Rebecca"
    },
    pilar: {
      name: "Pilar",
      role: "Técnico",
      desc: "Irmão de Rebecca, técnico cômico e grosseiro.",
      img: "assets/Pilar.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/Pilar"
    },
    kiwi: {
      name: "Kiwi",
      role: "Netrunner",
      desc: "Netrunner enigmática e sarcástica.",
      img: "assets/Kiwi.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/Kiwi"
    },
    dorio: {
      name: "Dorio",
      role: "Veterana / Braço direito",
      desc: "Braço direito de Maine e veterana da equipe.",
      img: "assets/Dorio.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/Dorio_Gunnarsd%C3%B3ttir"
    },
    falco: {
      name: "Falco",
      role: "Condutor nômade",
      desc: "Condutor nômade da equipe.",
      img: "assets/Falco.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/Falco"
    },
    faraday: {
      name: "Faraday",
      role: "Fixer corporativo",
      desc: "Fixer corporativo que atua como antagonista principal.",
      img: "assets/Faraday_Infobox_CPEDGE.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/Faraday"
    },
    doc: {
      name: "Doc (Ripperdoc)",
      role: "Ripperdoc",
      desc: "Cirurgião do mercado negro que fornece implantes ilegais.",
      img: "assets/Doc_Infobox_CPEDGE.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/Doc_(Edgerunners)"
    },
    gloria: {
      name: "Gloria Martinez",
      role: "Paramédica do Trauma Team",
      desc: "Mãe de David e paramédica do Trauma Team.",
      img: "assets/Gloria_Infobox_CPEDGE.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/Gloria_Martinez"
    },
    'adam-smasher': {
      name: "Adam Smasher",
      role: "Ciborgue corporativo",
      desc: "Ciborgue corporativo e antagonista final.",
      img: "assets/Adam_Smasher_Infobox_CPEDGE.webp",
      fandom: "https://cyberpunk.fandom.com/wiki/Adam_Smasher"
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
    if (fandomEl) {
      fandomEl.href = d.fandom || '#';
      fandomEl.style.display = d.fandom ? 'inline-flex' : 'none';
    }
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

  const SEEN_KEY = 'capri_intro_seen_v1';
  if (localStorage.getItem(SEEN_KEY) === '1') {
    intro.remove();
    document.body.style.overflow = '';
    return;
  }

  const total = 43;
  const frames = Array.from({length: total}, (_, i) => `assets/capri/legacy-intro/${i + 1}.png`);
  frames.forEach(src => { const im = new Image(); im.src = src; });

  let i = 0;
  const fps = 14;
  const interval = Math.round(1000 / fps);
  document.body.style.overflow = 'hidden';

  function finish() {
    localStorage.setItem(SEEN_KEY, '1');
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
    try { localStorage.setItem(SEEN_KEY, '1'); } catch (_) {}
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


// TikTok previews: carregar ao clicar
(() => {
  const previews = Array.from(document.querySelectorAll('.tiktok-preview[data-video-id][data-video-url]'));
  if (!previews.length) return;
  previews.forEach(btn => {
    btn.addEventListener('click', () => {
      const id = btn.dataset.videoId;
      const url = btn.dataset.videoUrl;
      const card = btn.closest('.tiktok-video-card');
      if (!card || !id || !url) return;
      card.innerHTML = `<blockquote class="tiktok-embed" cite="${url}" data-video-id="${id}" style="max-width:605px;min-width:325px;"><section><a target="_blank" href="${url}">Ver no TikTok</a></section></blockquote>`;
      if (window.tiktokEmbed && window.tiktokEmbed.lib && typeof window.tiktokEmbed.lib.render === 'function') {
        window.tiktokEmbed.lib.render(card);
      } else {
        const s = document.createElement('script');
        s.async = true;
        s.src = 'https://www.tiktok.com/embed.js';
        document.body.appendChild(s);
      }
    }, { once: true });
  });
})();


// Feedback / dúvidas (Supabase)
(() => {
  const root = document.getElementById('feedback');
  if (!root) return;

  const SUPABASE_URL = 'https://uxuagcqifqkunibazuor.supabase.co';
  const SUPABASE_KEY = 'sb_publishable_fQUVsFFRbE-ZjqASiBjTiw_xlncw-r6';
  const TABLE = 'feedback';
  const PROFILE_KEY = 'capri_feedback_profile_v1';
  const DEFAULT_AVATAR = 'assets/icon_capri.webp';

  const el = {
    profile: document.getElementById('feedback-profile'),
    avatarInput: document.getElementById('feedback-avatar-input'),
    avatarPreview: document.getElementById('feedback-avatar-preview'),
    name: document.getElementById('feedback-name'),
    save: document.getElementById('feedback-save-profile'),
    visitor: document.getElementById('feedback-visitor'),
    profileStatus: document.getElementById('feedback-profile-status'),
    compose: document.querySelector('.feedback-compose'),
    currentAvatar: document.getElementById('feedback-current-avatar'),
    currentName: document.getElementById('feedback-current-name'),
    editProfile: document.getElementById('feedback-edit-profile'),
    message: document.getElementById('feedback-message'),
    count: document.getElementById('feedback-count'),
    send: document.getElementById('feedback-send'),
    sendStatus: document.getElementById('feedback-send-status'),
    list: document.getElementById('feedback-list'),
  };

  const sb = window.supabase && window.supabase.createClient
    ? window.supabase.createClient(SUPABASE_URL, SUPABASE_KEY)
    : null;

  let profile = loadProfile();
  let avatarDraft = profile?.avatar || DEFAULT_AVATAR;

  function loadProfile() {
    try {
      const raw = localStorage.getItem(PROFILE_KEY);
      if (!raw) return null;
      const p = JSON.parse(raw);
      if (!p || typeof p.name !== 'string') return null;
      return { name: p.name, avatar: p.avatar || DEFAULT_AVATAR, visitor: !!p.visitor };
    } catch { return null; }
  }
  function saveProfile(p) {
    profile = p;
    localStorage.setItem(PROFILE_KEY, JSON.stringify(p));
  }
  function setStatus(node, msg, type) {
    if (!node) return;
    node.textContent = msg || '';
    node.className = 'feedback-status' + (type ? ' ' + type : '');
  }
  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }
  function fmtTime(iso) {
    try {
      return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
    } catch { return ''; }
  }

  function applyProfileUI() {
    const has = !!profile;
    if (el.profile) el.profile.style.display = has ? 'none' : 'block';
    if (el.compose) el.compose.classList.toggle('locked', !has);
    if (el.currentName) el.currentName.textContent = has ? profile.name : 'Visitante';
    if (el.currentAvatar) el.currentAvatar.src = has ? (profile.avatar || DEFAULT_AVATAR) : DEFAULT_AVATAR;
    if (el.name && !has) el.name.value = '';
    if (el.avatarPreview) el.avatarPreview.src = has ? (profile.avatar || DEFAULT_AVATAR) : DEFAULT_AVATAR;
  }

  function readAvatarFile(file) {
    return new Promise((resolve, reject) => {
      if (!file) return resolve(null);
      if (!file.type.startsWith('image/')) return reject(new Error('Escolha uma imagem válida.'));
      const reader = new FileReader();
      reader.onerror = () => reject(new Error('Não foi possível ler a imagem.'));
      reader.onload = () => {
        const img = new Image();
        img.onerror = () => reject(new Error('Imagem inválida.'));
        img.onload = () => {
          const canvas = document.createElement('canvas');
          const size = 128;
          canvas.width = size; canvas.height = size;
          const ctx = canvas.getContext('2d');
          const ratio = Math.max(size / img.width, size / img.height);
          const w = img.width * ratio, h = img.height * ratio;
          ctx.fillStyle = '#111';
          ctx.fillRect(0, 0, size, size);
          ctx.drawImage(img, (size - w) / 2, (size - h) / 2, w, h);
          resolve(canvas.toDataURL('image/jpeg', 0.82));
        };
        img.src = String(reader.result);
      };
      reader.readAsDataURL(file);
    });
  }

  async function loadComments() {
    if (!el.list) return;
    if (!sb) {
      el.list.innerHTML = '<p class="feedback-empty">Supabase não carregou. Recarregue a página.</p>';
      return;
    }
    const { data, error } = await sb.from(TABLE).select('id,name,avatar,message,created_at').order('created_at', { ascending: false }).limit(50);
    if (error) {
      console.warn('Feedback load error', error);
      el.list.innerHTML = '<p class="feedback-empty">Não foi possível carregar os comentários agora. Verifique se a tabela de feedback foi criada no Supabase.</p>';
      return;
    }
    if (!data || !data.length) {
      el.list.innerHTML = '<p class="feedback-empty">Nenhuma dúvida ainda. Seja o primeiro a comentar!</p>';
      return;
    }
    el.list.innerHTML = data.map(item => {
      const name = escapeHtml(item.name || 'Visitante');
      const msg = escapeHtml(item.message || '');
      const avatar = escapeHtml(item.avatar || DEFAULT_AVATAR);
      const time = fmtTime(item.created_at);
      return `<article class="feedback-item">
        <img class="feedback-item-avatar" src="${avatar}" alt="Avatar de ${name}" loading="lazy">
        <div class="feedback-item-body">
          <div class="feedback-item-meta">
            <span class="feedback-item-name">${name}</span>
            <span class="feedback-item-time">${time}</span>
          </div>
          <p class="feedback-item-msg">${msg}</p>
        </div>
      </article>`;
    }).join('');
  }

  async function sendComment() {
    if (!profile) { setStatus(el.sendStatus, 'Crie seu perfil ou entre como visitante primeiro.', 'error'); return; }
    if (!sb) { setStatus(el.sendStatus, 'Supabase não carregou.', 'error'); return; }
    const message = (el.message?.value || '').trim();
    if (!message) { setStatus(el.sendStatus, 'Escreva uma dúvida ou comentário.', 'error'); return; }
    el.send.disabled = true;
    setStatus(el.sendStatus, 'Enviando...', '');
    const payload = {
      name: profile.name.slice(0, 40),
      avatar: profile.avatar || DEFAULT_AVATAR,
      message: message.slice(0, 500),
    };
    const { error } = await sb.from(TABLE).insert(payload);
    el.send.disabled = false;
    if (error) {
      console.warn('Feedback send error', error);
      setStatus(el.sendStatus, 'Não foi possível enviar. Confira se a tabela de feedback existe no Supabase.', 'error');
      return;
    }
    el.message.value = '';
    if (el.count) el.count.textContent = '0/500';
    setStatus(el.sendStatus, 'Comentário enviado!', 'ok');
    loadComments();
  }

  if (el.avatarInput) {
    el.avatarInput.addEventListener('change', async () => {
      const file = el.avatarInput.files && el.avatarInput.files[0];
      if (!file) return;
      try {
        avatarDraft = await readAvatarFile(file);
        if (el.avatarPreview) el.avatarPreview.src = avatarDraft;
        setStatus(el.profileStatus, 'Foto carregada.', 'ok');
      } catch (e) {
        setStatus(el.profileStatus, e.message || 'Erro ao carregar foto.', 'error');
      }
    });
  }

  if (el.save) {
    el.save.addEventListener('click', () => {
      const name = (el.name?.value || '').trim();
      if (!name) { setStatus(el.profileStatus, 'Digite seu nome ou entre como visitante.', 'error'); return; }
      saveProfile({ name: name.slice(0, 40), avatar: avatarDraft || DEFAULT_AVATAR, visitor: false });
      setStatus(el.profileStatus, 'Perfil salvo!', 'ok');
      applyProfileUI();
    });
  }

  if (el.visitor) {
    el.visitor.addEventListener('click', () => {
      saveProfile({ name: 'Visitante', avatar: DEFAULT_AVATAR, visitor: true });
      setStatus(el.profileStatus, '', '');
      applyProfileUI();
    });
  }

  if (el.editProfile) {
    el.editProfile.addEventListener('click', () => {
      avatarDraft = profile?.avatar || DEFAULT_AVATAR;
      if (el.profile) el.profile.style.display = 'block';
      if (el.name) el.name.value = profile?.visitor ? '' : (profile?.name || '');
      if (el.avatarPreview) el.avatarPreview.src = avatarDraft;
      if (el.compose) el.compose.classList.add('locked');
      setStatus(el.profileStatus, 'Edite seu perfil e salve.', '');
    });
  }

  if (el.message && el.count) {
    el.message.addEventListener('input', () => {
      const len = el.message.value.length;
      el.count.textContent = len + '/500';
    });
  }

  if (el.send) el.send.addEventListener('click', sendComment);

  applyProfileUI();
  loadComments();
})();


// Saudação por horário
(() => {
  const el = document.getElementById('hero-greeting');
  if (!el) return;
  const h = new Date().getHours();
  let msg = 'Boa noite, choom!';
  if (h >= 5 && h < 12) msg = 'Bom dia, choom!';
  else if (h >= 12 && h < 18) msg = 'Boa tarde, choom!';
  el.textContent = msg;
  const img = document.getElementById('hero-greeting-img');
  if (img) img.alt = 'Capri falando ' + msg;
})();
