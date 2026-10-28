# Atlasmith

Sprite atlas direto dos arquivos do Krita, para o Unity ou qualquer engine.

*[Read in English](README.md)*

Desenhe os sprites como camadas de um arquivo do Krita, dê nomes como
`menu-tab-active`, e o Atlasmith empacota tudo num PNG só, mais uma planilha
pequena que diz onde fica cada sprite. Sem etapa de exportação, sem pasta de PNGs
soltos para manter em dia.

São duas partes, que usam o mesmo código de empacotamento e geram o mesmo atlas:

- **Atlasmith para Windows**: app independente, para artistas que nem abrem o
  Unity e para qualquer outra engine.
- **O pacote do Unity**: clique com o botão direito num `.kra` ou `.krz` na janela
  Project e escolha **Create Atlas**. Vem com importer próprio e converte atlas do
  TexturePacker sem quebrar as referências aos sprites.

## Recursos

- Lê `.kra` e `.krz` direto. Camadas com nome no padrão `nome-nome` viram sprites;
  um grupo com nome assim vira um sprite só, com os filhos compostos.
- **Ignorar camadas desativadas** (ligado por padrão): camadas com o olho fechado no
  Krita ficam fora do atlas.
- Também empacota uma pasta de imagens (PNG, JPG, BMP, GIF, TIFF, TGA).
- Empacotamento MaxRects com várias heurísticas, ficando com o resultado menor e
  mais equilibrado: `2048×1024` em vez de `1024×4096`.
- Grava PNG + [`.atlasmith.json`](docs/atlas-format.md), formato aberto que qualquer
  engine lê, e, se quiser, um `.tpsheet` no mesmo layout do TexturePacker (veja
  [a nota abaixo](#sobre-o-tpsheet)).
- Antes de sobrescrever um atlas, avisa quais sprites vão sair e se estão em
  camadas desativadas: no Unity, o que usa esses sprites perderia a referência.
- Português e inglês, seguindo o idioma do sistema; tema claro e escuro seguindo o
  Windows.

## Atlasmith para Windows

### Instalar

Baixe na página de [Releases](../../releases):

- **`Atlasmith-1.0.0-setup.exe`**: instala só para o seu usuário, sem pedir
  permissão de administrador, em `%LocalAppData%\Programs\Atlasmith`. Os atalhos na
  área de trabalho e no menu Iniciar são opcionais. Para desinstalar:
  *Configurações → Aplicativos*.
- **`Atlasmith.exe`**: portátil. Copie para onde quiser e abra.

Os dois são o mesmo arquivo autocontido: não precisa instalar mais nada (nem .NET,
nem Python). Windows 10 ou 11, x64.

> Os executáveis ainda não têm assinatura digital, então o SmartScreen do Windows
> pode mostrar *"O Windows protegeu o computador"*. Clique em **Mais informações →
> Executar assim mesmo**.

### Usar

1. **Entrada**: ligue **Ler direto do Krita** e escolha um `.kra`/`.krz`, ou escolha
   uma pasta de imagens (subpastas entram junto). Arrastar o arquivo ou a pasta para
   a janela também funciona.
2. **Saída**: pasta de destino, nome do atlas e formato da planilha.
3. **Empacotamento**: margens, tamanho máximo e as outras opções abaixo.
4. **Gerar atlas**, ou `Ctrl+G`.

Todo atlas gerado entra na lista **Atlas salvos**. Escolher um preenche tudo de
volta: depois de mexer na arte, gerar de novo é um clique.

| Opção | O que faz |
|---|---|
| **Margem entre sprites** | Pixels vazios garantidos entre um sprite e outro. Padrão 5. |
| **Margem da borda** | Pixels vazios entre os sprites e a borda do atlas. |
| **Extrude** | Repete a borda de cada sprite para fora, evitando *bleeding* com filtro bilinear ou mipmap. O retângulo do sprite não muda. |
| **Tamanho máximo** | Maior largura ou altura, de 512 a 16384. Padrão 4096. |
| **Dimensões em potência de 2** | Ligado por padrão. Desligado, o atlas encolhe até a área usada, arredondada para múltiplo de 4. |
| **Forçar atlas quadrado** | Só considera tamanhos N×N. |

As configurações ficam em `%AppData%\Atlasmith`. Na primeira abertura, as
configurações e os atlas salvos das versões beta (DaigoPacker) são copiados; a
pasta antiga não é alterada.

### Linha de comando

```
Atlasmith.exe [arquivo.kra | arquivo.krz | pasta]
Atlasmith.exe --install   [--quiet] [--no-desktop] [--no-start-menu] [--no-launch]
Atlasmith.exe --uninstall [--quiet]
Atlasmith.exe --self-check [relatorio.txt]
```

O `--self-check` faz o executável publicado montar uma janela, renderizar e gerar
um atlas completo, e sai com 0 se tudo passou.

## Arquivos do Krita

- Viram sprites as camadas com nome de duas ou mais partes separadas por hífen, sem
  espaço: `menu-tab-active`, `speech-balloon-A`. "Plano de Fundo", "Camada 1" ou
  "logo" ficam de fora.
- Um **grupo** com nome nesse padrão vira um sprite só, com os filhos compostos. Um
  grupo com outro nome é só organização: as camadas de dentro entram normalmente.
- Pixels e recorte são os que o Batch Exporter do Krita gravaria: a área desenhada
  da camada, sem a opacidade e o modo de mesclagem da própria camada. Máscaras de
  transparência são aplicadas.
- **Ignorar camadas desativadas**: camadas com o olho fechado no Krita, ou dentro de
  um grupo fechado, ficam de fora. Desligue para incluí-las.
- Se duas camadas tiverem o mesmo nome, só uma entra (a visível ganha da
  desativada) e o resumo aponta as outras.
- Vale o arquivo **salvo**: salve no Krita antes de gerar.
- Fora do suporte: camadas vetoriais, de filtro, de preenchimento, clone e de
  arquivo (converta para camada de pintura). Estilos de camada e máscaras de filtro
  não são aplicados. O app lista o que ficou de fora.
- Espaços de cor: RGBA e cinza, 8 ou 16 bits por canal. Os outros (CMYK, Lab, ponto
  flutuante) pedem *Imagem → Converter espaço de cor da imagem* no Krita antes.

## Pacote do Unity

Precisa do Unity 2022.3 ou mais novo. Depende do pacote 2D Sprite, que o Package
Manager adiciona sozinho.

### Instalar

Em *Window → Package Manager*, clique em **+ → Add package from git URL…** e cole:

```
https://github.com/digorlima/Atlasmith.git?path=/unity/com.daigo.atlasmith
```

Ou acrescente ao `Packages/manifest.json`, fixando uma versão:

```json
"com.daigo.atlasmith": "https://github.com/digorlima/Atlasmith.git?path=/unity/com.daigo.atlasmith#v1.0.1"
```

Na primeira vez que o pacote entra no projeto, o Unity reimporta todas as texturas
uma vez, como acontece com qualquer importer de textura (o TexturePacker Importer
também). Num projeto grande, instale quando puder deixar o Unity trabalhando um tempo.

### Criar um atlas

Clique com o botão direito em um ou mais `.kra`/`.krz` na janela Project e escolha
**Create Atlas**. O atlas é gerado na hora ao lado do arquivo do Krita, como
`<nome>.png` + `<nome>.atlasmith.json`, com os padrões: tamanho máximo 4096, margem
5, dimensões em potência de 2, camadas desativadas de fora.

A janela que abre mostra o resultado (tamanho, aproveitamento, preview com o
contorno dos sprites, o log) e as opções, para mudar e gerar de novo. Elas ficam
guardadas no próprio `.atlasmith.json` (`source` e `settings`): cada arquivo do
Krita tem as suas, e elas vão junto com o projeto para o controle de versão.

Se uma nova geração for tirar sprites do atlas, nada é gravado até você decidir. O
aviso lista esses sprites, diz quais estão em camadas desativadas e quais são usados
por cenas, prefabs ou outros assets, e oferece incluir as camadas desativadas.

### O importer

Planilhas `.atlasmith.json` são importadas como sprites do PNG ao lado. Cada sprite
mantém o ID pelo nome, então cenas, prefabs e animações continuam apontando para os
sprites certos a cada geração, mesmo quando o layout muda inteiro. Uma nova geração
só move o retângulo de cada sprite: pivôs e bordas de 9-slice editados no Sprite
Editor ficam como estão. Um sprite que sai e depois volta recupera o ID antigo, e as
referências a ele voltam a funcionar; o pivô e as bordas recomeçam pelos da planilha.

A busca de referências cobre os assets gravados como texto (cenas, prefabs,
animações, materiais, ScriptableObjects, arquivos do UI Toolkit). Sprites carregados
pelo nome em tempo de execução, via `Resources` ou Addressables, não são detectados.
A primeira busca lê todos esses arquivos; o que cada um cita fica guardado em
`Library/Atlasmith`, e as buscas seguintes só releem os arquivos que mudaram.
Arquivos binários, como os de iluminação, guardam as referências em bytes e ficam de
fora, assim como as pastas que o Unity ignora (`.git`, `Samples~`).

### Vindo do TexturePacker

*Tools → Atlasmith → Convert TexturePacker Sheets…* (ou botão direito num
`.tpsheet` → **Convert to Atlasmith**) converte atlas `.tpsheet` para
`.atlasmith.json`:

- a planilha nova sai do que o Unity tem hoje (retângulos, pivôs, bordas);
- cada sprite mantém o `spriteID` e o `fileID`, então as referências sobrevivem;
- o resultado é conferido sprite por sprite e, se algo não bater, a conversão é
  desfeita;
- antes de mexer, os arquivos originais são copiados para `AtlasmithBackups/`, na
  raiz do projeto. Deixe essa pasta fora do controle de versão.

Depois de converter, faça commit dos `.atlasmith.json` novos com os `.meta` deles,
dos `.tpsheet` apagados e do `SettingsTexturePackerImporter.txt` atualizado. Os
`.meta` das texturas não mudam.

### Sobre o `.tpsheet`

O TexturePacker Importer só aceita planilhas criadas pelo próprio TexturePacker, e o
Atlasmith não finge ser o TexturePacker: o `.tpsheet` gravado pelo Atlasmith diz isso
no cabeçalho, e o TexturePacker Importer o recusa. Para o Unity, converta o atlas com
o pacote e use `.atlasmith.json` daí em diante. Quando o app do Windows vai
sobrescrever um `.tpsheet` que o TexturePacker Importer lê, ele avisa antes e sugere
converter. A saída `.tpsheet` continua para outras ferramentas que leem esse layout.

## Compilar

Precisa do [.NET SDK 10](https://dotnet.microsoft.com/download).

```powershell
.\build.ps1                      # dist\Atlasmith.exe e dist\Atlasmith-<versão>-setup.exe
.\build.ps1 -Runtime win-arm64   # versão ARM64
dotnet run --project src\Atlasmith\Atlasmith.csproj
```

```
src/Atlasmith/              app do Windows (WPF)
src/Atlasmith.Core/         compila o código compartilhado abaixo como biblioteca do app
unity/com.daigo.atlasmith/  pacote do Unity
  Editor/Core/              código compartilhado: leitor do Krita, empacotador, PNG e planilhas
tools/
  PackerBench/              teste de integração do empacotamento e comparação com um atlas de referência
  KraCheck/                 confere o leitor do Krita contra o próprio Krita e contra atlas existentes
  krita/                    scripts que rodam dentro do Krita (export de referência, .kra de teste)
  verify_atlas.py           conferência independente, pixel a pixel, de um par PNG + .tpsheet
docs/atlas-format.md        o formato .atlasmith.json
```

O código em `unity/com.daigo.atlasmith/Editor/Core` é escrito em C# 9 para .NET
Standard 2.1, para que os mesmos arquivos compilem no Unity e no app.

## Licença

[MIT](LICENSE). Componentes de terceiros e marcas registradas estão em
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

O Atlasmith é um projeto independente, sem vínculo com a CodeAndWeb (TexturePacker),
a Krita Foundation ou a Unity Technologies, nem endossado por elas.
