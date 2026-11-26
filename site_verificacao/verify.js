(() => {
  const form = document.getElementById('verify-form');
  const birthdate = document.getElementById('birthdate');
  const consent = document.getElementById('consent');
  const googleButton = document.getElementById('google-button');
  const manualButton = document.getElementById('manual-button');
  const status = document.getElementById('status');
  const preview = document.getElementById('age-preview');
  const ageValue = document.getElementById('age-value');
  const ageTag = document.getElementById('age-tag');
  const joinBlock = document.getElementById('join-block');
  const joinButton = document.getElementById('join-button');
  const joinStatus = document.getElementById('join-status');
  const joinText = document.getElementById('join-text');

  const MINOR_AGE = 18;
  const MIN_AGE = 13;
  const STORAGE_KEY = 'capri-verify-date';
  const DEEP_LINK = 'https://discord.com/channels/1287190286925303858';

  const params = new URLSearchParams(window.location.search);
  const ticket = params.get('ticket');
  const verifyToken = params.get('vt');
  let useDeepLink = false;

  function ageFrom(value) {
    if (!value) return null;
    const parts = value.split('-').map(Number);
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
    if (age < 0 || age > 120) return null;
    return age;
  }

  function refresh() {
    const age = ageFrom(birthdate.value);
    manualButton.disabled = !ticket;

    if (age === null) {
      preview.hidden = true;
      ageTag.textContent = '';
      ageTag.className = 'age-tag';
      googleButton.disabled = true;
      return;
    }

    preview.hidden = false;
    ageValue.textContent = `${age} anos`;

    if (age < MIN_AGE) {
      ageTag.textContent = 'Idade mínima';
      ageTag.className = 'age-tag minor';
      googleButton.disabled = true;
      return;
    }

    const minor = age < MINOR_AGE;
    ageTag.textContent = minor ? 'Menor' : 'Maior';
    ageTag.className = `age-tag ${minor ? 'minor' : 'adult'}`;
    googleButton.disabled = !consent.checked;
  }

  function setStatus(message, kind) {
    status.textContent = message || '';
    status.className = `status${kind ? ` ${kind}` : ''}`;
  }

  function remember() {
    try { sessionStorage.setItem(STORAGE_KEY, birthdate.value); } catch { /* modo privado */ }
  }

  function restore() {
    try {
      const saved = sessionStorage.getItem(STORAGE_KEY);
      if (saved && !birthdate.value) birthdate.value = saved;
    } catch { /* modo privado */ }
  }

  birthdate.max = new Date().toISOString().slice(0, 10);
  birthdate.addEventListener('input', refresh);
  birthdate.addEventListener('change', () => { remember(); refresh(); });
  consent.addEventListener('change', refresh);
  restore();
  refresh();

  if (ticket) {
    manualButton.hidden = false;
    manualButton.disabled = false;
  }

  const result = params.get('status');
  if (result === 'ok') {
    form.hidden = true;
    const role = params.get('role');
    if (role === 'minor') {
      setStatus('Verificação concluída. A data conferiu com o Google e o cargo Menor foi aplicado.', 'ok');
    } else if (role === 'adult') {
      setStatus('Verificação concluída. A data conferiu com o Google. Você já pode entrar no servidor.', 'ok');
    } else {
      setStatus('Verificação concluída, mas o cargo não foi aplicado automaticamente. Avise a moderação.', 'ok');
    }

    // O convite só aparece depois da verificação.
    joinBlock.hidden = false;
    joinButton.textContent = verifyToken ? 'Gerar meu convite' : 'Abrir o Discord';
    if (!verifyToken) {
      joinText.textContent = 'Você já foi adicionado ao servidor. Clique para abrir o Discord.';
      useDeepLink = true;
    }
  } else if (result === 'no-birthday') {
    setStatus('Não encontramos sua data de nascimento na conta Google. Você pode continuar com a data que digitou.', '');
  } else if (result === 'error') {
    const reason = params.get('reason');
    const messages = {
      state: 'A verificação expirou. Tente novamente.',
      oauth: 'O Discord não autorizou a verificação. Tente novamente.',
      'google-oauth': 'O Google não autorizou a verificação. Tente novamente.',
      'google-config': 'A verificação com Google ainda não está configurada.',
      age: 'Data de nascimento inválida. Confira e tente novamente.',
      config: 'A verificação ainda não está configurada no servidor.',
      discord: 'Não foi possível falar com o Discord agora. Tente novamente.',
      role: 'Sua verificação foi salva, mas o cargo não pôde ser aplicado. Avise a moderação.',
      mismatch: 'A data que você digitou não confere com a da sua conta Google. O cargo não foi aplicado. Se a data do Google estiver errada, corrija na sua conta Google e tente de novo, ou fale com a moderação.',
    };
    setStatus(messages[reason] || 'Não foi possível concluir a verificação. Tente novamente.', 'error');
  }

  async function goDiscord(body) {
    try {
      const response = await fetch('/api/verify/start', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      const data = await response.json().catch(() => ({}));
      if (!response.ok || !data.url) {
        setStatus(data.error || 'Não foi possível iniciar a verificação.', 'error');
        return false;
      }

      window.location.href = data.url;
      return true;
    } catch (error) {
      setStatus('Sem conexão com o servidor. Tente novamente.', 'error');
      return false;
    }
  }

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const age = ageFrom(birthdate.value);
    if (age === null) {
      setStatus('Escolha uma data de nascimento válida.', 'error');
      return;
    }
    if (age < MIN_AGE) {
      setStatus('É necessário ter pelo menos 13 anos para usar o Discord.', 'error');
      return;
    }
    if (!consent.checked) {
      setStatus('Confirme que as informações são verdadeiras para continuar.', 'error');
      return;
    }

    remember();
    googleButton.disabled = true;
    setStatus('Abrindo o Google...', '');

    try {
      const response = await fetch('/api/verify/google-start', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ birthdate: birthdate.value }),
      });

      const data = await response.json().catch(() => ({}));
      if (!response.ok || !data.url) {
        googleButton.disabled = false;
        manualButton.hidden = false;
        manualButton.disabled = false;
        setStatus(`${data.error || 'Não foi possível abrir o Google.'} Se o Google não funcionar, use o botão abaixo com a data digitada.`, 'error');
        return;
      }

      window.location.href = data.url;
    } catch (error) {
      googleButton.disabled = false;
      setStatus('Sem conexão com o servidor. Tente novamente.', 'error');
    }
  });

  manualButton.addEventListener('click', async () => {
    manualButton.disabled = true;
    setStatus('Abrindo o Discord...', '');
    const ok = ticket
      ? await goDiscord({ ticket })
      : await goDiscord({ birthdate: birthdate.value });
    if (!ok) manualButton.disabled = false;
  });

  joinButton.addEventListener('click', async () => {
    if (useDeepLink || !verifyToken) {
      window.location.href = DEEP_LINK;
      return;
    }

    joinButton.disabled = true;
    joinStatus.textContent = 'Gerando seu convite...';
    joinStatus.className = 'status';

    try {
      const response = await fetch('/api/verify/invite', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ vt: verifyToken }),
      });

      const data = await response.json().catch(() => ({}));
      if (!response.ok || !data.url) {
        useDeepLink = true;
        joinButton.disabled = false;
        joinButton.textContent = 'Abrir o Discord';
        joinStatus.textContent = `${data.error || 'Não foi possível gerar o convite.'} Você já pode abrir o Discord.`;
        joinStatus.className = 'status error';
        return;
      }

      window.location.href = data.url;
    } catch (error) {
      useDeepLink = true;
      joinButton.disabled = false;
      joinButton.textContent = 'Abrir o Discord';
      joinStatus.textContent = 'Sem conexão com o servidor. Você já pode abrir o Discord.';
      joinStatus.className = 'status error';
    }
  });
})();
