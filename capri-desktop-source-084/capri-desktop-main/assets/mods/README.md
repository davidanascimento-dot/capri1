# Capri Clássica

`capri-classic.capri-skin` adapta os sprites compatíveis da Capri original ao formato de skins do Desktop. O build inclui o pacote e o instala, desativado, na área Mods na primeira execução. Qualquer visual já escolhido continua ativo.

A arte original é creditada a **xilia_world00** no projeto original da Capri. O pacote adapta os sprites sentados à quantidade de quadros usada pelo Desktop. As poses deitada não foram incluídas porque usam uma organização de quadros incompatível com o carregador de arte do Desktop.

## Maroraga

`maroraga.capri-mod` é o mod oficial de teste da Capri. O build inclui o pacote e o instala ativado na área Mods na primeira execução, a menos que o usuário o desligue. O bloco `maroraga` guarda o que o mod precisa; o comportamento dela será escrito sobre esse bloco conforme for definido.

Enquanto o mod está ativo, a Capri ganha o anel dourado do Maroraga. Ele fica pequeno na cabeça dela, como uma coroa, e gira em 3D: as esferas douradas são recortadas do próprio desenho enviado e presas nos aros dourados dele, então é o anel dele girando no próprio eixo, com as esferas da frente maiores e as de trás menores. Gira devagar quando ela está parada, uma volta completa e rápida a cada pedido, e um giro de comemoração quando ela termina o job ou aprende com um erro. Os quatro movimentos continuam existindo por baixo do giro: o que ela viu, o que o pedido realmente quer, como vai fazer e o que conferir antes de dizer que terminou. O anel também observa em segundo plano qual aplicativo está em primeiro plano e quanto tempo o usuário passa ali, sem guardar títulos de janela. O que ela aprende com erros e acertos fica em `maroraga.json`, junto dos dados dela, e vale nas próximas execuções. `watch` e `remember` no bloco `maroraga` controlam a observação e o aprendizado.
