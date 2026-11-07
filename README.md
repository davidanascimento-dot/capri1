# Capri — companhia de desktop

Uma adaptação pessoal da Capri para Windows, reunindo a presença animada da personagem na área de trabalho com recursos de conversa, movimento, aparência e integração com ferramentas de IA. A proposta é manter as funções úteis das versões de referência e organizá-las em uma experiência só.

> Este repositório é o espaço do projeto **capri1**. A versão fusionada mais recente ainda está no workspace local e não foi publicada como release aqui.

## Prévia visual

As imagens abaixo vêm da galeria do [Capri Desktop de referência](https://github.com/lachydotmcg/capri-desktop) e mostram a personagem, suas animações e exemplos de personalização. A build fusionada pode ter diferenças de interface.

<div align="center">
  <img src="https://raw.githubusercontent.com/lachydotmcg/capri-desktop/main/assets/readme/idle.webp" alt="Capri em animação de espera" height="180">
  <img src="https://raw.githubusercontent.com/lachydotmcg/capri-desktop/main/assets/readme/talk.webp" alt="Capri falando" height="180">
  <img src="https://raw.githubusercontent.com/lachydotmcg/capri-desktop/main/assets/readme/glitch.webp" alt="Capri em efeito de glitch" height="180">
  <img src="https://raw.githubusercontent.com/lachydotmcg/capri-desktop/main/assets/readme/sleep.webp" alt="Capri descansando" height="180">
</div>

<div align="center">
  <img src="https://raw.githubusercontent.com/lachydotmcg/capri-desktop/main/assets/readme/perch.webp" alt="Capri sentada na borda de uma janela" height="170">
  <img src="https://raw.githubusercontent.com/lachydotmcg/capri-desktop/main/assets/readme/perch-music.webp" alt="Capri sentada e vibrando com a música" height="170">
</div>

<p align="center"><sub>Animações, passeio e reação à música.</sub></p>

<p align="center">
  <img src="https://raw.githubusercontent.com/lachydotmcg/capri-desktop/main/assets/readme/mods.png" alt="Prévia de skins, cores e mods da Capri" width="820">
  <br><sub>Exemplos de cores, skins e mods disponíveis no projeto de referência.</sub>
</p>

## Download para Windows

[![Baixar Capri para Windows](https://img.shields.io/badge/Windows-baixar%20Capri-ff2a6d?style=for-the-badge&logo=windows)](https://github.com/lachydotmcg/capri-desktop/releases/latest/download/Capri-Setup.exe)

O botão baixa o instalador da **release pública mais recente do Capri Desktop upstream**. Ele não é um pacote da fusão personalizada deste repositório. A build personalizada ainda não tem release pública; quando ela for publicada, este link poderá apontar para o instalador próprio.

Veja também as [releases do Capri Desktop](https://github.com/lachydotmcg/capri-desktop/releases) e o [código-fonte upstream](https://github.com/lachydotmcg/capri-desktop).

## O que a Capri reúne

### Presença na área de trabalho

- Sprites e animações da personagem, incluindo a entrada e as expressões disponíveis na build.
- Movimento pela tela, passeio automático, seguir o cursor, sentar em janelas e interagir com as bordas da tela.
- Modo mini com sprites próprios, menu de contexto e retorno ao tamanho normal.
- Opções de tamanho, aparência, movimentos, holograma e visibilidade.
- Ações de repouso, sono, dança e reações configuráveis.

### Conversa e personalização

- Conversa por texto e recursos de voz conforme os componentes configurados.
- Integração com cérebro local ou provedor compatível configurado pela pessoa usuária.
- Memória, clipes, idioma, ajustes de comportamento e opções de privacidade.
- Mods e skins para personalizar aparência e recursos compatíveis.
- Controles no menu para passeio, modo mini, Ouvidos, Aparência, Tamanho, Cérebro, Doar e Ajustes.

### Recursos opcionais

Algumas integrações dependem de configuração e compatibilidade do Windows, dos aplicativos em uso e do provedor escolhido. Entre elas podem estar observar a tela, reagir a jogos, participar de chamadas, usar voz, controlar mídia e conversar com outros serviços. Os itens disponíveis são os que aparecem na build instalada.

## Como usar

1. Baixe e instale a Capri pelo link de Windows acima.
2. Abra o painel da Capri para configurar o cérebro e a voz disponíveis.
3. Clique com o botão direito na personagem para abrir o menu.
4. Use **Passear pela tela** para ligar ou pausar o passeio; no modo mini, o mesmo controle fica no menu de contexto dela.
5. Abra **Ajustes**, **Aparência** e **Tamanho** para personalizar os recursos disponíveis.

## Repositórios Capri consultados

Estes são os projetos Capri identificados nas fontes locais e consultados durante a adaptação. Eles têm autores, versões e licenças próprias; as referências não significam que todo o código ou conteúdo foi incorporado.

| Projeto | Para que serve como referência |
| --- | --- |
| **Esta Capri** — [davidanascimento-dot/capri1](https://github.com/davidanascimento-dot/capri1) | README e apresentação desta adaptação. |
| **Capri antiga / Capri Rápida** — [Capri-cmd/capri-ai](https://github.com/Capri-cmd/capri-ai) | Código Python local da versão clássica; referência para a introdução, sprites, passeio, modo mini, menu e comportamento na área de trabalho. O endereço antigo `Capri-AI` redireciona para este repositório. |
| **Capri Desktop para PC** — [lachydotmcg/capri-desktop](https://github.com/lachydotmcg/capri-desktop) | Versão Desktop em C# usada como base de funcionalidades e interface para Windows. |
| **Mods da Capri** — [lachydotmcg/capri-mods](https://github.com/lachydotmcg/capri-mods) | Catálogo de mods, skins e exemplos de personalização. |
| **Capri Desktop / Code-Husk** — [mochiiwuff/capri-desktop](https://github.com/mochiiwuff/capri-desktop) | Projeto Electron consultado para ideias de integração com assistentes de programação e presença da personagem durante tarefas. |
| **Capri Room VR** — [fadievr/capri-BD-VR](https://github.com/fadievr/capri-BD-VR) | Versão de realidade mista para Quest; projeto relacionado, não parte da versão de PC. |

### Ferramenta auxiliar

[morluto/rea](https://github.com/morluto/rea) é uma ferramenta de engenharia reversa usada como apoio para inspecionar binários. Não é um projeto Capri nem uma dependência do aplicativo.

Também existe uma versão Android local no workspace, com recursos próprios para celular. Ela ainda não tem um repositório remoto identificado, por isso não há um link GitHub para incluir nesta lista.

## Créditos, licença e uso

Capri é um projeto de fã não oficial, sem vínculo ou aprovação da CD PROJEKT RED, dos responsáveis por *Cyberpunk: Edgerunners* ou de seus dubladores. Os nomes, personagens e artes pertencem aos respectivos titulares e criadores.

Antes de redistribuir código, sprites, vozes, mods ou instaladores, confira a licença e os créditos no repositório de origem. A licença de cada componente é independente; este README não substitui nem altera esses termos. Não use o projeto para vender conteúdo de terceiros sem autorização.

## Privacidade

O comportamento depende das opções ativadas. Um cérebro local pode processar as conversas no próprio computador; quando um provedor online é configurado, mensagens e dados necessários à solicitação podem ser enviados a esse provedor. Revise as opções de tela, voz, chamadas e o aviso de privacidade do serviço escolhido antes de ativá-los.

## Estado da publicação

- O README está preparado para documentar o projeto neste repositório.
- A fusão personalizada compilada permanece local; nenhum instalador, executável, arquivo temporário ou pacote de testes foi adicionado a este repositório.
- O link de download acima aponta para a release upstream pública enquanto não houver uma release própria da fusão.
