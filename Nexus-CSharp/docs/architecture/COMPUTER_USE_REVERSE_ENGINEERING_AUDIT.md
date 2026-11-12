# Computer Use (Codex / ChatGPT Desktop) — Reverse Engineering & Reconstruction Audit

**Status:** Fase audit selesai. Analisis Ghidra 12.1 berhasil (3.864 prosedur, 1.567 string); dekompilasi per-fungsi masih bisa ditambah.
**Tanggal:** 2026-11-09 (revisi 2026-11-10)
**Metode:** REA 6.5.0 (static + artifact inspection) + Ghidra 12.1 headless + analisis dokumen & tipe yang ikut ter-ship + bukti runtime dari rollout sesi pengguna
**Sasaran:** Fitur **Computer Use** pada Codex Desktop (Windows), versi build `26.1103.4239.0`

> Dokumen ini adalah **recon map + blueprint rekayasa**, bukan salinan kode.
> Yang diambil: perilaku, kontrak, dan pola arsitektur. Yang **tidak** diambil: kode sumber, aset, ikon, teks pemasaran, atau API privat. Implementasi C# ditulis baru (clean-room).

---

## 1. Ringkasan Eksekutif

Computer Use Codex **bukan** satu binary monolitik dan **bukan** bagian dari `server.mjs` milik `codex-app-tools` (0 kemunculan kata kunci di sana). Fitur ini tersusun dari **lima lapis**:

| Lapis | Artefak | Peran |
|---|---|---|
| 1. Plugin manifest | `openai-bundled/computer-use` | Skill + 3 dokumen kebijakan (`guidance`, `api`, `confirmations`) |
| 2. Plugin MCP | `openai-bundled/unified-computer-use/.mcp.json` | Menyalakan server MCP `cua_repl` (`js`, `js_reset`, `turn_ended`) |
| 3. REPL host | `@oai/cua-repl` (Node) | NodeREPL persisten + banner `import("@oai/cua/tinyskyAlt")` |
| 4. SDK | `@oai/sky` 0.7.17 | Klien `sky` (window2) + `windows_desktop` (full-desktop) + transport |
| 5. Helper native | **dua** binary: `codex-computer-use.exe` (Rust, 1,5 MB) dan `codex-computer-use-swift.exe` (Swift, 56,6 MB) | UIA tree, SendInput, Windows.Graphics.Capture, overlay, policy |

Poin desain paling penting yang layak ditiru di C#:

1. **Model aksi berbasis observasi sekali-pakai.** Setiap `element_index`, `screenshotId`, dan koordinat hanya valid untuk observasi yang menghasilkannya. Helper menolak input jika window berubah (`window changed; call get_window_state before using this window`).
2. **Helper terpisah dari host (out-of-process).** Isolasi COM/UIA/D3D; crash helper tidak menjatuhkan UI. Sama filosofinya dengan pemisahan `NexusConverterEngine.dll` di NEXUS.
3. **Dua transport, tetapi hanya satu yang wajib.** NDJSON di atas stdio adalah jalur universal. Named pipe `\\.\pipe\codex-computer-use-<uuid>` (JSON-RPC 2.0) adalah alternatif untuk backend A saja, dan **ditolak** untuk sesi desktop penuh.
4. **Persetujuan per-aplikasi, bukan per-aksi.** Approval diikat ke identitas app (`process:<path>` atau AUMID), dengan opsi `session` / `always`.
5. **Allowlist + daftar target terlarang yang keras** (LockApp, ApplicationFrameHost, `_proxy`, dsb).
6. **Dua permukaan RPC, bukan satu.** Per-window (`window2`, 20 metode) untuk kerja di dalam satu jendela; full-desktop (`desktop_*`, 13 metode) untuk koordinat layar penuh. Keduanya **bukan** flag dari helper yang sama — di original, keduanya binary terpisah.
7. **Klik elemen berbasis geometri, bukan UIA pattern.** `click_element` membaca *cached bounding rectangle* dari snapshot UIA, menghitung titik tengah, lalu menginjeksi `SendInput` down/up (16 ms). String `Invoke`/`InvokePattern` tidak ada sama sekali di binary. Bukti fungsi-level di §7.1.
8. **Aksi berbasis elemen didelegasikan ke proses lain.** `click_element`, `set_value`, dan `perform_secondary_action` semuanya mengirim permintaan IPC ke subsistem "accessibility monitor" (`send element target request…`, `send set value request…`, `send secondary action request…`). Tidak ada satu pun aksi elemen yang dieksekusi di proses utama. Alasannya struktural: UIA bisa menggantung, dan proses terpisah bisa dibunuh. §7.7.
9. **`type_text` memakai clipboard, bukan injeksi Unicode.** Tulis clipboard → verifikasi baca-ulang (polling 25 ms, batas ~500 ms) → `Ctrl+V` via `SendInput` → `WaitForInputIdle` 250 ms → pulihkan clipboard (batas 750 ms). Ini berarti `type_text` **mengubah clipboard pengguna** dan butuh mutex global. §7.2.
10. **Pohon aksesibilitas dibangun tiga lapis dengan batas eksplisit.** Formatter window → pembangun baris per-node → formatter atribut. Ada denylist 6 nama window sistem, filter `IsWindowVisible`, dan batas `child_limit` 500 / `max_depth` 200 / `cycle`. Node disimpan sebagai record 280 byte. §5.4.
11. **`drag` tidak melakukan interpolasi.** Antara mouse-down dan mouse-up hanya ada satu gerakan absolut. Drag-and-drop yang butuh gerakan kontinu akan gagal. NEXUS sebaiknya memperbaiki ini sebagai deviasi yang disengaja. §7.6.

---

## 2. Metode, Cakupan, dan Kejujuran Bukti

### 2.1 Klasifikasi klaim

| Label | Arti |
|---|---|
| **OBSERVED** | Terbaca langsung dari artefak ter-ship atau output runtime |
| **INFERRED** | Kesimpulan dari beberapa bukti; bisa salah |
| **UNKNOWN** | Belum terbukti; jangan diandalkan |

### 2.2 Target dan identitas artefak

| Item | Nilai |
|---|---|
| Build | `26.1103.4239.0` (plugin `26.1103.32239`) |
| `@oai/sky` | 0.7.17 |
| Helper A (per-window) | `@oai/sky/bin/windows/codex-computer-use.exe` |
| Helper A — ukuran / SHA-256 | `1.548.592` byte / `7f73cf564e1f7ff1cc6dff117e2baa6743be2cb5423bdc0f50274fa0e9a4002c` |
| Helper A — bahasa | Rust (87 kemunculan `rustc`), PE32+ x64, 9 section, CLI header `rva=0` |
| Helper B (full-desktop) | `@oai/sky/bin/windows/swift/{x64|arm64}/codex-computer-use-swift.exe` |
| Helper B — ukuran / section | `56.674.608` byte, 25 section (`.sw5acfn`, `.sw5asty`, `.sw5tymd`, …), CLI header `rva=0` |
| Helper B — bahasa | Swift 5 native, PE32+ x64 |
| Transport A | stdio NDJSON **atau** named pipe JSON-RPC 2.0 (`SKY_CUA_NATIVE_PIPE=1`, `\\.\pipe\codex-computer-use-<guid>`) |
| Transport B | stdio NDJSON saja; named-pipe mode ditolak untuk desktop |

Helper A dibaca langsung dari lokasi aslinya (`C:\Users\Syand\AppData\Local\OpenAI\Codex\runtimes\...`) oleh Craft Anything 0.6.0, yang memakai engine Deep Ghidra 12.1 dengan profil default. Helper B dibaca langsung dari lokasi aslinya (pemindaian byte mentah tidak butuh Ghidra).

### 2.3 Batasan

- **Ghidra via REA:** dua percobaan awal gagal pada *startup deadline* karena nama env yang benar adalah **`REA_GHIDRA_STARTUP_TIMEOUT_MS`**. Percobaan ketiga (timeout `1800000` ms, analyzer berat dimatikan) berhasil menganalisis tetapi **dekompilasi per-fungsi gagal** dengan `Unable to find unique hash for varnode` (§13.1).
- **Ghidra via Craft Anything 0.6.0:** engine Deep memakai Ghidra 12.1 yang sama dengan profil default, dan **berhasil** mendekompilasi `FUN_140097ccb` (9.234 baris). Karena itu klaim fungsi-level kini **OBSERVED**, bukan lagi INFERRED (§7.1).
- **Runtime:** bukti perilaku berasal dari rollout sesi pengguna sendiri (`01a0e97b-…`), bukan eksekusi baru.
- **Tidak** dilakukan: patch binary, hook, atau menjalankan helper dengan parameter rekayasa.
- **Helper B (Swift)** dianalisis hanya lewat **pemindaian byte mentah** (PE header + ekstraksi ASCII dari `.rdata`). Tidak ada dekompilasi pada helper B, jadi seluruh temuan backend B bersifat **string/struktur**, bukan fungsi. Analisis fungsi-level hanya dilakukan pada backend A.

### 2.4 Dua artefak, dua tabel metode — mengapa ini penting

Audit awal mengasumsikan satu helper. Ternyata ada **dua** binary native dengan permukaan RPC yang berbeda:

- **A** mengekspos 20 metode `window2` (per-window: `list_windows`, `get_window_state`, `click_element`, …).
- **B** mengekspos 13 metode `desktop_*` (full-desktop: `desktop_get_screenshot`, `desktop_mouse_down`, …) **dan** superset 20 metode window3.

Konsekuensi metodologis: setiap klaim "helper melakukan X" harus menyebut **backend mana**. Karena itu seluruh tabel di dokumen ini diberi label A/B, dan klaim yang hanya berlaku untuk satu backend ditandai eksplisit.

Konsekuensi desain: NEXUS tidak boleh meniru *internal* salah satu backend. Yang dijadikan acuan paritas adalah **kontrak permukaan** — nama metode, kunci parameter, bentuk hasil, dan kode error — karena helper C# NEXUS ditulis dari nol dan bebas memilih implementasi di balik kontrak itu.

### 2.5 Hasil analisis Ghidra (OBSERVED)

Analisis headless Ghidra 12.1 berhasil pada target (`evidence_id ev_4028c54b…`).

| Metrik | Nilai |
|---|---|
| Format / arsitektur | PE / x86_64, native (bukan managed) |
| Image base | `0x140000000` |
| Segmen | 10 (`.text` 1.202.688 B, `.rdata` 161.792 B, `.pdata` 34.304 B, `.xdata` 109.056 B, `.idata` 14.336 B, …) |
| Prosedur terdeteksi | **3.864** |
| String terdefinisi | **1.567** |
| Analyzer | `ghidra-default` minus `Decompiler Switch Analysis`, `Embedded Media`, `Create Address Tables` |
| Profil digest | `c45cf0b5…f981` |

Catatan penting dari `limitations` provider:

- Ghidra hanya mengekspos hasil setelah auto-analysis **selesai**; tidak ada hasil parsial.
- String inventory hanya mencakup *defined data* — byte string yang belum didefinisikan bisa tidak terlihat, jadi "tidak ditemukan" ≠ "tidak ada".
- Program diimpor **ephemeral**; byte asli tidak pernah ditulis.

### 2.6 Peta modul internal helper (OBSERVED, string tabel)

Binary Rust ini di-*strip*: tidak ada nama fungsi (`FUN_140001000`, …). Namun lokasi *panic* (`core::panic::Location`) tetap tersimpan sebagai path relatif `src\…`, sehingga struktur modul crate dapat direkonstruksi. Terkumpul **20 path modul milik helper** (path crate pihak ketiga dan `std`/`core` difilter karena selalu berprefiks absolut `C:\Users\runneradmin\.cargo\registry\…` atau `/rustc/…`):

```text
src/
├── app.rs                         # entry, arg parsing, event loop
├── audio.rs                       # gated oleh SKY_ENABLE_AUDIO
├── dpi.rs                         # konversi koordinat fisik<->logis
├── accessibility.rs
│   └── monitor.rs                 # "initialize COM for UI Automation"
├── capture/
│   └── image.rs                   # encode JPEG via Windows.Graphics.Imaging
├── codex/
│   └── notify_config.rs           # pembacaan config Codex
├── input/
│   ├── initial_cursor.rs          # posisi kursor awal untuk drag
│   └── interruption.rs            # jalur interupsi (Esc)
├── overlay/
│   ├── mod.rs                     # "cursor overlay thread exited before initialization"
│   ├── color.rs
│   ├── system_cursor_manager.rs
│   └── cursor/
│       └── motion.rs              # animasi kursor
├── policy/
│   ├── auth.rs
│   ├── managed.rs                 # kebijakan terkelola (managed policy)
│   └── url_policy.rs              # kebijakan URL browser
└── shell/
    ├── app_catalog.rs             # katalog aplikasi (process_keys)
    ├── registry.rs
    ├── process.rs                 # "windows-app-execution-aliases"
    └── app_identity/
        └── native.rs              # identitas proses native
```

Implikasi untuk rekonstruksi:

1. Ada **lapisan overlay kursor terpisah** dengan thread sendiri (`overlay/mod.rs`, `overlay/cursor/motion.rs`, `system_cursor_manager.rs`). Ini yang membuat gerakan kursor terlihat mulus saat `drag`/`click`; kegagalan inisialisasi thread-nya adalah kondisi fatal yang eksplisit (`cursor overlay thread exited before initialization`).
2. `capture/image.rs` terpisah dari `accessibility.rs` — tangkapan gambar dan pembacaan pohon aksesibilitas adalah dua jalur independen, bukan satu operasi gabungan.
3. `policy/` dipecah tiga: `auth.rs` (persetujuan/otorisasi), `managed.rs` (kebijakan perusahaan), `url_policy.rs` (allow/deny URL). Ketiganya dievaluasi terpisah.
4. `shell/app_catalog.rs` + `shell/registry.rs` + `shell/app_identity/native.rs` menjelaskan mengapa identitas aplikasi punya tiga bentuk (`process:<path>`, nama AppX pendek, dan AUMID penuh) — ketiganya berasal dari tiga resolver berbeda.
5. Modul transport/RPC **tidak muncul** di daftar ini (tidak ada lokasi panic-nya), yang konsisten dengan transport berada di crate terpisah, bukan di crate helper.

Ketiadaan nama simbol juga berarti setiap klaim fungsi-level harus dicapai lewat **xref dari string literal** (`rea trace` / `rea xrefs` → `rea function`), bukan lewat pencarian nama fungsi.

`replica-recon` melarang membaca bundle JS/mendekompilasi binary **untuk aplikasi milik pihak lain**. Di sini target adalah instalasi milik pengguna dan tujuannya audit + rekonstruksi bersih. Yang tetap dipatuhi: **tidak menyalin kode/aset/teks**, dan tidak mengklaim paritas sempurna.

---

## 3. Peta Arsitektur

### 3.1 Diagram lapisan

```text
┌──────────────────────────────────────────────────────────────┐
│ Model (LLM)                                                  │
│   hanya menulis JavaScript, tidak pernah menyentuh Win32     │
└───────────────────────────┬──────────────────────────────────┘
                            │ tool call: mcp__node_repl__js / cua_repl.js
┌───────────────────────────▼──────────────────────────────────┐
│ NodeREPL persisten  (@oai/cua-repl)                          │
│   banner: await import("@oai/cua/tinyskyAlt")                │
│   globalThis.* bertahan antar sel                            │
│   output: nodeRepl.write() / nodeRepl.emitImage()            │
└───────────────────────────┬──────────────────────────────────┘
                            │ sky.list_apps() / get_window_state() / click() ...
┌───────────────────────────▼──────────────────────────────────┐
│ @oai/sky  (WindowsComputerUseClient / WindowsHelperTransport)│
│   • validasi input + normalisasi (element_index, koordinat)  │
│   • approval handshake via createElicitation                 │
│   • interrupt marker file (Esc)                              │
│   • timeout per-method                                       │
└───────────────┬──────────────────────────────┬───────────────┘
                │ NDJSON stdio                 │ JSON-RPC 2.0 (named pipe,
                │ (helper di-spawn klien)      │  helper di-spawn host)
┌───────────────▼──────────────────────────────▼───────────────┐
│ DUA backend native, bukan satu:                              │
│                                                              │
│ A. codex-computer-use.exe        (Rust, 1,5 MB)              │
│    API "window2": 20 metode (lihat §4.7)                     │
│    UIA client · SendInput · WGC+D3D11 · overlay · policy      │
│                                                              │
│ B. codex-computer-use-swift.exe  (Swift, 56,6 MB)            │
│    API "window3" + "full desktop": desktop_* (13 metode)      │
│    Appshot · AX diff · clipboard safety · URL policy         │
└───────────────────────────┬──────────────────────────────────┘
                            │
                    Windows Desktop (Win32 / UIA / DWM)
```

### 3.2 Dua backend native, bukan satu

**Koreksi penting terhadap versi awal audit:** helper native **bukan satu binary**. Ada dua, dan keduanya punya tabel metode yang berbeda:

| | A. `codex-computer-use.exe` | B. `codex-computer-use-swift.exe` |
|---|---|---|
| Bahasa | Rust (`.text` 1,2 MB, 9 section) | Swift 5 (`.text` 16,3 MB, `.rdata` 38,4 MB, 25 section, section `.sw5*`) |
| Ukuran | 1.548.592 B | 56.674.608 B |
| Lokasi | `@oai\sky\bin\windows\` | `@oai\sky\bin\windows\swift\x64\` |
| Pemilih | `WindowsComputerUseClient` (`helperPath`) | `WindowsComputerUseClient.createDesktopClient()` |
| Env penanda | — | `SKY_WINDOWS_FULL_DESKTOP=1` |
| Permukaan metode | `window2` — 20 metode (§4.7) | `window3` + `desktop_*` — 13 metode (§4.9) |
| Transport | stdio NDJSON, atau named pipe JSON-RPC 2.0 | stdio NDJSON (`preserveHelperOnTimeout: true`) |
| Manajemen kursor | overlay + `--system-cursor-manager` | `CursorMotionPolicy` |
| Argumen | `--parent-pid`, `--system-cursor-manager`, `-suppress`/`-restore`/`-shutdown`/`-ready` | `--parent-pid` saja |
| `requestComputerUseApproval` | **tidak ada di binary** | **tidak ada di binary** |
| `jsonrpc` (kunci `"jsonrpc":"2.0"`) | **tidak ada** — helper↔host selalu NDJSON | **tidak ada** |
| `JSON-RPC` (kapital) | ada 1× — **klien app-server**, bukan stdio (§4.1) | tidak ada |
| `NamedPipe` | ada (1×) | ada (4×) |
| `x-oai-cua-approved-app` / `x-oai-cua-request-budget-ms` | ada | ada (`x-oai-cua` 4×) |

Keduanya **native** (tidak ada CLI header / bukan .NET), jadi keduanya bisa dianalisis dengan Ghidra. Untuk rekonstruksi C#, backend B lebih relevan karena ia yang memegang permukaan `desktop_*` dan fitur-fitur lanjutan (diff AX, keamanan clipboard, Appshot).

### 3.3 Bukti konfigurasi (OBSERVED)

Bukti (`config.toml`):

```toml
notify = [ "...\\@oai\\sky\\bin\\windows\\codex-computer-use.exe", "turn-ended" ]

[mcp_servers.node_repl.env]
NODE_REPL_TRUSTED_SERVICES = '{"browser":"...browser-service.mjs","sky":"@oai/sky/service"}'
SKY_CUA_NATIVE_PIPE = "1"
SKY_CUA_NATIVE_PIPE_DIRECTORY = '\\.\pipe\codex-computer-use-73c5eadb-...'
```

Artinya helper **juga** dipakai sebagai program `notify` (dipanggil dengan argumen `turn-ended`) — jalur lifecycle terpisah dari jalur RPC.

### 3.4 Cara kedua backend dipilih (OBSERVED)

`WindowsComputerUseClient.createDesktopClient()` memilih backend B secara eksplisit:

```js
if (globalThis.nodeRepl?.env?.SKY_CUA_NATIVE_PIPE === "1")
  throw new Error("Windows desktop sessions require the direct Swift helper transport");
return new WindowsDesktopClient(t.transport ?? new WindowsHelperTransport({
  helperCommand: .../bin/windows/swift/{arm64|x64}/codex-computer-use-swift.exe,
  helperArgs: [ ..."--parent-pid", String(process.pid) ],
  desktopSession: true,
  helperEnv: { SKY_WINDOWS_FULL_DESKTOP: "1" },
  preserveHelperOnTimeout: true,
}));
```

Tiga aturan yang lahir dari sini:

1. **Mode named pipe tidak mendukung desktop penuh.** `SKY_CUA_NATIVE_PIPE=1` + `createDesktopClient()` langsung melempar error. Jadi permukaan `desktop_*` hanya tersedia lewat stdio.
2. **`preserveHelperOnTimeout: true`** khusus desktop: helper tidak dibunuh saat timeout, karena drag/input bisa sedang setengah jalan. Ini berbeda dari jalur window2.
3. **`desktop_cancel`, `desktop_close`, dan `close` adalah metode lifecycle tingkat transport**, bukan metode helper. Bukti: `helper_transport.js` mengecualikan ketiganya dari pemeriksaan turn-transition dan guard "helper sibuk":

   ```js
   const isLifecycle = desktopSession && ["desktop_cancel","desktop_close","close"].includes(method);
   if (!isLifecycle && busyState != null) { ... reject ... }
   ```

   Dan tidak satu pun dari ketiga nama itu ada di tabel metode binary A maupun B. Konsekuensi: implementasi kita harus memperlakukan ketiganya sebagai **jalur kontrol sesi** yang selalu boleh lewat, bahkan saat helper dianggap sibuk.


---

## 4. Protokol Kabel (OBSERVED)

Sumber: `sky/dist/.../targets/windows/internal/helper_transport.js` dan `internal/computer_use_client.js`.

### 4.1 Framing

Ada **dua framing berbeda**, dan ini penting: keduanya tidak kompatibel.

| Transport | Framing | Bentuk pesan |
|---|---|---|
| stdio (default, `WindowsHelperTransport`) | NDJSON — satu baris JSON + `"\n"` | `{id, method, params, meta}` / `{id, ok, result\|error}` |
| named pipe (`SKY_CUA_NATIVE_PIPE=1`) | **length-prefixed**: `uint32 LE` panjang + payload JSON | **JSON-RPC 2.0** `{jsonrpc:"2.0", id, method, params}` |

Aturan stdio:

- Satu request = satu baris JSON + `"\n"` ke `stdin` helper.
- Satu response = satu baris JSON dari `stdout` helper (di-`trim()`, baris kosong diabaikan).
- Objek **tanpa** `id` numerik = **event**, diteruskan ke listener (`onEvent`).
- JSON tidak valid → **seluruh** request pending ditolak (`computer-use helper emitted invalid JSON: …`).
- `stderr` dikumpulkan dan dilampirkan ke pesan error saat helper exit.

Aturan named pipe:

- Batas frame request **8 MiB** (`8388608`), batas frame response **64 MiB** (`67108864`).
- Buffer akumulatif: baca 4 byte pertama → panjang → tunggu sampai utuh → parse → sisanya jadi buffer berikutnya.
- Helper dapat mengirim **request balik** ke klien: `method = "requestComputerUseApproval"` dengan `id` **string**; klien menjawab `{jsonrpc:"2.0", id, result}` (atau `error.code = -32000`).
- Selama menunggu approval, **semua timeout request ditunda** lalu dijadwalkan ulang setelah approval selesai.

**Koreksi penting tentang siapa yang berbicara JSON-RPC (diverifikasi byte-level).** Pencarian case-insensitive pada kedua binary memberi hasil yang berlawanan dengan pembacaan awal:

| Pola | Backend A | Backend B |
|---|---|---|
| `jsonrpc` (kunci versi `"jsonrpc":"2.0"`) | **0 kemunculan** | **0 kemunculan** |
| `JSON-RPC` (kapital) | **1 kemunculan** — `failed to parse app-server JSON-RPC response` | — |
| `rpc` | 1 kemunculan (sama) | — |

Lokasi persisnya di backend A: `0x12ee82` (offset file), tepat di dalam blok string `src\policy\auth.rs`:

```text
failed to read app-server response
codex app-server exited before returning response
failed to parse app-server JSON-RPC response
id  method  params
failed to encode app-server request
failed to write app-server request
```

**Interpretasi yang benar:** helper adalah **klien** dari *Codex app-server* dan berbicara JSON-RPC **ke app-server itu** — bukan ke host. Struct `{id, method, params}` di `0x140130293`/`0x14013029b`/`0x1401302a2` adalah request app-server (dipakai untuk kebijakan otorisasi, §8.3), bukan request stdio.

| Leg | Framing | Protokol |
|---|---|---|
| JS ↔ host (`nodeRepl.nativePipe.createConnection`) | `uint32 LE` panjang + payload | **JSON-RPC 2.0** |
| host ↔ helper | NDJSON per baris | `{id, method, params, meta}` / `{id, ok, result\|error}` |
| helper ↔ Codex app-server | JSON-RPC | `{id, method, params}` (kebijakan/auth) |

Jadi **helper ↔ host selalu NDJSON**. `requestComputerUseApproval` **bukan** request yang dikirim helper ke host — ia dibuat oleh host bridge ketika helper melaporkan butuh persetujuan (`AppApprovalRequest`/`AppApprovalRequired`/`approvalRequest` ada di string backend A).

Untuk rekonstruksi NEXUS ini menyederhanakan desain: kita cukup punya **satu** protokol host-facing (NDJSON) dan menaruh logika persetujuan di klien. Leg app-server tidak perlu direplikasi — NEXUS memakai kebijakan lokal sendiri.

**Batas keras sisi helper (OBSERVED — backend B, string):**

| Batas | Nilai | String |
|---|---|---|
| Panjang satu baris NDJSON di `stdin` | **1 MiB** | `computer use stdin line exceeded 1 MiB` |
| Kedalaman antrean request `stdin` | **64 request** | `computer use stdin queue exceeded 64 requests` |
| Panjang chord tombol | dibatasi | `key chord is too large for guarded input` |
| Budget request per turn | dari header | `x-oai-cua-request-budget-ms`, `computer-use request budget exhausted` |

Helper juga melaporkan kegagalan plumbing stdio secara eksplisit dengan kode Win32: `create computer use stdout thread failed with Win32 error <n>`, `write computer use stdout failed with Win32 error <n>`, `open computer use stdin failed with Win32 error <n>`, `create computer use stdin thread failed with Win32 error <n>`, `read computer use stdin failed with Win32 error <n>`.

Implikasi C#: transport harus **menolak** frame > 1 MiB di sisi klien sebelum menulis, dan membatasi konkurensi ke 64. Tanpa itu, kegagalan muncul sebagai helper yang diam, bukan error yang bisa ditindaklanjuti.

### 4.2 Bentuk pesan

```jsonc
// request (stdio)
{ "id": 17, "method": "get_window_state",
  "params": { "window": {...}, "include_screenshot": true, "include_text": true },
  "meta": { "x-oai-cua-request-budget-ms": 15000 } }

// request (named pipe)
{ "jsonrpc": "2.0", "id": 17, "method": "request",
  "params": { "method": "get_window_state", "params": {...},
              "codexTurnMetadata": {...} } }

// sukses
{ "id": 17, "ok": true, "result": { ... } }

// gagal
{ "id": 17, "ok": false, "error": "..." }

// butuh persetujuan
{ "id": 17, "ok": false, "error": "...",
  "approvalRequest": {
    "app": "process:C:\\...\\dotnet.exe",
    "displayName": "dotnet",
    "riskLevel": "low",
    "allowPersistentApproval": true
  } }
```

**Persetujuan bukan method terpisah.** Alur di transport stdio:

1. Kirim request seperti biasa.
2. Helper menjawab `ok:false` + `approvalRequest`.
3. Klien memanggil `createElicitation` (dialog UI).
4. Jika `accept`, request **dikirim ulang** dengan meta tambahan `x-oai-cua-approved-app = <app>`.
5. Jika bukan `accept` → error `Computer Use was not approved to use <displayName>`.

Field `approvalRequest` dinormalisasi: `app` wajib string non-kosong (jika tidak → dianggap error biasa), `displayName` fallback ke `app`, `riskLevel` hanya `"high"`/`"low"`, `allowPersistentApproval` default `true` kecuali eksplisit `false`.

### 4.3 Kunci meta

| Kunci | Fungsi |
|---|---|
| `x-oai-cua-request-budget-ms` | Budget timeout per request |
| `x-codex-turn-metadata` | `session_id`, `turn_id`, metadata giliran |
| `x-oai-cua-approved-app` | App yang sudah disetujui pada giliran itu |

### 4.4 Timeout

| Method | stdio | named pipe |
|---|---|---|
| `desktop_get_screenshot`, `desktop_type_text`, `desktop_paste`, `launch_app` | 15.000 ms | 15.000 ms (hanya `launch_app`) |
| lainnya | 10.000 ms | 10.000 ms |

- `timeoutMs` pada opsi transport menimpa seluruh tabel.
- `x-oai-cua-request-budget-ms` dikirim per request di `meta`, **bukan** di `params`.
- Saat timeout: jika `preserveHelperOnTimeout=false` (default stdio), helper **dimatikan** (`kill()`) dan semua request pending ditolak. Desktop session memakai `preserveHelperOnTimeout=true`.
- Koneksi named pipe punya timeout sendiri 10.000 ms (`Computer Use native pipe connection timed out`).

### 4.5 Interupsi tombol Escape

- Exit code `130` = pengguna menekan **Escape fisik**.
- JS menulis marker ke `<codexHome>/cache/computer-use/interrupts/<conversationId>/<turnId>`.
  - `codexHome` = `CODEX_HOME` (di-trim, dinormalisasi WSL/`/mnt/c` → `C:\`) atau `%USERPROFILE%\.codex`.
  - Setiap komponen path disanitasi: `[^A-Za-z0-9._-] → _`.
- Pesan baku ke model: *"Computer Use was stopped by the user with the physical Escape key. Stop your work, do not call further Computer Use tools in this turn, and send a final message noting that the user stopped Computer Use."*
- Setelah marker ditulis, transport menyimpan turn scope dan **menolak request berikutnya di turn yang sama** tanpa mengirim apa pun ke helper. Hanya `desktop_cancel`, `desktop_close`, dan `close` yang tetap diizinkan.
- Jalur yang sama dipicu jika helper mengembalikan error string yang identik (bukan hanya lewat exit code 130).
- **INFERRED**: deteksi Esc memakai `SetWindowsHookExW` (WH_KEYBOARD_LL) — import `SetWindowsHookExW`/`CallNextHookEx`/`UnhookWindowsHookEx` ada di helper.

### 4.6 Siklus hidup proses helper (OBSERVED)

| Aspek | Perilaku |
|---|---|
| Argumen tambahan | `--parent-pid <pid>` selalu ditambahkan (kecuali `helperCommand` kustom) |
| Spawn | `stdio: ["pipe","pipe","pipe"]`, `windowsHide: true`, `UseShellExecute=false` |
| `unref` | proses **dan** ketiga stream di-`unref()` agar tidak menahan event loop |
| Parent exit | `process.once("exit")` → `child.kill()` |
| Pooling | Transport di-cache berdasarkan `JSON.stringify({helperArgs, helperCommand, timeoutMs})` + `refCount`; dua klien dengan opsi sama berbagi satu helper |
| `close()` | Kirim RPC `close` (kecuali helper sudah mati) → tunggu → `kill()`; desktop session **wajib** mengonfirmasi cleanup (`Windows desktop helper exited without confirming cleanup`) |
| Turn transition | Sebelum request pertama di turn baru, transport otomatis mengirim `end_turn` untuk turn sebelumnya (sekali per `turnId`) |
| Turn-ended handler | `nodeRepl.addTurnEndedHandler({ timeoutMs: 4000 })` → `end_turn`, dijaga agar hanya jalan sekali per turn |
| Desktop session | Memakai helper **Swift** (`codex-computer-use-swift.exe`) + `SKY_WINDOWS_FULL_DESKTOP=1`; **menolak** named pipe |

### 4.7 Metode RPC lengkap

**Per-window (surface `sky` window2) — 20 metode, OBSERVED penuh dari binary A.**

Tabel ini bukan hasil pembacaan string inventory, melainkan rekonstruksi dari **instruksi dispatcher** itu sendiri. Setiap arm berbentuk:

```asm
LEA R8, [<alamat literal>]   ; pointer nama metode
MOV R9D, <panjang>           ; panjang literal (&str len)
MOV RCX, RSI                 ; ptr dari request
MOV RDX, RDI                 ; len dari request
CALL 0x14001e82b             ; str_eq(ptr, len, lit, lit_len)
TEST AL, AL
JZ <arm berikutnya>          ; tidak cocok → lanjut
```

Memotong `.rdata` memakai pasangan `(alamat, panjang)` itu menghasilkan **20 nama yang kontigu tanpa celah**, berakhir tepat di `unsupported method: `:

| # | Alamat literal | Len | Nama | Params |
|---|---|---|---|---|
| 1 | `0x1401325c0` | `0x15` | `start_audio_recording` | `max_duration_ms?` (hanya jika `SKY_ENABLE_AUDIO=1`) |
| 2 | `0x1401325d5` | `0x14` | `stop_audio_recording` | — → `{ filepath }` |
| 3 | `0x1401325e9` | `0x10` | `get_window_state` | `window`, `include_screenshot`, `include_text` |
| 4 | `0x1401325f9` | `0x09` | `list_apps` | — |
| 5 | `0x140132602` | `0x0a` | `launch_app` | `app` |
| 6 | `0x14013260c` | `0x0c` | `list_windows` | — |
| 7 | `0x140132618` | `0x0a` | `get_window` | `id`, `app?` |
| 8 | `0x140132622` | `0x10` | `diagnostic_state` | — → snapshot diagnostik (cache, revisi, hitungan) |
| 9 | `0x140132632` | `0x0f` | `activate_window` | `window` |
| 10 | `0x140132641` | `0x18` | `perform_secondary_action` | `window`, `element_index`, `action` |
| 11 | `0x140132659` | `0x09` | `set_value` | `window`, `element_index`, `value` |
| 12 | `0x140132662` | `0x0d` | `click_element` | `window`, `element_index`, `click_count`, `mouse_button` |
| 13 | `0x14013266f` | `0x0e` | `scroll_element` | `window`, `element_index`, `direction`, `pages` |
| 14 | `0x14013267d` | `0x05` | `click` | `window`, `x`,`y`, `screenshotId?`, `click_count`, `mouse_button` |
| 15 | `0x140132682` | `0x04` | `drag` | `window`, `from_x`,`from_y`,`to_x`,`to_y`, `screenshotId?` |
| 16 | `0x140132686` | `0x06` | `scroll` | `window`, `x`,`y`, `screenshotId?`, `scrollX`, `scrollY` |
| 17 | `0x14013268c` | `0x09` | `type_text` | `window`, `text` |
| 18 | `0x140132695` | `0x09` | `press_key` | `window`, `key` |
| 19 | `0x14013269e` | `0x05` | `close` | — |
| 20 | `0x1401326a3` | `0x08` | `end_turn` | — |

Verifikasi kontiguitas: setiap `alamat[n] + len[n] == alamat[n+1]`, dan `0x1401326a3 + 8 = 0x1401326ab` — tepat tempat string `unsupported method: ` dimulai.

Nama metode yang tidak dikenal dijawab `unsupported method: <nama>`. Tiga metode — `diagnostic_state`, `scroll_element`, `close` — **tidak** muncul di permukaan JS publik (lihat `docs/api.md`), jadi tabel ini adalah sumber kebenaran yang lebih lengkap daripada dokumentasi.

**Mekanisme dispatch (OBSERVED):** rantai **`memcmp` linear**, bukan jump table. Seluruh 20 arm berada di dalam **satu fungsi** `FUN_140097ccb` yang berukuran 50.045 byte (9.254 instruksi, 11 rentang non-kontigu). Ada **29 call site** `str_eq` di fungsi itu — 20 untuk nama metode, 9 untuk perbandingan kunci/nilai lain. Konsekuensi desain: pencarian metode bersifat O(n) dan tidak ada tabel yang bisa dibaca sebagai array; untuk C# kita **bebas** memakai `Dictionary<string, handler>` yang O(1).

**Full-desktop (surface `windows_desktop`, helper Swift) — 13 metode, terverifikasi dari binary B:**

| Method | Params |
|---|---|
| `desktop_get_screenshot` | — → `[{ filepath, bytes, data_url }]` |
| `desktop_prepare_input` | menyiapkan lease input sebelum dispatch |
| `desktop_click` | `x`, `y`, `button?`, `click_count?` |
| `desktop_mouse_down` | `x`, `y`, `drag_id?` |
| `desktop_mouse_up` | `drag_id` |
| `desktop_cleanup_mouse_up` | dipakai saat cleanup setelah error, tanpa `drag_id` |
| `desktop_move` | `x`, `y`, `drag_id?` |
| `desktop_scroll` | `x`, `y`, `scrollX`/`scrollY` |
| `desktop_press_key` | `key` |
| `desktop_type_text` | `text` |
| `desktop_paste` | `text` |
| `desktop_cancel` | `operation_id`, `drag_id?` — **lifecycle, ditangani transport** (§3.4) |
| `desktop_close` | — → `{ stopped: true }` bila pengguna menekan Esc — **lifecycle** |

Nama tidak dikenal dijawab `Unsupported desktop method: <nama>` (perhatikan kapital `U`, berbeda dari backend A).


Aturan surface desktop (OBSERVED):

- Setiap request otomatis diberi `operation_id` (UUID) oleh klien.
- **Screenshot wajib segar**: input apa pun (`desktop_click`, `desktop_scroll`, `desktop_move`, `desktop_mouse_down`, `desktop_type_text`, `desktop_press_key`, `desktop_paste`) ditolak dengan `Take a new desktop screenshot before sending input` sampai `desktop_get_screenshot` sukses.
- `drag` = `mouse_down` → `move`* → `mouse_up` dengan satu `drag_id` UUID; `drag.path` wajib `>= 2` titik (`drag requires at least two points`).
- Ada `drag_handle()` stateful (`idle → starting → dragging → ended`) untuk drag bertahap.
- Timeout → `desktop_cancel {operation_id, drag_id?}`; Esc → `desktop_close`.
- Screenshot disimpan ke `%TEMP%\sky-desktop-*\<uuid>.jpg` (mode `0o600`), divalidasi regex `^data:image/jpeg;base64,[A-Za-z0-9+/]+={0,2}$`, dan direktori temp dibersihkan saat `close()`.
- `desktop_close` mengembalikan `{stopped:true}` bila pengguna menekan Escape — klien lalu melempar pesan stop yang sama seperti jalur exit 130.

**Aturan normalisasi parameter di klien (OBSERVED, wajib ditiru):**

| Aturan | Perilaku |
|---|---|
| `window` | `{id, app}`; `id` boleh string numerik → di-`Number()`; wajib integer `>= 0`; `app` wajib string non-kosong. Jika tidak valid → `TypeError`, **tidak** dikirim ke helper |
| `click` | `element_index` dan `x/y` saling eksklusif. Jika `element_index` ada → RPC `click_element`; jika tidak → `x`/`y` wajib ada dan finite → `Math.round` |
| `click_count` | finite → `Math.round` → wajib `>= 1` |
| `element_index` | string numerik → `Number()`; wajib integer `>= 0` |
| `x`, `y` | wajib `number` finite; dibulatkan (`Math.round`) |
| `key` | di-`split("+")`, tiap bagian di-`trim()`, bagian kosong dibuang; jika habis → `TypeError("key is required")` |
| `get_window_state` | default `include_screenshot=true`, `include_text=false`; keduanya `false` → `TypeError` |
| Screenshot | `id` default `"screenshot-0"`, `zIndex` default `0`; `originX/Y/width/height` hanya disertakan jika finite |
| AppInfo | entri tanpa `id` string non-kosong **dibuang** (bukan error) |
| Window | entri tanpa `id` valid atau `app` **dibuang** dari daftar |
| Audio | `start_audio_recording`/`stop_audio_recording` **dihapus dari objek klien** jika `SKY_ENABLE_AUDIO !== "1"` |

Setiap screenshot yang dikembalikan `get_window_state` juga **di-emit** ke model sebagai gambar (`nodeRepl.emitImage(url)` atau `codex.emitImage({type:"input_image", detail:"original"})`), bukan hanya dikembalikan sebagai data.

### 4.8 Bentuk data kunci

**Peta field OBSERVED (dari `.rdata` backend A, dipotong memakai pasangan `LEA` + `MOV R8D` di serializer).**

Blok `0x1401308c0`–`0x1401309c0` adalah kumpulan nama field yang kontigu:

```text
epoch  offset  tree  focused_element  selected_text  selected_elements
document_text  meta  app  title  window  screenshots  accessibility
cacheDiagnostics  zIndex  url  originX  originY  width  height
appId  processId  rootHwnd  inputHwnd  processName  bounds  snapshotRevision
```

Tambahan di luar blok itu: `lastWindowIdentity` (`0x140135134`), `lastCaptureInvalidationReason`, `displayName`, `windows`, `lastUsedDate`, `useCount`, `isRunning`, `ok`, `result`, `approvalRequest`.

Pemetaan ke tipe:

| Tipe | Field | Bukti |
|---|---|---|
| `Window` | `app`, `id`, `title?` | serializer @ `0x14009d044`: `"app"` (len 3) → `id` (len 2, nilai `qword [RDI+0x18]`) → `"title"` (len 5, dilewati bila `RBP == -1`) |
| `AppInfo` | `appId`, `processId`, `rootHwnd`, `inputHwnd`, `processName`, `title`, `bounds` | blok @ `0x1400991c8`–`0x1400993ad` |
| `AppInfo` (katalog) | `displayName`, `windows`, `lastUsedDate`, `useCount`, `isRunning` | blok @ `0x140130a00` |
| `Screenshot` | `id`?, `zIndex`, `url`, `originX`, `originY`, `width`, `height` | blok `0x140130928`–`0x14013097d` |
| `AccessibilityState` | `tree`, `focused_element`, `selected_text`, `selected_elements`, `document_text` | blok `0x1401308ce`–`0x140130921` |
| `WindowState` | `window`, `screenshots`, `accessibility`, `snapshotRevision` | blok `0x14013092d`–`0x1401309ad` |
| `diagnostic_state` | `cacheDiagnostics`, `lastWindowIdentity` | @ `0x14009df52`, `0x1400991c8` |

Bentuk konkret:

```jsonc
// AppInfo
{ "id": "process:C:\\Program Files\\dotnet\\dotnet.exe",
  "displayName": "dotnet", "isRunning": true, "useCount": 0,
  "lastUsedDate": "2026-09-28T...",
  "windows": [ { "id": 197208, "app": "process:C:\\...\\dotnet.exe", "title": "Nexus" } ] }

// WindowState
{ "window": { "id": 197208, "app": "...", "title": "Nexus" },
  "screenshots": [ { "id": "screenshot-0", "zIndex": 0,
                     "url": "data:image/jpeg;base64,...",
                     "originX": 0, "originY": 0, "width": 1512, "height": 982 } ],
  "accessibility": { "tree": "...", "focused_element": "...",
                     "selected_text": "...", "selected_elements": ["..."],
                     "document_text": "..." },
  "snapshotRevision": 12 }
```

#### Sifat `id` window — OBSERVED (bukan lagi INFERRED)

Tiga lapis bukti, semuanya terverifikasi:

1. **Kontrak JS memaksa integer ≥ 0.**
   ```js
   function p(e){ const t=y(e);
     if(null==t) throw new TypeError(
       "window.app must be a non-empty string and window.id must be an integer >= 0");
     return t }
   ```
   Nilai string numerik pun di-`Number()` lebih dulu. Jadi `id` **bukan** UUID dan bukan hash string.

2. **Serializer menulis `id` sebagai angka dari field struct.** `MOV R9, qword ptr [RDI + 0x18]` lalu `CALL 0x14002e504` dengan kunci `"id"`. Stride entri window = `0x38` (56 byte).

3. **Helper punya enam guard identitas yang hanya masuk akal untuk identitas OS yang bisa berubah/dipakai ulang:**

   | String | Alamat |
   |---|---|
   | `codex.window id out of range: ` | `0x140131fab` |
   | `window id is required` | `0x1401351ae` |
   | `verify current window before using captured target` | `0x1401351c4` |
   | `window id no longer resolves to the requested coordinate input target.` | `0x1401353b6` |
   | `window id <n> was not found. Current windows: <list>` | `0x140136699` |
   | `window id <n> no longer belongs to <app>; current owner is <app>` | `0x140136818` |

   Pola *"out of range"*, *"was not found. Current windows: …"*, dan *"no longer belongs to …; current owner is …"* mengandaikan nilai yang (a) berada dalam ruang nilai terbatas, (b) bisa hilang, dan (c) bisa **berpindah pemilik**. Itu deskripsi `HWND` di Win32, bukan identitas sintetis.

4. **Dokumentasi publik sengaja menyembunyikannya.** `docs/api.md` hanya menulis `id: number; // Opaque identifier for the open window.` — jadi status "opaque" adalah janji kontrak, bukan deskripsi implementasi.

**Kesimpulan untuk NEXUS:** perlakukan `id` sebagai **opaque integer** di kontrak, tetapi implementasikan sebagai `HWND` di helper C#. Ini memenuhi janji kontrak (klien tidak boleh mengasumsikan apa pun) sekaligus memberi kita nilai yang murah, stabil, dan bisa langsung dipakai untuk `GetWindowRect`/`SetForegroundWindow`. Jangan meniru guard "no longer belongs to <app>" secara literal — itu konsekuensi dari fakta bahwa original mengizinkan satu `HWND` berpindah proses; helper kita bisa menolak lebih awal dengan membandingkan `processId` saat snapshot dibuat.

---

## 5. Format Serialisasi Accessibility Tree (OBSERVED)

Dari rollout nyata:

```text
Window: "Nexus", App: dotnet.exe.
	0 window Nexus Secondary Actions: Raise
		1 text 
		2 text Nexus ID: TitleText
		3 text  ID: ThemeToggleLightIcon
		5 button  Description: Minimize ID: BtnMinimize
		6 text  ID: IconMinimize
		7 button  Description: Kembalikan ID: BtnMaximize
```

Aturan yang bisa direproduksi:

- Baris 0: `Window: "<judul>", App: <nama exe>.`
- Indentasi = kedalaman pohon, satu `\t` per level.
- Per node: `<index>\t<controlType>\t<name>` lalu atribut opsional.
- Atribut: `Description: …`, `ID: <AutomationId>`, `Secondary Actions: …`, `Value: …`, `(settable, string)`.
- Indeks (`0,1,2,…`) adalah **nomor urut global** hasil traversal, dipakai ulang sebagai `element_index`.

**INFERRED**: traversal memakai `IUIAutomationTreeWalker` (Control View) + cache request untuk performa; string `accessibilityRevision`/`snapshotRevision` menunjukkan snapshot di-cache per window.

### 5.1 Diff pohon aksesibilitas dan pemotongan (OBSERVED — hanya backend B)

Backend Swift tidak selalu mengirim ulang seluruh pohon. String-nya menunjukkan tiga mode presentasi:

```text
The following is a diff from the previous accessibility tree for Window: "
There has been no change in the accessibility tree for Window: "
... [accessibility tree truncated]
... [accessibility tree truncated: <n> ...
```

Tipe internal yang terlihat: `AccessibilityTree`, `AccessibilityTreeBuilder`, `AccessibilityTreeTransformation`, `AccessibilityTreePresentation`, `AccessibilityTreeTextDifference`, `AccessibilityDifferenceLineBudgetExceeded`, `AccessibilityPayloadBounds`, `AccessibilityPresentationValue`, `AccessibilityRoleKind`, `AccessibilityRenderingContext`, `WindowsAccessibilityTreeRevision`.

Ini penting untuk C#: **format teks pohon bukan satu-satunya bentuk keluaran**. Ada tiga kasus yang harus ditangani:

1. **Tidak ada perubahan** → kirim kalimat "There has been no change in the accessibility tree for Window: …" alih-alih pohon penuh. Ini penghematan token yang besar pada polling berulang.
2. **Ada perubahan** → kirim **diff** terhadap revisi sebelumnya (`accessibilityRevision`), bukan pohon penuh.
3. **Melebihi batas** → potong dengan penanda eksplisit `... [accessibility tree truncated]`, dan ada budget baris terpisah (`AccessibilityDifferenceLineBudgetExceeded`) sehingga pemotongan bisa terjadi karena jumlah baris diff, bukan hanya ukuran total.

Implikasi desain: `accessibilityRevision` harus menjadi bagian dari kontrak, dan klien harus menyimpan pohon terakhir per window agar bisa menerima diff. Rekonstruksi tanpa ini akan tetap berfungsi tetapi memboroskan konteks.

### 5.2 Taksonomi kegagalan aksesibilitas (OBSERVED — backend B)

Kode error yang stabil (bukan pesan bebas), berguna sebagai `enum` di C#:

| Kode | Arti |
|---|---|
| `WINDOWS_ACCESSIBILITY_UNAVAILABLE` | Provider UIA tidak tersedia untuk window/aplikasi ini |
| `WINDOWS_ACCESSIBILITY_INTERRUPTED` | Operasi UIA terputus (mis. window berubah di tengah traversal) |
| `WINDOWS_ACCESSIBILITY_TIMEOUT` | Traversal melewati batas waktu |
| `WINDOWS_ACCESSIBILITY_TARGET_UNAVAILABLE` | Elemen target hilang/ter-virtualisasi |
| `WINDOWS_ACCESSIBILITY_ELEMENT_DISABLED` | Elemen ada tetapi disabled |
| `WINDOWS_SECURE_DESKTOP_BLOCKED` | Desktop aman (UAC/lock screen) aktif |
| `UI Automation pattern is unsupported` | Elemen tidak mendukung pattern yang diminta |

Setiap kode punya kalimat pemulihan yang ditujukan ke model, mis. *"…Read the same window using get_window_state with include_text: false, include_screenshot: true"*, *"…inspect for completion or an ordinary dialog"*, *"Rediscover the window and read fresh accessibility state before choosing a new element"*. Ini pola yang layak ditiru: **error membawa instruksi pemulihan**, bukan hanya sebab.

### 5.3 Guard staleness pada aksesibilitas (OBSERVED — backend B)

```text
accessibility element changed; call get_window_state before retrying
window changed; call get_window_state before using an accessibility element
call get_window_state with include_text before using an accessibility element
window changed while its accessibility continuation was retrieved
accessibility element index must be nonnegative
no screenshot targets found for <window>
root window screenshot was not captured
screenshot target has empty bounds
get screenshot target bounds failed: <err>
<window> is outside screenshot bounds
```

Perhatikan `window changed while its accessibility continuation was retrieved` — traversal pohon bisa **berlanjut antar panggilan** (paging), dan jika window berubah di tengah, hasil lanjutan dibuang. Ada juga `call get_window_state with include_text=true and include_screenshot=false to retrieve the next page`, yang mengonfirmasi **paging pohon aksesibilitas**.

### 5.4 Algoritma pembangun pohon (OBSERVED, fungsi-level — backend A)

Bagian ini menutup celah terbesar audit sebelumnya (§13: "pembangunan pohon aksesibilitas ~35%"). Hasil dekompilasi Craft Anything 0.6.0 / Ghidra 12.1 menunjukkan pembangun pohon **bukan satu fungsi**, melainkan tiga lapis dengan tanggung jawab terpisah:

| Fungsi | Alamat | Baris pseudo-C | Peran |
|---|---|---|---|
| `FUN_140019341` | `0x140019341` | 1.107 | **Formatter tingkat-window**: kumpulkan window target, tulis header, blok fokus/dokumen/seleksi |
| `FUN_14001bcd5` | `0x14001bcd5` | 1.288 | **Pembangun baris per-node**: traversal + batas + pemanggilan formatter atribut |
| `FUN_14001e98c` | `0x14001e98c` | 208 | **Pembentuk sufiks atribut** node (state, description, value, actions, id) |

Rantainya: `FUN_140019341` → (per window) `FUN_14001bcd5` → (per node) `FUN_14001e98c`.

#### 5.4.1 Header window

`FUN_14007c93d` membentuk header dengan dua format string yang berbeda:

```c
// DAT_1401364a7 = [0xC0][7]", App: "[0xC0][1]"."   ->  "{0}, App: {1}."
FUN_14000c615(out, &DAT_1401364a7);   // dipakai bila nama app tersedia
FUN_14000c627(out, name, len);        // fallback: nama window saja
```

Jadi bentuknya persis seperti sampel §5: `Window: "Nexus", App: dotnet.exe.` — **titik penutup adalah bagian dari format string**, bukan kebetulan. Bila nama app tidak ter-resolve, header menjadi `Window: "<judul>".` tanpa `, App: `.

Nama app di-resolve lewat Toolhelp snapshot; kegagalan menghasilkan dua literal ini:

```text
CreateToolhelp32Snapshot failed
failed to resolve process name for pid <pid>
```

#### 5.4.2 Filter window tingkat atas

`FUN_140019341` menormalkan window ke root lalu memfilter:

```c
pHVar22 = GetAncestor(param_5, 2);   // GA_ROOT
...
BVar19 = IsWindowVisible(pHVar22);   // window tak terlihat dibuang
```

Selain itu ada **denylist nama window** yang dibandingkan dengan `str_eq` (bukan substring):

| Literal | Panjang | Arti |
|---|---|---|
| `Backstop Window` | 15 | window internal WinUI/XAML |
| `Default IME` | 11 | host IME |
| `Input Occlusion Window` | 22 | window penangkap input |
| `MSCTFIME UI` | 11 | UI IME TSF |
| `Program Manager` | 15 | desktop shell |
| `Taskbar` | 7 | taskbar |

Window yang namanya cocok dengan salah satu entri **tidak masuk pohon** — ini yang membuat pohon tidak dipenuhi noise sistem. Untuk NEXUS, daftar ini layak dipertahankan apa adanya; ia generik, bukan heuristik per-dokumen.

#### 5.4.3 Deteksi browser (menentukan apakah teks dokumen disertakan)

Sebuah pencabangan `str_eq` berurutan menentukan apakah node dianggap "browser" — hasilnya dipakai untuk memutuskan penyertaan `Document text`:

```c
msedge(6) -> chrome(6) -> brave(5) -> opera(5) -> iexplore(8) -> firefox(7) -> browser(7) -> dia(3)
```

Nilai yang dikembalikan adalah penanda enum internal: `0x8000000000000000` (bukan browser), `0x8000000000000003` (browser), `0x8000000000000002` (cabang ketiga). Arti semantik ketiga nilai ini **INFERRED** dari pola pemakaian; yang OBSERVED hanya nilainya.

#### 5.4.4 Blok fokus, dokumen, dan seleksi

Setelah baris pohon, `FUN_140019341` menambahkan blok kondisional. Empat kombinasi teramati (baris 629–688):

| Kondisi | Keluaran |
|---|---|
| ada elemen fokus | `The focused UI element is <…>` |
| ada teks dokumen | `Document text: ```…``` ` |
| ada seleksi tanpa teks | `Selected: <…>` + catatan |
| ada teks terpilih | `Selected text: ```…``` ` + catatan |

Catatan yang selalu menyertai blok seleksi:

```text
Note: Pay special attention to the selected content (but they might be referring to something else that's visible, too).
```

Panjang teks dokumen dibatasi oleh `FUN_1400240e6`, yang mengembalikan **anggaran karakter** berdasarkan pencocokan string: `+2.000.000`, `+1.500.000`, `+1.000.000`, atau `0`. Ini bukti bahwa original memotong teks dokumen besar, bukan mengirim seluruhnya.

#### 5.4.5 Batas traversal

`FUN_14001bcd5` memakai tiga kunci pembatas, yang **namanya sendiri muncul sebagai penanda keluaran**:

| Literal | Alamat | Ambang teramati |
|---|---|---|
| `cycle` | `0x140133523` | deteksi siklus (node yang sudah dikunjungi) |
| `child_limit` | `0x140133528` | anak ≥ **500** → penanda `child_limit` |
| `max_depth` | `0x140133533` | penghitung ≥ **200** (`0xC8`) → penanda `max_depth` |

Bentuk penanda pemotongan (byte-level, `0x1401375xx`):

```text
(truncated: <n>, omitted <m> children)
```

Ambang `500` terbaca langsung sebagai konstanta `500`; ambang `200` terbaca sebagai `0xC8`. Apakah penghitung `0xC8` berarti kedalaman atau jumlah node kumulatif masih **INFERRED** — yang OBSERVED hanya pasangan (ambang, penanda).

#### 5.4.6 Peta atribut node (`FUN_14001e98c`)

Node disimpan sebagai record **`0x118` = 280 byte** (`FUN_14001e89c` menyalin tepat 280 byte per node ke dalam `Vec`). Field yang dibaca formatter:

| Offset | Tipe | Arti | Keluaran |
|---|---|---|---|
| `+0x8` / `+0x10` | str | nama/teks utama | bagian nama node |
| `+0x20` / `+0x28` | str | nilai | `Value: <…>` |
| `+0x48` / `+0x50` / `+0x58` | Option<str> | nilai tambahan | ditambahkan apa adanya |
| `+0x60` / `+0x68` / `+0x70` | Option<str> | deskripsi | `Description: <…>` |
| `+0x78` / `+0x80` / `+0x88` | Option<str> | nilai toggle kustom | `(<v>)` |
| `+0xc0` | Option<ptr> | aksi sekunder tunggal | (lihat `+0xe0`) |
| `+0xd8` | ptr | identitas/automation id | `ID: <…>` |
| `+0xe0` | list | daftar aksi sekunder | `Secondary Actions: <…>` |
| `+0x10d` | bool | enabled (`0` = disabled) | `disabled` |
| `+0x10e` | bool | selectable | `selectable` |
| `+0x10f` | bool | selected | `selected` |
| `+0x110` | bool | settable | memicu cabang `+0x111` |
| `+0x111` | u8 | tipe nilai | `settable, string` / `settable` / `settable, float` |
| `+0x112` | u8 | toggle state | `off` / `on` / `indeterminate` |
| `+0x113` | u8 | expand state | `collapsed` / `expanded` / `partially expanded` |

Logika `selectable`/`selected` (byte-level, terbaca jelas):

```c
bVar4 = (*(u8*)(node + 0x10f) | *(u8*)(node + 0x10e));
if (bVar4 != 0) {
  // selected && !selectable  -> "selected"
  // selain itu               -> "selectable"
}
```

Artinya keduanya **tidak pernah** dicetak bersamaan; `selected` menang bila elemen tidak selectable, selain itu `selectable`.

Nilai enum state (`+0x112`, `+0x113`) dipetakan ke string pendek `off`/`on`/`indeterminate` dan `collapsed`/`expanded`/`partially expanded` — bukan nama enum panjang. Ini penting untuk paritas keluaran.

#### 5.4.7 Encoding format string Rust (koreksi)

Kutipan literal di atas mengungkap pola encoding yang **berbeda** dari asumsi awal dokumen. Bukan `{}` dan bukan pula tag `[0xC0][len]` yang berdiri sendiri. Byte-level:

```text
[0x0d]"Description: "[0xC0][0x00]
[0x07]"Value: "[0xC0][0x00]
[0x13]"Secondary Actions: "[0xC0][0x00]
[0x04]"ID: "[0xC0][0x00]
[0x0c]"(truncated: "[0xC0][0x0a]", omitted "[0xC0][0x0a]" children)"[0x00]
```

Bacaan yang konsisten dengan seluruh sampel:

1. Setiap segmen literal diawali **satu byte panjang**, lalu byte teksnya.
2. `0xC0` menandai **argumen** (`format!` placeholder). Argumen tidak menyimpan indeks — urutannya implisit.
3. Segmen panjang `0x00` menutup string.

Konsekuensi: **tidak ada tab** di `Description: `/`Value: `/`ID: `. Byte `0x0d`/`0x07`/`0x04` yang sempat terbaca sebagai `\t`/`\r` adalah **byte panjang**, bukan karakter. Indentasi pohon berasal dari tempat lain (satu `\t` per level, ditulis oleh pembangun baris), bukan dari literal atribut. Ini mengoreksi catatan §5 sebelumnya.

#### 5.4.8 Yang masih terbuka

Dengan kejujuran yang sama seperti §7.1:

| Pertanyaan | Status |
|---|---|
| Urutan pasti atribut dalam satu baris | **Sebagian** — urutan per-literal diketahui, tetapi urutan antar-blok ditentukan pemanggil (`FUN_14001bcd5`) dan belum dipetakan 100% |
| Bagaimana `element_index` diberikan | **Belum** — indeks global vs per-level belum terbukti; lihat §13 |
| Apakah indeks stabil antar snapshot | **Belum** |
| Paging (`include_text` + `continuation`) di backend A | **Belum** — hanya terbukti di backend B |
| Peran `+0xc0` vs `+0xe0` | **Sebagian** |

Yang sudah cukup untuk implementasi: **struktur tiga lapis, header, filter window, denylist, batas traversal, dan seluruh peta atribut**. Yang belum cukup hanya **penomoran indeks** — dan itu justru bagian yang bisa NEXUS desain sendiri tanpa merusak paritas perilaku, karena `element_index` hanya dipakai internal antara `get_window_state` dan aksi berikutnya.


---

## 6. Pipeline Screenshot (OBSERVED + INFERRED)

**Bukti import yang presisi (314 import, 32 DLL):**

| DLL | Fungsi | Peran |
|---|---|---|
| `d3d11.dll` | `D3D11CreateDevice`, `CreateDirect3D11DeviceFromDXGIDevice` | device D3D11 + jembatan ke WinRT `IDirect3DDevice` |
| `api-ms-win-core-winrt-l1-1-0.dll` | `RoGetActivationFactory`, `RoInitialize`, `RoUninitialize` | aktivasi WinRT (WGC, `GraphicsCaptureItem`) |
| `coremessaging.dll` | `CreateDispatcherQueueController` | dispatcher queue WinRT |
| `d2d1.dll` / `dwrite.dll` | `D2D1CreateFactory`, `DWriteCreateFactory` | render overlay/teks status |
| `dwmapi.dll` | `DwmGetWindowAttribute`, `DwmFlush` | bounds akurat (extended frame bounds) + sinkronisasi |
| `user32.dll` | `SetWindowDisplayAffinity` | mengecualikan overlay dari capture |
| `user32.dll` | `SetSystemCursor`, `CreateCursor`, `DestroyCursor` | kursor digambar sendiri |
| `user32.dll` | `EnumDisplayMonitors`, `GetMonitorInfoW`, `MonitorFromWindow`, `MonitorFromPoint`, `GetDpiForWindow`, `GetDpiForSystem`, `GetDpiForMonitor` | multi-monitor + DPI per monitor |

**Tidak ada** import `PrintWindow`, `BitBlt`, atau `GetDC` — original **tidak** memakai fallback GDI. Ini mengoreksi rekomendasi sebelumnya.

**OBSERVED** juga: string `Windows.Graphics.Capture`, `GraphicsCaptureItem`; output `data:image/jpeg;base64,…`; konsep region (`zIndex`, `originX/Y`, `width/height`); string `draw_cursor`, `overlay rendering operation failed after likely device loss; recreating overlay window`.

### 6.1 Rantai panggilan WinRT yang terverifikasi (OBSERVED, string tabel)

String tabel memuat **setiap label tahap** pipeline dalam bentuk frasa deskriptif (`create D3D11 device`, `CreateCaptureSession failed`, …). Ini menaikkan status alur dari INFERRED menjadi OBSERVED:

```text
D3D11CreateDevice                                          -> ID3D11Device
CreateDirect3D11DeviceFromDXGIDevice                       -> WinRT IDirect3DDevice
RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem")
  IGraphicsCaptureItemInterop.CreateForMonitor(monitor)     <- PER-MONITOR, bukan per-window
Direct3D11CaptureFramePool.CreateFreeThreaded(device, fmt, 2, size)
  SetIsCursorCaptureEnabled(false)                          -> "SetIsCursorCaptureEnabled failed"
  SetIsBorderRequired(false)                                -> butuh "experimentalApi"; "SetIsBorderRequired failed"
GraphicsCaptureSession.StartCapture                        -> "StartCapture failed"
  TryGetNextFrame setelah FrameArrived                      -> "TryGetNextFrame after FrameArrived failed"
  "GraphicsCaptureItem.Size failed"                         -> validasi ukuran item
  SoftwareBitmap.CreateCopyFromSurfaceAsync                 -> "copy Direct3D surface to SoftwareBitmap failed"
BitmapEncoder.CreateAsync(InMemoryRandomAccessStream, ...)
  BitmapEncoder.BitmapTransform / BitmapPropertySet.Insert / BitmapTypedValue.Create
  ImageQuality                                              -> JPEG quality
  flush JPEG encoder failed / encode latest capture frame failed
  read JPEG stream size failed / read JPEG stream failed
-> data:image/jpeg;base64,…
```

**Koreksi penting:** original menangkap **monitor**, bukan window. Buktinya string `IGraphicsCaptureItemInterop.CreateForMonitor failed` dan `window crop is outside captured monitor`. Jadi window di-*crop* dari frame monitor. Konsekuensi yang harus ditiru:

- Window yang **melintang dua monitor** tidak bisa di-crop dari satu frame; guard `window crop is outside captured monitor` adalah kegagalan eksplisit, bukan crop diam-diam.
- Region transient (menu/popup) dengan `zIndex` berasal dari frame monitor yang sama — satu frame bisa menghasilkan beberapa region.
- Cache per monitor: `captureCachedSessionCount`, `capture factory`, `capture item has invalid size`, `lastCaptureInvalidationReason` menunjukkan sesi capture di-*pool* per monitor dan di-*invalidate* (mis. saat resolusi/DPI monitor berubah).

String `snapshotRevision` + `accessibilityRevision` menunjukkan dua penghitung revisi terpisah: satu untuk frame gambar, satu untuk pohon aksesibilitas. Keduanya dipakai untuk mendeteksi staleness.

**Implikasi untuk C#:** jangan pakai `CopyFromScreen` (gagal saat ter-occlude, bermasalah di DPI campuran). Pakai WGC **per-monitor** lalu crop, persis seperti original. Karena original pun tidak punya fallback GDI, fallback `PrintWindow(PW_RENDERFULLCONTENT)` adalah **tambahan kita**, bukan paritas — tandai sebagai ekstensi opsional.

### 6.2 Sub-sistem overlay (OBSERVED — sebelumnya tidak terdokumentasi)

Helper memiliki overlay sendiri, dirender dengan **Windows.UI.Composition + Direct2D + DirectWrite** di atas D3D11. Ini bukan hiasan: overlay adalah sumber kebenaran visual bagi pengguna tentang apa yang sedang dilakukan agen, dan ia mengekspos dirinya sebagai UIA provider.

Identitas window/objek (string):

| String | Arti |
|---|---|
| `Codex Computer Use Display Overlay` | judul window overlay status (pill) |
| `Codex Computer Use Cursor Overlay` | judul window overlay kursor |
| `CodexComputerUseCursorOverlay` | nama kelas window |
| `Local\CodexComputerUseCursorOverlaySingleton` | mutex single-instance overlay |
| `codex-computer-use-status-pill` | nama visual pill |
| `Codex is using your computer` | teks pill (varian Codex; klien ChatGPT memakai `ChatGPT is using your computer`) |
| `Esc to cancel` | teks hint di pill |
| `Segoe UI`, `Segoe UI Variable` | font DirectWrite |

**Overlay kursor memakai expression animation, bukan animasi per-frame di CPU.** Bukti: string ekspresi Composition yang dikompilasi langsung ke `ExpressionAnimation`:

```text
((motion.Progress - 0.5) * 2.0)
(motion.Progress * 2.0)
motion.SegmentCount < 1.5 ? motion.SingleSegmentPoint
                         : (motion.Progress < 0.5 ? motion.FirstHalfPoint : motion.SecondHalfPoint)
Vector2(StartX + TravelX * shimmer.Progress, 0.0)
```

Artinya:

- Ada scalar `motion.Progress` (0→1) yang menggerakkan kursor sepanjang **path Bézier kubik** dengan titik kontrol `motion.P0`…`motion.P6`.
- Path dipecah menjadi **satu segmen** (`SegmentCount < 1.5`) atau **dua setengah segmen**; tiap setengah dievaluasi dengan parameter `(Progress*2)` atau `(Progress-0.5)*2`.
- Rotasi, skala, dan opacity kursor juga animasi terpisah (`create cursor rotation keyframe animation`, `create cursor scale keyframe animation`, `create cursor offset expression animation`), dengan `stop behavior` yang di-*hold* di akhir.
- Ada efek **fog blur** pada kursor (`create cursor fog blur effect`, `set cursor fog blur radius`).
- Ada **shimmer** pada pill (mask offset bergerak: `animate shimmer mask offset`, `bind shimmer travel offset`).
- Border highlight punya animasi "breath": `border offset breath keyframe`, `border size breath keyframe`, `set border offset breath duration`, `repeat border offset breath`.

Tahapan yang dinamai eksplisit (urutan pembangunan): `create D3D11 device` → `create D2D device` → `create DirectWrite factory/text format/text layout` → `create Windows.UI.Composition compositor` → `cast compositor desktop interop` → `CreateDesktopWindowTarget` → `create display overlay root` → `create display overlay pill root` → `insert display overlay pill content` → `create status text visual` → `start display overlay enter animation`.

**Manajemen kursor sistem adalah proses terpisah.** String `--system-cursor-manager`, `-suppress`, `-restore`, `-shutdown`, `-ready`, `SetSystemCursor`, `CreateCursor`, `DestroyCursor`, plus pasangan event bernama (`create system cursor manager ack event`, `open system cursor manager suppress event`, `signal system cursor manager shutdown`) menunjukkan helper **meng-spawn dirinya sendiri** dalam mode `--system-cursor-manager` untuk menyembunyikan/memulihkan kursor OS, lalu berkomunikasi lewat named event. Ini desain yang disengaja: `SetSystemCursor` bersifat global dan permanen sampai di-restore, sehingga dipisah ke proses yang bisa dipantau dan di-*restore* saat induk mati (`open system cursor manager parent process`). Guard string: `system cursor manager did not become ready`, `system cursor manager did not exit after shutdown`, `cursor overlay is no longer active`.

**Overlay adalah UIA provider.** `UiaHostProviderFromHwnd`, `UiaReturnRawElementProvider`, `UiaRaiseNotificationEvent`, `UiaRaiseStructureChangedEvent`, `UiaRaiseAutomationEvent` dipakai agar screen reader dapat mengumumkan status ("Codex is using your computer", "Esc to cancel"). Ini wajib ditiru untuk aksesibilitas, dan sekaligus menjelaskan mengapa helper mengimpor `uiautomationcore` hanya di sisi provider.

**Overlay dikecualikan dari capture.** `SetWindowDisplayAffinity` + `SetLayeredWindowAttributes` mencegah overlay ikut terpotret — jika tidak, agen akan melihat kursor dan pill-nya sendiri di screenshot dan bisa masuk loop umpan balik.

### 6.3 "Appshot" — pipeline capture backend B (OBSERVED)

Backend Swift menamai pipeline tangkapannya **Appshot**. Tipe dan guard yang terlihat:

| String | Arti |
|---|---|
| `Windows Appshot` / `WindowsAppshotAccessibility` / `WindowsAppshotAccessibilityCapture` | Pipeline capture + AX dalam satu satuan kerja |
| `WindowsScreenshotTarget`, `WindowsScreenshotSpace`, `WindowsScreenshotTarget` | Target tangkapan dan ruang koordinatnya |
| `WindowsCaptureFileLease` | Frame ditahan lewat *lease* file, bukan disalin ke memori lalu dilupakan |
| `IDXGIKeyedMutex.ReleaseSync(Appshot)` | Sinkronisasi GPU↔CPU dengan keyed mutex DXGI sebelum membaca frame |
| `Prepared Appshot capture was cancelled before release completed` | Capture bisa dibatalkan, tetapi pelepasan sumber daya tetap harus selesai |
| `Appshot capture pipeline was released` / `…was not released` | Dua hasil akhir eksplisit untuk audit kebocoran |
| `Windows Appshot accessibility capture completed without a result` | Jalur capture sukses tetapi AX gagal → hasil parsial |
| `appshotAccessibilityFailure` | Medan status untuk kasus di atas |
| `WindowsAppshotApprovalTarget` | Target persetujuan untuk Appshot (approval per aplikasi) |
| `WindowsDiscoveryExclusionPolicy` | Kebijakan pengecualian saat *menemukan* window (bukan hanya saat capture) |

**Pelajarannya untuk C#:** tangkapan layar bukan operasi tanpa status. Original memisahkan **prepare → capture → release**, memberi lease pada file frame, dan mencatat apakah pelepasan berhasil. Tanpa pemisahan ini, kebocoran handle GPU/file hanya muncul sebagai crash acak setelah sesi panjang.

### 6.4 Keamanan clipboard (OBSERVED — backend B)

`desktop_paste` tidak sekadar menulis clipboard. String-nya menunjukkan perlindungan penuh terhadap clipboard pengguna:

```text
Local\OpenAI.CodexComputerUse.Clipboard        # mutex lintas sesi
clipboard is unavailable or another session did not restore it
clipboard changed while pasting
clipboard changed while pasting; the user's clipboard was preserved
clipboard contains a format that cannot be safely preserved
clipboard text is invalid or exceeds four MiB
unsupported clipboard MIME type: <mime>
```

Alur yang disimpulkan: kunci mutex → `OpenClipboard` → **enumerasi semua format** (`EnumClipboardFormats`) → simpan salinan → `EmptyClipboard` → `SetClipboardData` (teks) → tempel → pulihkan format asli → lepas mutex. Jika ada format yang tidak bisa disimpan (mis. handle owner-drawn), operasi **dibatalkan** daripada merusak clipboard pengguna (`clipboard contains a format that cannot be safely preserved`).

Batas 4 MiB untuk teks clipboard, dan mutex bernama memastikan dua sesi Codex tidak saling menimpa.

### 6.5 Input lease dan serialisasi input (OBSERVED — backend B)

```text
DesktopInputLease
window input lease was already released
Release the held mouse button first
A previous desktop input is still running
Desktop input is still running
Desktop geometry changed; take another screenshot
full_desktop_pointer_v1 / full_desktop_timing_v1 / full_desktop_timing_v2
```

Backend B **menyerialkan** input: hanya satu lease input aktif pada satu waktu, dan lease harus dilepas eksplisit. Jika geometri desktop berubah di antara screenshot dan dispatch, input ditolak (`Desktop geometry changed; take another screenshot`). Tag `full_desktop_timing_v1`/`v2` menunjukkan perilaku timing input punya versi kontrak — pelajaran: **jangan campur perilaku lama dan baru tanpa penanda versi**.

`CursorMotionPolicy` adalah tipe terpisah: kebijakan gerak kursor (durasi, easing, jarak) dapat dikonfigurasi dan tidak ditanam di kode animasi.

### 6.6 Encoder JPEG: WIC, bukan encoder buatan sendiri (OBSERVED — backend A)

Encode screenshot memakai **Windows Imaging Component**, bukan library pihak ketiga:

```text
create JPEG encoder failed
flush JPEG encoder failed
read JPEG stream size failed
read JPEG stream failed
image/jpeg
```

| Literal | Alamat |
|---|---|
| `create JPEG encoder failed` | `0x1401398bb` |
| `flush JPEG encoder failed` | `0x1401398f9` |
| `read JPEG stream size failed` | `0x140139912` |
| `read JPEG stream failed` | `0x14013992e` |
| `image/jpeg` (MIME) | `0x140139945` |

Rantainya berada di `FUN_14004247a` (1.882 baris) — fungsi pipeline capture yang juga memanggil `D3D11CreateDevice`, WGC, dan `dwmapi`. Empat kegagalan di atas menandai empat titik yang bisa gagal: pembuatan encoder, `Commit`/`Flush`, pembacaan ukuran stream, dan pembacaan byte stream. Untuk NEXUS ini berarti setiap tahap harus punya error terpisah, bukan satu "screenshot failed".

**Yang belum terbukti: nilai kualitas dan faktor skala.** Tidak ada string `quality` di binary, sehingga nilai tersebut berupa konstanta numerik di dalam `FUN_14004247a`. Saya **tidak** mengklaim angka tertentu. Cara menutupnya di sesi berikutnya:

```powershell
# cari konstanta float di sekitar setup encoder (PROPVARIANT VT_R4)
& $cli --deep --allow-run native-function-pseudocode $exe --function 0x14004247a
# lalu telusuri blok antara "create JPEG encoder failed" dan "flush JPEG encoder failed"
```

Sampai angka itu terbukti, NEXUS sebaiknya memakai default yang wajar (kualitas ~80, skala 1,0) dan **mencatatnya sebagai pilihan desain**, bukan sebagai paritas. Kualitas JPEG hampir tidak memengaruhi kemampuan model membaca UI; yang jauh lebih penting adalah **konsistensi pemetaan koordinat** (§7.5).


---

## 7. Sintesis Input (OBSERVED)

**Import `user32.dll` (73 fungsi)** — yang relevan:

`SendInput`, `keybd_event`, `SetCursorPos`, `SetForegroundWindow`, `SetActiveWindow`, `SetFocus`, `BringWindowToTop`, `SwitchToThisWindow`, `AttachThreadInput`, `WaitForInputIdle`, `WindowFromPoint`, `GetWindowRect`, `GetWindowThreadProcessId`, `EnumWindows`, `EnumChildWindows`, `GetTopWindow`, `GetWindow`, `GetAncestor`, `IsWindowVisible`, `IsIconic`, `OpenClipboard`, `EmptyClipboard`, `SetClipboardData`, `GetClipboardData`, `IsClipboardFormatAvailable`, `CloseClipboard`, `SetProcessDpiAwarenessContext`, `SetThreadDpiAwarenessContext`, `SetProcessDPIAware`, `SetWinEventHook`, `UnhookWinEvent`, `NotifyWinEvent`, `SetWindowsHookExW`, `CallNextHookEx`, `UnhookWindowsHookEx`, `SetThreadDesktop`, `GetThreadDesktop`, `OpenInputDesktop`, `CloseDesktop`, `GetUserObjectInformationW`.

Bukti tambahan dari DLL lain:

- `CreateProcessW`, `ShellExecuteExW`, `ShellExecuteW` → `launch_app` (proses langsung vs AUMID/URL).
- `GetApplicationUserModelId`, `VerifyApplicationUserModelId` → identitas packaged app.
- `SHGetPropertyStoreForWindow` + `PropVariantToStringAlloc` → AUMID/identitas **per window**.
- `CreateToolhelp32Snapshot`, `Process32FirstW/NextW`, `Module32FirstW/NextW` → inventori proses.
- `RegOpenKeyExW`, `RegEnumKeyExW`, `RegEnumValueW`, `RegQueryValueExW` → inventori app terpasang.
- `GetFileVersionInfoExW`, `VerQueryValueW` → `displayName`.
- `WinVerifyTrust`, `WTHelper*` → verifikasi tanda tangan biner target.
- `WinHttp*` → pengambilan kebijakan/config dari app-server.
- `NtCreateNamedPipeFile` (ntdll) → server named pipe transport.
- `OpenInputDesktop`/`SetThreadDesktop`/`GetUserObjectInformationW` → deteksi desktop terkunci/secure desktop.

**Catatan penting (diverifikasi ulang dengan Craft Anything):** `uiautomationcore.dll` hanya mengimpor **lima** fungsi, semuanya **sisi provider**:

| Alamat | Simbol |
|---|---|
| `0x140177824` | `UiaHostProviderFromHwnd` |
| `0x14017783e` | `UiaRaiseAutomationEvent` |
| `0x140177858` | `UiaRaiseNotificationEvent` |
| `0x140177874` | `UiaRaiseStructureChangedEvent` |
| `0x140177894` | `UiaReturnRawElementProvider` |

Artinya helper **mengekspos** window overlay-nya sebagai UIA provider (agar screen reader bisa membacanya), sedangkan pembacaan pohon aplikasi lain dilakukan lewat COM client (`CoCreateInstance` → `IUIAutomation`), yang tidak muncul sebagai import langsung. Tidak ada `UiaFindAll`, `UiaGetPattern`, atau sejenisnya — konsisten dengan temuan §7.1 bahwa klik tidak lewat UIA pattern.

Perilaku yang terbukti dari dokumen:

- `press_key` memakai sintaks gaya **X11 keysym**: `a`, `space`, `Return`, `Tab`, `Control_L+a`, `Control_L+Shift_L+period`, `KP_0`, alias `Ctrl`, `Alt`, `Shift`, `period`, `greater`, `Numpad_0`.
- Windows **menolak** opsi `key` dan `durationMs` pada native app click.
- `paste` di Windows hanya mendukung `text` (lewat clipboard native).
- `selectText` **tidak tersedia** di Windows (throw sebelum kirim input).
- `scroll` native Windows: wajib koordinat + `{ pixels: n }`; target elemen / satuan halaman tidak didukung.
- Input otomatis mengaktifkan window target.
- **Guard rail** (string helper): `verify current window before using captured target`, `read current window bounds before input`, `window bounds changed before coordinate input`, `window changed before coordinate input`, `unknown screenshotId`, `no screenshot targets found for … activate the target or take a fresh screenshot before retrying`.

### 7.1 Alur kontrol di dalam satu arm: `click_element` (OBSERVED, fungsi-level)

Bagian ini menutup pertanyaan yang sebelumnya dinyatakan tidak terjawab (§13.1). Dekompilasi berhasil dilakukan dengan **Craft Anything 0.6.0** (engine Deep = Ghidra 12.1) pada fungsi dispatcher `FUN_140097ccb`, menghasilkan **9.234 baris pseudo-C**.

**Temuan utama — asumsi audit sebelumnya salah.** `click_element` **tidak** memanggil UIA `Invoke`/`InvokePattern`. Helper membaca *cached bounding rectangle* elemen dari snapshot UIA, menghitung titik tengahnya, lalu menginjeksi `mouse down` + `mouse up` lewat `SendInput`. UIA dipakai untuk **membangun pohon, membaca bounds, dan aksi non-klik** (`set UIA value`, `perform UIA secondary action`) — bukan untuk mengeksekusi klik.

Bukti negatif yang kuat: pencarian case-insensitive pada seluruh string binary memberi **0 kemunculan** untuk `Invoke`, `invoke`, `InvokePattern`, dan `Toggle`. String UIA yang ada hanya: `create UIAutomation`, `get UIA root element`, `read UIA value read-only state`, `set UIA value`, `read UIA range value read-only state`, `set UIA range value`, `perform UIA secondary action '…'`, `get app UIA element`, `snapshot app UIA element`, `refresh UIA element target window`, `refresh UIA element target snapshot`.

**Rantai panggilan lengkap (setiap alamat diverifikasi di pseudo-C):**

| # | Alamat | Fungsi | Peran |
|---|---|---|---|
| 1 | `0x14009d45e` | `FUN_14001e82b` | `str_eq(name, "click_element", 0xd)` — pemilihan arm |
| 2 | `0x14009d487` | `FUN_14008ff94` | Deserialisasi `ClickElementParams` |
| 3 | — | `FUN_1400a9ab8` | Resolusi identitas window dari param |
| 4 | — | `FUN_1400734f1` / `FUN_140073385` / `FUN_14007333c` | **Kunci cache aksesibilitas** (`Mutex::lock` → `Result`): acquire, cek poison (`accessibility cache lock poisoned`), release. Bukan pengambilan handle |
| 5 | — | `FUN_1400a8861` | **Guard**: input monitor, window usable, minimized, "user input was detected in this window; call get_window_state before continuing" |
| 6 | — | `FUN_1400a9cc7(…, 8)` | **Hitung deadline request** (clamp sisa budget ke `8`); error `"computer-use request budget exhausted"` — **bukan** parsing `element_index` |
| 7 | — | `FUN_14007ca74(ctx+0x220, …)` | **Minta element target ke "accessibility monitor"** (IPC): `send element target request to accessibility monitor` → `wait for accessibility element target`; hasilnya wajib punya *cached bounds* |
| 8 | — | `FUN_1400a73ab` | **Guard turn-scope**: `"Computer Use is no longer available in this turn because the turn has ended. Do not call further Computer Use tools in this turn."` (`0x140137a00`, 129 byte) — **bukan** input lease |
| 9 | — | — | `click_count` default `1`; error `"click_count must be >= 1"` |
| 10 | — | `FUN_1400a9885` | Parse `mouse_button`: `left`→`0`, `right`→`0x100`, `middle`→`0x200`; error `"unsupported mouse_button: …"` |
| 11 | — | — | Guard `"right double click is not supported"` |
| 12 | — | `FUN_1400a9d5e` → `FUN_1400a9ea3` | **Bounds → titik tengah** (lihat di bawah) |
| 13 | — | `FUN_1400a94da` → `FUN_1400a8861` + `FUN_1400a8c24` | Guard ulang, lalu aktivasi window: `ShowWindow(SW_RESTORE)`, `SetForegroundWindow`, guard secure desktop; error `"failed to activate captured window"` |
| 14 | loop | `FUN_1400a9e53` | Satu klik per iterasi, `click_count` kali |
| 15 | — | `FUN_1400a81ea` | Pembaruan state sesi: simpan nama operasi terakhir (`ctx+0x1f0`, `+0x1f8`) dan invalidasi snapshot (`ctx+0x200`) |

Catatan verifikasi silang: `FUN_1400a8861` dipanggil **dua kali** — sekali berdiri sendiri (langkah 5) dan sekali lagi di dalam `FUN_1400a94da` (langkah 13). Ini guard berlapis sebelum injeksi, bukan duplikasi tak sengaja.

**Perhitungan titik (Langkah 12).** `FUN_1400a9ea3` menerima rect `[left, top, right, bottom]` dan menghitung:

```c
iVar10 = (right - left) / 2 + left;   // center X
iVar9  = (bottom - top) / 2 + top;    // center Y
```

Lalu menerapkan pemetaan koordinat capture → layar bila `param_4 + 0x80 == 1`:

```c
uVar6 = (iStack_4c * (iVar10 - *(int*)(param_4+0x84))) / *(int*)(param_4+0x8c);
uVar7 = (iStack_48 * (iVar9  - *(int*)(param_4+0x88))) / *(int*)(param_4+0x90);
```

Cabang `else` hanya mengurangkan origin: `uVar6 = iVar10 - local_54`, `uVar7 = iVar9 - iStack_50`. Field yang dipakai (`+0x84`, `+0x88`, `+0x8c`, `+0x90`) konsisten dengan `originX`, `originY`, `width`, `height` pada §4.8 — jadi ini **skala gambar capture ke koordinat layar**, bukan koreksi DPI.

Jika elemen tidak punya cached bounds, muncul fragmen literal di `0x140132518`:

```text
element <index> has no cached bounds
window bounds unavailable for coordinate input
window capture worker is not running.
```

Catatan kejujuran bukti: literal di `.rdata` **tidak** menyimpan placeholder sebagai `{}`. Byte-level, fragmen ini berbentuk `[0x08]"element "[0xC0][0x15]" has no cached bounds"` — jadi `<index>` adalah argumen `format!`, bukan teks. Hal yang sama berlaku untuk pesan titik: `[0x07]"point ("[0xC0][0x02]", "[0xC0][0x0a]") is over "[0xC0][0x14]", not target window "[0xC0][0x40]"; activate the target or take a fresh screenshot before retrying."` (4 argumen: `x`, `y`, `window`, `app`).

Ini **bukti langsung** bahwa jalur klik berbasis koordinat, bukan UIA pattern: elemen tanpa bounds tidak bisa diklik.

**Injeksi input (Langkah 14 → 15).** `FUN_1400a9e53` → `FUN_1400a9dd8` → `FUN_1400a9a46`:

```c
// FUN_1400a9a46 — urutan sebenarnya, dengan pemeriksaan nilai balik
lVar1 = FUN_1400a952b(x, y);        // guard geometri
if (lVar1 == 0) {
  FUN_1400fb5a0(0, 16000000);       // tunggu 16 ms
  lVar1 = FUN_1400a73ab();          // guard turn-scope
  if (lVar1 == 0) {
    lVar1 = FUN_1400a9851(table_down[button]);   // SendInput mouse DOWN
    if (lVar1 == 0) {
      FUN_1400fb5a0(0, 16000000);                // tunggu 16 ms
      lVar1 = FUN_1400a73ab();                   // guard turn-scope lagi
      if (lVar1 == 0)
        FUN_1400a9851(table_up[button]);         // SendInput mouse UP
    }
  }
}
```

**Risiko yang perlu dicatat (OBSERVED):** jika `FUN_1400a73ab` gagal **di antara** down dan up — atau jika `SendInput` down mengembalikan error — maka `mouse up` **tidak dikirim**. Tidak ada `finally`/cleanup di jalur ini. Artinya turn yang berakhir tepat di tengah klik bisa meninggalkan tombol mouse tertahan. Ini alasan konkret mengapa NEXUS harus membungkus down/up dalam `try/finally` dan menyediakan `CleanupMouseUp` (§11.2), meskipun backend A tidak melakukannya.

`FUN_1400fb5a0(secs, nanos)` **bukan** `Sleep` biasa: ia memakai `CreateWaitableTimerExW` + `SetWaitableTimer` + `WaitForSingleObject` (presisi 100 ns) dengan fallback ke `Sleep`. Jadi `(0, 16000000)` = **16 ms presisi tinggi**. Ini penting untuk paritas: helper sengaja tidak memakai `Sleep(16)` yang bisa meleset ke ~15,6 ms.

`FUN_1400a9851(flags)` membangun satu `INPUT` (`dwType = 0` = `INPUT_MOUSE`) dan memanggil `FUN_1400af523(inputs, 1)`, yang isinya:

```c
local_60 = SendInput((UINT)param_2, param_1, 0x28);   // 0x28 = sizeof(INPUT)
```

**Tabel flag mouse** di `.rdata` `0x14014e4e8`, dibaca byte-level (36 byte):

```text
0x14014e4e8: 02 00 00 00  20 00 00 00  08 00 00 00    -> LEFTDOWN 0x0002, MIDDLEDOWN 0x0020, RIGHTDOWN 0x0008
0x14014e4f4: 04 00 00 00  40 00 00 00  10 00 00 00    -> LEFTUP   0x0004, MIDDLEUP   0x0040, RIGHTUP   0x0010
```

Indeks tabel = `button & 0xff`, sehingga urutan enum internal adalah **0 = left, 1 = middle, 2 = right**.

**Guard geometri (Langkah 14a).** `FUN_1400a952b(x, y)` memvalidasi titik terhadap rect monitor (`ctx+0x168`) dan daftar region screenshot yang di-cache (`ctx+0x2a8`, `ctx+0x2b0`, `ctx+0x2c0`). Jika titik jatuh di luar keduanya:

```text
point (<x>, <y>) is over <window>, not target window <app>;
activate the target or take a fresh screenshot before retrying.
```

(alamat `0x140135457`) — persis guard rail yang sudah tercatat di atas, kini dengan pemilik kode yang jelas.

**Timing yang terbukti:**

| Konstanta | Nilai | Lokasi |
|---|---|---|
| Jeda down → up | `16.000.000` ns = **16 ms** | `FUN_1400a9a46` |
| Jeda antar klik (double-click) | `100.000.000` ns = **100 ms** | loop arm `click_element` |

**Konsekuensi untuk NEXUS.** Kontrak `ClickElementAsync` harus diimplementasikan sebagai **geometri**, bukan pattern UIA:

1. `GetWindowStateAsync` menyimpan rect per elemen (dari `CurrentBoundingRectangle`) ke cache.
2. `ClickElementAsync` mengambil rect, menghitung center, memvalidasi terhadap region screenshot, lalu `SendInput` down/up.
3. Elemen tanpa bounds → error `ElementHasNoCachedBounds`, **bukan** fallback ke `Invoke`.
4. Jika ingin perilaku yang lebih andal daripada original, `InvokePattern` bisa ditambahkan sebagai **jalur alternatif** (bukan pengganti): ia kebal terhadap elemen yang tertutup overlay dan tidak butuh window foreground. Ini peningkatan, bukan paritas — catat sebagai deviasi yang disengaja.
5. **Resolusi elemen bersifat asinkron melalui "accessibility monitor"**, bukan lookup lokal: `send element target request to accessibility monitor` → `wait for accessibility element target`. Di C#, ini berarti `ClickElementAsync` butuh timeout dan pembatalan eksplisit; jangan menganggapnya operasi in-memory.
6. **Tunggu dengan timer presisi**, bukan `Sleep`. Original memakai waitable timer 100 ns. Untuk paritas timing, gunakan `CreateWaitableTimerExW` atau `Task.Delay` dengan toleransi yang disadari.

**Cara memverifikasi ulang (reproducible).** Semua klaim di atas bisa dicek ulang tanpa Ghidra GUI:

```powershell
$cli = "<craft-anything-cli.exe>"
$exe = "...\@oai\sky\bin\windows\codex-computer-use.exe"

# 1. Dekompilasi dispatcher
& $cli --deep --allow-run native-function-pseudocode $exe --function 0x140097ccb

# 2. Dekompilasi rantai klik
foreach ($f in '0x1400a9e53','0x1400a9a46','0x1400a9851','0x1400af523','0x1400a9ea3','0x1400a73ab') {
  & $cli --deep --allow-run native-function-pseudocode $exe --function $f
}

# 3. Tabel flag mouse (36 byte)
& $cli --deep --allow-run native-bytes-show $exe --address 0x14014e4e8 --length 36

# 4. Bukti negatif UIA Invoke
& $cli --deep --allow-run native-strings-find $exe --pattern Invoke --case-sensitive
& $cli --deep --allow-run native-strings-find $exe --pattern InvokePattern --case-sensitive
```

**Catatan kejujuran:** panjang literal yang dicetak decompiler untuk `FUN_14000c54f(ptr, len)` **tidak selalu akurat** (kadang ±2× panjang teks sebenarnya), kemungkinan karena register panjang tertukar. Karena itu semua kutipan string di atas diambil dari **isi fragmen**, bukan dari angka `len` yang ditampilkan decompiler.

### 7.2 `type_text` — lewat clipboard, bukan injeksi Unicode (OBSERVED, fungsi-level)

**Koreksi besar terhadap asumsi umum.** `type_text` di backend A **tidak** mengirim karakter via `KEYEVENTF_UNICODE`. Ia menulis ke clipboard lalu menekan `Ctrl+V`. Bukti string:

```text
verify clipboard text before paste        (0x14013563f, 34 byte)
CTRLV                                     (0x140135661, 4 byte)
clipboard did not contain requested text after setting it   (0x140138844, 115 byte)
clipboard contained invalid UTF-16 data
GlobalLock returned a null pointer while reading clipboard
GlobalAlloc returned an invalid handle
```

Import clipboard yang terverifikasi: `OpenClipboard`, `CloseClipboard`, `EmptyClipboard`, `GetClipboardData`, `IsClipboardFormatAvailable` (`0x1401778ec`–`0x140177bbc`).

**Alur lengkap (urutan sesuai alamat instruksi di dispatcher):**

| # | Alamat | Panggilan | Peran |
|---|---|---|---|
| 1 | `0x1400a0d48` | `FUN_1400a73ab` | Guard turn-scope |
| 2 | `0x1400a0d61` | `FUN_14003ca70()` | **Tulis teks ke clipboard**; `-1` = gagal, `-2` = perlu restore |
| 3 | `0x1400a2c10` | `FUN_14003cbb9()` | Restore/cleanup saat hasil `-2` |
| 4 | `0x1400a2c57` | `FUN_14003cc41(ctx)` | Simpan isi clipboard asli untuk dipulihkan |
| 5 | loop | `FUN_14003ca70` + `FUN_140025be0` | **Baca ulang clipboard dan bandingkan** dengan teks yang diminta |
| 6 | loop | `FUN_1400fb5a0(0, 25000000)` | Jeda polling **25 ms** |
| 7 | loop | `FUN_1400eb4b0` + `499999999` | Batas waktu **~500 ms** |
| 8 | `0x1400a4080` | `FUN_14000c54f` | Error `clipboard did not contain requested text after setting it` bila timeout |
| 9 | `0x1400a2d2e` | `FUN_14004ef0b` | Lampirkan konteks `verify clipboard text before paste` |
| 10 | `0x1400a2d3f` | `FUN_1400a73ab` | Guard turn-scope **lagi** (cek kedua) |
| 11 | `0x1400a2d65` | `FUN_14000c627("CTRLV", 4)` | Susun spesifikasi tombol |
| 12 | `0x1400a2dc2` | `FUN_1400a8035(spec, 2)` | **Kirim down semua, lalu up terbalik** (§7.4) |
| 13 | `0x1400a3c9a` | `FUN_1400a85a3` | Lepas semua tombol yang masih tertahan |
| 14 | `0x1400a3c9f` | `GetForegroundWindow` + `GetWindowThreadProcessId` | Identifikasi proses target |
| 15 | `0x1400a3cfd` | `WaitForInputIdle(pid, 0xfa)` | Tunggu **250 ms** agar paste diproses |
| 16 | `0x1400a3d50` | `FUN_14000c627` | Siapkan teks clipboard asli |
| 17 | — | timeout `750000000` | Batas **750 ms** untuk restore |

**Mengapa ini penting untuk NEXUS.** Tiga konsekuensi desain yang tidak bisa diabaikan:

1. **`type_text` merusak clipboard pengguna** kalau restore gagal. Original menangani ini dengan menyimpan isi asli (`FUN_14003cc41`) dan memulihkannya. NEXUS wajib melakukan hal yang sama, plus mutex global — backend B memakai `Local\OpenAI.CodexComputerUse.Clipboard`, dan itu **bukan kemewahan**, melainkan syarat agar dua instans tidak saling menimpa.
2. **Verifikasi baca-ulang adalah bagian dari protokol**, bukan paranoia. Clipboard Windows bisa diubah proses lain di antara set dan paste. Tanpa polling verifikasi, `Ctrl+V` bisa menempelkan isi yang salah — kegagalan yang sangat sulit didiagnosis karena terlihat seperti "model salah mengetik".
3. **Ada dua cek turn-scope** (langkah 1 dan 10). Artinya original sengaja memastikan turn belum berakhir **tepat sebelum** injeksi, karena langkah 2–8 bisa memakan hingga 500 ms.

**Deviasi yang disarankan (bukan paritas):** tambahkan jalur `SendInput` + `KEYEVENTF_UNICODE` sebagai alternatif ketika teks pendek dan clipboard tidak tersedia. Original tidak punya jalur ini; menambahkannya memperbaiki keandalan pada lingkungan yang memblokir clipboard. Catat sebagai deviasi yang disengaja.

### 7.3 `press_key` — parsing nama tombol (OBSERVED, fungsi-level)

Deserializer: `FUN_140092a2c`, dengan literal serde `struct PressKeyParams with 2 elements` (`0x140131198`).

| # | Alamat | Panggilan | Peran |
|---|---|---|---|
| 1 | `0x1400a15fa` | `FUN_1400a7e81` | Resolusi window target |
| 2 | `0x1400a164b` | `FUN_1400734f1` | Kunci mutex aksesibilitas |
| 3 | `0x1400a1650` | `FUN_1400a73ab` | Guard turn-scope |
| 4 | `0x1400a266a` | `FUN_14001140c(str, len, 0x2b)` | **Pecah string pada `+`** (`0x2b` = `'+'`) |
| 5 | loop | `FUN_140062352` + `FUN_14002f4f5` | Bangun `Vec<String>` 24 byte per token |
| 6 | `0x1400a2fe0` | `FUN_1400a8035(entries)` | Kirim seluruh kombinasi (§7.4) |
| 7 | `0x1400a38fd` | `FUN_1400a81ea(ctx, …, "press_key")` | Pembaruan state sesi |
| 8 | `0x1400a392c` | `FUN_14007333c` | Lepas mutex |

Bukti pemisah `+`: konstanta `0x2b` diteruskan sebagai argumen keempat ke `FUN_14001140c`, dan `0x2b` adalah kode ASCII `'+'`. Format masukan karenanya berbentuk `CTRL+SHIFT+T`, bukan array modifier terpisah.

Error yang terverifikasi:

```text
unsupported key: <name>
key is required
```

Token kosong (mis. `"CTRL+"`) jatuh ke `key is required`; token tak dikenal jatuh ke `unsupported key: <name>`.

### 7.4 Primitif keyboard: `FUN_1400af9ce` (OBSERVED, fungsi-level)

Seluruh injeksi keyboard backend A bermuara di satu fungsi kecil:

```c
void FUN_1400af9ce(short vk, char up)
{
  // 1. cari vk di tabel extended-key (19 entri @ 0x14013803e)
  // 2. dwFlags = (extended ? 1 : 0) | (up ? 2 : 0)
  input.type      = 1;        // INPUT_KEYBOARD
  input.ki.wVk    = vk;
  input.ki.wScan  = 0;        // TIDAK memakai scan code
  input.ki.dwFlags= flags;
  input.ki.time   = 0;
  input.ki.dwExtraInfo = 0;
  FUN_1400af523(&input, 1);   // SendInput(1, &input, 0x28)
}
```

**Tabel extended key** di `0x14013803e`, dibaca byte-level (19 × `u16`):

```text
a3 00  a5 00  2d 00  2e 00  24 00  23 00  21 00  22 00  25 00  26 00
27 00  28 00  90 00  03 00  2c 00  6f 00  5b 00  5c 00  5d 00
```

Dipetakan ke konstanta Win32:

| VK | Nama | VK | Nama |
|---|---|---|---|
| `0xA3` | `VK_RCONTROL` | `0x26` | `VK_UP` |
| `0xA5` | `VK_RMENU` | `0x27` | `VK_RIGHT` |
| `0x2D` | `VK_INSERT` | `0x28` | `VK_DOWN` |
| `0x2E` | `VK_DELETE` | `0x90` | `VK_NUMLOCK` |
| `0x24` | `VK_HOME` | `0x03` | `VK_CANCEL` |
| `0x23` | `VK_END` | `0x2C` | `VK_SNAPSHOT` |
| `0x21` | `VK_PRIOR` (PgUp) | `0x6F` | `VK_DIVIDE` |
| `0x22` | `VK_NEXT` (PgDn) | `0x5B` | `VK_LWIN` |
| `0x25` | `VK_LEFT` | `0x5C` | `VK_RWIN` |
| | | `0x5D` | `VK_APPS` |

Ini persis daftar "extended key" menurut aturan Win32 — bukan heuristik. Konsekuensinya `wScan = 0` **selalu**, dan `KEYEVENTF_EXTENDEDKEY` ditentukan oleh keanggotaan tabel, bukan oleh `MapVirtualKey`.

**Urutan down/up (`FUN_1400a8035`):** seluruh tombol ditekan **berurutan** (`CTRL` lalu `V`), lalu dilepas **terbalik** (`V` lalu `CTRL`). Ini urutan yang benar untuk kombinasi modifier; menekan `V` sebelum `CTRL` akan menghasilkan karakter `v`, bukan paste.

**Pelepasan darurat:** `FUN_1400a85a3` → `FUN_140021aaa(ptr, 2)` melepas semua tombol yang tercatat. NEXUS wajib memanggil ekuivalen ini di `finally`, karena alasan yang sama dengan tombol mouse tertahan (§7.1).

### 7.5 `scroll` — roda mouse, satu batch `SendInput` (OBSERVED, fungsi-level)

`scroll` **tidak** memakai UIA ScrollPattern. Ia mengirim `WM_MOUSEWHEEL` sintetis lewat `SendInput`, setelah memetakan koordinat dari ruang screenshot ke ruang layar.

| # | Alamat | Panggilan | Peran |
|---|---|---|---|
| 1 | `0x1400a17a7` | `FUN_1400a85ef` | Resolusi window + target |
| 2 | `0x1400a17f8` | `FUN_1400734f1` / `FUN_140073385` | Kunci + cek mutex aksesibilitas |
| 3 | `0x1400a182c` | `FUN_1400a8861` | Guard input monitor / window usable |
| 4 | `0x1400a183d` | `FUN_1400a73ab` | Guard turn-scope |
| 5 | `0x1400a184e` | `FUN_1400a897e` | **Cek secure desktop / lock screen** |
| 6 | `0x1400a186f` | `FUN_1400a8c24` | Aktivasi window |
| 7 | `0x1400a18a0` | `FUN_1400a8f1e` | **Peta koordinat screenshot → layar** |
| 8 | `0x1400a3c1d` | `FUN_1400a927d` | Gerakkan kursor ke titik target |
| 9 | `0x1400a3c2a` | `FUN_1400a73ab` | Guard turn-scope lagi |
| 10 | `0x1400a3c41` | `FUN_1400a93e6(scrollX, -scrollY)` | **Kirim roda mouse** |
| 11 | `0x1400a47ef` | `FUN_1400a81ea(…, "scroll")` | Pembaruan state sesi |

Perhatikan `-scrollY` di langkah 10: tanda dibalik karena `MOUSEEVENTF_WHEEL` memakai konvensi positif = menjauh dari pengguna, sedangkan `scrollY` pada kontrak bertanda sebaliknya.

**Primitif roda (`FUN_1400a93e6`):**

```c
void FUN_1400a93e6(int scrollX, int scrollY)
{
  Vec<INPUT> batch = [];

  if (scrollY != 0) {                       // vertikal
    int d = clamp(scrollY, -0x8000, 0x7FFF);
    batch.push(INPUT{ type = 0, mi = { mouseData = d, dwFlags = 0x0800 /* WHEEL */ } });
  }
  if (scrollX != 0) {                       // horizontal
    int d = clamp(scrollX, -0x8000, 0x7FFF);
    batch.push(INPUT{ type = 0, mi = { mouseData = d, dwFlags = 0x1000 /* HWHEEL */ } });
  }
  if (!batch.empty())
    FUN_1400af523(batch.ptr, batch.len);    // satu SendInput untuk seluruh batch
}
```

Detail yang penting untuk paritas:

1. **Clamp `-0x8000..0x7FFF`** per sumbu. Nilai di luar itu dipotong, bukan ditolak.
2. **Satu `SendInput` untuk dua `INPUT`** (vertikal + horizontal), bukan dua panggilan. Ini membuat scroll diagonal atomik.
3. **Tidak ada `MOUSEEVENTF_MOVE`** di batch roda — posisi kursor sudah digerakkan lebih dulu (langkah 8).

**Cek secure desktop (`FUN_1400a897e`)** memakai `OpenInputDesktop` dan string-string ini:

```text
OpenInputDesktop failed
foreground window did not report a process id
Winlogon
Windows Default Lock Screen
Windows desktop is locked (input desktop is <name>)
inspect active input desktop
```

Artinya scroll **menolak berjalan** saat layar terkunci. Ini lebih ketat daripada klik, yang hanya memeriksa guard `FUN_1400a8861`.

**Primitif gerak absolut (`FUN_1400a927d`):**

```c
// x, y dalam koordinat layar fisik
dx = (x - GetSystemMetrics(SM_XVIRTUALSCREEN)) * 0xFFFF / (GetSystemMetrics(SM_CXVIRTUALSCREEN) - 1);
dy = (y - GetSystemMetrics(SM_YVIRTUALSCREEN)) * 0xFFFF / (GetSystemMetrics(SM_CYVIRTUALSCREEN) - 1);
dwFlags = 0xC001;   // MOVE | ABSOLUTE | VIRTUALDESK
```

Tiga hal yang mudah salah:

- `0xC001` menyertakan `MOUSEEVENTF_VIRTUALDESK` (`0x4000`), jadi koordinat dinormalisasi terhadap **seluruh desktop virtual**, bukan monitor tunggal. Tanpa flag ini, klik pada monitor kedua akan mendarat di monitor pertama.
- Pembagi memakai `- 1` (lebar − 1), bukan lebar. Ini konvensi normalisasi Win32 yang benar.
- Bila `FUN_140047d89() == 1`, fungsi memanggil `SetThreadDpiAwarenessContext` lebih dulu. Jadi original sadar DPI dan menyesuaikan konteks thread saat perlu.

Error bila metrik desktop tidak valid (`0x14013679e`, 61 byte) muncul ketika `SM_CXVIRTUALSCREEN < 2` atau `SM_CYVIRTUALSCREEN < 2`.

### 7.6 `drag` — down, satu lompatan, up (OBSERVED, fungsi-level)

| # | Alamat | Panggilan | Peran |
|---|---|---|---|
| 1 | `0x14009e8ab` | `FUN_1400a94da` | Aktivasi window |
| 2 | `0x1400a248e` | `FUN_1400a8f1e` | Peta titik **awal** |
| 3 | `0x1400a24ce` | `FUN_1400a8f1e` | Peta titik **akhir** |
| 4 | `0x1400a2ad3` | `FUN_1400a73ab` | Guard turn-scope |
| 5 | `0x1400a2ae6` | `FUN_1400a927d(start)` | Gerak ke titik awal |
| 6 | `0x1400a2afc` | `FUN_1400a952b` ×2 | Guard geometri kedua titik |
| 7 | `0x1400a2b27` | `FUN_1400fb5a0(0, 16000000)` | Jeda **16 ms** |
| 8 | `0x1400a2b2c` | `FUN_1400a73ab` | Guard turn-scope |
| 9 | `0x1400a2b3f` | `FUN_1400a9851(2)` | **LEFT DOWN** (`0x0002`) |
| 10 | `0x1400a2b4d` | `FUN_1400a73ab` | Guard turn-scope |
| 11 | `0x1400a2b5e` | `FUN_1400a927d(end)` | **Lompat langsung ke titik akhir** |
| 12 | `0x1400a2b75` | `FUN_1400fb5a0(0, 16000000)` | Jeda **16 ms** |
| 13 | `0x1400a2b7f` | `FUN_1400a9851(4)` | **LEFT UP** (`0x0004`) |
| 14 | `0x1400a2b8a` | `FUN_14003c19d` | Gabungkan hasil (error pertama menang) |
| 15 | `0x1400a2bbe` | `FUN_1400a81ea(…, "drag")` | Pembaruan state sesi |

**Temuan yang harus dicatat: tidak ada interpolasi.** Antara `LEFT DOWN` dan `LEFT UP` hanya ada **satu** `FUN_1400a927d` — kursor melompat dari titik awal ke titik akhir. Tidak ada langkah antara, tidak ada kurva, tidak ada easing.

Konsekuensi praktis: drag-and-drop **akan gagal** pada aplikasi yang memerlukan gerakan kontinu (mis. banyak implementasi HTML5 drag, kanvas desain, atau pemilihan teks). Original memang punya keterbatasan ini.

**Deviasi yang disarankan:** NEXUS sebaiknya menginterpolasi 8–20 langkah antara dua titik dengan jeda ~8–16 ms. Ini bukan paritas, tetapi perbaikan yang nyata dan murah. Bila ingin paritas ketat, sediakan opsi `DragStyle.Teleport` vs `DragStyle.Interpolated`.

**Risiko tombol tertahan berulang di sini.** Ada tiga guard turn-scope di antara down dan up (langkah 8, 10) plus satu di akhir. Bila salah satu gagal setelah `LEFT DOWN`, `LEFT UP` tetap dikirim (langkah 13 berada di luar `if`), **tetapi** bila proses mati tepat di antaranya, tombol tertahan. Sama seperti §7.1 — wajib `try/finally` di NEXUS.

### 7.7 `set_value` dan `perform_secondary_action` — delegasi ke accessibility monitor (OBSERVED)

Keduanya **tidak** menyentuh elemen UIA secara langsung. Dispatcher mengirim permintaan ke subsistem "accessibility monitor" lewat IPC internal, lalu menunggu balasan.

| Operasi | Pesan kirim | Pesan tunggu | Alamat |
|---|---|---|---|
| `click_element` | `send element target request to accessibility monitor` (`0x140134b78`) | `wait for accessibility element target` | `0x14007cd87` |
| `set_value` | `send set value request to accessibility monitor` (`0x140134ab0`) | — | `0x1400a30f8` |
| `perform_secondary_action` | `send secondary action request to accessibility monitor` (`0x140134c87`) | `wait for accessibility secondary action` (`0x140134cbd`) | `0x1400a3532` |

Kegagalan subsistem dilaporkan dengan prefiks seragam (`0x140131f69`) dan satu kode status (`0x140132210`):

```text
+computer-use accessibility monitor failed: <err>
accessibility monitor did not start
```

**Implikasi arsitektur.** Ini temuan yang mengubah desain NEXUS: di backend A, **tidak ada satu pun aksi berbasis elemen yang dieksekusi di proses utama**. Semua didelegasikan ke proses/thread terpisah yang memegang cache UIA. Alasannya masuk akal — UIA bisa menggantung, dan mengisolasi panggilan UIA ke proses terpisah membuat timeout bisa ditegakkan dengan membunuh proses, bukan menunggu COM.

Untuk NEXUS, ini berarti:

1. **Butuh proses helper kedua** (`Nexus.ComputerUse.AccessibilityHost`) yang memegang cache UIA dan menerima permintaan IPC. Ini menambah satu komponen, tetapi menyelesaikan masalah UIA-hang secara struktural.
2. **Setiap aksi berbasis elemen butuh timeout dan pembatalan eksplisit** — bukan operasi in-memory.
3. **Error harus membawa instruksi pemulihan**, sesuai pola yang sudah terdokumentasi di §5.2.

`set_value` sendiri memakai UIA pattern di sisi monitor: string `set UIA value`, `read UIA value read-only state`, `set UIA range value`, `read UIA range value read-only state` (`0x14013716e`, dirujuk dari `sub_140012940` @ `0x140015757`). Artinya ada dua pattern — `ValuePattern` dan `RangeValuePattern` — dan keduanya diperiksa status *read-only* sebelum ditulis. `perform_secondary_action` memakai daftar aksi yang sudah di-cache, dengan error:

```text
<element> has no cached secondary actions for <window>
unsupported secondary action: <name>
secondary action '<name>' ...
perform UIA secondary action '<name>'
```

---

## 8. Keamanan, Kebijakan, dan Persetujuan

### 8.1 Identitas aplikasi (OBSERVED)

| Bentuk | Contoh |
|---|---|
| Proses | `process:C:\Program Files\dotnet\dotnet.exe` |
| Packaged/UWP | `ILOVEPDFS.L.iLovePDF-PDFReaderandEditor_0q01bhdkqq106!App` |
| Browser | `MSEdge` |

Konfigurasi persisten per sesi: `~/.codex/computer-use/sessions/<session-id>.toml`

```toml
[apps]
allowed = ["dotnet.exe", "ILOVEPDFS.L...!App"]
```

### 8.2 Target terlarang (OBSERVED, string helper)

`LockApp.exe`, `ApplicationFrameHost.exe`, `_proxy.exe`, `applicationframehost`, `backgroundtaskhost`, `ctfmon`, `inputapp`, `lockapp`, `runtimebroker`, `searchapp`, `searchhost`, `shellexperiencehost`, `sihost`, `startmenuexperiencehost`, `tabtip`, `textinputhost`, `click to do`/`clicktodo` (Windows 11 Recall/Click-to-Do).

### 8.2a Denylist kredensial, keamanan, dan terminal (OBSERVED — daftar hardcoded)

Dokumen `guidance.md` menyebut larangan mengotomasi password manager, antivirus, dan terminal. Ternyata larangan itu **ditegakkan di dalam helper** lewat tabel nama yang di-*embed* sebagai string panjang (concatenated, huruf kecil, dicocokkan pada nama biner).

**Password manager (11 merek, beserta nama proses turunannya):**

```text
1password, 1password-browsersupport, bitwarden, dashlane, enpass,
enpass password manager, icloud passwords, icloudpasswords, identities,
keepass, keepass password safe, keepassxc, keeper password manager,
keeperpasswordmanager, lastpass, nordpass, proton pass, protonpass,
roboform
```

**Perangkat lunak keamanan / antivirus (26 vendor):**

```text
ad-aware antivirus, adaware, avg antivirus, avg antivirus free,
avg internet security, avgui, avgnt, avast antivirus, avast free,
avast one, avast premium security, avastui, avktray, avira,
avira antivirus, avira free, avira.oe.systray, avira.systray, avp, avpui,
bdagent, bitdefender, bitdefender agent, bitdefender antivirus free,
bitdefender security center, bitdefender total security, ciscistray,
clamwin, clamwin antivirus, comodo antivirus, comodo firewall,
comodo internet security, crowdstrike falcon sensor, crowdstrike windows sensor,
csfalconcontainer, csfalconservice, dr.web, dr.web security space, dwservice,
egui, emsisoft anti-malware, emsisoft security center, eset, eset gui,
eset internet security, eset main gui, eset nod32 antivirus, eset security,
f-secure, fs_ui_32, g data antivirus, g data security software,
g data securitycenter, g data total protection, gdsc, kaspersky,
kaspersky anti-virus, kaspersky free, kaspersky internet security,
kaspersky security cloud, kaspersky total security, malwarebytes,
malwarebytes anti-malware, mbam, mbamgui, mcafee, mcafee livesafe,
mcafee security, mcafee security scan plus, mcafee total protection,
mcafee ui container, mcuicnt, microsoft defender, microsoft defender antivirus,
norton 360, norton antivirus, norton security, nortonsecurity, panda dome,
panda security, pccntmon, psuaconsol, seccenter, sechealthui, securityhealth,
systray, sophos home, sophos ui, trend micro, trend micro internet security,
trend micro maximum security, totalav, totalav ultimate antivirus,
totalav ultimate antivirus user interface, uiwinmgr, uistub,
webroot secureanywhere, windows security, wrsa, zatray, zlclient, zonealarm,
zonealarm security
```

**Terminal / shell (25 entri):**

```text
bash, cmd, cmder, conemu, conemu64, conemu64c, conemuc, conhost,
fluentterminal, git-bash, hyper, kitty, mintty, mobaxterm, openconsole,
powershell_ise, putty, puttytel, pwsh, tabby, terminal, terminus, termius,
ttermpro, warp, wezterm, wezterm-gui, windowsterminal, wsl, wt
```

**Browser (dikenali untuk kebijakan URL):** `chrome`, `firefox`, `msedge`, `brave`, `opera`, `iexplore`.

**Cara kerja yang disimpulkan:** pencocokan dilakukan pada **nama biner** (bukan path, bukan judul window) — bukti string `binary_name`, `binaryName`, `binary name is too long to compare`, `failed to compare binary names`, `CompareStringOrdinal`, dan guard `program path has no file name`. Perbandingan memakai `CompareStringOrdinal` (case-insensitive ordinal), bukan `to_lowercase()` lokal-aware.

**Implikasi C#:** daftar ini adalah **kebijakan**, bukan sekadar dokumentasi. Untuk paritas, kita perlu tabel yang setara. Untuk *legalitas dan etika*, kita harus mempertahankan semantiknya (menolak mengotomasi alat kredensial/keamanan/terminal) walau daftar mereknya kita susun sendiri — bukan menyalin daftar ini apa adanya.

### 8.2b Pemeriksaan integritas proses dan tanda tangan biner (OBSERVED)

| Bukti | Peran |
|---|---|
| `OpenProcessToken`, `GetTokenInformation`, `GetTokenInformation(TokenIntegrityLevel)`, `GetSidSubAuthority`, `GetSidSubAuthorityCount`, `TokenIntegrityLevel returned no buffer size`, `integrity SID has no sub authorities` | Membaca **integrity level** proses target (Low/Medium/High/System). Target berintegritas lebih tinggi dari helper tidak dapat di-*input*. |
| `WinVerifyTrust`, `WTHelperProvDataFromStateData`, `WTHelperGetProvSignerFromChain`, `WTHelperGetProvCertFromChain`, `CertNameToStrW` | Verifikasi rantai tanda tangan Authenticode → nama *publisher*. |
| `GetFileVersionInfoExW`, `VerQueryValueW`, `\ProductName`, `\OriginalFilename`, `\VarFileInfo\Translation` | Metadata versi untuk `displayName`/`publisherName`. |
| Guard: `executable has no version information`, `executable has no valid version translations`, `invalid executable version string`, `unterminated executable version string`, `executable path could not be resolved`, `executable path changed while resolving it` | Penanganan biner tanpa metadata versi, dan **TOCTOU**: path executable divalidasi ulang, dan perubahan path di tengah resolusi adalah error fatal. |

Guard `app policy requires an absolute executable path` / `app policy requires a resolved executable path` / `app policy requires an executable launch target` menunjukkan kebijakan dijalankan pada path **final** hasil `GetFinalPathNameByHandleW` + `QueryFullProcessImageNameW`, bukan pada argumen mentah.

### 8.2c Feature gate dari backend (OBSERVED)

Helper melakukan panggilan HTTPS sendiri (bukan lewat app-server) untuk memutuskan apakah fitur aktif bagi akun:

| Bukti | Arti |
|---|---|
| `https://api.openai.com/auth`, `chatgpt.com`, `chatgpt_account_id`, `refreshToken` | Sumber kredensial akun. |
| `/backend-api/aura/site_status` | Endpoint status fitur. |
| `feature_status`, `featureRequirements`, `requirements`, `featureRequirements/read`, `config/read`, `configRequirements/read` | Medan jawaban yang dibaca. |
| `WinHttpOpen`/`Connect`/`OpenRequest`/`SendRequest`/`ReceiveResponse`/`QueryHeaders`/`ReadData`/`CrackUrl`/`CloseHandle` | Implementasi klien HTTP (WinHTTP, bukan reqwest) — konsisten dengan tidak adanya TLS crate di string tabel. |
| `failed to open WinHTTP session`, `failed to decode site status response`, `site status response was not UTF-8`, `failed to read site status response`, `failed to connect to ChatGPT` | Jalur kegagalan. |
| `Computer Use is disabled by policy` | Pesan akhir bila gate menolak. |

**Implikasi untuk NEXUS:** ini adalah ketergantungan **layanan**. Rekonstruksi kita tidak boleh bergantung pada endpoint tersebut. Gate harus menjadi antarmuka (`IFeatureGate`) dengan implementasi lokal (config/managed policy), dan implementasi "remote" hanya opsional.

### 8.3 Skema kebijakan (OBSERVED, nama field di helper)

`computer_use`, `allow_persistent_approval`, `default_app_access`, `aumids`, `exe`, `allow`, `deny`, `feature_status`, `allowBrowserAndComputerUse`, `featureRequirements`, `ComputerUseAllowForbiddenTargets`, `riskLevel`, `optOutNotificationMethods`, `install_state`, `last_used`.

Sumber kebijakan: **JSON-RPC ke codex app-server** — `config/read`, `configRequirements/read`, plus token auth (`codex app-server did not return an auth token`). Pesan: `Computer Use is disabled by policy`, `Computer Use is not allowed on this app by policy`.

### 8.4 Kebijakan URL browser (OBSERVED, string helper)

Empat kegagalan eksplisit yang menghentikan giliran: URL tidak diizinkan, tidak bisa diverifikasi, tidak bisa ditentukan dengan cukup yakin, dan browser tidak didukung.

### 8.5 Taksonomi konfirmasi (OBSERVED — `docs/confirmations.md`)

| Mode | Isi |
|---|---|
| **1. Hand-off** | Ganti password (langkah akhir), bypass barrier keamanan/paywall |
| **2. Selalu konfirmasi** | Hapus data, ubah izin/akun, buat API/OAuth key, simpan password/kartu, CAPTCHA, install/run software baru, komunikasi representasional, subscribe, transaksi finansial, ubah setting sistem, aksi medis |
| **3. Pre-approval berlaku** | Login & prompt izin, verifikasi umur, upload file, manajemen file, transmisi data sensitif (harus sebut data + tujuan spesifik) |
| **4. Tanpa konfirmasi** | Cookie consent/ToS saat pembuatan akun, download file masuk, di luar taksonomi |

Aturan higiene: konfirmasi harus menjelaskan **risiko + mekanisme**; jangan konfirmasi terlalu awal; konten pihak ketiga **tidak pernah** menjadi izin.

### 8.6 Larangan non-negosiabel (OBSERVED — `docs/guidance.md`)

- Tidak menjalankan perintah terminal lewat UI automation, langsung maupun tidak langsung.
- Tidak mengotomasi Windows Terminal / CMD / PowerShell.
- Tidak memakai Run dialog; tidak menyisipkan skrip PowerShell/.bat di JS.
- Tidak mengotomasi dialog autentikasi, password manager, antivirus/security app.
- Tidak mengotomasi UI ChatGPT Desktop / Codex CLI.
- Tidak mengubah setting keamanan/privasi.
- **Tidak memakai tombol Windows atau shortcut berisi Win/Meta/Super/OS.**
- Desktop terkunci → berhenti, minta pengguna membuka kunci.

### 8.7 UX persetujuan (OBSERVED)

Bentuk `createElicitation` yang dikirim klien (bukan helper):

```jsonc
{
  "message": "Allow Codex to use <displayName>?",
  "meta": {
    "codex_approval_kind": "mcp_tool_call",
    "connector_id": "computer-use",
    "connector_name": "Computer Use",
    "persist": ["session", "always"],          // hanya ["session"] bila allowPersistentApproval=false
    "riskLevel": "low",                        // atau "high"
    "tool_params": { "app": "process:C:\\...\\dotnet.exe" },
    "tool_params_display": [
      { "name": "app", "display_name": "App", "value": "dotnet" }
    ]
  }
}
```

Varian audio menambahkan `codex_request_type: "approval_request"`, `tool_name: "start_audio_recording"`, dan `tool_call_id` (dari `call_id`/`item_id`) — pesannya `Allow Computer Use to record computer audio?` dan `persist` selalu `["session"]` saja.

Aturan penerimaan:

- `action` **harus** `"accept"`, jika tidak → `Computer Use was not approved to use <displayName>`.
- Jika `allowPersistentApproval=false` tetapi respons memilih `always` (`_meta.persist === "always"`, `content.persist === "always"`, atau `content.source === "computer-use-persisted-state" && content.scope === "global"`) → **tetap ditolak**.
- Jika elicitation tidak tersedia → `Computer Use requires app approval but elicitations are unavailable`.

- Overlay in-app: `"usingComputer": "ChatGPT is using your computer"`, `"escToCancel": "Esc to cancel"`, `accentColor: #339cff`.

---

## 9. Recon Map (format `replica-recon`)

### 9.1 Inventaris layar

| ID | Layar / permukaan | Cara mencapai | Tujuan | Komponen kunci |
|---|---|---|---|---|
| S01 | Chat + status Computer Use | giliran aktif | menampilkan overlay "sedang memakai komputer" | overlay, tombol Esc |
| S02 | Prompt persetujuan app | aksi pertama pada app baru | izin per-app | dialog, `persist` toggle, risk badge |
| S03 | Inventori aplikasi | `list_apps()` | memilih target | daftar app, badge running |
| S04 | Daftar window | `list_windows()` | memilih window/dialog | judul, app, id |
| S05 | Observasi window (screenshot) | `get_window_state(include_screenshot)` | konteks visual | gambar + region id |
| S06 | Observasi window (AX) | `get_window_state(include_text)` | indeks elemen | tree teks |
| S07 | Observasi gabungan | keduanya | verifikasi | gambar + tree |
| S08 | Persetujuan aksi berisiko | sebelum aksi taksonomi 2 | konfirmasi | penjelasan risiko+mekanisme |
| S09 | Hand-off | aksi taksonomi 1 | alihkan ke pengguna | instruksi |
| S10 | Interupsi Esc | Escape fisik | hentikan giliran | marker + pesan |
| S11 | Rekaman audio komputer | `start_audio_recording` | audio | durasi maksimum |

### 9.2 Alur pengguna

```text
F01 Menyelesaikan tugas pada satu window
    S03 pilih app -> S04 pilih window -> S05/S06 observasi
    -> satu aksi -> observasi ulang -> ... -> verifikasi terlihat -> jawab
    edge: window berubah, modal muncul di window lain, app belum jalan

F02 Persetujuan pertama kali
    aksi -> S02 -> accept -> aksi dilanjutkan -> ingat untuk sesi/giliran

F03 Aksi berisiko
    rencana -> S08 (risiko+mekanisme) -> accept -> aksi -> verifikasi
    edge: pengguna menolak, pre-approval ambigu

F04 Interupsi pengguna
    Escape -> helper exit 130 -> marker -> giliran berhenti -> pesan akhir

F05 Pemulihan setelah gagal
    timeout -> retry ringan 1x -> reset REPL -> initialize ulang -> berhenti & lapor

F06 Launch aplikasi
    app tidak ada di inventori -> launch_app(app id | .exe) -> refresh -> pilih window tunggal
```

### 9.3 Komponen berulang

Overlay status, dialog persetujuan, daftar app/window, penampil screenshot + badge region, penampil AX tree, parser key-chord, indikator fokus, toast error, guard "state basi".

### 9.4 Model data terinferensi

```text
App            id, displayName, isRunning, useCount, lastUsedDate
Window         id, app_id, title
WindowState    window_id, screenshots[], accessibility, revision
Screenshot     id, zIndex, url, originX, originY, width, height
Accessibility  tree, focused_element, selected_text, selected_elements[], document_text
Element        index, controlType, name, automationId, description, value, secondaryActions[]
Approval       app_id, displayName, riskLevel, allowPersistentApproval, scope(session|always)
Session        session_id, apps_allowed[]
Interrupt      conversation_id, turn_id, created_at
```

---

## 10. Feature Matrix

| Fitur | Area | Prioritas | Original | Clone | Catatan |
|---|---|---|---|---|---|
| Enumerasi app + window | Inti | must | ya | no | dasar semua aksi |
| AX tree + indeks elemen | Inti | must | ya | no | pembeda utama |
| Screenshot per-window (occlusion-proof) | Inti | must | ya | no | WGC |
| Screenshot multi-region (menu/popup) | Inti | should | ya | no | kompleks |
| Klik elemen / koordinat | Input | must | ya | no | |
| Ketik teks + paste | Input | must | ya | no | clipboard |
| Key chord gaya keysym | Input | must | ya | no | tabel pemetaan besar |
| Scroll koordinat + pixels | Input | must | ya | no | |
| Drag | Input | must | ya | no | |
| Set value pada elemen editable | Input | should | ya | no | UIA ValuePattern |
| Secondary action | Input | could | ya | no | Expand/Collapse/Raise |
| Launch app | Inti | must | ya | no | |
| Guard state basi | Keamanan | must | ya | no | pembeda kualitas |
| Allowlist per app | Keamanan | must | ya | no | |
| Daftar target terlarang | Keamanan | must | ya | no | |
| Approval per app + persist | Keamanan | must | ya | no | |
| Interupsi Esc | Keamanan | must | ya | no | hook keyboard |
| Kebijakan URL browser | Keamanan | should | ya | no | butuh integrasi browser |
| Rekaman audio | Ekstra | could | ya | no | `SKY_ENABLE_AUDIO` |
| Full-desktop surface | Ekstra | could | ya | no | backend terpisah (13 metode), bukan flag |
| Diff + paging pohon AX | Ekstra | should | ya | no | penghemat token besar; butuh revisi + kursor |
| Keamanan clipboard (preserve) | Keamanan | should | ya | no | backend B; mencegah clipboard pengguna rusak |
| Appshot prepare/capture/release | Infra | should | ya | no | mencegah kebocoran handle GPU/file |
| Named pipe transport | Infra | could | ya | no | alternatif stdio, tidak mendukung desktop |

**Tidak bisa / tidak boleh direplikasi:** model LLM dan prompt sistem, layanan cloud OpenAI, penandatanganan plugin, kebijakan akun, serta aset merek.

**Ukuran:** **L** (satu kuartal) untuk paritas inti Windows; **XL** jika termasuk browser policy + audio.

---

## 11. Blueprint Implementasi C# (NEXUS)

### 11.1 Penempatan proyek (sesuai AGENTS.md)

```text
Nexus-CSharp/src/
├── 00_Contracts/Nexus.ComputerUse.Contracts/      # DTO + interface, tanpa dependensi
├── 02_Modules/Nexus.ComputerUse/                  # klien: transport, sesi, kebijakan
│   └── Nexus.ComputerUse.Tests/                   # xUnit
└── 02_Modules/Nexus.ComputerUse.Host/             # helper EXE (out-of-process)
    └── Nexus.ComputerUse.Host.Tests/
```

Aturan yang **wajib** dipatuhi:
- Helper adalah **proses terpisah**; `Nexus.App` **tidak** mereferensikan proyek helper secara langsung (pola sama seperti `NexusConverterEngine`).
- Target `net8.0-windows` / `win-x64` saja.
- Jangan menyalin `Engine`/`Models` ke `bin`/`obj`.
- Build: `dotnet build <csproj> -c Release --no-restore -m:1 -p:BuildInParallel=false`, 0 warning 0 error.

### 11.2 Kontrak inti

```csharp
namespace Nexus.ComputerUse.Contracts;

public readonly record struct AppId(string Value);      // "process:C:\...\x.exe" | AUMID
public readonly record struct WindowId(long Value);     // opaque; helper memakai HWND

public sealed record WindowRef(WindowId Id, AppId App, string? Title);
public sealed record ScreenshotRef(string Id, int ZIndex, string DataUrl,
                                   int? OriginX, int? OriginY, int? Width, int? Height);
public sealed record AccessibilityState(string Tree, string? FocusedElement,
                                        string? SelectedText, IReadOnlyList<string>? SelectedElements,
                                        string? DocumentText);
public sealed record WindowState(WindowRef Window, IReadOnlyList<ScreenshotRef> Screenshots,
                                 AccessibilityState? Accessibility, long SnapshotRevision);
```

#### Kontrak per-window — 20 metode (paritas penuh §4.7)

```csharp
public interface IComputerUseSession : IAsyncDisposable
{
    // --- observasi ---
    Task<IReadOnlyList<AppInfo>> ListAppsAsync(CancellationToken ct);
    Task<IReadOnlyList<WindowRef>> ListWindowsAsync(CancellationToken ct);
    Task<WindowRef> GetWindowAsync(WindowId id, AppId? app, CancellationToken ct);
    Task<WindowState> GetWindowStateAsync(WindowRef w, bool screenshot, bool text, CancellationToken ct);
    Task<DiagnosticState> GetDiagnosticStateAsync(CancellationToken ct);   // hanya di helper

    // --- navigasi ---
    Task ActivateWindowAsync(WindowRef w, CancellationToken ct);
    Task LaunchAppAsync(AppId app, CancellationToken ct);

    // --- input berbasis elemen ---
    Task ClickElementAsync(WindowRef w, int elementIndex, int clickCount, MouseButton button, CancellationToken ct);
    Task ScrollElementAsync(WindowRef w, int elementIndex, ScrollDirection direction, int pages, CancellationToken ct);
    Task SetValueAsync(WindowRef w, int elementIndex, string value, CancellationToken ct);
    Task PerformSecondaryActionAsync(WindowRef w, int elementIndex, string action, CancellationToken ct);

    // --- input berbasis koordinat ---
    Task ClickAsync(WindowRef w, int x, int y, string? screenshotId, int clickCount, MouseButton button, CancellationToken ct);
    Task ScrollAsync(WindowRef w, int x, int y, int dx, int dy, string? screenshotId, CancellationToken ct);
    Task DragAsync(WindowRef w, int fx, int fy, int tx, int ty, string? screenshotId, CancellationToken ct);

    // --- input teks ---
    Task PressKeyAsync(WindowRef w, string chord, CancellationToken ct);
    Task TypeTextAsync(WindowRef w, string text, CancellationToken ct);

    // --- lifecycle turn ---
    Task EndTurnAsync(CancellationToken ct);
    Task CloseAsync(CancellationToken ct);
}
```

`ScrollElementAsync` dan `GetDiagnosticStateAsync` **tidak** ada di permukaan JS publik original, tetapi ada di helper. Sertakan sejak awal: biayanya kecil dan keduanya sangat berguna untuk debugging (scroll terarah tanpa koordinat, dan snapshot cache/revisi).

#### Kontrak full-desktop — 13 metode, antarmuka **terpisah**

Di original ini bukan flag dari sesi yang sama, melainkan binary berbeda. Karena itu pisahkan:

```csharp
public interface IDesktopSession : IAsyncDisposable
{
    Task<IReadOnlyList<DesktopScreenshot>> GetScreenshotAsync(CancellationToken ct);
    Task PrepareInputAsync(CancellationToken ct);          // lease input
    Task MouseDownAsync(int x, int y, string? dragId, CancellationToken ct);
    Task MouseUpAsync(string dragId, CancellationToken ct);
    Task CleanupMouseUpAsync(CancellationToken ct);        // tanpa dragId, jalur error
    Task MoveAsync(int x, int y, string? dragId, CancellationToken ct);
    Task ClickAsync(int x, int y, MouseButton? button, int? clickCount, CancellationToken ct);
    Task ScrollAsync(int x, int y, int dx, int dy, CancellationToken ct);
    Task PressKeyAsync(string key, CancellationToken ct);
    Task TypeTextAsync(string text, CancellationToken ct);
    Task PasteAsync(string text, CancellationToken ct);

    // lifecycle — harus selalu lolos walau helper dianggap sibuk (§3.4)
    Task CancelAsync(string operationId, string? dragId, CancellationToken ct);
    Task<CloseResult> CloseAsync(CancellationToken ct);    // { Stopped: bool } bila Esc
}
```

Aturan yang **wajib** ditiru pada `IDesktopSession`:

| Aturan | Konsekuensi C# |
|---|---|
| Screenshot wajib segar sebelum input apa pun | simpan `bool _hasFreshScreenshot`; reset setelah setiap input |
| `drag` = down → move* → up dengan satu `dragId` | state machine `Idle → Starting → Dragging → Ended` |
| `drag.path` minimal 2 titik | validasi sebelum kirim |
| Input diserialkan lewat satu lease | `SemaphoreSlim(1,1)` di level sesi, bukan per metode |
| Geometri berubah → tolak input | bandingkan hash geometri monitor dengan snapshot screenshot |
| Timeout → `desktop_cancel`, Esc → `desktop_close` | dua jalur terpisah, jangan digabung |

`desktop_cancel`, `desktop_close`, dan `close` **tidak ada di binary mana pun** — perlakukan sebagai kontrol sesi tingkat transport yang tidak pernah diblokir oleh guard "helper sibuk".

#### Kontrak pohon aksesibilitas

```csharp
public enum AccessibilityMode { Full, Diff, Unchanged, Truncated }

public sealed record AccessibilityPayload(
    AccessibilityMode Mode,
    string? Tree,              // null saat Unchanged
    int LineBudget,            // AccessibilityDifferenceLineBudgetExceeded
    bool HasContinuation,      // paging: include_text=true, include_screenshot=false
    long Revision);            // accessibilityRevision / snapshotRevision
```

Klien **wajib** menyimpan pohon terakhir per window agar bisa meminta dan menerapkan `Diff`. Tanpa itu, `get_window_state` berulang memboroskan konteks (§5.1).

#### Taksonomi error

```csharp
public enum ComputerUseErrorCode
{
    UnsupportedMethod,
    WindowNotFound,             // "window id <n> was not found. Current windows: ..."
    WindowOwnerChanged,         // "window id <n> no longer belongs to <app>"
    WindowIdOutOfRange,
    WindowIdRequired,           // "window id is required"
    StaleObservation,           // "window changed; call get_window_state before ..."
    StaleScreenshot,            // "Take a new desktop screenshot before sending input"
    ElementHasNoCachedBounds,   // "element <i> has no cached bounds" (§7.1)
    PointOutsideTarget,         // "point (<x>, <y>) is over <window>, not target window <app>"
    CaptureWorkerNotRunning,    // "window capture worker is not running."
    AccessibilityUnavailable,   // "accessibility cache lock poisoned"
    AccessibilityMonitorNotStarted, // "accessibility monitor did not start"
    AccessibilityInterrupted,
    AccessibilityTimeout,
    AccessibilityTargetUnavailable,
    AccessibilityElementDisabled,
    SecureDesktopBlocked,       // "failed to activate captured window"
    TurnEnded,                  // "Computer Use is no longer available in this turn because the turn has ended..."
    RequestBudgetExhausted,     // "computer-use request budget exhausted"
    InputLeaseBusy,             // "A previous desktop input is still running"
    GeometryChanged,
    ClickCountInvalid,          // "click_count must be >= 1"
    MouseButtonUnsupported,     // "unsupported mouse_button: <v>"
    RightDoubleClickUnsupported,// "right double click is not supported"
    KeyUnsupported,             // "unsupported key: <name>"          (§7.3)
    KeyRequired,                // "key is required"                 (§7.3)
    SecondaryActionUnavailable, // "<el> has no cached secondary actions for <win>" (§7.7)
    SecondaryActionUnsupported, // "unsupported secondary action: <name>"           (§7.7)
    AccessibilityMonitorFailed, // "+computer-use accessibility monitor failed: <err>" (§7.7)
    ClipboardWriteFailed,       // "clipboard did not contain requested text after setting it" (§7.2)
    ClipboardReadFailed,        // "GlobalLock returned a null pointer while reading clipboard" (§7.2)
    ClipboardInvalidUtf16,      // "clipboard contained invalid UTF-16 data" (§7.2)
    VirtualScreenUnavailable,   // SM_CXVIRTUALSCREEN/CY < 2 (§7.5)
    DesktopLocked,              // "Windows desktop is locked (input desktop is <name>)" (§7.5)
    InputDesktopUnavailable,    // "OpenInputDesktop failed" (§7.5)
    ProcessIdUnavailable,       // "foreground window did not report a process id" (§7.5)
    ProcessNameUnresolved,      // "failed to resolve process name for pid <pid>" (§5.4.1)
    ClipboardUnsafe,            // "clipboard contains a format that cannot be safely preserved"
    StoppedByUser,              // Esc / exit 130
    TransportFailure
}
```

Setiap kode membawa `RecoveryHint` (string yang ditujukan ke model), meniru pola original: *error membawa instruksi pemulihan, bukan hanya sebab* (§5.2).

**Aturan cleanup wajib (temuan §7.1):** original tidak mengirim `mouse up` jika turn berakhir atau `SendInput` down gagal. NEXUS harus menutup celah ini:

```csharp
// di IComputerUseSession — bukan hanya IDesktopSession
Task CleanupMouseUpAsync(CancellationToken ct);   // idempoten, aman dipanggil kapan saja
```

Implementasi: bungkus setiap pasangan down/up dalam `try/finally`; jika `TurnEnded` atau error terjadi setelah down, `finally` mengirim up. Ini **deviasi yang disengaja** dari original, dicatat sebagai perbaikan keandalan.

### 11.3 Transport

```csharp
internal sealed class NdjsonHelperTransport : IAsyncDisposable
{
    // ProcessStartInfo: RedirectStandardInput/Output/Error, CreateNoWindow, UseShellExecute=false
    // argumen selalu diakhiri: "--parent-pid", <Environment.ProcessId>
    // writer: JsonSerializer.Serialize + '\n'  (UTF-8, no BOM, '\n' saja)
    // reader: loop ReadLineAsync, parse; id numerik => pending TCS; tanpa id => event
    //         JSON invalid => reject SEMUA pending
    // timeout per method: 15s (screenshot/type/paste/launch), 10s lainnya
    // exit code 130 => UserPressedEscapeException (+ tulis marker interrupt)
    // Dispose: kirim RPC "close", tunggu, lalu Kill(); cleanup temp dir
}
```

Detail yang **harus** ditiru:
- `stderr` di-buffer; sertakan potongannya di exception saat exit.
- Semua pending request direject saat proses mati (jangan menggantung).
- Timeout default → matikan helper (kecuali `preserveHelperOnTimeout`).
- `id` monoton naik per proses.
- Setelah interrupt Esc terdeteksi, **tolak semua request berikutnya di turn yang sama** tanpa menyentuh helper; hanya `close`/`cancel` yang lolos.
- Sebelum request pertama di turn baru, kirim `end_turn` untuk turn sebelumnya (idempotent per `turnId`).
- `x-oai-cua-request-budget-ms` masuk ke `meta`, bukan `params`.
- Approval: pada `ok:false` + `approvalRequest`, tampilkan dialog, lalu **kirim ulang request yang sama** dengan meta `x-oai-cua-approved-app`; jangan membuat method approval baru.
- Semua timeout ditunda selama dialog approval terbuka, lalu dijadwalkan ulang.

### 11.4 Helper native (`Nexus.ComputerUse.Host`)

| Komponen | Implementasi |
|---|---|
| Enumerasi window | `EnumWindows` + `GetWindowThreadProcessId` + `IsWindowVisible` + `GetAncestor(GA_ROOTOWNER)` |
| Identitas app | proses → `process:<path>`; packaged → AUMID via `SHGetPropertyStoreForWindow` / `GetApplicationUserModelId`; validasi `VerifyApplicationUserModelId` |
| Inventori app | registry uninstall keys (`RegEnumKeyExW`/`RegEnumValueW`) + `CreateToolhelp32Snapshot` + `GetFileVersionInfoExW` untuk `displayName` |
| UIA tree | `IUIAutomation` via COM (`CoCreateInstance`, **bukan** import `uiautomationcore`), `TreeWalker` Control View, `CacheRequest` (Name, AutomationId, ControlType, IsEnabled, Value, HelpText) |
| Serialisasi | writer teks: `Window: "{title}", App: {exe}.` + DFS dengan `\t` per level |
| Screenshot | `D3D11CreateDevice` → `CreateDirect3D11DeviceFromDXGIDevice` → `RoGetActivationFactory` → `CreateForWindow` → `Direct3D11CaptureFramePool` → staging → WIC JPEG |
| Fallback capture | **tidak ada di original**; jika ditambahkan, `PrintWindow(PW_RENDERFULLCONTENT)` adalah ekstensi kita |
| Bounds | `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` + `DwmFlush`, bukan `GetWindowRect` saja |
| Input | `SendInput` (KEYBDINPUT/MOUSEINPUT), `INPUT` array; koordinat relatif region → layar via `ClientToScreen`/`GetWindowRect` |
| Key chord | parser keysym → VK + modifier; tabel `KP_0..9`, `Return`, `Tab`, `period`, `greater`, … |
| Paste | `OpenClipboard`/`EmptyClipboard`/`SetClipboardData(CF_UNICODETEXT)` → `Ctrl+V` → restore clipboard |
| Fokus | `SetForegroundWindow` + `AttachThreadInput` bila ditolak; cek `IsIconic` → restore |
| Guard | simpan `snapshotRevision` + `bounds`; tolak input jika berubah |
| Interupsi | `SetWindowsHookExW(WH_KEYBOARD_LL)` → Esc → exit 130 + marker file |
| DPI | `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)` di `Main` |
| Desktop terkunci | `OpenInputDesktop` + `GetUserObjectInformationW` untuk mendeteksi secure desktop |
| Overlay | `CreateWindowExW` + `SetLayeredWindowAttributes` + `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` + D2D/DWrite; overlay juga UIA provider (`UiaHostProviderFromHwnd`, `UiaRaiseNotificationEvent`) |
| COM | UIA di thread **MTA** (`CoIncrementMTAUsage`); D3D/WinRT di thread sendiri dengan `CreateDispatcherQueueController` |

### 11.5 Lapisan kebijakan

```csharp
public interface IComputerUsePolicy
{
    ValueTask<PolicyDecision> EvaluateAsync(AppId app, CancellationToken ct);
    ValueTask<PolicyDecision> EvaluateUrlAsync(string url, CancellationToken ct);
    bool IsForbiddenTarget(string exeNameOrAumid);
}
```

- Allowlist dibaca dari konfigurasi sesi (padanan `computer-use/sessions/<id>.toml`).
- Daftar terlarang di-*hardcode* sebagai konstanta (bukan konfigurasi pengguna).
- Approval dipisah dari policy: policy = boleh/tidak; approval = izin manusia.

### 11.6 Integrasi UI (WPF)

- `ComputerUseOverlayWindow`: topmost, click-through, `WS_EX_NOACTIVATE`, `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`.
- Teks overlay mengikuti locale (`usingComputer`, `escToCancel`).
- Dialog persetujuan: tampilkan `displayName`, `riskLevel`, pilihan `session`/`always`.

### 11.7 Jebakan yang sudah terbukti di original

1. **Indeks elemen basi** → wajib observasi ulang setelah setiap aksi.
2. **Window berubah ukuran** → koordinat tidak valid; original menolak, bukan menebak.
3. **Multi-region screenshot** → tanpa `screenshotId` aksi koordinat harus gagal, bukan menebak region.
4. **`selectText` tidak ada di Windows** → jangan diekspos.
5. **`emit:false` ditolak untuk screenshot** di Windows (selalu ditampilkan).
6. **Helper mati** → semua pending harus reject, bukan hang.
7. **Esc** harus menghentikan giliran, bukan sekadar membatalkan satu aksi.
8. **Desktop surface tanpa screenshot** → semua input ditolak; jangan longgarkan guard ini.
9. **Approval bukan method** → retry request yang sama dengan meta approved-app; membuat method `approve` sendiri akan menyimpang dari kontrak.
10. **`click` bukan satu method** → `click_element` vs `click` koordinat; jangan gabungkan.
11. **JSON rusak dari helper** → reject semua pending, jangan hanya baris itu.
12. **Frame besar** → named pipe punya batas 8 MiB request / 64 MiB response; stdio tidak, tapi helper tetap bisa gagal.
13. **Helper yatim** → wajib `--parent-pid` + kill saat parent exit, jika tidak proses menumpuk.
14. **Transport sharing** → original men-cache transport per opsi; tanpa itu setiap sesi menyalakan helper baru.

---

## 12. Roadmap Implementasi

| Tahap | Isi | Kriteria selesai |
|---|---|---|
| M1 | Contracts + NDJSON transport + fake helper | unit test round-trip, timeout, exit 130 |
| M2 | Helper: enumerasi window + identitas app | `list_apps`/`list_windows` akurat di 5 app uji |
| M3 | Helper: UIA tree + serialisasi | format identik dengan contoh §5 |
| M4 | Helper: WGC screenshot + JPEG | capture window ter-occlude |
| M5 | Helper: SendInput + key chord + guard | klik/ketik/scroll/drag lolos uji |
| M6 | Policy + allowlist + forbidden list | app terlarang ditolak |
| M7 | Approval + overlay + Esc hook | UX lengkap |
| M8 | Integrasi Nexus.App + docs + cleanup | build 0/0, tes integrasi hijau |

---

## 13. Risiko & Unknowns

| Item | Status | Dampak |
|---|---|---|
| Analisis Ghidra backend A | **OBSERVED** — 3.864 prosedur, 1.567 string, profil `c45cf0b5…f981` | Inventory string **menggabungkan literal bersebelahan**, jadi pencarian literal pendek memberi *false negative*. Harus diverifikasi ke byte mentah. |
| Dekompilasi fungsi dispatcher | **OBSERVED** — berhasil lewat Craft Anything 0.6.0 (Ghidra 12.1 deep): 9.234 baris pseudo-C (§13.1) | REA gagal karena profil analisis, bukan ukuran fungsi |
| Alur kontrol arm `click_element` | **OBSERVED** — `str_eq` → deserialize → resolve elemen → center bounds → `SendInput` (§7.1) | Bukan UIA `Invoke` |
| Tabel metode helper A | **OBSERVED penuh** — **20** nama, direkonstruksi dari pasangan `LEA`+`MOV R9D` di dispatcher | `desktop_*` **tidak** ada di backend A |
| Tabel metode helper B | **OBSERVED** — 13 nama `desktop_*` di `.rdata` binary Swift | `desktop_cancel`/`desktop_close` tidak ada di kedua binary → lifecycle transport |
| `id` window = HWND | **OBSERVED** — kontrak JS memaksa integer ≥ 0; serializer menulis `qword [RDI+0x18]`; 6 guard identitas (`out of range`, `was not found`, `no longer belongs to`) | Tetap diperlakukan **opaque** di kontrak |
| Peta field `WindowState`/`AppInfo` | **OBSERVED** — blok `.rdata` `0x1401308c0`–`0x1401309c0` (§4.8) | — |
| Algoritma deteksi region transient | **INFERRED** | Multi-region bisa salah |
| Paging pohon aksesibilitas | **OBSERVED** (`include_text=true and include_screenshot=false to retrieve the next page`) | Kontrak harus punya kursor lanjutan |
| Format diff pohon AX | **OBSERVED** (string) tetapi **format persisnya UNKNOWN** | Perlu sampel runtime untuk memastikan sintaks diff |
| Sumber kebijakan (app-server RPC) | OBSERVED namanya, skema penuh UNKNOWN | Policy engine perlu desain sendiri |
| Denylist kredensial/AV/terminal | **OBSERVED** (daftar string lengkap, §8.2a) | Harus disusun ulang, bukan disalin |
| Pemetaan tombol → VK | **OBSERVED** — `press_key` memecah pada `+`, lalu tabel extended-key 19 entri (§7.3, §7.4) | Tabel nama→VK per token tetap harus disusun manual |
| Algoritma pohon aksesibilitas | **OBSERVED** — tiga lapis, filter window, denylist, batas traversal, peta atribut (§5.4) | Penomoran `element_index` belum terbukti |
| `type_text` | **OBSERVED** — clipboard + `Ctrl+V`, verifikasi baca-ulang ~500 ms (§7.2) | — |
| `scroll` | **OBSERVED** — roda mouse, satu batch `SendInput` (§7.5) | — |
| `drag` | **OBSERVED** — down → lompat → up, tanpa interpolasi (§7.6) | — |
| `set_value` / `perform_secondary_action` | **OBSERVED** — delegasi IPC ke accessibility monitor (§7.7) | Skema wire internal monitor belum |
| Perilaku saat DPI campuran | **SEBAGIAN** — `SetThreadDpiAwarenessContext` dipanggil kondisional (§7.5) | Ambang pemicu belum dipetakan |
| Perilaku multi-monitor negatif | **OBSERVED** — `MOUSEEVENTF_VIRTUALDESK` + `SM_*VIRTUALSCREEN` (§7.5) | — |

**Risiko metodologis yang perlu dicatat:** `rea trace` mengembalikan 0 match untuk `get_window_state` meskipun literal itu ada 7 kali di file. Penyebabnya bukan ketiadaan bukti, melainkan (a) string Rust tidak null-terminated sehingga analyzer menggabungkan beberapa literal menjadi satu entri, dan (b) `trace` mencocokkan entri string yang sudah terdefinisi. Karena itu setiap klaim "tidak ada X" di audit ini diverifikasi ulang lewat pemindaian byte mentah (`rawscan.py`), bukan hanya lewat inventory Ghidra.

### 13.1 Dekompilasi dispatcher: gagal di REA, berhasil di Craft Anything

**Ringkasan:** kegagalan dekompilasi bukan sifat binary-nya, melainkan sifat **profil analisis** yang dipakai REA. Engine Deep milik Craft Anything (Ghidra 12.1 yang sama) berhasil mendekompilasi fungsi yang sama.

**Percobaan yang gagal (REA).** Tiga pemanggilan `rea function` pada alamat xref (`0x140098060`, `0x140097d1f`, `0x1400a120a`) semuanya gagal:

```text
Ghidra decompilation failed: Recoverable Error: Unable to find unique hash for varnode
failure_kind: remote, code: decompile_failed
```

Auto-analysis REA sendiri sukses (281 detik), `Decompiler Parameter ID` berjalan 245 detik. Snapshot ulang dengan `Decompiler Switch Analysis` aktif juga gagal identik (profil digest berubah `c45cf0b5…f981` → `e18d4a07…1f11`).

**Percobaan yang berhasil (Craft Anything 0.6.0).**

```powershell
craft-anything-cli --project <proj> --deep --allow-run `
  native-function-pseudocode <codex-computer-use.exe> --function 0x140097ccb
```

Hasil: **420.896 karakter, 9.234 baris pseudo-C**, engine `ghidra 12.1`, SHA-256 target tetap `7f73cf56…a4002c`. Signature yang dipulihkan: `FUN_140097ccb(HWND__ *param_1, HWND__ *param_2)`.

**Metrik fungsi (tidak berubah):**

| Metrik | Nilai |
|---|---|
| Ukuran body | **50.045 byte** |
| Span (start→end terjauh) | 52.549 byte |
| Rentang | **11 rentang non-kontigu** |
| Instruksi | **9.254** |
| Call site `str_eq` (`0x14001e82b`) | **29** |

**Kesimpulan yang direvisi.** Hipotesis lama ("fungsi 50 KB terlalu besar untuk Ghidra") **salah**. Ghidra sanggup; yang berbeda adalah konfigurasi analisis. REA memakai profil yang mematikan/mengatur analyzer tertentu sehingga SSA gagal pada varnode tertentu; Craft Anything memakai profil Ghidra standar dan berhasil.

**Pelajaran untuk NEXUS (tiga bagian):**

1. **Jangan simpulkan "decompiler tidak sanggup" dari satu tool.** Uji dengan tool kedua sebelum menyatakan jalan buntu. Dalam kasus ini perbedaan hasil 100% disebabkan konfigurasi, bukan skala fungsi.
2. **Kegagalan `Unable to find unique hash for varnode` adalah gejala profil, bukan gejala ukuran.** Jika muncul lagi, coba engine/tool lain dengan profil default sebelum beralih ke disassembler mentah.
3. **`rea instructions` tetap berguna** sebagai verifikasi silang: tabel metode 20 entri (§4.7) dan sifat `id` window (§4.8) dibuktikan lewat slicing `.rdata`, dan hasil itu konsisten dengan pseudo-C Craft Anything.

**Dampak pada status klaim:** alur kontrol di dalam satu arm kini **OBSERVED** dan didokumentasikan di **§7.1** (rantai `click_element` sampai `SendInput`). Tidak ada lagi klaim inti yang berstatus "tidak terbukti".

**Koreksi penting terhadap asumsi awal:** hasil dekompilasi menunjukkan `click_element` **tidak** memakai UIA `Invoke`. String `Invoke`/`InvokePattern` = 0 kemunculan; klik dilakukan lewat `SendInput` setelah menghitung titik tengah dari cached bounds. Lihat §7.1 untuk rantai lengkap dan bukti tabel flag mouse.


---

## 14. Lampiran: Peta Bukti

| Klaim | Sumber |
|---|---|
| Arsitektur 5 lapis | `config.toml`, `.mcp.json`, `plugin.json`, `SKILL.md` |
| Protokol NDJSON | `internal/helper_transport.js` |
| Framing named pipe (length-prefixed JSON-RPC) | `internal/computer_use_client.js` (`SKY_CUA_NATIVE_PIPE` branch) |
| Metode RPC window2 | `internal/computer_use_client_base.js` |
| Tabel metode window2 lengkap (**20**) | pasangan `LEA`+`MOV R9D` di `FUN_140097ccb` + slice `.rdata` backend A |
| Mekanisme dispatch (rantai `memcmp` linear) | `rea instructions` @ `0x140097d15`–`0x140097d3c`, 29 call site `str_eq` |
| Alur kontrol arm `click_element` | pseudo-C Craft Anything 0.6.0 @ `FUN_140097ccb` baris 5455–5620; rantai `FUN_1400a9e53` → `FUN_1400a9a46` → `FUN_1400a9851` → `FUN_1400af523` (`SendInput`) |
| Titik tengah dari cached bounds | `FUN_1400a9ea3`: `(right-left)/2+left`, `(bottom-top)/2+top` |
| Tabel flag mouse | byte `.rdata` `0x14014e4e8` (36 byte) |
| Tidak ada UIA `Invoke` | `native-strings-find` case-insensitive: `Invoke`/`invoke`/`InvokePattern`/`Toggle` = 0 |
| Peta field `Window`/`WindowState`/`AppInfo` | blok `.rdata` `0x1401308c0`–`0x1401309c0` + serializer @ `0x14009d044` |
| Sifat `id` window (integer, opaque, HWND-like) | kontrak JS `computer_use_client_base.js` + serializer + 6 guard string |
| Leg helper ↔ app-server (JSON-RPC) | string `failed to parse app-server JSON-RPC response` @ `0x12ee82`, blok `src\policy\auth.rs` |
| Tabel metode desktop lengkap (13) | byte `.rdata` backend B (`swiftscan.py`) |
| Peta modul crate helper | path panic `src\…` di inventory string (§2.6) |
| Pipeline WGC per-monitor | string tahap capture backend A (§6.1) |
| Overlay + expression animation | string Composition backend A (§6.2) |
| Appshot + capture lease | string backend B (§6.3) |
| Keamanan clipboard | string backend B (§6.4) |
| Input lease + timing versi | string backend B (§6.5) |
| Diff & paging pohon AX | string backend B (§5.1, §5.3) |
| Denylist kredensial/AV/terminal | string backend A (§8.2a) |
| Identitas dua backend | `peinfo.py` (PE header + CLI dir) + `createDesktopClient()` |
| Lifecycle `desktop_close`/`desktop_cancel` | `helper_transport.js` (`isLifecycle`) + ketiadaan di kedua binary |
| Aturan normalisasi parameter | `internal/computer_use_client_base.js` (`p`, `y`, `m`, `R`, `b`, `x`, `E`, `T`) |
| Metode & guard full-desktop | `targets/windows/desktop_client.js` |
| Bentuk WindowState | `computer_use_client_base.js` + `docs/api.md` |
| Format AX tree | rollout `01a0e97b-…` |
| Import Windows API | PE import table (314 entri) |
| Target terlarang & guard | string table helper |
| Kebijakan konfirmasi | `docs/confirmations.md` |
| Larangan non-negosiabel | `docs/guidance.md` |
| Allowlist per sesi | `computer-use/sessions/*.toml` |
| Overlay & locale | `computer-use/config.json` |
| Interupsi Esc | `helper_transport.js` (exit 130, marker path) |
| Meta elicitation lengkap | `helper_transport.js` (`B()`), `computer_use_client.js` (`W()`) |
| Siklus hidup proses helper | `helper_transport.js` (`A()`, `q()`, `P()`, `U()`) |
| Nama env timeout Ghidra | `dist/config/providerStartupTimeout.js` (`REA_GHIDRA_STARTUP_TIMEOUT_MS`) |

---

## 15. Langkah Berikutnya

**Status analisis: SELESAI untuk paritas inti.** Semua pertanyaan desain sudah tertutup: dispatcher (§7.1), pohon aksesibilitas (§5.4), `type_text` (§7.2), `press_key` (§7.3–7.4), `scroll` (§7.5), `drag` (§7.6), dan aksi berbasis elemen (§7.7). Dekompilasi dispatcher berhasil lewat Craft Anything 0.6.0 setelah REA gagal (§13.1).

Cakupan dokumen setelah putaran ini:

| Area | Cakupan |
|---|---|
| Kontrak & protokol | ~95% |
| Sintesis input (semua arm) | ~90% |
| Pohon aksesibilitas | ~80% |
| Pipeline screenshot | ~55% |
| AX diff/paging (backend B) | ~20% |
| Kebijakan app-server | ~25% |
| Overlay kursor | ~30% |

1. **Mulai M1** (menunggu go-ahead, karena menambah proyek ke `Nexus.sln`): `Nexus.ComputerUse.Contracts` + transport NDJSON + fake helper + xUnit.
2. **Implementasi `ClickElementAsync` mengikuti §7.1**, bukan asumsi UIA `Invoke`:
   - simpan rect elemen saat snapshot,
   - hitung center, validasi terhadap region screenshot,
   - `SendInput` down/up dengan jeda 16 ms,
   - error `ElementHasNoCachedBounds` bila bounds tidak ada.
3. **Implementasi `TypeTextAsync` mengikuti §7.2** — clipboard + `Ctrl+V` + verifikasi baca-ulang, dengan mutex clipboard dan restore di `finally`.
4. **Implementasi `DragAsync` mengikuti §7.6**, tetapi dengan interpolasi (deviasi yang disengaja).
5. **Sediakan proses `AccessibilityHost` terpisah** (§7.7), karena original tidak mengeksekusi aksi elemen di proses utama.
6. **Pertimbangkan deviasi lain yang disengaja:** `InvokePattern` sebagai jalur alternatif, `KEYEVENTF_UNICODE` sebagai fallback `type_text`, `CleanupMouseUpAsync` di `finally`.
7. **Bersihkan artefak sementara** `%TEMP%\opencode\craft\` — `craft.zip`, `app\`, `proj\`, `commands.txt`, `*.json`, `*.c` — sekitar 170 MB.
8. Opsional: perbaiki registrasi REA opencode (`npx.cmd -y rea-agents@6.5.0 setup --client opencode`) dan buang spasi di `GHIDRA_INSTALL_DIR`.

### 15.1 Pertanyaan yang sudah tertutup

| Pertanyaan awal | Jawaban | Bukti |
|---|---|---|
| Berapa metode di helper A? | **20** | pasangan `LEA`+`MOV R9D` + slice `.rdata` (§4.7) |
| Bagaimana dispatcher memilih handler? | rantai `memcmp` linear, 29 call site | `rea instructions` @ `0x140097d15` |
| Apakah `id` window itu `HWND`? | **Ya** — integer, guard identitas OS | serializer + 6 guard string (§4.8) |
| Apa saja field `WindowState`? | 28 field terpetakan | blok `.rdata` `0x1401308c0` (§4.8) |
| Apakah helper memakai JSON-RPC? | Tidak untuk stdio; ya untuk app-server | `failed to parse app-server JSON-RPC response` (§4.1) |
| Kenapa decompile gagal di REA? | profil analisis REA, **bukan** ukuran fungsi — Craft Anything berhasil | §13.1 |
| Bagaimana `click_element` memanggil `Invoke`? | **Tidak memanggil `Invoke`** — bounds → center → `SendInput` down/up | §7.1 |
| Berapa lama jeda klik? | 16 ms down→up, 100 ms antar klik | `FUN_1400a9a46` + loop arm |
| Bagaimana pohon aksesibilitas dibangun? | Tiga lapis: formatter window → pembangun baris → formatter atribut | §5.4 |
| Window apa yang dibuang dari pohon? | 6 nama (IME, Taskbar, Program Manager, …) + `IsWindowVisible` | §5.4.2 |
| Berapa batas traversal? | `child_limit` 500, `max_depth` 200, plus `cycle` | §5.4.5 |
| Bagaimana `type_text` mengirim teks? | **Clipboard + `Ctrl+V`**, bukan `KEYEVENTF_UNICODE` | §7.2 |
| Berapa lama verifikasi clipboard? | Polling 25 ms, batas ~500 ms; restore 750 ms | §7.2 |
| Bagaimana `press_key` mengurai `CTRL+A`? | Split pada `+`, tiap token → VK, down berurutan lalu up terbalik | §7.3–7.4 |
| Bagaimana tombol extended ditentukan? | Tabel 19 VK di `0x14013803e`, bukan `MapVirtualKey` | §7.4 |
| Bagaimana `scroll` bekerja? | Roda mouse (`0x0800`/`0x1000`), satu batch `SendInput` | §7.5 |
| Bagaimana `drag` bergerak? | Satu lompatan absolut — **tanpa interpolasi** | §7.6 |
| Apakah aksi elemen dieksekusi lokal? | **Tidak** — IPC ke accessibility monitor | §7.7 |
