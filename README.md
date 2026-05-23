# Tubes_AyamGaJago

## Pemanfaatan Algoritma Greedy dalam Bot Robocode Tank Royale

Repository ini dibuat untuk memenuhi Tugas Besar 1 mata kuliah **IF25-21013 Strategi Algoritma** dengan topik **Pemanfaatan Algoritma Greedy dalam Pembuatan Bot Permainan Robocode Tank Royale**.

Robocode Tank Royale adalah permainan pemrograman bot berbentuk tank virtual. Dalam permainan ini, pemain tidak mengendalikan tank secara langsung, tetapi membuat program yang berisi logika atau "otak" bot. Program tersebut menentukan cara bot bergerak, memindai musuh, menembak, menghindar, serta bereaksi terhadap kejadian selama pertandingan.

Pada tugas besar ini, strategi yang digunakan adalah **algoritma greedy**. Algoritma greedy digunakan karena bot harus mengambil keputusan secara cepat pada setiap turn. Bot memilih aksi terbaik berdasarkan kondisi saat ini, seperti posisi musuh, jarak, energi, ancaman, posisi arena, dan peluang memperoleh skor.

Repository ini berisi 4 bot:

1. **TankAlbino** sebagai bot utama.
2. **BantengMerah** sebagai bot alternatif 1.
3. **AiranScube** sebagai bot alternatif 2.
4. **Gryffin** sebagai bot alternatif 3.

Setiap bot menggunakan strategi greedy dan heuristic yang berbeda. Tujuan utama dari seluruh strategi adalah memperoleh skor setinggi mungkin pada akhir pertandingan dengan mengoptimalkan komponen skor seperti bullet damage, bullet damage bonus, survival score, last survival bonus, ram damage, dan ram damage bonus.

---

## Struktur Repository

```text
Tubes_AyamGaJago/
├── src/
│   ├── main-bot/
│   │   └── TankAlbino/
│   │       ├── TankAlbino.cs
│   │       ├── TankAlbino.csproj
│   │       ├── TankAlbino.json
│   │       ├── TankAlbino.cmd
│   │       └── TankAlbino.sh
│   └── alternative-bots/
│       ├── alt-bot-1/
│       │   └── BantengMerah/
│       │       ├── BantengMerah.cs
│       │       ├── BantengMerah.csproj
│       │       ├── BantengMerah.json
│       │       ├── BantengMerah.cmd
│       │       └── BantengMerah.sh
│       ├── alt-bot-2/
│       │   └── AiranScube/
│       │       ├── AiranScube.cs
│       │       ├── AiranScube.csproj
│       │       ├── AiranScube.json
│       │       ├── AiranScube.cmd
│       │       └── AiranScube.sh
│       └── alt-bot-3/
│           └── Gryffin/
│               ├── Gryffin.cs
│               ├── Gryffin.csproj
│               ├── Gryffin.json
│               ├── Gryffin.cmd
│               └── Gryffin.sh
├── doc/
│   └── laporan.pdf
└── README.md
```

Folder `src/` berisi seluruh source code bot. Folder `src/main-bot/` berisi bot utama yang dipilih untuk kompetisi, sedangkan folder `src/alternative-bots/` berisi tiga bot alternatif dengan strategi greedy yang berbeda. Folder `doc/` berisi laporan tugas besar dalam format PDF.

---

## Dasar Penerapan Algoritma Greedy

Algoritma greedy adalah algoritma yang memilih solusi terbaik pada kondisi saat ini tanpa mengevaluasi seluruh kemungkinan solusi jangka panjang. Dalam Robocode Tank Royale, pendekatan greedy cocok digunakan karena bot harus mengambil keputusan dalam waktu singkat pada setiap turn.

Pada setiap turn, bot dapat melakukan beberapa aksi, seperti memindai musuh, memilih target, mengatur arah gerak, mengarahkan radar, mengarahkan gun, menentukan firepower, menembak, atau menghindari bahaya. Setiap aksi tersebut dapat dipilih menggunakan pendekatan greedy, yaitu dengan membentuk beberapa kandidat aksi, memberikan nilai atau skor pada setiap kandidat, lalu memilih kandidat dengan nilai terbaik.

Dalam konteks permainan ini, elemen greedy yang digunakan dapat dijelaskan sebagai berikut:

**Himpunan kandidat** adalah semua kemungkinan pilihan yang dapat dipilih bot pada suatu turn. Kandidat dapat berupa daftar musuh yang terdeteksi, kandidat arah gerak, kandidat posisi, kandidat target, atau kandidat besar firepower.

**Fungsi seleksi** adalah aturan yang digunakan untuk memilih kandidat terbaik. Contohnya adalah memilih musuh dengan skor target tertinggi, memilih musuh terdekat, memilih posisi paling aman, atau memilih jalur gerak dengan nilai terbaik.

**Fungsi kelayakan** digunakan untuk memastikan bahwa kandidat yang dipilih masih valid. Contohnya adalah target masih hidup, data scan musuh masih baru, posisi tujuan masih berada di dalam arena, bot tidak terlalu dekat dengan dinding, dan energi bot masih cukup untuk menembak.

**Fungsi objektif** adalah tujuan yang ingin dimaksimalkan. Pada tugas ini, fungsi objektifnya adalah memperoleh skor akhir setinggi mungkin dengan cara mengoptimalkan damage, survival, kill bonus, dan peluang bertahan hingga akhir ronde.

**Himpunan solusi** adalah keputusan akhir yang diambil bot pada turn tersebut, misalnya target yang ditembak, arah gerak yang dipilih, besar firepower, atau mode taktis yang digunakan.

---

## Penjelasan Strategi Greedy Setiap Bot

## 1. TankAlbino

**TankAlbino** adalah bot utama dalam repository ini. Bot ini dirancang sebagai bot yang seimbang antara menyerang, bertahan, menghindar, dan menjaga posisi di arena. Strategi greedy utama yang digunakan oleh TankAlbino adalah **greedy target scoring**.

Pada strategi ini, TankAlbino mengevaluasi musuh-musuh yang terdeteksi, lalu memberikan skor kepada setiap target. Musuh dengan skor tertinggi akan dipilih sebagai target utama. Skor tersebut dihitung berdasarkan beberapa faktor, seperti jarak musuh, energi musuh, peluang serangan, peluang finishing, ancaman dari musuh, serta kondisi apakah pertandingan masih melee atau sudah duel.

TankAlbino tidak hanya memilih target berdasarkan jarak terdekat. Bot ini juga mempertimbangkan apakah musuh tersebut mudah ditembak, apakah musuh memiliki energi rendah sehingga bisa dihabisi, dan apakah menyerang musuh tersebut dapat memberi keuntungan skor. Dengan begitu, keputusan greedy yang diambil tidak hanya agresif, tetapi juga tetap mempertimbangkan peluang bertahan hidup.

Pada bagian movement, TankAlbino juga menggunakan pendekatan greedy untuk memilih posisi yang relatif aman. Bot berusaha menjaga jarak ideal, menghindari dinding, menghindari posisi yang berpotensi terkena crossfire, dan tetap mempertahankan sudut tembak yang menguntungkan. Movement yang digunakan cenderung orbit dan evasive agar bot tidak mudah terkena peluru musuh.

Heuristic yang digunakan TankAlbino antara lain memprioritaskan target dengan skor tertinggi, memberi bonus pada musuh dengan energi rendah, menghindari posisi dekat dinding, menjaga jarak ideal, menghindari crossfire, serta menggunakan prediksi posisi musuh sebelum menembak.

Tujuan utama strategi TankAlbino adalah memperoleh skor secara stabil melalui bullet damage, peluang kill, dan survival score. Karena itu, TankAlbino dipilih sebagai bot utama karena strategi yang digunakan lebih seimbang dibandingkan bot alternatif lain.

---

## 2. BantengMerah

**BantengMerah** adalah bot alternatif pertama. Bot ini menggunakan strategi greedy yang lebih agresif dibandingkan TankAlbino. Strategi utama BantengMerah adalah **greedy closest target rammer**, yaitu memilih musuh terdekat sebagai target utama.

Pada setiap turn, BantengMerah mencari musuh yang jaraknya paling dekat. Musuh terdekat dianggap sebagai target paling menguntungkan karena lebih cepat dijangkau dan lebih memungkinkan untuk ditekan menggunakan serangan jarak dekat atau ramming. Setelah target dipilih, bot akan bergerak mendekati target tersebut dan mencoba mempertahankan tekanan.

Strategi BantengMerah berorientasi pada serangan langsung. Bot ini mencoba mendekati target, menembak pada jarak dekat, dan memanfaatkan peluang ram ketika posisi memungkinkan. Karena dalam Robocode Tank Royale ram damage dan ram damage bonus termasuk komponen skor, strategi ini dapat memberikan keuntungan jika bot berhasil mendominasi pertarungan jarak dekat.

Untuk mengurangi risiko terkena tembakan saat mengejar musuh, BantengMerah menggunakan pola gerak zig-zag. Gerakan ini bertujuan agar lintasan bot tidak terlalu mudah diprediksi oleh lawan. Bot juga tetap memiliki mekanisme untuk menghindari dinding agar tidak menerima wall damage.

Heuristic yang digunakan BantengMerah antara lain memilih musuh terdekat, mengejar target secara agresif, melakukan zig-zag ketika mendekati target, menggunakan firepower lebih tinggi pada jarak dekat, melakukan ramming ketika memungkinkan, dan menghindari dinding saat posisi terlalu dekat dengan batas arena.

Tujuan utama strategi BantengMerah adalah memperoleh skor dari ram damage, ram damage bonus, dan bullet damage jarak dekat. Kelebihan bot ini adalah tekanan serangan yang tinggi, sedangkan kelemahannya adalah risiko terkena damage lebih besar karena sering mendekati musuh.

---

## 3. AiranScube

**AiranScube** adalah bot alternatif kedua. Bot ini menggunakan strategi greedy yang berfokus pada gerakan evasive dan strafing. Strategi utama AiranScube adalah **greedy evasive strafing**, yaitu memilih gerakan terbaik agar bot tetap sulit ditembak sambil tetap bisa menyerang musuh.

Pada setiap turn, AiranScube membentuk beberapa kandidat arah gerak. Setiap kandidat diberi skor berdasarkan keamanan posisi, jarak terhadap musuh, kualitas strafing, posisi terhadap dinding, dan kemampuan menjaga jarak ideal. Kandidat gerak dengan skor terbaik akan dipilih sebagai gerakan bot pada turn tersebut.

AiranScube berusaha menjaga jarak dari musuh dan bergerak menyamping terhadap target. Gerakan menyamping atau strafing digunakan agar bot tidak bergerak lurus menuju musuh, sehingga lebih sulit diprediksi oleh lawan. Strategi ini berbeda dengan BantengMerah yang cenderung mendekati target secara agresif.

Dalam pemilihan target, AiranScube juga menggunakan priority scoring. Bot memilih target yang dianggap paling menguntungkan berdasarkan kondisi saat itu, seperti jarak, energi target, dan peluang tembakan. Setelah target dipilih, bot menggunakan prediksi posisi target untuk meningkatkan peluang peluru mengenai musuh.

Heuristic yang digunakan AiranScube antara lain menjaga jarak ideal, memilih kandidat gerakan dengan skor terbaik, mengutamakan gerakan menyamping, menghindari dinding, mengubah arah saat terkena peluru atau menabrak, memilih target berdasarkan prioritas, dan menggunakan predictive firing.

Tujuan utama strategi AiranScube adalah meningkatkan peluang bertahan hidup sambil tetap memberikan damage secara konsisten. Bot ini tidak terlalu agresif dalam mengejar musuh, tetapi lebih mengutamakan posisi aman dan pola gerak yang sulit ditebak.

---

## 4. Gryffin

**Gryffin** adalah bot alternatif ketiga. Bot ini menggunakan strategi greedy yang lebih taktis dan berbasis mode. Strategi utama Gryffin adalah **greedy tactical lane scoring**, yaitu memilih jalur gerak terbaik dari beberapa kandidat lane berdasarkan skor tertinggi.

Pada setiap turn, Gryffin mengevaluasi kondisi pertandingan terlebih dahulu. Bot memperhatikan posisi sendiri, posisi musuh, energi, jumlah musuh yang masih hidup, jarak ke dinding, dan tingkat ancaman. Berdasarkan kondisi tersebut, Gryffin menentukan mode taktis yang sesuai.

Mode yang digunakan Gryffin antara lain **Stabilize**, **Hunt**, **BreakLine**, **Pressure**, dan **Recover**. Mode Stabilize digunakan ketika bot perlu menjauh dari dinding atau kembali ke posisi aman. Mode Hunt digunakan saat bot berada dalam kondisi normal untuk mencari dan menyerang target. Mode BreakLine digunakan ketika bot berada dalam tekanan dan perlu memutus garis tembak lawan. Mode Pressure digunakan ketika masih ada banyak musuh sehingga bot perlu tetap aktif menyerang tanpa kehilangan keamanan posisi. Mode Recover digunakan ketika energi bot rendah sehingga bot lebih mengutamakan bertahan hidup.

Setelah mode ditentukan, Gryffin membentuk beberapa kandidat jalur gerak. Setiap jalur diberi skor berdasarkan keamanan dari dinding, jarak terhadap musuh, jarak dari pusat ancaman, pola gerakan lateral, penalti posisi yang sering dikunjungi, dan risiko berada terlalu dekat dengan musuh. Jalur dengan skor tertinggi dipilih sebagai aksi movement.

Untuk menyerang, Gryffin memilih target berdasarkan target scoring. Penilaian target mempertimbangkan jarak, energi target, peluang damage, peluang finishing, prediktabilitas gerak target, dan usia data scan. Firepower juga dipilih secara adaptif berdasarkan jarak, energi bot, energi target, dan jumlah musuh yang masih hidup.

Heuristic yang digunakan Gryffin antara lain tactical mode switching, lane movement scoring, wall safety evaluation, threat centroid escape, recent location penalty, visited cell penalty, target scoring, adaptive firepower, dan predictive aiming.

Tujuan utama strategi Gryffin adalah menjaga keseimbangan antara survival dan damage. Bot ini berusaha tetap aman, tidak mudah diprediksi, dan tetap mencari peluang menyerang ketika kondisi menguntungkan.

---

## Perbandingan Strategi Greedy

Keempat bot dalam repository ini menggunakan pendekatan greedy, tetapi setiap bot memiliki fokus dan heuristic yang berbeda. TankAlbino menggunakan strategi yang paling seimbang karena mempertimbangkan target scoring, posisi aman, peluang finishing, dan survival. Bot ini tidak hanya memilih musuh yang dekat, tetapi memilih target yang secara keseluruhan paling menguntungkan untuk diserang.

BantengMerah memiliki strategi yang paling agresif. Bot ini memilih musuh terdekat dan mencoba menekan target melalui serangan jarak dekat atau ramming. Strategi ini cocok untuk mengejar ram damage dan kill cepat, tetapi lebih berisiko karena bot sering berada dekat dengan musuh.

AiranScube memiliki strategi yang lebih defensif dan evasive. Bot ini berfokus pada pemilihan gerakan terbaik agar tetap sulit ditembak. Dibandingkan BantengMerah, AiranScube tidak terlalu mengejar kontak dekat, tetapi lebih mengutamakan posisi aman, strafing, dan serangan yang konsisten.

Gryffin memiliki strategi yang paling taktis karena menggunakan mode berbeda sesuai kondisi pertandingan. Bot ini tidak hanya memilih target dan arah gerak, tetapi juga menyesuaikan perilaku berdasarkan energi, ancaman, jumlah musuh, dan posisi arena. Strategi Gryffin lebih adaptif, tetapi juga lebih kompleks dibandingkan bot lain.

Secara umum, TankAlbino dipilih sebagai bot utama karena memiliki keseimbangan terbaik antara damage, survival, dan fleksibilitas. BantengMerah unggul dalam agresivitas, AiranScube unggul dalam movement evasive, sedangkan Gryffin unggul dalam adaptasi taktis.

---

## Requirement Program

Program ini membutuhkan beberapa komponen berikut:

1. **.NET SDK**

   Bot dibuat menggunakan bahasa C# dengan target framework:

   ```text
   net10.0
   ```

   Pastikan .NET SDK yang digunakan mendukung target framework tersebut.

2. **Robocode Tank Royale**

   Robocode Tank Royale digunakan sebagai game engine untuk menjalankan pertandingan bot. Pengujian sebaiknya dilakukan menggunakan engine yang telah dimodifikasi oleh asisten sesuai ketentuan tugas besar.

3. **Robocode Tank Royale Bot API**

   Package utama yang digunakan adalah:

   ```text
   Robocode.TankRoyale.BotApi
   ```

4. **Dependency tambahan**

   Beberapa project dapat menggunakan dependency tambahan seperti:

   ```text
   System.Drawing.Common
   Microsoft.Extensions.Configuration.Binder
   ```

5. **Sistem Operasi**

   Program dapat dijalankan pada Windows dan Linux selama environment sudah mendukung .NET SDK dan Robocode Tank Royale.

---

## Instalasi

Clone repository:

```bash
git clone https://github.com/gunturanggoro/Tubes_AyamGaJago.git
cd Tubes_AyamGaJago
```

Cek apakah .NET SDK sudah terpasang:

```bash
dotnet --version
```

Jika command tersebut tidak dikenali, install .NET SDK terlebih dahulu.

Dependency akan otomatis di-restore ketika menjalankan:

```bash
dotnet build
```

Jika ingin melakukan restore secara manual, masuk ke folder bot lalu jalankan:

```bash
dotnet restore
```

---

## Cara Build dan Menjalankan Program

Setiap bot memiliki folder project masing-masing. Command build dan run dijalankan dari folder bot yang ingin digunakan.

---

## 1. TankAlbino

Masuk ke folder bot:

```bash
cd src/main-bot/TankAlbino
```

Build program:

```bash
dotnet build
```

Jalankan bot:

```bash
dotnet run
```

Alternatif pada Windows:

```bash
TankAlbino.cmd
```

Alternatif pada Linux/macOS:

```bash
sh TankAlbino.sh
```

---

## 2. BantengMerah

Masuk ke folder bot:

```bash
cd src/alternative-bots/alt-bot-1/BantengMerah
```

Build program:

```bash
dotnet build
```

Jalankan bot:

```bash
dotnet run
```

Alternatif pada Windows:

```bash
BantengMerah.cmd
```

Alternatif pada Linux/macOS:

```bash
sh BantengMerah.sh
```

---

## 3. AiranScube

Masuk ke folder bot:

```bash
cd src/alternative-bots/alt-bot-2/AiranScube
```

Build program:

```bash
dotnet build
```

Jalankan bot:

```bash
dotnet run
```

Alternatif pada Windows:

```bash
AiranScube.cmd
```

Alternatif pada Linux/macOS:

```bash
sh AiranScube.sh
```

---

## 4. Gryffin

Masuk ke folder bot:

```bash
cd src/alternative-bots/alt-bot-3/Gryffin
```

Build program:

```bash
dotnet build
```

Jalankan bot:

```bash
dotnet run
```

Alternatif pada Windows:

```bash
Gryffin.cmd
```

Alternatif pada Linux/macOS:

```bash
sh Gryffin.sh
```

---

## Cara Menjalankan Battle di Robocode Tank Royale

Langkah umum untuk menjalankan battle adalah sebagai berikut:

1. Jalankan aplikasi Robocode Tank Royale dari starter pack.
2. Pastikan server Robocode Tank Royale sudah aktif.
3. Tambahkan folder bot ke konfigurasi **Bot Root Directories**.
4. Jalankan bot dari terminal menggunakan `dotnet run` atau script `.cmd` / `.sh`.
5. Buka menu battle pada Robocode Tank Royale.
6. Pilih bot yang ingin dipertandingkan.
7. Jalankan battle.
8. Amati performa bot berdasarkan skor, energi, damage, survival, dan hasil akhir ronde.

---

## Kendala Saat Development

Beberapa kendala yang dihadapi atau perlu diperhatikan saat development adalah sebagai berikut.

Pertama, versi .NET harus sesuai dengan target framework project. Jika versi .NET SDK yang digunakan tidak mendukung target framework `net10.0`, maka proses build dapat gagal. Karena itu, sebelum menjalankan bot, perlu dipastikan bahwa .NET SDK sudah terpasang dan dapat diakses melalui terminal.

Kedua, bot perlu dijalankan dari folder project masing-masing agar file konfigurasi seperti `.json` dapat terbaca dengan benar. Jika bot dijalankan dari direktori yang salah, konfigurasi bot dapat tidak terbaca atau bot tidak muncul sesuai identitas yang diharapkan.

Ketiga, pengujian perlu dilakukan menggunakan game engine yang telah dimodifikasi oleh asisten. Engine modifikasi memiliki perbedaan dengan engine asli, misalnya pada tampilan skor, energi, dan turn limit. Karena itu, hasil pengujian pada engine asli belum tentu sepenuhnya sama dengan engine tugas besar.

Keempat, radar harus terus digerakkan agar bot tetap dapat memindai musuh. Jika radar tidak bergerak, scan arc dapat menjadi nol dan bot dapat kehilangan informasi posisi musuh. Hal ini dapat menyebabkan bot salah memilih target atau tidak dapat menembak dengan optimal.

Kelima, folder hasil build seperti `bin/`, `obj/`, dan `artifacts/` tidak perlu dimasukkan ke repository karena folder tersebut merupakan hasil kompilasi dan dapat memperbesar ukuran repository. Repository sebaiknya hanya berisi source code, konfigurasi bot, laporan, dan README.

Keenam, terdapat perbedaan cara menjalankan bot di Windows dan Linux. Pada Windows, bot dapat dijalankan menggunakan file `.cmd`, sedangkan pada Linux/macOS dapat menggunakan file `.sh`. Pada Linux/macOS, file `.sh` mungkin perlu diberi permission terlebih dahulu agar dapat dieksekusi.

---

## Catatan

Repository ini disusun untuk memenuhi ketentuan tugas besar, yaitu membuat 4 bot dalam bahasa C# dengan strategi greedy yang berbeda. Bot utama yang dipilih adalah TankAlbino, sedangkan tiga bot lain digunakan sebagai alternatif strategi. Setiap bot memiliki pendekatan greedy dan heuristic yang berbeda agar dapat dibandingkan dari sisi efektivitas, efisiensi, dan performa di arena.

---

## Author

Kelompok: **Ayam Gak Jago**

Anggota:

1. **Guntur Anggoro Rahardianto** - 124140105
2. **Muhammad Radhitya Hammam** - 124140189
3. **Dina Olivia** - 124140213

Program Studi Teknik Informatika  
Fakultas Teknologi Industri  
Institut Teknologi Sumatera  
2026