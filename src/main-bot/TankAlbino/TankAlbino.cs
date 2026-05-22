using System;
using System.Collections.Generic;
using Robocode.TankRoyale.BotApi;
using System.Drawing;
using Robocode.TankRoyale.BotApi.Events;

/// <summary>
/// TankAlbino â€” bot tank berbasis strategi "greedy" yang memilih target berdasarkan
/// skor keuntungan tertinggi per giliran. Menggabungkan radar berputar, prediksi posisi
/// musuh, orbit menghindar, dan keputusan menembak adaptif.
/// </summary>
public class TankAlbino : Bot
{
    // Jarak minimum dari dinding agar bot dianggap "aman" dari tabrakan dinding
    private const double WallMargin = 72.0;

    // Jumlah giliran maksimum sejak terakhir terdeteksi agar musuh masih dianggap valid
    private const int MaxTargetAge = 18;

    // Dalam arena banyak bot, data ancaman boleh sedikit lebih lama agar bot sadar musuh belakang/samping
    private const int MaxThreatAge = 30;

    // Jarak proyeksi untuk menilai apakah arah gerak berikutnya aman atau tidak
    private const double MeleeProjectionDistance = 145.0;

    // Jarak proyeksi pendek untuk mengecek apakah mundur akan masuk blind spot/dinding
    private const double ReverseProjectionDistance = 105.0;

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

    // Jendela jink pendek setelah musuh menembak / bot terkena peluru
    private int duelJinkUntil = 0;

    // Giliran terakhir bot terkena peluru
    private int lastBulletHitTurn = -999;

    /// <summary>
    /// Entry point program. Membuat instance TankAlbino dan menjalankannya.
    /// </summary>
    static void Main(string[] args)
    {
        new TankAlbino().Start();
    }

    /// <summary>
    /// Konstruktor: mendaftarkan metadata bot ke sistem Robocode Tank Royale
    /// (nama, versi, penulis, deskripsi, bahasa, kategori, dll).
    /// </summary>
    public TankAlbino() : base(new BotInfo(
        "TankAlbino",
        "1.9",
        new List<string> { "Ayam Gak Jago" },
        "Bot ini mengimplementasikan algoritma greedy dengan pendekatan \"serangan dan pertahanan ditentukan melalui pemilihan target berbasis skor, tembakan prediktif, orbit evasion, dan pergerakan adaptif.\"",
        null,
        new List<string> { "ID" },
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

        // Warna tema putih metal / abu-abu cerah (tema visual modern)
        BodyColor   = Color.FromArgb(235, 235, 235); // putih utama
        TurretColor = Color.FromArgb(180, 180, 180); // abu metal
        RadarColor  = Color.FromArgb(220, 220, 220); // abu terang radar
        BulletColor = Color.FromArgb(25, 25, 25);    // hitam pekat
        ScanColor   = Color.FromArgb(160, 160, 160); // efek scan abu lembut

        // Senjata dan radar berputar secara independen dari badan
        AdjustGunForBodyTurn = true;
        AdjustRadarForBodyTurn = true;
        AdjustRadarForGunTurn = true;

        while (IsRunning)
        {
            // Pilih target terbaik berdasarkan skor greedy
            Enemy? target = SelectGreedyTarget();

            if (target == null)
            {
                // Tidak ada target terdeteksi â€” putar radar dan bergerak mencari musuh
                SearchForEnemy();
            }
            else
            {
                // Ada target â€” jalankan kontrol radar, tembakan, dan pergerakan
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
            Enemy? enemy;
            // Tambahkan entri baru jika musuh belum pernah terdeteksi sebelumnya
            if (!enemies.TryGetValue(e.ScannedBotId, out enemy))
            {
                enemy = new Enemy();
                enemy.Id = e.ScannedBotId;
                enemy.PreviousEnergy = e.Energy;
                enemy.PreviousX = e.X;
                enemy.PreviousY = e.Y;
                enemy.PreviousSpeed = e.Speed;
                enemy.PreviousScanTurn = e.TurnNumber;
                enemies[e.ScannedBotId] = enemy;
            }

            // Hitung penurunan energi musuh dibanding pembacaan sebelumnya
            double energyDrop = enemy.Energy - e.Energy;
            int scanDelta = Math.Max(1, e.TurnNumber - enemy.LastSeen);
            double previousLateralVelocity = enemy.LateralVelocity;

            if (enemy.LastSeen > 0)
            {
                enemy.PreviousX = enemy.X;
                enemy.PreviousY = enemy.Y;
                enemy.PreviousSpeed = enemy.Speed;
                enemy.PreviousScanTurn = enemy.LastSeen;

                double velocityX = (e.X - enemy.X) / scanDelta;
                double velocityY = (e.Y - enemy.Y) / scanDelta;
                double dx = e.X - X;
                double dy = e.Y - Y;
                double distanceForLateral = Math.Max(1.0, Math.Sqrt(dx * dx + dy * dy));
                double lateralVelocity = (velocityY * dx - velocityX * dy) / distanceForLateral;

                enemy.VelocityX = velocityX;
                enemy.VelocityY = velocityY;
                enemy.PreviousLateralVelocity = previousLateralVelocity;
                enemy.LateralVelocity = lateralVelocity;
                enemy.Acceleration = (e.Speed - enemy.Speed) / scanDelta;

                if (Math.Abs(previousLateralVelocity) > 0.35
                    && Math.Abs(lateralVelocity) > 0.35
                    && Math.Sign(previousLateralVelocity) != Math.Sign(lateralVelocity))
                {
                    enemy.LastLateralChangeTurn = e.TurnNumber;
                }
            }

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
            // kemungkinan musuh baru menembak â€” balik orbit untuk menghindar
            if (energyDrop > 0.09 && energyDrop <= 3.1 && enemy.Distance < 700)
            {
                if (!IsMeleeMode())
                    duelJinkUntil = Math.Max(duelJinkUntil, TurnNumber + (energyDrop >= 2.0 ? 16 : 10));

                ReverseOrbit();
            }

            enemy.PreviousEnergy = e.Energy;
        }
    }

    /// <summary>
    /// Dipanggil saat bot terkena peluru. Langsung membalik arah orbit sebagai
    /// respons menghindar.
    /// </summary>
    public override void OnHitByBullet(HitByBulletEvent e)
    {
        lastBulletHitTurn = TurnNumber;
        if (!IsMeleeMode())
            duelJinkUntil = Math.Max(duelJinkUntil, TurnNumber + 18);

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
            // Situasi ram menguntungkan â€” tandai sebagai target prioritas
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
    /// menggunakan algoritma greedy â€” musuh dengan skor tertinggi dipilih.
    /// Memberi bonus kecil jika target adalah musuh yang sama dari giliran sebelumnya
    /// (untuk menghindari pergantian target terus-menerus).
    /// </summary>
    private Enemy? SelectGreedyTarget()
    {
        // Ambil salinan musuh yang masih aktif dan terdeteksi baru-baru ini
        List<Enemy> snapshot = GetFreshEnemies(MaxTargetAge);

        Enemy? best = null;
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
        // Saat melee, musuh dekat di samping/belakang diprioritaskan agar bot tidak tunnel vision
        double meleeThreatBonus = 0.0;
        if (IsMeleeMode())
        {
            double closeFactor = 1.0 - Math.Min(distance, 560.0) / 560.0;
            double enemyBearingFromBody = Math.Abs(NormalizeRelativeAngle(DirectionTo(enemy.X, enemy.Y) - Direction));
            double sideOrBackThreat = enemyBearingFromBody > 70.0 ? 0.9 : 0.0;

            meleeThreatBonus = closeFactor * (1.6 + enemy.Energy / 85.0 + sideOrBackThreat);
        }
        // Penalti untuk data musuh yang sudah lama tidak diperbarui
        double agePenalty = Math.Max(0, TurnNumber - enemy.LastSeen) * 0.12;

        return hitChance * 5.0
            + killBonus
            + weakBonus
            + closeThreat * 1.4
            + ramOpportunity
            + meleeThreatBonus
            - agePenalty;
    }

    /// <summary>
    /// Mengontrol putaran radar agar terus mengunci posisi target.
    /// Menggunakan teknik "radar lock" dengan mengalikan sudut bearing target Ã— 2.2
    /// sehingga radar selalu melewati target (overscan) untuk menjaga kontak.
    /// </summary>
    private void ControlRadar(Enemy target)
    {
        // Di melee, radar tidak boleh terlalu lama mengunci satu musuh.
        // Sweep berkala menjaga data musuh belakang/samping tetap segar.
        if (IsMeleeMode() && TurnNumber % 24 < 8)
        {
            RadarTurnRate = radarSign * MaxRadarTurnRate;
            return;
        }

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

        // Target normal tetap memakai prediksi GreedyViper. Target zig-zag memakai
        // lead pendek agar peluru tidak terus melewati arah belok target.
        AimPoint aim = SelectAimPoint(target, power);
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
    /// - Dekat dinding atau paksa ke tengah â†’ bergerak ke tengah arena
    /// - Kondisi memungkinkan ram â†’ mençªçª ke arah musuh
    /// - Normal â†’ orbit melingkar di sekitar musuh dengan sudut adaptif
    /// Juga membalik arah jika terlalu lama tidak berputar.
    /// </summary>
    private void ControlMovement(Enemy target)
    {
        if (IsMeleeMode())
        {
            ControlMeleeMovement(target);
            return;
        }

        double distance = DistanceTo(target.X, target.Y);
        bool nearWall = IsNearWall(WallMargin) || TurnNumber < forceCenterUntil;
        bool jinkMode = TurnNumber < duelJinkUntil || TurnNumber - lastBulletHitTurn <= 12;

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
            // - Terlalu dekat (< 170): sudut besar (138Â°) untuk kabur
            // - Jarak ideal (170â€“560): sudut 92Â° (sirkular)
            // - Jauh (> 560): sudut kecil (42Â°) untuk mendekat
            double orbitAngle = distance < 170 ? 138 : distance > 560 ? 42 : 92;
            if (jinkMode)
                orbitAngle = distance < 190 ? 146 : distance > 560 ? 54 : ((TurnNumber / 4) % 2 == 0 ? 112 : 76);

            desiredHeading = NormalizeAbsoluteAngle(toEnemy + orbitAngle * orbitSign);

            // Cek apakah heading ini akan membawa bot mendekati dinding â€” jika ya, putar ke tengah
            AimPoint projected = Project(X, Y, desiredHeading, 130);
            if (OutsideSafeArea(projected.X, projected.Y))
                desiredHeading = DirectionTo(ArenaWidth / 2.0, ArenaHeight / 2.0);
        }

        // Faktor kecepatan disesuaikan agar bot tidak terlalu liar saat dekat dinding/musuh
        double speedFactor = 1.0;

        // Kurangi kecepatan saat dekat dinding atau terlalu dekat musuh (bukan ram)
        if (nearWall)
            speedFactor = 0.75;
        else if (distance < 120 && !ShouldRam(target, distance))
            speedFactor = 0.65;
        else if (jinkMode)
            speedFactor = (TurnNumber / 4) % 2 == 0 ? 1.0 : 0.72;

        MoveToward(desiredHeading, speedFactor);
    }

    /// <summary>
    /// Mode khusus arena banyak bot. Bot mengevaluasi beberapa arah kandidat dan
    /// memilih arah dengan skor keselamatan tertinggi dari semua musuh yang terlihat.
    /// </summary>
    private void ControlMeleeMovement(Enemy target)
    {
        double targetDistance = DistanceTo(target.X, target.Y);
        bool nearWall = IsNearWall(WallMargin) || TurnNumber < forceCenterUntil;

        // Perubahan arah lebih sering pada melee supaya pola gerak tidak mudah dibaca.
        if (TurnNumber - lastReverseTurn > 31)
            ReverseOrbit();

        if (ShouldRam(target, targetDistance))
        {
            MoveToward(DirectionTo(target.X, target.Y), 1.0);
            return;
        }

        List<Enemy> threats = GetFreshEnemies(MaxThreatAge);
        if (threats.Count == 0)
        {
            MoveToward(DirectionTo(ArenaWidth / 2.0, ArenaHeight / 2.0), 0.75);
            return;
        }

        double bestHeading = Direction;
        double bestScore = double.NegativeInfinity;

        // Kandidat arah dibuat mengelilingi 360 derajat. Offset kecil per turn
        // mencegah bot memilih titik yang terlalu repetitif.
        double offset = (TurnNumber % 2) * 11.25;
        for (int i = 0; i < 16; i++)
        {
            double heading = NormalizeAbsoluteAngle(i * 22.5 + offset);
            AimPoint projected = Project(X, Y, heading, MeleeProjectionDistance);
            double score = ScoreMeleePosition(projected, heading, target, threats, nearWall);

            if (score > bestScore)
            {
                bestScore = score;
                bestHeading = heading;
            }
        }

        double speedFactor = nearWall ? 0.78 : 1.0;
        if (targetDistance < 130 && !ShouldRam(target, targetDistance))
            speedFactor = 0.72;

        MoveToward(bestHeading, speedFactor);
    }

    /// <summary>
    /// Fungsi evaluasi greedy untuk movement melee. Posisi yang dipilih adalah posisi
    /// dengan jarak aman dari dinding, jauh dari musuh terdekat, tidak masuk kerumunan,
    /// dan tetap bergerak lateral terhadap target tembak.
    /// </summary>
    private double ScoreMeleePosition(AimPoint point, double moveHeading, Enemy target, List<Enemy> threats, bool nearWall)
    {
        if (point.X < 18 || point.Y < 18 || point.X > ArenaWidth - 18 || point.Y > ArenaHeight - 18)
            return -1000000.0;

        double wallDistance = Math.Min(
            Math.Min(point.X, ArenaWidth - point.X),
            Math.Min(point.Y, ArenaHeight - point.Y)
        );

        double wallScore = Limit(wallDistance / 130.0, 0.0, 1.6) * (nearWall ? 3.2 : 2.0);
        double nearestDistance = double.PositiveInfinity;
        double averageDistance = 0.0;
        double closePenalty = 0.0;
        double lateralTotal = 0.0;

        foreach (Enemy enemy in threats)
        {
            double distance = Distance(point.X, point.Y, enemy.X, enemy.Y);
            nearestDistance = Math.Min(nearestDistance, distance);
            averageDistance += Math.Min(distance, 900.0);

            if (distance < 165.0)
                closePenalty += (165.0 - distance) / 34.0;

            double enemyDirection = DirectionTo(enemy.X, enemy.Y);
            lateralTotal += Math.Abs(Math.Sin(ToRadians(NormalizeRelativeAngle(moveHeading - enemyDirection))));
        }

        averageDistance /= threats.Count;
        double lateralScore = lateralTotal / threats.Count;
        double nearestScore = Limit(nearestDistance / 260.0, 0.0, 2.6);
        double averageScore = Limit(averageDistance / 540.0, 0.0, 1.8);

        double targetDistance = Distance(point.X, point.Y, target.X, target.Y);
        double idealDistance = Energy > 35.0 ? 430.0 : 520.0;
        double targetDistanceScore = 1.0 - Math.Min(Math.Abs(targetDistance - idealDistance), 420.0) / 420.0;

        double centerDistance = Distance(point.X, point.Y, ArenaWidth / 2.0, ArenaHeight / 2.0);
        double maxCenterDistance = Distance(0, 0, ArenaWidth / 2.0, ArenaHeight / 2.0);
        double centerScore = 1.0 - Math.Min(centerDistance / maxCenterDistance, 1.0);

        return wallScore
            + nearestScore * 2.4
            + averageScore * 1.25
            + lateralScore * 1.35
            + targetDistanceScore * 0.85
            + centerScore * 0.45
            - closePenalty * 2.7;
    }

    /// <summary>
    /// Mengubah arah gerak aktual menuju heading tertentu. Jika heading berada jauh
    /// di belakang badan tank, bot akan mundur agar arah geraknya tetap menuju kandidat.
    /// </summary>
    private void MoveToward(double moveHeading, double speedFactor)
    {
        double turn = NormalizeRelativeAngle(moveHeading - Direction);
        double speed = MaxSpeed * Limit(speedFactor, 0.0, 1.0);

        if (Math.Abs(turn) > 100.0)
        {
            if (IsReverseUnsafe())
            {
                speed *= 0.48;
            }
            else
            {
                turn = NormalizeRelativeAngle(turn + 180.0);
                speed = -speed;
            }
        }

        TurnRate = Limit(turn, -MaxTurnRate, MaxTurnRate);
        TargetSpeed = speed;
    }

    /// <summary>
    /// Hindari keputusan mundur jika proyeksi pendeknya masuk dinding atau
    /// mendekati musuh yang berada di samping/belakang.
    /// </summary>
    private bool IsReverseUnsafe()
    {
        double reverseHeading = NormalizeAbsoluteAngle(Direction + 180.0);
        AimPoint projected = Project(X, Y, reverseHeading, ReverseProjectionDistance);

        if (OutsideSafeArea(projected.X, projected.Y))
            return true;

        foreach (Enemy enemy in GetFreshEnemies(MaxThreatAge))
        {
            double currentDistance = DistanceTo(enemy.X, enemy.Y);
            double projectedDistance = Distance(projected.X, projected.Y, enemy.X, enemy.Y);
            double bearingFromBody = Math.Abs(NormalizeRelativeAngle(DirectionTo(enemy.X, enemy.Y) - Direction));

            if (bearingFromBody > 75.0 && projectedDistance < currentDistance && projectedDistance < 245.0)
                return true;
        }

        return false;
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

        // Melawan target zig-zag seperti GreedyViper, peluru kecil-sedang lebih cepat
        // sampai sehingga peluang hit lebih baik daripada damage besar yang lambat.
        if (!IsMeleeMode() && IsZigzaggingTarget(target) && target.Energy > BulletDamage(power) + 0.2)
            power = Math.Min(power, distance < 260.0 ? 1.35 : 1.10);

        // Mode hemat energi saat energi kita rendah
        if (Energy < 18) power = Math.Min(power, 1.15);
        if (Energy < 8)  power = Math.Min(power, 0.55);

        // Saat melee, survival score sering lebih bernilai daripada menukar energi
        // dengan tembakan besar. Power besar disimpan untuk jarak dekat atau killshot.
        if (IsMeleeMode() && target.Energy > BulletDamage(power) + 0.2)
        {
            double meleeCap = distance < 180.0 ? 1.75 : 1.25;
            if (Energy < 28.0)
                meleeCap = Math.Min(meleeCap, 0.95);

            power = Math.Min(power, meleeCap);
        }

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
    /// Memilih model bidik. Untuk target stabil tetap gunakan prediksi GreedyViper.
    /// Untuk target zig-zag, gunakan prediksi pendek atau head-on saat baru berbalik.
    /// </summary>
    private AimPoint SelectAimPoint(Enemy target, double firepower)
    {
        if (IsMeleeMode() || !IsZigzaggingTarget(target))
            return PredictEnemyPosition(target, firepower);

        double distance = DistanceTo(target.X, target.Y);
        double leadScale = HasRecentLateralReversal(target)
            ? (distance < 280.0 ? 0.24 : 0.16)
            : (distance < 360.0 ? 0.48 : 0.36);

        AimPoint shortLead = PredictObservedEnemyPosition(target, firepower, leadScale);

        if (HasRecentLateralReversal(target))
        {
            double headOnBearing = Math.Abs(GunBearingTo(target.X, target.Y));
            double shortLeadBearing = Math.Abs(GunBearingTo(shortLead.X, shortLead.Y));

            if (headOnBearing <= shortLeadBearing + 2.0)
                return new AimPoint(target.X, target.Y);
        }

        return shortLead;
    }

    /// <summary>
    /// Prediksi berdasarkan perpindahan nyata antar-scan, bukan arah badan target.
    /// Ini lebih cocok melawan bot yang bergerak kiri-kanan cepat.
    /// </summary>
    private AimPoint PredictObservedEnemyPosition(Enemy target, double firepower, double leadScale)
    {
        double bulletSpeed = Math.Max(0.1, CalcBulletSpeed(firepower));
        double vx = target.VelocityX;
        double vy = target.VelocityY;

        if (Math.Abs(vx) + Math.Abs(vy) < 0.05)
        {
            double radians = ToRadians(target.Direction);
            vx = Math.Cos(radians) * target.Speed;
            vy = Math.Sin(radians) * target.Speed;
        }

        double px = target.X;
        double py = target.Y;

        for (int i = 0; i < 3; i++)
        {
            double travelTime = DistanceTo(px, py) / bulletSpeed;
            travelTime = Limit(travelTime, 0.0, 48.0);

            px = target.X + vx * travelTime * leadScale;
            py = target.Y + vy * travelTime * leadScale;
            px = Limit(px, 18, ArenaWidth - 18);
            py = Limit(py, 18, ArenaHeight - 18);
        }

        return new AimPoint(px, py);
    }

    private bool IsZigzaggingTarget(Enemy target)
    {
        double distance = DistanceTo(target.X, target.Y);
        bool lateralFast = Math.Abs(target.LateralVelocity) > 2.2 && distance > 170.0;
        bool recentReverse = HasRecentLateralReversal(target);
        bool highAcceleration = Math.Abs(target.Acceleration) > 0.75 && Math.Abs(target.Speed) > 2.5;

        return lateralFast || recentReverse || highAcceleration;
    }

    private bool HasRecentLateralReversal(Enemy target)
    {
        return target.LastLateralChangeTurn > 0
            && TurnNumber - target.LastLateralChangeTurn <= 16;
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
    /// Mengecek apakah permainan masih dalam mode melee (musuh banyak).
    /// </summary>
    private bool IsMeleeMode()
    {
        return EnemyCount > 2;
    }

    /// <summary>
    /// Mengambil snapshot musuh yang masih hidup dan masih cukup baru datanya.
    /// Snapshot dipakai agar logika greedy tidak terganggu event scan yang datang paralel.
    /// </summary>
    private List<Enemy> GetFreshEnemies(int maxAge)
    {
        List<Enemy> snapshot = new List<Enemy>();

        lock (enemyLock)
        {
            foreach (Enemy enemy in enemies.Values)
            {
                if (enemy.Alive && TurnNumber - enemy.LastSeen <= maxAge)
                    snapshot.Add(enemy.Clone());
            }
        }

        return snapshot;
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
        // Tambahkan margin 0.8Â° dan batasi antara 1.15Â° hingga 5.0Â°
        return Limit(botRadiusAngle + 0.8, 1.15, 5.0);
    }

    /// <summary>
    /// Menghitung kerusakan yang ditimbulkan oleh peluru dengan daya tertentu.
    /// Formula: 4 Ã— power untuk power â‰¤ 1; ditambah 2 Ã— (power - 1) untuk power > 1.
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

        // Untuk musuh sangat lemah (â‰¤4 energi): power = energy / 4
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
            return; // Terlalu cepat dibalik â€” abaikan

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
        duelJinkUntil = 0;
        lastBulletHitTurn = -999;
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
        public double PreviousX;
        public double PreviousY;
        public double Energy;
        public double PreviousEnergy; // Energi musuh pada scan sebelumnya (untuk deteksi tembakan)
        public double Direction;
        public double Speed;
        public double PreviousSpeed;
        public double Distance;
        public int LastSeen;          // Nomor giliran terakhir saat musuh ini terdeteksi
        public int PreviousScanTurn;
        public double VelocityX;
        public double VelocityY;
        public double LateralVelocity;
        public double PreviousLateralVelocity;
        public double Acceleration;
        public int LastLateralChangeTurn = -999;
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
