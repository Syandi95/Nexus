# [Fase 8] Native WPF Host Application (Nexus.exe) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Membangun WPF Application Native (.NET 8 Windows) `Nexus.App.csproj` yang menghasilkan `Nexus.exe` sebagai host utama aplikasi menggantikan host PowerShell (`ManajemenFile.ps1`), dengan 100% paritas perilaku UI/UX, Clean MVVM architecture, 0 warning/error, dan 0 regresi.

**Architecture:** 
- `App.xaml.cs`: Application Composition Root & Dependency Injection configuration (`IServiceProvider`).
- `MainWindow.xaml` & `MainViewModel.cs`: Navigation Shell, Sidebar, & Tab Hosting.
- `ViewModels/`: `HomeViewModel`, `ConversationViewModel`, `EditingLiteViewModel`, `FileUtilitiesViewModel`, `ImageSubsystemViewModel`, `PdfUtilitiesViewModel`, `SettingsViewModel`.
- Business Logic strictly resides inside C# libraries (`Nexus.Foundation`, `Nexus.Conversation`, `Nexus.AI.EditingLite`, `Nexus.FileUtilities`, `Nexus.AI.Llama`, `Nexus.Engine.ImageSubsystem`, `Nexus.PdfUtilities`, `Nexus.Core.HomeSettings`).

**Tech Stack:** C# 12 / .NET 8 WPF (`net8.0-windows`), `Microsoft.Extensions.DependencyInjection`, xUnit.

---

## Directives & Constraints

* ❌ **BUKAN FASE MODERNISASI UI**: Tidak ada redesign atau perubahan UX. Mengikuti tampilan dan alur `ManajemenFile.ps1` 1:1.
* ❌ **DILARANG MENGGUNAKAN SERVICE LOCATOR**: Dependency Injection dilakukan secara bersih pada startup di `App.xaml.cs`.
* 📌 **MAINWINDOW SEBAGAI COMPOSITION ROOT**: `MainWindow` hanya menjadi container/host dan tidak boleh berisi logika bisnis.
* 📌 **VIEWMODEL SEBAGAI JEMBATAN**: Communication dilakukan dari View $\rightarrow$ ViewModel $\rightarrow$ C# Services. View tidak boleh memanggil Service/Engine secara langsung.
* 📌 **SOURCE POWERSHELL TETAP DIPERTAHANKAN**: Skrip PowerShell lama tetap berada di tempatnya sebagai referensi baseline.

---

## TASK DECOMPOSITION & PLAN STEPS

### Task 1: Scaffolding Proyek `Nexus.App` (`net8.0-windows` WPF App)
- Create `Nexus-CSharp/src/03_App/Nexus.App/Nexus.App.csproj`
- Add project references to all 8 C# Class Libraries.
- Register `Nexus.App.csproj` to `Nexus.sln`.

### Task 2: Configure Application Composition Root & Dependency Injection
- Implement DI Container setup in `App.xaml.cs` registering all Foundation, Module, and ViewModel services.
- Configure theme loader (Dark/Light palette switching).

### Task 3: Implement Main Navigation Shell & ViewModels
- Implement `MainViewModel.cs` handling active tab switching (`Home`, `Conversation`, `EditingLite`, `FileUtilities`, `ImageSubsystem`, `PdfUtilities`, `Settings`).
- Implement `MainWindow.xaml` matching the sidebar layout and module header of `ManajemenFile.ps1`.

### Task 4: Implement Module ViewModels & Views with Feature Parity
- Implement `HomeView` & `HomeViewModel` (Dashboard activity & status).
- Implement `ConversationView` & `ConversationViewModel` (AI chat & model selector).
- Implement `FileUtilitiesView` & `FileUtilitiesViewModel` (Clean Downloads & Rapikan File).
- Implement `PdfUtilitiesView` & `PdfUtilitiesViewModel` (PDF Merge/Split & Conversion).
- Implement `ImageSubsystemView` & `ImageSubsystemViewModel` (Remove Background & Model Slots).
- Implement `SettingsView` & `SettingsViewModel` (Theme, Downloads Path, Reset).

### Task 5: Build, Test, Integration Verification, and Manual Regression Check
- Execute `dotnet build Nexus.sln` (0 Warning, 0 Error).
- Execute `dotnet test Nexus.sln` (100% PASS).
- Verify `Nexus.exe` startup, navigation, and service interaction.
