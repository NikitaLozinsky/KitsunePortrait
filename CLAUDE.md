# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Kitsune Portrait is a Unity Mod Manager mod for **Pathfinder: Wrath of the Righteous** (Owlcat Games). It lets kitsune characters use two independent portraits — one for fox form, one for human form — and auto-switches between them in-game. Patches are applied via Harmony (`0Harmony.dll`) against the game's `Assembly-CSharp` and Owlcat UI assemblies.

Single project: `KitsunePortrait/KitsunePortrait.csproj`, target framework `net48`. Mod manifest is `KitsunePortrait/Info.json` (entry point `KitsunePortrait.Main.Load`).

## Build

Building requires the env var **`WOTR_PATH`** set to the game's install directory — it's used both to resolve game assembly references (`$(WOTR_PATH)\Wrath_Data\Managed\...`) and, after every build, to auto-deploy: the `DeployMod` MSBuild target copies the built DLL, `Info.json`, `Assets/`, and `Locales/` straight into `$(WOTR_PATH)\Mods\KitsunePortrait` — the live game's mods folder. This means a normal `dotnet build` has a side effect outside the repo (overwriting whatever mod build is currently installed there).

There is no automated test project. Changes are verified by building (which deploys automatically) and testing manually in-game.

## Localization

Translated strings live in a single file: `KitsunePortrait/Locales/Localization.json`, an array of entries each carrying `ruRU`/`enGB`/`zhCN` translations plus a `SimpleName`/`ProcessTemplates` flag. Loaded via `Localization.cs`.