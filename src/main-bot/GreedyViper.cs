using System;
using System.Collections.Generic;
using Robocode.TankRoyale.BotApi;
using System.Drawing;
using Robocode.TankRoyale.BotApi.Events;

/// <summary>
/// GreedyViper — bot tank berbasis strategi "greedy" yang memilih target berdasarkan
/// skor keuntungan tertinggi per giliran. Menggabungkan radar berputar, prediksi posisi
/// musuh, orbit menghindar, dan keputusan menembak adaptif.
/// </summary>
public class GreedyViper : Bot
{
    // Jarak minimum dari dinding agar bot dianggap "aman" dari tabrakan dinding
    private const double WallMargin = 72.0;

    // Jumlah giliran maksimum sejak terakhir terdeteksi agar musuh masih dianggap valid
    private const int MaxTargetAge = 18;

    // Lock untuk akses thread-safe ke kamus enemies (karena event berjalan di thread lain)
    private readonly object enemyLock = new object();

    // Menyimpan data semua musuh yang pernah terdeteksi, diindeks oleh ID bot
    private readonly Dictionary<int, Enemy> enemies = new Dictionary<int, Enemy>();

    // Arah putaran orbit: +1 (berlawanan jarum jam) atau -1 (searah jarum jam)
    private int orbitSign = 1;

    // Arah putaran radar: +1 atau -1
    private int radarSign = 1;

    // Giliran terakhir saat orbit dibalik (untuk mencegah pembalikan terlalu sering)
    private int lastReverseTurn = -999;

    // Giliran hingga bot dipaksa bergerak ke tengah arena (setelah menabrak dinding)
    private int forceCenterUntil = 0;

    // ID target terakhir yang ditembak (untuk memberikan bonus skor kontinuitas)
    private int lastTargetId = -1;

    /// <summary>
    /// Entry point program. Membuat instance GreedyViper dan menjalankannya.
    /// </summary>
    static void Main(string[] args)
    {
        new GreedyViper().Start();
    }

    /// <summary>
    /// Konstruktor: mendaftarkan metadata bot ke sistem Robocode Tank Royale
    /// (nama, versi, penulis, deskripsi, bahasa, kategori, dll).
    /// </summary>
    public GreedyViper() : base(new BotInfo(
        "Greedy Viper",
        "1.0",
        new List<string> { "Ayam Gak Jago" },
        "Greedy score hunter with predictive firing and orbit evasion.",
        null,
        new List<string> { "id" },
        new HashSet<string> { "classic" },
        "dotnet",
        "csharp",
        null
    ))
    {
    }

    /// <summary>
    /// Loop utama bot yang berjalan setiap ronde. Menginisialisasi ulang state ronde,
    /// menetapkan warna bot, lalu terus-menerus memilih target dan menjalankan
    /// kontrol radar, senjata, dan pergerakan setiap giliran.
    /// </summary>
    public override void Run()
    {
        // Reset semua variabel state dari ronde sebelumnya
        ResetRoundState();

        // Warna tema militer / hijau army
        BodyColor   = Color.FromArgb(70, 85, 45);    // hijau army utama
        TurretColor = Color.FromArgb(45, 60, 30);    // hijau gelap
        RadarColor  = Color.FromArgb(120, 140, 70);  // olive radar
        BulletColor = Color.FromArgb(170, 220, 90);  // peluru hijau terang
        ScanColor   = Color.FromArgb(140, 200, 90);  // scan hijau neon lembut

        // Senjata dan radar berputar secara independen dari badan
        AdjustGunForBodyTurn = true;
        AdjustRadarForBodyTurn = true;
        AdjustRadarForGunTurn = true;

        while (IsRunning)
        {
            // Pilih target terbaik berdasarkan skor greedy
            Enemy target = SelectGreedyTarget();

            if (target == null)
            {
                // Tidak ada target terdeteksi — putar radar dan bergerak mencari musuh
                SearchForEnemy();
            }
            else
            {
                // Ada target — jalankan kontrol radar, tembakan, dan pergerakan
                ControlRadar(target);
                ControlGunAndFire(target);
                ControlMovement(target);
            }

            // Kirim semua perintah giliran ini ke engine
            Go();
        }
    }

    /// <summary>
    /// Dipanggil setiap kali radar mendeteksi bot musuh. Memperbarui data musuh
    /// di kamus, dan memicu pembalikan orbit jika musuh seperti baru menembak.
    /// </summary>
    public override void OnScannedBot(ScannedBotEvent e)
    {
        // Abaikan jika bot yang dipindai adalah rekan satu tim
        if (IsTeammate(e.ScannedBotId))
            return;

        lock (enemyLock)
        {
            Enemy enemy;
            // Tambahkan entri baru jika musuh belum pernah terdeteksi sebelumnya
            if (!enemies.TryGetValue(e.ScannedBotId, out enemy))
            {
                enemy = new Enemy();
                enemy.Id = e.ScannedBotId;
                enemy.PreviousEnergy = e.Energy;
                enemies[e.ScannedBotId] = enemy;
            }

            // Hitung penurunan energi musuh dibanding pembacaan sebelumnya
            double energyDrop = enemy.Energy - e.Energy;

            // Perbarui semua data terbaru musuh
            enemy.X = e.X;
            enemy.Y = e.Y;
            enemy.Energy = e.Energy;
            enemy.Direction = e.Direction;
            enemy.Speed = e.Speed;
            enemy.Distance = Distance(X, Y, e.X, e.Y);
            enemy.LastSeen = e.TurnNumber;
            enemy.Alive = true;

            // Jika energi musuh turun dalam rentang wajar peluru dan jarak dekat,
            // kemungkinan musuh baru menembak — balik orbit untuk menghindar
            if (energyDrop > 0.09 && energyDrop <= 3.1 && enemy.Distance < 700)
                ReverseOrbit();

            enemy.PreviousEnergy = e.Energy;
        }
    }

    /// <summary>
    /// Dipanggil saat bot terkena peluru. Langsung membalik arah orbit sebagai
    /// respons menghindar.
    /// </summary>
    public override void OnHitByBullet(HitByBulletEvent e)
    {
        ReverseOrbit();
    }

    /// <summary>
    /// Dipanggil saat bot menabrak dinding. Memaksa bot bergerak ke tengah
    /// arena selama beberapa giliran dan membalik orbit.
    /// </summary>
    public override void OnHitWall(HitWallEvent e)
    {
        forceCenterUntil = TurnNumber + 18;
        ReverseOrbit();
    }

    /// <summary>
    /// Dipanggil saat bot bertabrakan langsung dengan bot lain. Memperbarui
    /// data posisi musuh dan memutuskan apakah harus menghindari atau
    /// melanjutkan manuver ram (tabrak).
    /// </summary>
    public override void OnHitBot(HitBotEvent e)
    {
        lock (enemyLock)
        {
            // Perbarui data musuh dengan informasi tabrakan terbaru
            if (enemies.ContainsKey(e.VictimId))
            {
                enemies[e.VictimId].X = e.X;
                enemies[e.VictimId].Y = e.Y;
                enemies[e.VictimId].Energy = e.Energy;
                enemies[e.VictimId].Distance = Distance(X, Y, e.X, e.Y);
                enemies[e.VictimId].LastSeen = e.TurnNumber;
            }
        }

        // Jika bukan situasi ram yang menguntungkan, hindari dengan bergerak ke tengah
        if (!e.IsRammed || e.Energy > 16 || Energy < e.Energy + 10)
        {
            forceCenterUntil = TurnNumber + 12;
            ReverseOrbit();
        }
        else
        {
            // Situasi ram menguntungkan — tandai sebagai target prioritas
            lastTargetId = e.VictimId;
        }
    }

    /// <summary>
    /// Dipanggil saat peluru kita mengenai musuh. Memperbarui energi musuh
    /// dan mengunci musuh tersebut sebagai target terakhir.
    /// </summary>
    public override void OnBulletHit(BulletHitBotEvent e)
    {
        lock (enemyLock)
        {
            if (enemies.ContainsKey(e.VictimId))
                enemies[e.VictimId].Energy = e.Energy;
        }

        // Pertahankan musuh yang baru kita kena sebagai target prioritas
        lastTargetId = e.VictimId;
    }

    /// <summary>
    /// Dipanggil saat sebuah bot mati. Menandai musuh sebagai tidak aktif
    /// di kamus dan mereset target terakhir jika bot tersebut adalah targetnya.
    /// </summary>
    public override void OnBotDeath(BotDeathEvent e)
    {
        lock (enemyLock)
        {
            if (enemies.ContainsKey(e.VictimId))
                enemies[e.VictimId].Alive = false;
        }

        if (lastTargetId == e.VictimId)
            lastTargetId = -1;
    }

    /// <summary>
    /// Perilaku saat tidak ada target: memutar radar dan senjata untuk mencari
    /// musuh, sambil bergerak lambat memutar di arena.
    /// </summary>
    private void SearchForEnemy()
    {
        RadarTurnRate = radarSign * MaxRadarTurnRate;
        GunTurnRate = radarSign * Math.Min(12, MaxGunTurnRate);
        TurnRate = 3 * orbitSign;
        TargetSpeed = MaxSpeed * 0.75;
    }

    /// <summary>
    /// Memilih target terbaik dari semua musuh yang terdeteksi baru-baru ini
    /// menggunakan algoritma greedy — musuh dengan skor tertinggi dipilih.
    /// Memberi bonus kecil jika target adalah musuh yang sama dari giliran sebelumnya
    /// (untuk menghindari pergantian target terus-menerus).
    /// </summary>
    private Enemy SelectGreedyTarget()
    {
        List<Enemy> snapshot = new List<Enemy>();

        // Ambil salinan musuh yang masih aktif dan terdeteksi baru-baru ini
        lock (enemyLock)
        {
            foreach (Enemy enemy in enemies.Values)
            {
                if (enemy.Alive && TurnNumber - enemy.LastSeen <= MaxTargetAge)
                    snapshot.Add(enemy.Clone());
            }
        }

        Enemy best = null;
        double bestScore = double.NegativeInfinity;

        foreach (Enemy enemy in snapshot)
        {
            double score = GreedyTargetScore(enemy);
            // Bonus kontinuitas: mempertahankan target yang sama lebih stabil
            if (enemy.Id == lastTargetId)
                score += 0.45;

            if (score > bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }

        if (best != null)
            lastTargetId = best.Id;

        return best;
    }

    /// <summary>
    /// Menghitung skor keuntungan greedy untuk satu musuh. Skor mempertimbangkan:
    /// peluang hit (jarak + keselarasan senjata), potensi kill, kelemahan musuh,
    /// ancaman musuh terhadap kita, peluang ram, dan penalti data yang sudah usang.
    /// </summary>
    private double GreedyTargetScore(Enemy enemy)
    {
        double distance = DistanceTo(enemy.X, enemy.Y);
        double gunBearing = Math.Abs(GunBearingTo(enemy.X, enemy.Y));
        double power = EstimateFirepower(enemy, distance, gunBearing);
        double damage = BulletDamage(power);

        // Peluang hit berdasarkan jarak (semakin dekat semakin baik)
        double hitByDistance = 1.0 / (1.0 + distance / 360.0);
        // Peluang hit berdasarkan seberapa lurus senjata mengarah ke musuh
        double hitByAlignment = 1.0 - Math.Min(gunBearing, 90.0) / 90.0;
        double hitChance = 0.65 * hitByDistance + 0.35 * hitByAlignment;

        // Bonus besar jika tembakan ini bisa langsung membunuh musuh
        double killBonus = enemy.Energy <= damage + 0.2 ? 5.5 : 0.0;
        // Bonus untuk musuh yang energinya sudah rendah
        double weakBonus = (100.0 - Math.Min(100.0, enemy.Energy)) / 45.0;
        // Skor ancaman: musuh kuat yang dekat lebih berbahaya
        double closeThreat = (enemy.Energy / 100.0) * (1.0 - Math.Min(distance, 650.0) / 650.0);
        // Bonus tambahan jika kondisi memungkinkan strategi tabrak
        double ramOpportunity = ShouldRam(enemy, distance) ? 2.5 : 0.0;
        // Penalti untuk data musuh yang sudah lama tidak diperbarui
        double agePenalty = Math.Max(0, TurnNumber - enemy.LastSeen) * 0.12;

        return hitChance * 5.0 + killBonus + weakBonus + closeThreat * 1.4 + ramOpportunity - agePenalty;
    }

    /// <summary>
    /// Mengontrol putaran radar agar terus mengunci posisi target.
    /// Menggunakan teknik "radar lock" dengan mengalikan sudut bearing target × 2.2
    /// sehingga radar selalu melewati target (overscan) untuk menjaga kontak.
    /// </summary>
    private void ControlRadar(Enemy target)
    {
        double bearing = RadarBearingTo(target.X, target.Y);
        double turn = bearing * 2.2;

        // Jaga putaran minimum agar radar tidak berhenti berputar sama sekali
        if (Math.Abs(turn) < 4.0)
            turn = 4.0 * radarSign;

        turn = Limit(turn, -MaxRadarTurnRate, MaxRadarTurnRate);

        // Catat arah putaran saat ini untuk digunakan saat tidak ada target
        if (Math.Abs(turn) > 0.01)
            radarSign = turn > 0 ? 1 : -1;

        RadarTurnRate = turn;
    }

    /// <summary>
    /// Mengarahkan senjata ke posisi prediksi musuh dan menembak jika kondisi terpenuhi:
    /// senjata tidak panas, sudut sudah cukup lurus, dan energi kita mencukupi.
    /// </summary>
    private void ControlGunAndFire(Enemy target)
    {
        double roughDistance = DistanceTo(target.X, target.Y);
        double roughBearing = Math.Abs(GunBearingTo(target.X, target.Y));
        double power = EstimateFirepower(target, roughDistance, roughBearing);

        if (power < 0.1)
            return; // Daya tembak terlalu kecil, tidak layak menembak

        // Hitung posisi prediksi musuh saat peluru tiba
        AimPoint aim = PredictEnemyPosition(target, power);
        double preciseDistance = DistanceTo(aim.X, aim.Y);
        double gunBearing = GunBearingTo(aim.X, aim.Y);

        // Putar senjata menuju titik prediksi
        GunTurnRate = Limit(gunBearing, -MaxGunTurnRate, MaxGunTurnRate);

        // Hitung toleransi sudut tembak berdasarkan jarak (target dekat = jendela lebih lebar)
        double fireWindow = FireWindow(preciseDistance);
        bool gunReady = GunHeat == 0;                        // Senjata tidak sedang mendingin
        bool aligned = Math.Abs(gunBearing) <= fireWindow;   // Senjata cukup lurus
        bool affordable = Energy > power + 0.25;             // Energi kita mencukupi

        if (gunReady && aligned && affordable)
            SetFire(power);
    }

    /// <summary>
    /// Mengontrol pergerakan bot setiap giliran. Memilih strategi berdasarkan kondisi:
    /// - Dekat dinding atau paksa ke tengah → bergerak ke tengah arena
    /// - Kondisi memungkinkan ram → men突突 ke arah musuh
    /// - Normal → orbit melingkar di sekitar musuh dengan sudut adaptif
    /// Juga membalik arah jika terlalu lama tidak berputar.
    /// </summary>
    private void ControlMovement(Enemy target)
    {
        double distance = DistanceTo(target.X, target.Y);
        bool nearWall = IsNearWall(WallMargin) || TurnNumber < forceCenterUntil;

        // Paksa pembalikan orbit secara periodik agar pergerakan tidak terprediksi
        if (TurnNumber - lastReverseTurn > 43)
            ReverseOrbit();

        double desiredHeading;

        if (nearWall)
        {
            // Prioritas utama: menjauh dari dinding
            desiredHeading = DirectionTo(ArenaWidth / 2.0, ArenaHeight / 2.0);
        }
        else if (ShouldRam(target, distance))
        {
            // Strategi ram: langsung maju ke arah musuh
            desiredHeading = DirectionTo(target.X, target.Y);
        }
        else
        {
            // Orbit normal: bergerak melingkar di sekitar musuh
            double toEnemy = DirectionTo(target.X, target.Y);

            // Sudut orbit disesuaikan dengan jarak:
            // - Terlalu dekat (< 170): sudut besar (138°) untuk kabur
            // - Jarak ideal (170–560): sudut 92° (sirkular)
            // - Jauh (> 560): sudut kecil (42°) untuk mendekat
            double orbitAngle = distance < 170 ? 138 : distance > 560 ? 42 : 92;
            desiredHeading = NormalizeAbsoluteAngle(toEnemy + orbitAngle * orbitSign);

            // Cek apakah heading ini akan membawa bot mendekati dinding — jika ya, putar ke tengah
            AimPoint projected = Project(X, Y, desiredHeading, 130);
            if (OutsideSafeArea(projected.X, projected.Y))
                desiredHeading = DirectionTo(ArenaWidth / 2.0, ArenaHeight / 2.0);
        }

        // Hitung putar badan yang diperlukan
        double turn = NormalizeRelativeAngle(desiredHeading - Direction);
        double speed = MaxSpeed;

        // Jika perlu berbalik lebih dari 100°, lebih efisien mundur
        if (Math.Abs(turn) > 100)
        {
            turn = NormalizeRelativeAngle(turn + 180);
            speed = -MaxSpeed;
        }

        // Kurangi kecepatan saat dekat dinding atau terlalu dekat musuh (bukan ram)
        if (nearWall)
            speed *= 0.75;
        else if (distance < 120 && !ShouldRam(target, distance))
            speed *= 0.65;

        TurnRate = Limit(turn, -MaxTurnRate, MaxTurnRate);
        TargetSpeed = speed;
    }

    /// <summary>
    /// Memperkirakan daya tembak optimal berdasarkan jarak, energi kita, energi musuh,
    /// dan keselarasan senjata. Daya lebih tinggi di jarak dekat, dikurangi saat
    /// energi kita rendah atau senjata tidak sejajar.
    /// </summary>
    private double EstimateFirepower(Enemy target, double distance, double gunBearing)
    {
        double maxAffordable = Math.Min(3.0, Energy - 0.2);
        if (maxAffordable < 0.1)
            return 0.0; // Energi terlalu rendah untuk menembak

        double power;

        // Daya dasar berdasarkan jarak ke target
        if (distance < 140)       power = 3.0;
        else if (distance < 300)  power = 2.25;
        else if (distance < 550)  power = 1.55;
        else if (distance < 800)  power = 1.0;
        else                      power = 0.65;

        // Jika musuh hampir mati, kurangi daya agar tidak boros energi
        if (target.Energy <= 18)
            power = Math.Min(power, RequiredPowerToKill(target.Energy) + 0.08);

        // Kurangi daya jika senjata tidak terlalu lurus atau jarak terlalu jauh
        if (Math.Abs(gunBearing) > 28 || distance > 850)
            power = Math.Min(power, 0.85);

        // Mode hemat energi saat energi kita rendah
        if (Energy < 18) power = Math.Min(power, 1.15);
        if (Energy < 8)  power = Math.Min(power, 0.55);

        return Limit(power, 0.1, maxAffordable);
    }

    /// <summary>
    /// Memprediksi posisi musuh saat peluru tiba menggunakan iterasi berulang.
    /// Setiap iterasi menghitung ulang waktu tempuh peluru berdasarkan jarak
    /// ke posisi prediksi sebelumnya, lalu menggeser prediksi lebih akurat (4x iterasi).
    /// </summary>
    private AimPoint PredictEnemyPosition(Enemy target, double firepower)
    {
        double bulletSpeed = Math.Max(0.1, CalcBulletSpeed(firepower));
        double px = target.X;
        double py = target.Y;

        for (int i = 0; i < 4; i++)
        {
            // Estimasi waktu yang dibutuhkan peluru untuk mencapai posisi prediksi saat ini
            double travelTime = DistanceTo(px, py) / bulletSpeed;
            travelTime = Limit(travelTime, 0, 62); // Batasi maksimum 62 giliran ke depan

            // Hitung posisi musuh setelah travelTime giliran bergerak lurus
            double radians = ToRadians(target.Direction);
            px = target.X + Math.Cos(radians) * target.Speed * travelTime;
            py = target.Y + Math.Sin(radians) * target.Speed * travelTime;

            // Klem posisi agar tidak keluar arena (musuh tidak akan menembus dinding)
            px = Limit(px, 18, ArenaWidth - 18);
            py = Limit(py, 18, ArenaHeight - 18);
        }

        return new AimPoint(px, py);
    }

    /// <summary>
    /// Menentukan apakah kondisi saat ini cocok untuk strategi ram (tabrak langsung).
    /// Ram hanya dilakukan jika: sedikit musuh tersisa, kita jauh lebih kuat dari musuh,
    /// musuh hampir mati, dan jaraknya cukup dekat.
    /// </summary>
    private bool ShouldRam(Enemy target, double distance)
    {
        return EnemyCount <= 2          // Hanya saat musuh tersisa sedikit
            && Energy > target.Energy + 14  // Kita jauh lebih kuat
            && target.Energy < 12           // Musuh hampir mati
            && distance < 175;              // Musuh cukup dekat
    }

    /// <summary>
    /// Menghitung toleransi sudut tembak (fire window) dalam derajat.
    /// Target yang lebih jauh membutuhkan akurasi lebih tinggi (jendela sempit),
    /// sedangkan target dekat memiliki jendela lebih lebar.
    /// </summary>
    private double FireWindow(double distance)
    {
        // Sudut yang disubtended oleh radius bot (18px) pada jarak tertentu
        double botRadiusAngle = ToDegrees(Math.Atan2(18.0, Math.Max(1.0, distance)));
        // Tambahkan margin 0.8° dan batasi antara 1.15° hingga 5.0°
        return Limit(botRadiusAngle + 0.8, 1.15, 5.0);
    }

    /// <summary>
    /// Menghitung kerusakan yang ditimbulkan oleh peluru dengan daya tertentu.
    /// Formula: 4 × power untuk power ≤ 1; ditambah 2 × (power - 1) untuk power > 1.
    /// </summary>
    private double BulletDamage(double power)
    {
        if (power <= 0)
            return 0;

        return power <= 1.0
            ? 4.0 * power
            : 4.0 * power + 2.0 * (power - 1.0);
    }

    /// <summary>
    /// Menghitung daya tembak minimum yang dibutuhkan untuk membunuh musuh
    /// dengan energi tertentu. Digunakan untuk menghindari pemborosan energi
    /// saat musuh hampir mati.
    /// </summary>
    private double RequiredPowerToKill(double energy)
    {
        if (energy <= 0)
            return 0.1;

        // Untuk musuh sangat lemah (≤4 energi): power = energy / 4
        // Untuk musuh agak lemah: power = (energy + 2) / 6
        return energy <= 4.0 ? energy / 4.0 : (energy + 2.0) / 6.0;
    }

    /// <summary>
    /// Memeriksa apakah bot saat ini berada dalam jarak margin dari salah satu dinding arena.
    /// </summary>
    private bool IsNearWall(double margin)
    {
        return X < margin || Y < margin
            || ArenaWidth - X < margin || ArenaHeight - Y < margin;
    }

    /// <summary>
    /// Memeriksa apakah titik koordinat (x, y) berada di luar zona aman arena
    /// (yaitu dalam jarak WallMargin dari tepi mana pun).
    /// </summary>
    private bool OutsideSafeArea(double x, double y)
    {
        return x < WallMargin || y < WallMargin
            || x > ArenaWidth - WallMargin || y > ArenaHeight - WallMargin;
    }

    /// <summary>
    /// Memproyeksikan titik baru sejauh distance satuan dari posisi (x, y)
    /// ke arah heading tertentu. Digunakan untuk memprakirakan posisi bot
    /// setelah bergerak lurus.
    /// </summary>
    private AimPoint Project(double x, double y, double heading, double distance)
    {
        double radians = ToRadians(heading);
        return new AimPoint(
            x + Math.Cos(radians) * distance,
            y + Math.Sin(radians) * distance
        );
    }

    /// <summary>
    /// Membalik arah orbit (orbitSign). Ada jeda minimum 8 giliran antar pembalikan
    /// untuk mencegah bot bolak-balik terlalu cepat yang bisa dieksploitasi musuh.
    /// </summary>
    private void ReverseOrbit()
    {
        if (TurnNumber - lastReverseTurn < 8)
            return; // Terlalu cepat dibalik — abaikan

        orbitSign = -orbitSign;
        lastReverseTurn = TurnNumber;
    }

    /// <summary>
    /// Mereset semua variabel state ke nilai awal di awal setiap ronde baru.
    /// Membersihkan data musuh lama dan mengembalikan semua flag ke kondisi default.
    /// </summary>
    private void ResetRoundState()
    {
        lock (enemyLock)
            enemies.Clear();

        orbitSign = 1;
        radarSign = 1;
        lastReverseTurn = -999;
        forceCenterUntil = 0;
        lastTargetId = -1;
    }

    /// <summary>
    /// Menghitung jarak Euclidean antara dua titik (x1, y1) dan (x2, y2).
    /// </summary>
    private static double Distance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Membatasi nilai agar berada dalam rentang [min, max].
    /// Jika value < min maka kembalikan min; jika value > max maka kembalikan max.
    /// </summary>
    private static double Limit(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    /// <summary>
    /// Mengonversi sudut dari derajat ke radian (untuk fungsi trigonometri C#).
    /// </summary>
    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    /// <summary>
    /// Mengonversi sudut dari radian ke derajat (untuk ditampilkan atau dibandingkan).
    /// </summary>
    private static double ToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }

    /// <summary>
    /// Struktur data internal yang menyimpan semua informasi tentang satu musuh:
    /// posisi, energi, arah, kecepatan, jarak, giliran terakhir terdeteksi, dan status hidup.
    /// </summary>
    private sealed class Enemy
    {
        public int Id;
        public double X;
        public double Y;
        public double Energy;
        public double PreviousEnergy; // Energi musuh pada scan sebelumnya (untuk deteksi tembakan)
        public double Direction;
        public double Speed;
        public double Distance;
        public int LastSeen;          // Nomor giliran terakhir saat musuh ini terdeteksi
        public bool Alive = true;

        /// <summary>
        /// Membuat salinan dangkal (shallow copy) dari objek Enemy ini,
        /// digunakan agar snapshot tidak terpengaruh update thread lain.
        /// </summary>
        public Enemy Clone()
        {
            return (Enemy)MemberwiseClone();
        }
    }

    /// <summary>
    /// Struct ringan untuk merepresentasikan koordinat titik tujuan (X, Y),
    /// digunakan sebagai hasil prediksi posisi musuh dan proyeksi pergerakan.
    /// </summary>
    private readonly struct AimPoint
    {
        public readonly double X;
        public readonly double Y;

        public AimPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
}