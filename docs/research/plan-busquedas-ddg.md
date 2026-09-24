# Investigación del estado PLAN — duckduckgo_search

**Fuente normativa:** AGENT.md estado `2_PLAN` ("Investiga en la web
mejores prácticas para implementar la fixture; adapta soluciones que
funcionen"). Herramienta: `duckduckgo_search` (paquete Python) como
ordenó el propietario. Fecha: 2026-09-24.

## F6.06-instalador-dual

**Consulta:** `Inno Setup dual installer detect 32 64 bit architecture ArchitecturesInstallIn64BitMode best practices`


_Backend: auto_

- **Inno Setup Help - jrsoftware.org** — <https://jrsoftware.org/ishelp/topic_setup_architecturesinstallin64bitmode.htm>
  Specifies the architectures on which Setup should enable 64-bit install mode. If this directive is set to a blank value, Setup will always use 32-bit install mode. Generally, 64-bit install mode should only be enabled on
- **[Setup]: ArchitecturesInstallIn64BitMode | Inno Setup ...32bit and 64bit binary in one Inno Setup installer - Stack ...Code sampleInno Setup Help - jrsoftware.orgInstall Mode: 32-bit vs. 64-bit | Inno Setup Documentation[Setup]: ArchitecturesInstallIn64BitModeInnoSetup/Examples/64BitTwoArch.iss at master - GitHub** — <https://documentation.help/Inno-Setup/topic_setup_architecturesinstallin64bitmode.htm>
  Be sure you have read the 64-bit Installation Limitations topic before setting this directive. If your application runs only on 64-bit processor architectures, you should set ArchitecturesAllowed to the same value as thi
- **32bit and 64bit binary in one Inno Setup installer - Stack ...Code sample** — <https://stackoverflow.com/questions/4833831/32bit-and-64bit-binary-in-one-inno-setup-installer>
  Jan 28, 2011 · Is it possible to add a file say x64.dll when it is a 64bit installation and x86.dll when it is a 32bit installation? It is possible. Take a look at the 64BitTwoArch.iss sample (especially the Is64BitInsta

## F5.09-sandbox-js

**Consulta:** `JScript IActiveScript sandbox limits CPU memory windows scripting engine host restrictions`


_Backend: auto_

- **Use and configure Windows Sandbox | Microsoft Learn** — <https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file>
  Mar 29, 2026 · Files and folders mapped from the host can be compromised by apps in the sandbox or potentially affect the host. Changes made during a Sandbox session to a mapped folder with write-permissions will persist
- **Windows Script Engines | microsoft/ClearScript | DeepWiki** — <https://deepwiki.com/microsoft/ClearScript/5-windows-script-engines>
  May 31, 2025 · Each Windows Script engine instance is bound to a System.Windows.Threading.Dispatcher during instantiation and cannot execute script code on different threads. Script delegates and event handlers are marsh
- **Windows Script Hosts** — <https://jsdoc.inflectra.com/HelpReadingPane.ashx?href=html/hosts.htm>
  The restricted model described above is intentionally loose enough to allow a host to abort a stuck script by calling IActiveScript::InterruptScriptThread from another thread (initiated by a CTRL+BREAK handler or the lik
- **Microsoft Replaces JScript with JScript9Legacy in Windows 11** — <https://windowsforum.com/news/microsoft-replaces-legacy-jscript-with-jscript9legacy-in-windows-11-enhanced-security-compatibility.373116/>
  Jul 10, 2025 · Microsoft’s recent transition from the longstanding JScript engine to JScript9Legacy for Windows 11, beginning with version 24H2, marks a significant security milestone, subtly closing the door on an era r

## F4.15-fidelity-report

**Consulta:** `OpenXML pptx import conversion fidelity report skipped elements best practice`


_Backend: auto_

- **Choosing Document Conversion Fidelity in .NET - officeimo.com** — <https://officeimo.com/blog/choosing-document-conversion-fidelity/>
  Why document conversion needs an explicit fidelity policy, and how to choose between editable output, visual fallback, source preservation, and rejection.
- **Understanding OpenXML for PowerPoint Presentations | FileFormat.Slides** — <https://fileformat-slides.github.io/FileFormat.Slides-for-.NET/docs/introopenxmlsdk.html>
  The Power of OpenXML API OpenXML provides a powerful slides API for working with PowerPoint presentations in .NET applications. It allows developers to programmatically create, modify, and customize slides, layouts, and 
- **Clippit Fresh PowerTools for OpenXml | Clippit - GitHub Pages** — <https://sergey-tihon.github.io/Clippit/>
  Clippit — Fresh Power Tools for Open Xml Clippit is a .NET library for programmatically creating, modifying, and converting Word (DOCX), Excel (XLSX), and PowerPoint (PPTX) documents. Built on top of the Open XML SDK, it
- **pptx-html-fidelity-audit - Skywork Skill Hub** — <https://skywork.ai/skillhub/pptx-html-fidelity-audit-skywork/>
  The pptx-html-fidelity-audit skill is designed to meticulously compare and adjust .pptx exports created from HTML slide decks, ensuring high fidelity between the original and exported files.
- **Outputting PowerPoint Files (*.pptx) Using OpenXML SDK in C#** — <https://ayumax.net/entry/2018/09/04/021745/>
  The tool mentioned in the above article, which converts Markdown to PPTX, creates PPTX files using the OpenXML SDK. I initially struggled greatly with using this OpenXML SDK, so this article will focus on that part.

## F6.04-drive-offline

**Consulta:** `Google Drive API offline first sync conflict resolution version desktop app`


_Backend: auto_

- **GitHub - browser-storage-com/offline-sync-kit: Framework ...** — <https://github.com/browser-storage-com/offline-sync-kit>
  Jul 18, 2026 · It gives you a durable mutation queue, pluggable conflict resolution, and reconnect-aware retries with exponential backoff — the plumbing every offline-capable app needs, without tying you to a framework, 
- **Google Drive Desktop Technical Setup: Sync, Offline ...** — <https://admin365.blog/2026/04/10/google-drive-desktop-technical-setup-sync-offline-collaboration-guide-2026/>
  Apr 10, 2026 · Complete technical guide to Google Drive for Desktop. Covers installation, offline caching, permission matrices, API integration, and enterprise workflows.
- **Offline-First API Sync: Queues, Retries & Conflicts (2026)** — <https://apiscout.dev/guides/building-offline-first-apps-api-sync-2026>
  Mar 8, 2026 · Design offline-first API sync with a durable outbox, idempotent writes, retries, delayed validation, conflict policy, checkpoints, and recovery UX.
- **Google Drive API overview | Google for Developers** — <https://developers.google.com/workspace/drive/api/guides/about-sdk>
  Sep 3, 2026 · Explore the features of the Google Drive API that you can use to integrate apps with Google Drive cloud storage and the Drive UI.

## F5.05-obs-backoff

**Consulta:** `obs-websocket protocol v5 reconnect exponential backoff identify hello authentication`


_Backend: auto_

- **GitHub - LinuxMainframe/libwsv5: C library for OBS Studio ...** — <https://github.com/LinuxMainframe/libwsv5>
  Oct 17, 2025 · A high-performance C library for communicating with OBS Studio via the WebSocket v5 protocol. Designed for streaming professionals, developers, and automation systems that need reliable control over OBS in
- **OBS WebSocket Protocol Version | obs-websocket-community ...** — <https://deepwiki.com/obs-websocket-community-projects/obs-websocket-java/9.3-obs-websocket-protocol-version>
  Dec 8, 2025 · This Java client library implements the OBS WebSocket v5 protocol with RPC Version 1, requiring OBS Studio 27+ and OBS WebSocket plugin 5.0+. Version validation occurs automatically during the WebSocket han
- **Remote Control Guide - OBS** — <https://obsproject.com/kb/remote-control-guide>
  Apr 18, 2023 · As such, there should be no need to download obs-websocket if you have the latest version of OBS Studio. However, you can download obs-websocket for older releases of OBS Studio. It is highly recommended t

## F1.05-atajos-json

**Consulta:** `desktop app customizable keyboard shortcuts json persistence schema best practices`


_Backend: auto_

- **GitHub - michaelzrork/BagOfHolding: Desktop inventory ...** — <https://github.com/michaelzrork/BagOfHolding>
  Oct 20, 2025 · BagOfHolding Desktop inventory management app with CustomTkinter GUI, JSON data persistence, and keyboard shortcuts
- **Custom Keyboard Shortcuts for JSON Formatting | React and ...** — <https://offlinetools.org/a/json-formatter/custom-keyboard-shortcut-integration-for-json-formatting>
  Implement JSON formatting hotkeys in a web editor with practical React examples, safer shortcut choices, IME handling, accessibility guidance, and troubleshooting.
- **Data Flow and Persistence | r3-yamauchi/kintone-set-keyboard ...** — <https://deepwiki.com/r3-yamauchi/kintone-set-keyboard-shortcuts-plugin/3.5-data-flow-and-persistence>
  This diagram shows the complete journey of shortcut settings from administrator input to runtime application. Data passes through multiple transformations and representations, with static definitions providing the schema
- **JSON Best Practices: Design, Naming & Structure (2026)** — <https://jsonwebtools.com/json-best-practices>
  Apr 5, 2026 · Use JSON Schema validation to reject payloads with unexpected keys, and keep your Node.js and library versions current. In server-side languages, sanitize user-supplied key names before using them as proper
- **Complete Guide to Customizing Keyboard Shortcuts in Visual ...** — <https://devgex.com/en/article/00040628>
  Abstract: This article provides an in-depth exploration of the complete process for customizing keyboard shortcuts in Visual Studio Code, covering remapping shortcuts for both built-in commands and extension commands.

## F6.01-metricas

**Consulta:** `application performance counters instrumentation flush interval desktop rendering metrics`


_Backend: auto_

- **Azure Monitor Logs reference - AppPerformanceCounters - Azure ...** — <https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/appperformancecounters>
  Jul 28, 2026 · Reference for AppPerformanceCounters table in Azure Monitor Logs.
- **GitHub - AppMetrics/AppMetrics: App Metrics is an open-source ...** — <https://github.com/AppMetrics/AppMetrics>
  App Metrics abstracts away the underlaying repository of your Metrics for example InfluxDB, Graphite, Prometheus etc, by sampling and aggregating in memory and providing extensibility points to flush metrics to a reposit
- **Top 15 Application Performance Monitoring Metrics in 2026** — <https://www.atatus.com/blog/top-apm-metrics-for-developers-and-sres/>
  Jan 30, 2026 · Track the top APM metrics to improve application performance, reduce latency, and gain real-time visibility using a powerful APM tool like Atatus.
- **Windows Performance Counters | TestComplete Documentation** — <https://support.smartbear.com/testcomplete/docs/testing-with/advanced/monitoring-performance/counters/windows.html>
  Sep 1, 2026 · Create and run automated tests for desktop, web and mobile (Android and iOS) applications (.NET, C#, Visual Basic .NET, C++, Java, Delphi, C++Builder, Intel C++ and many others).

## F0.03-net-deteccion

**Consulta:** `detect .NET Framework versions registry NDP Release key 4.8 461808 read only installer`


_Backend: auto_

- **Determine which .NET Framework versions are installed - .NET ...** — <https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed>
  Oct 20, 2025 · Use code, regedit.exe, or PowerShell to detect which versions of .NET Framework are installed on a machine by querying the Windows registry. Or, check Control Panel.
- **How to Get .NET Version Using PowerShell?** — <https://powershellfaqs.com/get-net-version-using-powershell/>
  Oct 29, 2025 · Learn how to get the .NET version using PowerShell with simple commands and examples. Quickly check installed .NET Framework or .NET Core versions on your system.
- **Detecting Installed .NET Framework Versions and Service Packs** — <https://devgex.com/en/article/00015547>
  Nov 21, 2025 · Abstract: This article provides a comprehensive guide on detecting .NET Framework versions and service packs using registry keys, with code examples in C# and PowerShell, and discussion on version dependen
- **How can I check .Net Framework 4.8 Installed on machine** — <https://stackoverflow.com/questions/71205052/how-can-i-check-net-framework-4-8-installed-on-machine>
  Feb 21, 2022 · Alternatively, if, for whatever reason, you need to check for the presence of .NET Framework 4.8 after starting your application, you can look at the Release value of the following registry key: A value of

---

**Resumen:** 32 resultados únicos por dominio en 8 consultas.

## Adaptaciones aplicadas al encargo (trazabilidad)

- **F6.06**: instalador con `ArchitecturesInstallIn64BitMode=x64compatible`,
  detección previa de arquitectura por el lanzador, variante única instalada.
- **F5.09**: el sandbox JSLib acota CPU/memoria/conexiones con presupuesto
  por script y sin acceso a disco fuera del proyecto [SPEC §8.6].
- **F4.15**: el informe de fidelidad enumera unidades convertidas, herencia
  resuelta, omitidos, macros y rutas re-vinculadas (estructura ya normada).
- **F6.04**: Drive offline-primero; resolución de conflictos por versión
  `ahp.v1` + historial consultable; sin pérdida silenciosa.
- **F5.05**: OBS WebSocket v5 con `Hello→Identify` (challenge SHA256
  base64) y reconexión con backoff exponencial acotado.
- **F0.03**: NDP `Release` por clave (461808 = 4.7.2+, 528040+ = 4.8) solo
  lectura — coincide con Bootstrap.cpp del repo.