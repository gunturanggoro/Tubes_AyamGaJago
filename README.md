# Tugas Besar Strategi Algoritma - Robocode Tank Royale

## Pemanfaatan Algoritma Greedy dalam Bot Robocode Tank Royale

Repository ini berisi implementasi bot Robocode Tank Royale untuk Tugas Besar Strategi Algoritma. Bot dibuat menggunakan bahasa pemrograman C# dengan framework .NET dan menerapkan algoritma greedy dalam pengambilan keputusan selama permainan.

Strategi greedy digunakan untuk memilih keputusan terbaik pada kondisi saat ini, seperti memilih target, menentukan arah gerak, memilih daya tembak, serta menentukan kapan bot harus menyerang atau bertahan. Selain algoritma greedy, setiap bot juga menggunakan heuristic yang berbeda agar strategi lebih adaptif terhadap kondisi arena.

---

## Struktur Repository

```text
├── src/
│   ├── main-bot/
│   │   └── TankAlbino/
│   └── alternative-bots/
│       ├── alt-bot-1/
│       │   └── BantengMerah/
│       └── alt-bot-2/
│           └── AiranScube/
├── doc/
│   └── laporan.pdf
└── README.md
```

---

## Daftar Bot

### 1. TankAlbino - Main Bot

TankAlbino merupakan bot utama yang menerapkan strategi **greedy score hunter**. Bot ini memilih target berdasarkan skor keuntungan tertinggi pada setiap turn. Skor tersebut mempertimbangkan beberapa faktor, seperti peluang tembakan mengenai target, potensi menghabisi musuh, tingkat ancaman musuh, peluang ram, serta usia data hasil scan.

TankAlbino tidak hanya berfokus pada serangan, tetapi juga memperhatikan kemampuan bertahan. Bot ini menggunakan pendekatan yang seimbang antara menyerang musuh yang paling menguntungkan dan menghindari posisi berbahaya.

Heuristic yang digunakan pada TankAlbino:

- **Predictive firing**, yaitu menembak ke posisi prediksi musuh, bukan hanya posisi musuh saat ini.
- **Radar lock**, yaitu menjaga radar tetap mengarah ke target agar data musuh tetap baru.
- **Orbit evasion**, yaitu bergerak melingkar terhadap musuh untuk mengurangi peluang terkena peluru.
- **Enemy wave detection**, yaitu memperkirakan tembakan musuh berdasarkan penurunan energi musuh.
- **Adaptive movement**, yaitu menyesuaikan pola gerak berdasarkan kondisi duel 1v1 atau melee.

TankAlbino dipilih sebagai bot utama karena memiliki strategi greedy dan heuristic yang paling lengkap serta adaptif dibandingkan bot lainnya.

---

### 2. BantengMerah - Alternative Bot 1

BantengMerah merupakan bot alternatif yang menggunakan strategi **greedy rammer**. Bot ini berfokus pada pemilihan target terdekat, kemudian mendekati target tersebut untuk melakukan serangan jarak dekat atau ramming.

Strategi greedy pada BantengMerah terlihat dari pemilihan target berdasarkan jarak terdekat. Target yang paling dekat dianggap sebagai pilihan terbaik karena dapat dicapai lebih cepat dan memberikan peluang lebih besar untuk melakukan ram damage.

Heuristic yang digunakan pada BantengMerah:

- **Closest target selection**, yaitu memilih musuh terdekat sebagai target utama.
- **Zig-zag chase**, yaitu bergerak zig-zag saat mengejar musuh agar tidak mudah ditembak.
- **Ram engage range**, yaitu batas jarak tertentu untuk memulai mode ramming.
- **Ram sustain**, yaitu mempertahankan dorongan setelah menabrak musuh.
- **Predictive fire**, yaitu menembak ke posisi prediksi musuh saat bot sedang mengejar.

BantengMerah memiliki karakter agresif dan cocok digunakan sebagai pembanding strategi greedy berbasis jarak dan peluang tabrakan.

---

### 3. AiranScube - Alternative Bot 2

AiranScube merupakan bot alternatif yang menggunakan strategi **greedy evasive strafing**. Bot ini memilih target berdasarkan skor prioritas yang mempertimbangkan jarak musuh, energi musuh, dan bonus jika target sudah dikunci.

Pada movement, AiranScube mengevaluasi beberapa kandidat arah gerak, kemudian memilih kandidat dengan skor terbaik. Skor tersebut mempertimbangkan jarak ideal dari musuh, keamanan dari dinding, posisi terhadap tengah arena, serta kemampuan melakukan strafing terhadap musuh.

Heuristic yang digunakan pada AiranScube:

- **Target priority scoring**, yaitu memilih target berdasarkan skor prioritas.
- **Candidate movement evaluation**, yaitu mengevaluasi beberapa kandidat gerak dan memilih yang terbaik.
- **Strafing movement**, yaitu bergerak menyamping terhadap musuh agar lebih sulit ditembak.
- **Velocity manipulation**, yaitu mengubah kecepatan dan arah gerak agar pola gerak tidak mudah diprediksi.
- **Wall safety**, yaitu menghindari posisi terlalu dekat dengan dinding.
- **Random reverse**, yaitu membalik arah secara acak untuk mengurangi kemungkinan terkena tembakan musuh.

AiranScube memiliki karakter evasive atau defensif, sehingga cocok digunakan sebagai pembanding terhadap TankAlbino dan BantengMerah.

---

## Ringkasan Strategi Greedy

Secara umum, ketiga bot menggunakan algoritma greedy karena setiap bot mengambil keputusan terbaik berdasarkan kondisi saat ini tanpa menghitung seluruh kemungkinan jangka panjang.

Komponen greedy yang digunakan:

1. **Himpunan kandidat**  
   Kandidat dapat berupa daftar musuh yang terdeteksi, pilihan arah gerak, pilihan firepower, atau mode pergerakan tertentu.

2. **Fungsi seleksi**  
   Bot memilih kandidat terbaik berdasarkan skor atau aturan tertentu, misalnya target dengan skor tertinggi atau musuh dengan jarak terdekat.

3. **Fungsi kelayakan**  
   Bot memastikan keputusan yang dipilih masih valid, misalnya target belum basi, energi cukup untuk menembak, posisi tidak terlalu dekat dinding, dan gun sudah cukup mengarah ke target.

4. **Fungsi objektif**  
   Bot berusaha memaksimalkan keuntungan, seperti damage, peluang kill, peluang bertahan hidup, atau peluang menghindari serangan musuh.

5. **Solusi greedy**  
   Solusi greedy adalah aksi yang dipilih pada turn tersebut, seperti target yang ditembak, arah gerak, firepower, atau mode serangan.

---

## Perbandingan Bot

| Bot | Jenis Strategi | Fokus Utama | Karakter |
|---|---|---|---|
| TankAlbino | Greedy score hunter | Serangan dan pertahanan seimbang | Adaptif dan stabil |
| BantengMerah | Greedy rammer | Mengejar dan menabrak musuh | Agresif |
| AiranScube | Greedy evasive strafing | Menghindar dan menjaga jarak | Defensif/evasive |

---

## Requirement Program

Untuk menjalankan program ini, diperlukan:

1. **.NET SDK**  
   Digunakan untuk melakukan build dan menjalankan bot berbasis C#.

2. **Robocode Tank Royale**  
   Digunakan sebagai game engine tempat bot dijalankan dan diuji.

3. **Robocode Tank Royale Bot API**  
   Digunakan sebagai library untuk menghubungkan bot dengan Robocode Tank Royale.

4. **Sistem Operasi**  
   Program dapat dijalankan pada Windows atau Linux selama sudah mendukung .NET SDK dan Robocode Tank Royale.

---

## Cara Build dan Menjalankan Bot

### 1. Menjalankan Main Bot - TankAlbino

Masuk ke folder bot dengan CMD/Powershell:

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

---

### 2. Menjalankan Alternative Bot 1 - BantengMerah

Masuk ke folder bot dengan CMD/Powershell:

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

---

### 3. Menjalankan Alternative Bot 2 - AiranScube

Masuk ke folder bot dengan CMD/Powershell:

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

---

## Cara Menjalankan Battle di Robocode Tank Royale

1. Jalankan Robocode Tank Royale Engine.
2. Pastikan server Robocode Tank Royale sudah aktif.
3. Klik Config pada bagian atas tampilan robocode.
4. Klik Boot Root Directories kemudian tambahkan direktori tempat menaruh bot.
5. Klik Battle kemudian start battle pada bagian atas tampilan robocode.
6. Lakukan boot kepada bot yang diinginkan. 
7. Jalankan battle dengan meng klik start battle dan amati performa bot.

---

## Author

Kelompok: **Ayam Gak Jago**

Anggota:

1. **Guntur Anggoro Rahardianto** - 124140105  
2. **Muhammad Radhitya Hammam** - 124140189  
3. **Dina Olivia** - 124140213  

Program Studi Teknik Informatika  
Institut Teknologi Sumatera  
2026