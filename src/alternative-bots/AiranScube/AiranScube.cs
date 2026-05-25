using System;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class AiranScube : Bot
{
    // ====================== KONSTANTA BOT ======================

    private const double BotRadius = 18;
    private const double WallMargin = BotRadius + 20;
    private const int TargetStaleTurns = 8;
    private const int RadarUnlockTurns = 3;
    private const int FullScanTimeout = 12;
    private const double WallFireMargin = 50;

    // ====================== VARIABEL STATE ======================

    private readonly Random rng = new Random();

    private EnemySnapshot? target;
    private int moveFlip = 1;
    private int lastDirectionSwitchTurn = -999;
    private int radarSweepDirection = 1;
    private bool wasTrackingTarget;

    private int velocityTurnCounter = 0;
    private int velocityMode = 1;

    // Mode full scan digunakan agar radar memutar 360 derajat
    // untuk mencari musuh secara menyeluruh.
    private bool fullScanMode = false;
    private int fullScanStartTurn = -999;
    private int lastObservedTurn = -1;

    // Variabel untuk membuat gerakan wiggle agar bot tidak bergerak terlalu lurus.
    private double wiggle = 0;
    private int wiggleCounter = 0;

    // ====================== ENTRY POINT & RUN LOOP ======================

    // Fungsi utama program.
    // Fungsi ini menjadi entry point pertama saat program dijalankan,
    // lalu membuat objek AiranScube dan menjalankan bot.
    static void Main(string[] args)
    {
        new AiranScube().Start();
    }

    // Constructor bot.
    // Fungsi ini memuat konfigurasi bot dari file AiranScube.json
    // agar bot dapat dikenali oleh Robocode Tank Royale.
    private AiranScube() : base(BotInfo.FromFile("AiranScube.json"))
    {
    }

    // Fungsi utama bot selama pertandingan berlangsung.
    // Fungsi ini mengatur alur utama bot, mulai dari reset state,
    // pengecekan target, movement, firing, radar lock, sampai search pattern.
    public override void Run()
    {
        BodyColor = Color.FromArgb(25, 25, 25);
        TurretColor = Color.FromArgb(220, 80, 35);
        RadarColor = Color.FromArgb(255, 190, 70);
        ScanColor = Color.FromArgb(255, 220, 100);
        BulletColor = Color.FromArgb(255, 140, 70);

        AdjustGunForBodyTurn = true;
        AdjustRadarForBodyTurn = true;
        AdjustRadarForGunTurn = true;
        MaxSpeed = 8.0;

        // Pada awal ronde, bot langsung melakukan full scan 360 derajat
        // agar dapat menemukan musuh secepat mungkin.
        BeginFullScan();

        while (IsRunning)
        {
            ResetRoundStateIfNeeded();

            bool hasTarget = HasFreshTarget();

            if (hasTarget)
            {
                int targetAge = TurnNumber - target!.LastSeenTurn;

                ExecuteLightningMovement(target);
                ExecuteGreedyAimAndFire(target);

                if (fullScanMode)
                {
                    // Jika full scan sudah terlalu lama, matikan full scan.
                    if (TurnNumber - fullScanStartTurn >= FullScanTimeout)
                    {
                        fullScanMode = false;
                    }
                    else
                    {
                        ExecuteWideRadarSweep();
                    }
                }
                else if (targetAge <= RadarUnlockTurns)
                {
                    // Jika data target masih segar, radar dikunci ke target.
                    LockRadar(target);
                }
                else
                {
                    // Jika data target mulai basi, radar melakukan sweep lebar
                    // untuk mencari ulang posisi target.
                    ExecuteWideRadarSweep();
                }

                wasTrackingTarget = true;
            }
            else
            {
                if (wasTrackingTarget)
                {
                    target = null;
                }

                ExecuteSearchPattern();
                wasTrackingTarget = false;
            }

            Go();
        }
    }

    // ====================== FULL SCAN ======================

    // Fungsi ini memulai mode full scan.
    // Radar diputar 360 derajat agar bot dapat mencari musuh di seluruh arena.
    private void BeginFullScan()
    {
        fullScanMode = true;
        fullScanStartTurn = TurnNumber;
        SetTurnRadarLeft(360 * radarSweepDirection);
    }

    // Fungsi ini mengecek apakah ronde baru dimulai.
    // Jika TurnNumber kembali lebih kecil dari sebelumnya, state bot direset
    // agar data musuh dan mode lama tidak terbawa ke ronde berikutnya.
    private void ResetRoundStateIfNeeded()
    {
        if (lastObservedTurn >= 0 && TurnNumber < lastObservedTurn)
        {
            target = null;
            wasTrackingTarget = false;
            fullScanMode = true;
            fullScanStartTurn = TurnNumber;
            velocityMode = 1;
            velocityTurnCounter = 0;
        }

        lastObservedTurn = TurnNumber;
    }

    // ====================== LIGHTNING MOVEMENT ======================

    // Fungsi ini mengatur movement utama AiranScube.
    // Bot membuat beberapa kandidat posisi, memberi skor pada setiap kandidat,
    // lalu memilih kandidat terbaik secara greedy untuk bergerak cepat dan sulit ditebak.
    private void ExecuteLightningMovement(EnemySnapshot enemy)
    {
        velocityTurnCounter++;
        wiggleCounter++;

        // Membalik arah velocity secara periodik atau saat musuh dekat,
        // agar pola gerak bot tidak mudah diprediksi.
        if (velocityTurnCounter % 32 == 0 ||
            (DistanceTo(enemy.X, enemy.Y) < 220 && rng.NextDouble() < 0.13))
        {
            velocityMode *= -1;
            velocityTurnCounter = 0;
        }

        // Membalik arah strafing secara acak jika terlalu lama bergerak
        // dengan arah yang sama.
        if (TurnNumber - lastDirectionSwitchTurn > 22 && rng.NextDouble() < 0.08)
        {
            moveFlip *= -1;
            lastDirectionSwitchTurn = TurnNumber;
        }

        double toEnemy = DirectionTo(enemy.X, enemy.Y);

        // Wiggle digunakan agar arah gerak sedikit berosilasi dan tidak lurus.
        wiggle = Math.Sin(wiggleCounter * 0.85) * 18;

        // Kandidat gerak dibuat dari beberapa arah relatif terhadap musuh.
        // Kandidat ini akan dinilai lalu dipilih yang skornya paling tinggi.
        var candidates = new[]
        {
            BuildCandidate(toEnemy + 85 * moveFlip + wiggle, 165),
            BuildCandidate(toEnemy + 110 * moveFlip + wiggle, 150),
            BuildCandidate(toEnemy + 65 * moveFlip + wiggle, 140),
            BuildCandidate(toEnemy + 135 * moveFlip, 130),
            BuildCandidate(toEnemy - 85 * moveFlip + wiggle, 155),
            BuildCandidate(toEnemy + 155 * moveFlip, 120),
            BuildCandidate(toEnemy + 175, 105),
            BuildCandidate(toEnemy + 45 * moveFlip, 115),
            BuildCandidate(toEnemy - 45 * moveFlip, 110)
        };

        CandidateAction best = candidates[0];
        double bestScore = double.NegativeInfinity;

        foreach (CandidateAction candidate in candidates)
        {
            double score = ScoreLightningCandidate(candidate, enemy);

            double angleDiff = Math.Abs(
                NormalizeRelativeAngle(candidate.Heading - toEnemy)
            );

            // Bonus diberikan pada gerakan menyamping atau strafing,
            // karena gerakan lateral membuat bot lebih sulit ditembak.
            if (angleDiff > 65 && angleDiff < 115)
            {
                score += 48;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        double desiredVelocity = velocityMode switch
        {
            1 => 8.0,
            -1 => -8.0,
            _ => 6.5
        };

        // Sesekali bot memperlambat gerakan secara mendadak
        // untuk mengacaukan prediksi tembakan musuh.
        if (velocityTurnCounter % 17 == 0 && rng.NextDouble() < 0.45)
        {
            desiredVelocity *= 0.32;
        }

        double strafeFactor = Math.Abs(
            NormalizeRelativeAngle(best.Heading - toEnemy)
        ) > 70 ? 1.65 : 1.0;

        TurnRate = 4.8 * moveFlip * strafeFactor;

        DriveToVelocity(best.TargetX, best.TargetY, desiredVelocity);
    }

    // Fungsi ini menghitung skor untuk satu kandidat gerak.
    // Skor mempertimbangkan jarak ideal dari musuh, keamanan dari dinding,
    // kualitas strafing, posisi terhadap tengah arena, bahaya jarak dekat, dan variasi acak.
    private double ScoreLightningCandidate(CandidateAction candidate, EnemySnapshot enemy)
    {
        double distanceToEnemy = Distance(
            candidate.TargetX,
            candidate.TargetY,
            enemy.X,
            enemy.Y
        );

        double preferredDistance = Energy > 45 ? 275 : Energy > 25 ? 340 : 420;
        double distanceScore = 265 - Math.Abs(distanceToEnemy - preferredDistance) * 0.68;

        double wall = WallSafety(candidate.TargetX, candidate.TargetY);
        double wallScore = wall * 2.25;

        double strafeAngle = Math.Abs(
            NormalizeRelativeAngle(
                candidate.Heading -
                AbsoluteDirectionBetween(candidate.TargetX, candidate.TargetY, enemy.X, enemy.Y)
            )
        );

        double strafeScore = (1 - Math.Abs(strafeAngle - 90) / 90.0) * 145;

        double centerScore =
            48 - Distance(
                candidate.TargetX,
                candidate.TargetY,
                ArenaWidth / 2.0,
                ArenaHeight / 2.0
            ) * 0.09;

        double closeDanger = distanceToEnemy < 115 ? -230 : 0;
        double randomScore = rng.NextDouble() * 14;

        return distanceScore
            + wallScore
            + strafeScore
            + centerScore
            + closeDanger
            + randomScore;
    }

    // Fungsi ini menggerakkan bot menuju koordinat tertentu dengan velocity tertentu.
    // Jika sudut tujuan terlalu besar, bot memilih bergerak mundur agar putaran lebih efisien.
    private void DriveToVelocity(double targetX, double targetY, double velocity)
    {
        double bearing = BearingTo(targetX, targetY);
        double distance = DistanceTo(targetX, targetY);

        if (Math.Abs(bearing) <= 90)
        {
            SetTurnLeft(bearing);

            if (velocity >= 0)
            {
                SetForward(distance);
            }
            else
            {
                SetBack(distance);
            }
        }
        else
        {
            double backBearing = NormalizeRelativeAngle(
                bearing + (bearing > 0 ? -180 : 180)
            );

            SetTurnLeft(backBearing);

            if (velocity >= 0)
            {
                SetBack(distance);
            }
            else
            {
                SetForward(distance);
            }
        }

        TargetSpeed = velocity;
    }

    // ====================== HELPER ======================

    // Fungsi ini membangun kandidat gerakan berdasarkan heading dan jarak.
    // Titik hasil proyeksi dibatasi agar tetap berada dalam area aman arena.
    private CandidateAction BuildCandidate(double heading, double distance)
    {
        double normalizedHeading = NormalizeAbsoluteAngle(heading);
        double radians = normalizedHeading * Math.PI / 180.0;

        double projectedX = X + Math.Cos(radians) * distance;
        double projectedY = Y + Math.Sin(radians) * distance;

        double safeX = Math.Clamp(projectedX, WallMargin, ArenaWidth - WallMargin);
        double safeY = Math.Clamp(projectedY, WallMargin, ArenaHeight - WallMargin);

        return new CandidateAction(normalizedHeading, distance, safeX, safeY);
    }

    // Fungsi ini menghitung seberapa aman suatu posisi dari dinding.
    // Nilai semakin besar berarti posisi semakin jauh dari dinding arena.
    private double WallSafety(double x, double y)
    {
        double left = x - WallMargin;
        double right = ArenaWidth - WallMargin - x;
        double bottom = y - WallMargin;
        double top = ArenaHeight - WallMargin - y;

        return Math.Max(
            0,
            Math.Min(
                Math.Min(left, right),
                Math.Min(bottom, top)
            )
        );
    }

    // Fungsi ini menghitung jarak Euclidean antara dua titik.
    // Digunakan untuk menilai jarak bot, musuh, kandidat posisi, dan titik tengah arena.
    private static double Distance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;

        return Math.Sqrt(dx * dx + dy * dy);
    }

    // Fungsi ini menghitung arah absolut dari satu titik ke titik lain.
    // Arah ini dipakai untuk mengevaluasi sudut strafing terhadap musuh.
    private double AbsoluteDirectionBetween(double fromX, double fromY, double toX, double toY)
    {
        return NormalizeAbsoluteAngle(
            Math.Atan2(toY - fromY, toX - fromX) * 180 / Math.PI
        );
    }

    // Fungsi ini menormalkan sudut absolut ke rentang 0 sampai 360 derajat.
    // Tujuannya agar perhitungan arah tidak menghasilkan nilai negatif.
    private new double NormalizeAbsoluteAngle(double angle)
    {
        angle %= 360;

        return angle >= 0 ? angle : angle + 360;
    }

    // Fungsi ini menormalkan sudut relatif ke rentang -180 sampai 180 derajat.
    // Tujuannya agar bot dapat memilih arah putaran terpendek.
    private new double NormalizeRelativeAngle(double angle)
    {
        angle %= 360;

        if (angle <= -180)
        {
            angle += 360;
        }

        if (angle > 180)
        {
            angle -= 360;
        }

        return angle;
    }

    // Fungsi ini membatasi besar putaran radar.
    // Jika putaran terlalu kecil, diberi nilai minimum agar radar tetap bergerak.
    private static double ClampRadarTurn(double turn)
    {
        if (Math.Abs(turn) < 3)
        {
            return 3 * Math.Sign(turn == 0 ? 1 : turn);
        }

        return Math.Clamp(turn, -360, 360);
    }

    // ====================== TARGET & SEARCH ======================

    // Fungsi ini mengecek apakah target saat ini masih valid.
    // Target dianggap valid jika ada dan belum melewati batas TargetStaleTurns.
    private bool HasFreshTarget()
    {
        return target is not null
            && TurnNumber - target.LastSeenTurn <= TargetStaleTurns;
    }

    // Fungsi ini mengatur pola gerak ketika bot belum memiliki target.
    // Bot bergerak sambil memutar gun dan radar untuk mencari musuh baru.
    private void ExecuteSearchPattern()
    {
        double centerBearing = BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0);
        bool nearWall = WallSafety(X, Y) < 38;

        SetTurnLeft(nearWall ? centerBearing : 5 * moveFlip);
        SetForward(nearWall ? 160 : 110);
        SetTurnGunLeft(14 * moveFlip);

        ExecuteWideRadarSweep();
    }

    // ====================== EVENTS ======================

    // Fungsi ini dijalankan saat radar mendeteksi bot musuh.
    // Bot memperbarui data target, mengunci radar secara cepat,
    // mengarahkan gun, dan menembak jika kondisi memungkinkan.
    public override void OnScannedBot(ScannedBotEvent e)
    {
        UpdateTarget(e);

        if (target != null && target.Id == e.ScannedBotId)
        {
            fullScanMode = false;
            QuickLockRadar(target);

            double gunBearing = GunBearingTo(target.X, target.Y);
            SetTurnGunLeft(gunBearing);

            if (TurnNumber - target.LastSeenTurn <= 1
                && Math.Abs(gunBearing) < 6
                && GunHeat == 0
                && Energy > 1.2)
            {
                SetFire(Math.Min(2.2, Math.Max(0.5, Energy - 0.2)));
            }
        }
        else if (target == null)
        {
            fullScanMode = false;
        }
    }

    // Fungsi ini melakukan lock radar cepat ke target.
    // Radar diputar melewati target dengan faktor 2.4 agar tidak mudah kehilangan lock.
    private void QuickLockRadar(EnemySnapshot enemy)
    {
        double radarBearing = RadarBearingTo(enemy.X, enemy.Y);
        double turn = ClampRadarTurn(radarBearing * 2.4);

        SetTurnRadarLeft(turn);
    }

    // Fungsi ini dijalankan saat bot terkena peluru.
    // Bot langsung membalik arah gerak dan velocity untuk menghindari tembakan berikutnya,
    // lalu mengaktifkan full scan agar dapat menemukan ulang posisi musuh.
    public override void OnHitByBullet(HitByBulletEvent e)
    {
        moveFlip *= -1;
        velocityMode *= -1;
        lastDirectionSwitchTurn = TurnNumber;

        TurnRate = rng.Next(6, 11) * moveFlip;

        if (rng.NextDouble() < 0.55)
        {
            SetForward(170);
        }
        else
        {
            SetBack(170);
        }

        fullScanMode = true;
        fullScanStartTurn = TurnNumber;
    }

    // Fungsi ini dijalankan saat bot menabrak dinding.
    // Bot membalik arah movement, mundur, dan memutar badan agar keluar dari dinding.
    public override void OnHitWall(HitWallEvent e)
    {
        moveFlip *= -1;
        velocityMode *= -1;
        velocityTurnCounter = 0;

        SetBack(150);
        SetTurnLeft(48 * moveFlip);
    }

    // Fungsi ini dijalankan saat bot menabrak bot lain.
    // Bot mundur agar tidak terjebak, mengubah arah gerak,
    // dan menembak jika gun sudah cukup dingin.
    public override void OnHitBot(HitBotEvent e)
    {
        moveFlip *= -1;

        SetBack(90);
        SetTurnLeft(BearingTo(e.X, e.Y) + 30 * moveFlip);

        if (GunHeat <= 0.1)
        {
            SetFire(Math.Min(2.5, Math.Max(0.6, Energy - 0.4)));
        }
    }

    // Fungsi ini dijalankan saat peluru bot mengenai dinding.
    // Kondisi ini menandakan tembakan kemungkinan kurang akurat,
    // sehingga bot mengaktifkan full scan untuk memperbarui data target.
    public override void OnBulletHitWall(BulletHitWallEvent e)
    {
        fullScanMode = true;
        fullScanStartTurn = TurnNumber;
    }

    // Fungsi ini dijalankan saat peluru bot mengenai musuh.
    // Bot melakukan Rescan untuk mempertahankan lock dan memperbarui data musuh.
    public override void OnBulletHit(BulletHitBotEvent e)
    {
        Rescan();
    }

    // Fungsi ini dijalankan saat ada bot musuh yang mati.
    // Jika musuh yang mati adalah target saat ini, target direset
    // dan bot memulai full scan untuk mencari musuh berikutnya.
    public override void OnBotDeath(BotDeathEvent e)
    {
        if (target?.Id == e.VictimId)
        {
            target = null;
            wasTrackingTarget = false;

            BeginFullScan();
        }
    }

    // ====================== TARGET MANAGEMENT ======================

    // Fungsi ini memperbarui target berdasarkan hasil scan radar.
    // Bot memakai skor prioritas untuk menentukan apakah tetap pada target lama
    // atau mengganti target ke musuh baru yang lebih menguntungkan.
    private void UpdateTarget(ScannedBotEvent e)
    {
        double scannedDistance = DistanceTo(e.X, e.Y);
        double scannedScore = ComputeTargetPriority(
            scannedDistance,
            e.Energy,
            target?.Id == e.ScannedBotId
        );

        if (target is null)
        {
            target = EnemySnapshot.CreateFromScan(e);
            return;
        }

        double currentDistance = DistanceTo(target.X, target.Y);
        double currentScore = ComputeTargetPriority(currentDistance, target.Energy, true);
        int targetAge = TurnNumber - target.LastSeenTurn;

        if (e.ScannedBotId == target.Id)
        {
            target = EnemySnapshot.UpdateFromScan(target, e);
            return;
        }

        if (targetAge > 2 || scannedScore >= currentScore + 8)
        {
            target = EnemySnapshot.CreateFromScan(e);
        }
    }

    // Fungsi ini menghitung skor prioritas target.
    // Target yang lebih dekat, energinya lebih rendah, dan sudah terkunci
    // akan mendapatkan skor lebih tinggi.
    private static double ComputeTargetPriority(
        double distance,
        double enemyEnergy,
        bool alreadyLocked)
    {
        double lockBonus = alreadyLocked ? 90 : 0;

        return (2600 / (distance + 40)) + (140 - enemyEnergy) + lockBonus;
    }

    // ====================== AIMING & FIRING ======================

    // Fungsi ini memilih tembakan terbaik secara greedy.
    // Bot mencoba beberapa opsi firepower, memprediksi posisi musuh,
    // menghitung peluang hit dan damage, lalu memilih skor tertinggi.
    private void ExecuteGreedyAimAndFire(EnemySnapshot enemy)
    {
        double[] fireOptions = { 0.8, 1.3, 1.9, 2.5, 3.0 };

        double bestScore = double.NegativeInfinity;
        double bestFirepower = 1.0;
        double bestAimX = enemy.X;
        double bestAimY = enemy.Y;

        foreach (double firepower in fireOptions)
        {
            if (Energy <= firepower + 0.3)
            {
                continue;
            }

            var predicted = PredictEnemyPosition(enemy, firepower);
            double aimX = predicted.X;
            double aimY = predicted.Y;

            double aimWallDistance = Math.Min(
                Math.Min(aimX, ArenaWidth - aimX),
                Math.Min(aimY, ArenaHeight - aimY)
            );

            // Jika titik prediksi terlalu dekat atau keluar dari batas arena,
            // opsi tembakan ini dilewati.
            if (aimWallDistance < BotRadius)
            {
                continue;
            }

            double distance = DistanceTo(aimX, aimY);
            double gunOffset = Math.Abs(GunBearingTo(aimX, aimY));

            double hitChance = EstimateHitChance(distance, gunOffset, enemy.Speed);

            // Jika titik prediksi terlalu dekat dinding,
            // peluang hit dikurangi karena musuh bisa berubah arah akibat dinding.
            if (aimWallDistance < WallFireMargin)
            {
                hitChance *= 0.35 + 0.65 * (aimWallDistance / WallFireMargin);
            }

            double damage = 4 * firepower + (firepower > 1 ? 2 * (firepower - 1) : 0);
            double energyPenalty = firepower * (Energy < 28 ? 1.6 : 1.1);
            double score = damage * hitChance - energyPenalty;

            if (score > bestScore)
            {
                bestScore = score;
                bestFirepower = firepower;
                bestAimX = aimX;
                bestAimY = aimY;
            }
        }

        double gunBearing = GunBearingTo(bestAimX, bestAimY);
        SetTurnGunLeft(gunBearing);

        // Bot tidak menembak jika data target sudah terlalu lama
        // atau radar sedang full scan agar energi tidak terbuang.
        if (TurnNumber - enemy.LastSeenTurn > 2)
        {
            return;
        }

        if (fullScanMode)
        {
            return;
        }

        if (GunHeat <= 0.1 && Math.Abs(gunBearing) <= 9)
        {
            SetFire(Math.Min(bestFirepower, Math.Max(0.3, Energy - 0.3)));
        }
    }

    // Fungsi ini memprediksi posisi musuh saat peluru sampai.
    // Prediksi menggunakan velocity musuh dan kecepatan peluru berdasarkan firepower.
    private (double X, double Y) PredictEnemyPosition(EnemySnapshot enemy, double firepower)
    {
        double bulletSpeed = 20 - 3 * firepower;

        double predictedX = enemy.X;
        double predictedY = enemy.Y;

        for (int i = 0; i < 5; i++)
        {
            double distance = DistanceTo(predictedX, predictedY);
            double time = distance / bulletSpeed;

            predictedX = enemy.X + enemy.VelocityX * time;
            predictedY = enemy.Y + enemy.VelocityY * time;

            predictedX = Math.Clamp(predictedX, BotRadius, ArenaWidth - BotRadius);
            predictedY = Math.Clamp(predictedY, BotRadius, ArenaHeight - BotRadius);
        }

        return (predictedX, predictedY);
    }

    // Fungsi ini memperkirakan peluang peluru mengenai musuh.
    // Faktor yang digunakan adalah jarak, selisih sudut gun, dan kecepatan musuh.
    private static double EstimateHitChance(
        double distance,
        double gunOffset,
        double enemySpeed)
    {
        double distanceFactor = Math.Clamp(1.2 - distance / 620.0, 0.15, 1.0);
        double aimFactor = Math.Clamp(1.0 - gunOffset / 42.0, 0.08, 1.0);
        double speedFactor = Math.Clamp(1.0 - Math.Abs(enemySpeed) / 11.0, 0.25, 1.0);

        return Math.Clamp(
            distanceFactor * aimFactor * speedFactor,
            0.05,
            0.97
        );
    }

    // ====================== RADAR ======================

    // Fungsi ini mengunci radar ke musuh yang sedang menjadi target.
    // Radar dibuat melewati target agar scan tetap stabil meskipun musuh bergerak.
    private void LockRadar(EnemySnapshot enemy)
    {
        double radarBearing = RadarBearingTo(enemy.X, enemy.Y);

        // Narrow lock: turn dua kali bearing agar radar sweep melintasi target.
        double turn = radarBearing * 2.0;

        // Minimum turn rate agar radar tidak berhenti.
        if (Math.Abs(turn) < 3)
        {
            turn = 3 * (radarBearing >= 0 ? 1 : -1);
        }

        turn = Math.Clamp(turn, -360, 360);

        SetTurnRadarLeft(turn);

        // Jika radar sudah sangat dekat target, rescan untuk memastikan lock.
        if (Math.Abs(radarBearing) < 1.5)
        {
            Rescan();
        }
    }

    // Fungsi ini melakukan sweep radar lebar.
    // Jika radar sudah selesai berputar, arah sweep dibalik dan radar diputar 360 derajat lagi.
    private void ExecuteWideRadarSweep()
    {
        if (Math.Abs(RadarTurnRemaining) < 1)
        {
            radarSweepDirection *= -1;
            SetTurnRadarLeft(360 * radarSweepDirection);
        }
    }

    // ====================== RECORD & INNER CLASS ======================

    // Record ini menyimpan kandidat aksi movement.
    // Data ini berisi heading, jarak proyeksi, dan titik tujuan kandidat.
    private sealed record CandidateAction(
        double Heading,
        double Distance,
        double TargetX,
        double TargetY
    );

    // Class ini menyimpan snapshot data musuh hasil scan.
    // Data yang disimpan meliputi posisi, energi, arah, speed, velocity, dan turn terakhir terlihat.
    private sealed class EnemySnapshot
    {
        public int Id { get; private init; }
        public double X { get; private init; }
        public double Y { get; private init; }
        public double Energy { get; private init; }
        public double Direction { get; private init; }
        public double Speed { get; private init; }
        public double VelocityX { get; private init; }
        public double VelocityY { get; private init; }
        public int LastSeenTurn { get; private init; }

        // Fungsi ini membuat snapshot musuh baru dari event scan pertama.
        // Velocity dihitung dari arah dan speed musuh yang terbaca radar.
        public static EnemySnapshot CreateFromScan(ScannedBotEvent e)
        {
            double radians = e.Direction * Math.PI / 180.0;

            return new EnemySnapshot
            {
                Id = e.ScannedBotId,
                X = e.X,
                Y = e.Y,
                Energy = e.Energy,
                Direction = e.Direction,
                Speed = e.Speed,
                VelocityX = Math.Cos(radians) * e.Speed,
                VelocityY = Math.Sin(radians) * e.Speed,
                LastSeenTurn = e.TurnNumber
            };
        }

        // Fungsi ini memperbarui snapshot musuh dari scan terbaru.
        // Velocity digabungkan dari hasil pengukuran perpindahan dan arah gerak musuh
        // agar prediksi posisi menjadi lebih stabil.
        public static EnemySnapshot UpdateFromScan(EnemySnapshot previous, ScannedBotEvent e)
        {
            int delta = Math.Max(1, e.TurnNumber - previous.LastSeenTurn);

            double measuredVelocityX = (e.X - previous.X) / delta;
            double measuredVelocityY = (e.Y - previous.Y) / delta;

            double radians = e.Direction * Math.PI / 180.0;

            double directionVelocityX = Math.Cos(radians) * e.Speed;
            double directionVelocityY = Math.Sin(radians) * e.Speed;

            return new EnemySnapshot
            {
                Id = e.ScannedBotId,
                X = e.X,
                Y = e.Y,
                Energy = e.Energy,
                Direction = e.Direction,
                Speed = e.Speed,
                VelocityX = measuredVelocityX * 0.7 + directionVelocityX * 0.3,
                VelocityY = measuredVelocityY * 0.7 + directionVelocityY * 0.3,
                LastSeenTurn = e.TurnNumber
            };
        }
    }
}