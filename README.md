# Usage LLMs

Widget leve para Windows que mostra as cotas de **5 horas** e **semanal** do Codex na borda da tela.

[![Baixar para Windows](docs/download-windows.svg)](../../releases/latest/download/Usage-LLMs-Windows.exe)

## Como usar

1. Baixe e abra o executável. Ele é portátil e requer **Windows de 64 bits com .NET Framework 4.8 ou 4.8.1**.
2. Para ver suas cotas, conecte o Codex CLI à sua conta ChatGPT. O widget também encontra o CLI da extensão oficial do VS Code. [Instalar o Codex CLI](https://github.com/openai/codex#quickstart)
3. Passe o mouse sobre o ícone para ver o consumo e os horários de renovação. Arraste o widget para trocar de borda ou clique com o botão direito para ajustar a aparência, os provedores visíveis e a inicialização com o Windows.

As cotas são atualizadas automaticamente a cada 30 segundos. O brilho atrás do ícone indica atividade em uma sessão local do Codex. Claude Code, Antigravity e Grok Build são prévias da interface.

O Windows pode exibir um aviso porque o executável está sem assinatura digital.

## Compilar

Clone o repositório e execute estes comandos no PowerShell, a partir da pasta do projeto:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
& '.\bin\Usage LLMs.exe'
```

Use `-Release` no comando de build para gerar `bin\Usage-LLMs-Windows.exe`, o arquivo da GitHub Release.

## Créditos

Projeto independente, inspirado no Codenotch. O símbolo exibido para o Codex vem da extensão oficial da OpenAI para VS Code. [Codenotch](https://github.com/vinzdg/codenotch) · [Créditos dos demais ícones](src/provider-marks.LICENSE.txt)
