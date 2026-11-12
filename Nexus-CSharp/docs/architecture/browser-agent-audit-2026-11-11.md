# Audit Browser Agent NEXUS: Temuan dan Rencana Perbaikan

Tanggal: 11 November 2026  
Ruang lingkup: **NEXUS sebagai satu-satunya subjek audit**. OpenMausBot dan browser-use hanya menjadi pembanding pola arsitektur dari audit sebelumnya.  
Metode: pembacaan source dan inspeksi statis binary Release menggunakan Craft Anything 0.6.0. Tidak ada target yang dijalankan dan tidak ada source/config/dependency yang diubah.

## 1. Kesimpulan eksekutif

**Verdict NEXUS Browser Agent: PARTIAL — belum cukup bukti untuk menyatakan alur otonom andal atau PASS.**

NEXUS memiliki komponen browser dan loop agent yang nyata: intent diarahkan ke `DynamicReActControlLoop`, halaman diamati melalui snapshot WebView2, lalu aksi dijalankan melalui adapter/JavaScript. Namun audit menemukan kelemahan pada kontrak keputusan, ketersediaan planner lokal, kebenaran status aksi/navigasi, verifikasi hasil, dan approval. Akibatnya, ada jalur yang dapat melaporkan progres atau keberhasilan tanpa bukti yang cukup bahwa postcondition tugas benar-benar tercapai.

OpenMausBot dipakai sebagai pembanding terbatas: ia menunjukkan pola tool terstruktur, aksi browser yang eksplisit, observasi AX/screenshot, status halaman, evidence per langkah, dan kontrol egress. Ini adalah sasaran pola, bukan rekomendasi menyalin seluruh arsitekturnya. browser-use menjadi pembanding untuk loop persisten, hasil parsial dan verifikasi eksplisit; detail internal dependensi eksternalnya tidak diklaim.

## 2. Arsitektur NEXUS yang diaudit

```text
ConversationViewModel
  → routing intent Browser/Academic
  → DynamicReActControlLoop
  → snapshot DOM/WebView2
  → keputusan model berformat JSON atau fallback heuristik
  → adapter/dispatcher WebView2 dan JavaScript
  → hasil aksi dan observasi berikutnya
```

Komponen utama:

| Tanggung jawab | Lokasi |
|---|---|
| Loop keputusan dan fallback | `src/03_App/Nexus.App/Services/BrowserAgent/DynamicReActControlLoop.cs` — `RunDynamicAgentLoopAsync`, `ParseModelDecisionJsonOrFallback`, `BuildDefaultAutonomousStepDecision` |
| Pemanggilan planner/model dan routing intent | `src/03_App/Nexus.App/ViewModels/ConversationViewModel.cs` — `TryInterceptBrowserOrAcademicIntent`, `QueryActiveLlmForBrowserStepAsync` |
| Adapter aksi | `src/03_App/Nexus.App/Services/BrowserAgent/WebView2NativeAdapterAndDispatcher.cs` |
| Host browser dan eksekusi JavaScript | `src/03_App/Nexus.App/Views/AgentWorkspaceView.xaml.cs` — `ExecuteLiveScriptOnWebView2Async`, `LiveJsEvaluator`, `NavigateEmbeddedBrowserAsync` |

## 3. Temuan audit: kekurangan NEXUS

| ID | Temuan | Dampak | Dasar audit |
|---|---|---|---|
| N-01 | Keputusan tool diminta sebagai JSON teks lalu diparse dengan regex, bukan kontrak tool/function-call yang tervalidasi. | JSON rusak/ambigu dapat masuk fallback; skema tindakan lebih sulit dijaga konsisten. | `DynamicReActControlLoop.cs`, `ParseModelDecisionJsonOrFallback`; IL method loop/fallback diperiksa pada binary Release. Evidence Craft Anything `ev_5de67c06d76c10f39844d8c84739390cdc199b7663b6eebc284df655168a4f03`. |
| N-02 | Planner Browser hanya meneruskan request ke jalur cloud; untuk model yang tidak berawalan `cloud:` state machine mengembalikan `string.Empty`. | Model lokal tidak memberikan langkah planner pada jalur ini; loop harus memakai fallback lain atau tidak memperoleh keputusan planner. | Dikonfirmasi dalam IL state machine `<QueryActiveLlmForBrowserStepAsync>d__539::MoveNext` pada binary Release: pemeriksaan prefix `cloud:` bercabang ke `string.Empty`. Evidence `ev_b0794ff3658b549c88070cca05bf19cda1cd2555419ecde4cf1630fa4ace0fb7`. |
| N-03 | Fallback keputusan berisi aturan deterministik/heuristik. | Cakupan tugas web kompleks dan situs yang tak dikenal terbatas; keputusan dapat tampak masuk akal tetapi tidak sesuai maksud pengguna. | `DynamicReActControlLoop.cs`, `BuildDefaultAutonomousStepDecision`; IL menunjukkan cabang keputusan dari prompt, snapshot dan pola pencarian. Evidence `ev_5de67c06d76c10f39844d8c84739390cdc199b7663b6eebc284df655168a4f03`. |
| N-04 | Beberapa operasi adapter, termasuk `FillAsync`, `SelectAsync`, dan `ScrollAsync`, dilaporkan sebagai no-op pada jalur yang diaudit. | Loop berisiko melanjutkan seolah aksi selesai padahal halaman tidak berubah. | `WebView2NativeAdapterAndDispatcher.cs`, source audit pada method terkait. Perlu uji integrasi untuk memastikan perilaku di konfigurasi runtime aktual. |
| N-05 | Status URL browser dapat diperbarui secara optimistis sebelum navigasi berhasil dikonfirmasi. | Planner dan UI dapat memakai URL yang diniatkan, bukan halaman yang benar-benar dimuat; error navigasi dapat tersamarkan. | `AgentWorkspaceViewModel.cs` dan `AgentWorkspaceView.xaml.cs` pada jalur navigasi; bandingkan dengan `NavigationCompleted` dan status hasil navigasi. |
| N-06 | Kriteria sukses loop terlalu dekat dengan jumlah kegagalan berurutan yang belum mencapai ambang, bukan verifikasi tujuan/postcondition. | “Tidak gagal” dapat disalahartikan sebagai “berhasil”; tidak cukup untuk memastikan formulir terkirim, hasil ditemukan, atau halaman yang benar terbuka. | `DynamicReActControlLoop.cs` pada pemeriksaan `consecutiveFailures` dan penyelesaian checklist (source audit). |
| N-07 | Tidak ditemukan screenshot sebagai input observasi utama pada jalur agent yang diaudit; dominan DOM/teks. | Layout visual, dialog atau elemen yang tidak terwakili baik di DOM dapat luput. | Jalur snapshot dan eksekusi di `DynamicReActControlLoop.cs`/`AgentWorkspaceView.xaml.cs`; source audit sebelumnya tidak menemukan pemanggilan capture screenshot untuk agent. |
| N-08 | Callback approval yang diperiksa mengembalikan `false` secara langsung. | Ini merupakan penolakan tetap, bukan mekanisme persetujuan manusia yang dapat menyetujui atau menolak berdasarkan permintaan. | `ConversationViewModel.cs`, callback approval sekitar baris 3818 pada source audit. Method binary async terkait belum diperiksa tuntas. |
| N-09 | Tidak ada ledger evidence seragam yang menghubungkan niat → aksi → observasi → verifikasi → klaim akhir pada setiap langkah. | Sulit menjelaskan atau mengaudit dasar jawaban akhir, terutama ketika aksi gagal sebagian. | Dibandingkan dengan pola `BrowserSteps`/`BrowserEvidence` OpenMausBot yang diperiksa sebagai referensi, bukan klaim bahwa seluruh workflow-nya selalu benar. |

### Catatan evidence binary

Craft Anything memeriksa `Nexus.App.dll` Release di `src/03_App/Nexus.App/bin/Release/net8.0-windows/`. Hash yang diperiksa: `124d53097b06122e14f97a740168851adb08dda10de8bc71d7db9d30ec55ff72`. Assembly teridentifikasi sebagai .NET 8, IL-only, AnyCPU, 898 tipe dan 7,606 method. Evidence: `ev_ea791798cad0f95314b28440fe742f528b0266f754384bda77072715ad9a7ab2`.

IL `DynamicReActControlLoop` diperiksa statis (evidence `ev_5de67c06d76c10f39844d8c84739390cdc199b7663b6eebc284df655168a4f03`). State machine planner lokal diperiksa terpisah dan mengonfirmasi cabang `string.Empty` bagi model non-cloud (`ev_b0794ff3658b549c88070cca05bf19cda1cd2555419ecde4cf1630fa4ace0fb7`). Temuan callback approval selalu `false` berasal dari source; method/state machine binary terkait belum diisolasi, sehingga untuk build Release poin itu perlu konfirmasi lebih lanjut. Graph assembly `ev_d983ef40e612699505f53cd65e98054da421a6a0a8ec6ae3ddb45e44a31e7712` dipakai sebagai orientasi dependency, bukan bukti lengkap alur panggilan.

## 4. Peta arsitektur pembanding

Bagian ini memetakan logika OpenMausBot dan browser-use agar rekomendasi NEXUS punya konteks. Keduanya **bukan subjek verdict**; status NEXUS pada bagian 1–3 tetap menjadi fokus dokumen.

### 4.1 OpenMausBot — browser runtime/tool surface

Peta aplikasi dibuat oleh Craft Anything `js-app-map` dalam Deep mode, statis tanpa menjalankan aplikasi. Target `D:\NEXUS\OpenMausBot-main`, SHA-256 `e47141c72be6eb0ef2ed07f8e18c450aa55ddee241a805f0649e5f434aaf2435`. Evidence map: `ev_acb0a709dbd3c6f0ea41e4c303bbdd059a6ac86a340ee2a1fa66b237e5b144f5`.

```text
CLI agent eksternal (Claude/Codex/ACP)
  → server/browser/tools.ts
      tool names + Zod schema + parse/validation
  → server/browser/dispatch.ts
      address validation + tab selection + control gate + deadline
  → BrowserService / runtime / engine / relay
  → server/browser/cdp/*
      Chrome DevTools Protocol: page, frames, AX snapshot, actions, input
  → result shaping + page health
  → server/browser-steps.ts + browser-evidence.ts
      action record, geometry/marker, screenshot/evidence
  → tool result back to the external CLI agent
```

Pemisahan peran dan bukti source utama:

| Lapisan | Implementasi yang terpetakan | Arti bagi NEXUS |
|---|---|---|
| Kontrak tool | `server/browser/tools.ts:20-90` mendefinisikan nama aksi dan schema input; bagian lanjutannya membangun daftar tool dan memvalidasi panggilan. | Aksi memiliki bentuk input yang eksplisit, bukan hanya instruksi JSON bebas. |
| Dispatch | `server/browser/dispatch.ts:23-32,47-63,90-100` memisahkan timeout, follow-up observasi, konteks kontrol, status tab, dan pemilihan tab. | Ada tempat terpusat untuk validasi, deadline dan kebijakan sebelum aksi. |
| Driver | `server/browser/cdp/actions.ts`, `input.ts`, `keyboard.ts`, `snapshot.ts`, `screencast.ts`. | Aksi browser, input, accessibility tree dan screenshot adalah komponen yang berbeda dan dapat diuji. |
| Guard/runtime | `server/browser/control.ts`, `session-control.ts`, `browser/egress/*`, `browser-runtime.ts`, `browser-lane.ts`. | Ada pemisahan kontrol person/agent, kebijakan jaringan dan batas concurrency/budget. |
| Verifikasi/evidence | `browser-page-health.ts`, `browser-steps.ts`, `browser-evidence.ts`, `browser-tool-shape.ts`. | Hasil aksi dapat dibandingkan dengan state sesudah aksi dan direkam untuk audit. |
| Brain/loop | Tidak dimiliki subsistem browser ini; keputusan berikutnya berasal dari CLI agent yang menggunakan tools. | Jangan menganggap browser runtime ini sebagai agent loop mandiri. NEXUS tetap memerlukan loop/planner sendiri. |

Craft Anything mendeteksi 5,918 modul, 269 kanal IPC (183 berpasangan), 21 window, 489 route dan 5,609 endpoint di seluruh repo. Coverage peta parsial (6,035/7,393 item diperiksa), dengan kanal IPC dinamis dan node cap sebagai limitasi. Angka ini bukan hitungan khusus production/browser dan tidak dipakai untuk menyimpulkan kualitas runtime.

### 4.2 browser-use 1.0.2 — agent loop/code-as-action

Craft Anything 0.6.0 pada instalasi ini **tidak memiliki analyzer untuk Python source**; tool yang tersedia untuk Python hanya pembacaan bytecode terkompilasi. Percobaan `chat` pada target juga tidak berjalan karena AI backend Craft belum disetel. Target `D:\NEXUS\browser-use-main` dibuka di Craft dalam Deep mode (folder SHA-256 `4028bba6e175a48b23a97e5c5504a3b15793c83aa9847e75e71ad4104f9b3467`), tetapi peta logika berikut berasal dari pemeriksaan source Python langsung, bukan klaim bahwa Craft melakukan dekompilasi Python.

```text
Agent(task, llm, browser).run()                 agent/service.py
  → resolve model + Browser.start()             llm/__init__.py, browser/browser.py
  → Pydantic AI loop                            agent/loop.py
      python(code) / finish / finish_from_repl
      → Repl.call()                             repl/session.py
          → worker process                      repl/worker.py
              browser-harness helpers + raw CDP (dependency eksternal)
              AX helpers, screenshot, checkpoint, type_secret
  → delivery guard + AgentHistoryList            agent/loop.py, agent/views.py
```

| Lapisan | Source dan perilaku yang tampak | Batas yang perlu diingat |
|---|---|---|
| Entry/lifecycle | `browser_use/agent/service.py:1,23-45` menyambungkan `Agent.run`, model, Browser, Repl, Tools, dan hasil history. | Service membungkus loop; bukan implementasi browser action sendiri. |
| Loop agent | `agent/loop.py:117-160,163-197` menyimpan state run dan menjalankan tool `python(code)` berurutan pada REPL persisten; tool kustom juga dapat didaftarkan (`tools.py`). | Model harus mampu menulis kode Python yang tepat; ini bukan daftar aksi atomik saja. |
| Browser API | `browser/browser.py:1-9,47-53` membedakan Browser cloud default, Chromium lokal, dan Chrome yang di-attach. | Default `Browser()` menggunakan cloud dan memerlukan API key; biaya/runtime tidak diuji dalam audit ini. |
| Worker boundary | `repl/session.py:1-12,40-55,78-87` menjelaskan subprocess, allowlist environment, redaksi secret, dan menyatakan worker bukan sandbox. `repl/worker.py` menyediakan helper dan mengelola sel. | Kode model memiliki akses Python sebagai pengguna; redaksi secret bukan isolasi keamanan. |
| Observasi/aksi | `agent/prompt.py:9-28` mendeskripsikan helper, tab, raw CDP, AX tree, mouse/key, screenshot dan instruksi verifikasi setelah mutasi. `repl/capture.py:11-35` menangani screenshot via `Page.captureScreenshot`. | Sebagian helper browser-harness berada di dependency eksternal, sehingga detail internal/actionability-nya tidak dibuktikan dari repo ini. |
| Checkpoint/delivery | `agent/prompt.py:30-42`, `agent/loop.py:81-112,214-276,414-438` menetapkan checkpoint parsial, delivery-only turn, batas waktu/langkah dan guard hasil. | Verifikasi tetap mencakup arahan model; adanya prompt/guard bukan bukti bahwa semua task berhasil. |
| Timeout/recovery | `Session` menetapkan timeout run 1,700s, max 1,000 step dan cell timeout 120s (`loop.py:117-125`); parent REPL kill/restart dibahas di `repl/session.py:1` dan `_kill_tree` sekitar baris 222. | Waktu, reliability dan biaya tidak diukur secara runtime. |

### 4.3 Pemetaan tiga alur secara ringkas

```text
NEXUS
intent → loop C# → snapshot WebView2 → JSON/heuristik → adapter/JS → status/error

OpenMausBot
CLI agent → tool schema → dispatch/runtime → CDP aksi → snapshot/health/evidence → CLI agent

browser-use 1.0.2
Pydantic AI loop → python(code) → REPL worker persisten → browser-harness/raw CDP
             → teks/image/checkpoint → loop → finish/finish_from_repl
```

Perbedaan inti: OpenMausBot menyediakan **tool surface dan browser runtime**, sedangkan loop agent berada di luar; browser-use menyediakan **loop agent dan eksekusi kode persisten**, dengan browser helpers dari dependency; NEXUS menyatukan loop dan host WebView2 dalam aplikasi, tetapi audit menemukan kelemahan pada kontrak aksi, planner lokal, status aksi, verifikasi dan evidence.

## 5. Pembanding yang relevan

| Kekurangan NEXUS | Pola pembanding | Yang layak diadopsi |
|---|---|---|
| JSON teks dan regex (N-01) | OpenMausBot memiliki tool browser dengan schema dan validasi input. | Kontrak aksi terstruktur; validasi argumen dan respons sebelum dispatch. |
| Aksi dapat no-op (N-04) | OpenMausBot memisahkan actionability, aksi CDP, dan hasil tool. | Adapter wajib mengembalikan sukses hanya setelah aksi dijalankan; jika tidak didukung, kembalikan error eksplisit. |
| URL optimistis (N-05) | Runtime browser pembanding mengamati state halaman setelah navigasi dan menjelaskan perubahan. | Ambil URL/title dari state aktual setelah event navigasi selesai. |
| Sukses tanpa postcondition (N-06) | browser-use memisahkan hasil parsial dan mendorong verifikasi; OpenMausBot merekam step/evidence. | Definisikan postcondition per tindakan dan tandai attempted/completed/verified secara terpisah. |
| Hanya DOM/teks (N-07) | OpenMausBot menyediakan AX snapshot dan screenshot; browser-use mengirim screenshot sebagai image ke model. | Tambahkan screenshot/AX secara selektif, terutama saat DOM tak cukup atau verifikasi gagal. |
| Approval statis (N-08) | OpenMausBot memiliki kontrol person/agent; approval seharusnya berupa alur eksplisit. | Approval UI dengan konteks tindakan, timeout, keputusan, dan audit trail. |

## 6. Rencana perbaikan bertahap

### Tahap 0 — Kunci baseline dan uji regresi

- Buat fixture halaman lokal/terisolasi untuk navigasi sukses/gagal, input, select, scroll, submit, dialog, dan elemen yang tidak ditemukan.
- Tambahkan assertion bahwa adapter yang tidak mendukung aksi mengembalikan kegagalan, bukan sukses.
- Catat baseline: URL aktual, status navigasi, snapshot sebelum/sesudah, aksi, dan hasil verifikasi.
- Jangan menguji dengan data akun atau browser aktif milik pengguna.

### Tahap 1 — Kebenaran status dan hasil aksi (N-04, N-05)

- Perbaiki implementasi aksi no-op atau gagalkan secara eksplisit jika bridge/fitur tidak tersedia.
- Perbarui URL/status hanya dari event hasil navigasi WebView2; simpan status sukses/error.
- Jadikan hasil dispatch berisi status aksi yang eksplisit: `succeeded`, `failed`, atau `unsupported`.
- Tambahkan uji integrasi untuk setiap aksi dan navigasi.

### Tahap 2 — Verifikasi dan evidence per langkah (N-06, N-09)

- Setelah mutasi, ambil snapshot baru dan evaluasi postcondition yang spesifik; jangan menyimpulkan sukses hanya dari tidak adanya exception.
- Pisahkan status `attempted`, `completed`, `verified`, dan `failed` di model hasil.
- Rekam input teredaksi, target, observasi sebelum/sesudah, URL aktual, dan alasan verifikasi. Hindari menyimpan rahasia atau isi sensitif halaman.
- Kriteria selesai harus menyatakan bukti yang memenuhi tujuan pengguna; jika bukti tidak ada, hasil harus `partial`/`unknown`.

### Tahap 3 — Kontrak keputusan dan planner (N-01, N-02, N-03)

- Ganti keluaran JSON teks/regex dengan tool/function-call terstruktur, schema versioned, dan validasi sebelum eksekusi.
- Pertahankan fallback hanya sebagai jalur terbatas yang diberi label/log, bukan keberhasilan diam-diam.
- Aktifkan planner untuk model lokal dengan timeout dan fallback terukur; catat mengapa planner tidak dipakai.
- Tambahkan uji kontrak untuk output valid, malformed, timeout, tool tak dikenal, dan model lokal.

### Tahap 4 — Observasi multimodal (N-07)

- Tambahkan capture screenshot melalui WebView2 DevTools Protocol sebagai jalur bantuan, bukan pengganti DOM/AX.
- Gabungkan screenshot dengan metadata ukuran/viewport dan snapshot; pangkas/redaksi informasi sensitif sebelum dikirim ke model.
- Gunakan screenshot saat ambiguity tinggi, aksi gagal, atau verifikasi DOM tidak memadai; ukur biaya dan latensi.

### Tahap 5 — Approval dan boundary keselamatan (N-08)

- Ganti callback selalu-tolak dengan permintaan approval UI yang menyebut tindakan, target, dan dampak.
- Berikan pilihan approve/deny, pembatalan, timeout, dan pencatatan keputusan; default harus fail-closed saat tidak ada jawaban.
- Minta approval untuk tindakan berisiko tinggi (misalnya submit, upload, atau perubahan akun) sesuai kebijakan produk.
- Uji bahwa aksi sensitif tidak berjalan sebelum approval diterima.

### Urutan prioritas

1. **P0:** N-04/N-05 — jangan klaim aksi atau navigasi yang tidak terjadi.
2. **P1:** N-06/N-09 — kebenaran hasil, postcondition dan evidence.
3. **P1:** N-08 — approval yang benar-benar interaktif dan fail-closed.
4. **P2:** N-01/N-02/N-03 — kontrak tool dan planner yang stabil.
5. **P3:** N-07 — screenshot multimodal dan optimasi setelah jalur dasar dapat dipercaya.

## 7. Kriteria penerimaan untuk menaikkan verdict

NEXUS dapat dipertimbangkan **PASS** hanya setelah fixture terisolasi membuktikan seluruh kondisi berikut:

1. Setiap tool memiliki schema yang divalidasi; input rusak tidak dieksekusi.
2. Aksi yang tidak didukung atau gagal selalu menghasilkan status gagal/unsupported.
3. URL dan status navigasi sesuai hasil aktual WebView2, termasuk error.
4. Keberhasilan tugas memerlukan postcondition yang teramati; percobaan tanpa bukti tidak dihitung sukses.
5. Approval dapat diberikan/ditolak manusia dan tindakan sensitif terblokir sebelum persetujuan.
6. Evidence per langkah cukup untuk menelusuri keputusan tanpa menyimpan rahasia.
7. Jalur planner lokal, timeout, fallback, dan pembatalan diuji serta terlihat di log.

## 8. Batasan audit

- Tidak ada runtime/browser/target yang dijalankan; kesimpulan adalah hasil inspeksi source dan statis binary.
- Source audit mendukung temuan adapter, routing, URL, approval, dan kriteria loop. Planner lokal kini dikonfirmasi pada IL binary Release. Callback approval tetap temuan source sampai method/state machine binary yang tepat diisolasi.
- OpenMausBot/browser-use adalah pembanding, bukan subjek verdict NEXUS. Peta OpenMausBot Craft Anything bersifat parsial: `ev_acb0a709dbd3c6f0ea41e4c303bbdd059a6ac86a340ee2a1fa66b237e5b144f5`. Pada browser-use, Craft Anything hanya digunakan untuk membuka dan mengidentifikasi target; pemetaan logika didasarkan pada source Python dan keterbatasan analyzer dicatat di §4.2.
- Tidak ada perubahan kode yang dilakukan sebagai bagian dari dokumen audit ini.
