using System;
using System.Collections.Generic;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class BantengMerah : Bot
{
    // ── Jarak aman dari dinding sebelum bot menghindarinya ──────────
    private const double WALL_MARGIN = 80.0;

    // ── Jarak RAM aktif: bot mulai mengejar lurus dan tidak zig-zag ─
    private const double RAM_ENGAGE_RANGE = 120.0;

    // ── Jarak point-blank: bot sudah sangat dekat dengan musuh ──────
    private const double CLOSE_RANGE = 30.0;

    // ── Jarak menengah: batas pemilihan peluru sedang/berat ─────────
    private const double MEDIUM_RANGE = 350.0;

    // ── Daya tembak maksimum saat kondisi dekat atau ramming ────────
    private const double MAX_FIREPOWER = 3.0;

    // ── Ambang energi kritis: bot mulai hemat energi jika di bawah ini
    private const double ENERGY_CRITICAL = 15.0;

    // ── Batas maksimal data musuh masih dianggap valid ──────────────
    private const int STALE_THRESHOLD = 8;

    // ── Interval sweep radar penuh untuk mencari ulang semua musuh ──
    private const int SWEEP_INTERVAL = 20;

    // ── Durasi dasar satu fase zig-zag sebelum arah dibalik ─────────
    private const int ZIGZAG_PHASE_TURNS = 12;

    // ── Besar sudut zig-zag terhadap arah target ────────────────────
    private const double ZIGZAG_ANGLE = 30.0;

    // ── Lama bot mempertahankan dorongan setelah menabrak musuh ─────
    private const int RAM_SUSTAIN_TURNS = 8;

    // Menyimpan semua data musuh yang pernah terdeteksi radar.
    private readonly Dictionary<int, EnemyInfo> _enemies = new();

    // Target yang sedang dipilih untuk dikejar dan ditembak.
    private EnemyInfo? _target;

    // Arah sweep radar: +1 atau -1 agar arah putaran bisa bergantian.
    private int _radarSweepDir = 1;

    // Turn terakhir saat radar dipaksa melakukan sweep penuh.
    private int _lastSweepTurn = 0;

    // Menandakan apakah radar sedang dalam mode sweep penuh.
    private bool _sweeping = true;

    // Menyimpan total derajat putaran radar saat mode sweep.
    private double _sweepAccumulated = 0.0;

    // Menyimpan arah radar pada turn sebelumnya untuk menghitung delta putaran.
    private double _lastRadarDir = 0.0;

    // Arah zig-zag: +1 untuk satu sisi, -1 untuk sisi sebaliknya.
    private int _zigzagDir = 1;

    // Menghitung sudah berapa turn bot berada dalam satu fase zig-zag.
    private int _zigzagCounter = 0;

    // Random digunakan agar pola zig-zag tidak terlalu mudah ditebak.
    private readonly Random _rng = new();

    // Batas durasi fase zig-zag saat ini, dibuat berubah-ubah secara acak.
    private int _zigzagPhaseLimit = ZIGZAG_PHASE_TURNS;

    // Menandakan apakah bot sedang dalam mode ramming.
    private bool _isRamming = false;

    // Counter untuk mempertahankan momentum ram setelah bot menabrak musuh.
    private int _ramSustainCounter = 0;

    // Fungsi utama program.
    // Fungsi ini menjadi entry point yang pertama kali dijalankan,
    // lalu membuat objek BantengMerah dan menjalankan bot.
    static void Main(string[] args)
    {
        new BantengMerah().Start();
    }

    // Constructor bot.
    // Fungsi ini memuat konfigurasi bot dari file BantengMerah.json
    // agar bot dapat dikenali dan dijalankan oleh Robocode Tank Royale.
    BantengMerah() : base(BotInfo.FromFile("BantengMerah.json"))
    {
    }

    // Fungsi utama bot selama pertandingan berjalan.
    // Fungsi ini mengatur radar, memilih apakah bot harus mengejar target,
    // menembak, atau bergerak mencari musuh jika belum ada target.
    public override void Run()
    {
        AdjustGunForBodyTurn = true;
        AdjustRadarForBodyTurn = true;
        AdjustRadarForGunTurn = true;

        BodyColor = Color.FromArgb(180, 0, 0);
        TurretColor = Color.FromArgb(100, 0, 0);
        RadarColor = Color.FromArgb(255, 80, 0);
        BulletColor = Color.FromArgb(255, 200, 0);
        ScanColor = Color.FromArgb(255, 60, 0);

        _lastRadarDir = RadarDirection;

        while (IsRunning)
        {
            ExecuteRadarControl();

            if (HasFreshTarget())
            {
                // Tembak dulu agar gun mulai mengarah sebelum badan bergerak.
                ExecuteRamFire(_target!);

                // Setelah itu bot bergerak mengejar atau melakukan ram.
                ExecuteRamChase(_target!);
            }
            else
            {
                // Jika tidak ada target, reset mode ram dan masuk pola pencarian.
                _target = null;
                _isRamming = false;
                _ramSustainCounter = 0;
                ExecuteSearchPattern();
            }

            Go();
        }
    }

    // Fungsi ini mengontrol radar bot.
    // Radar akan melakukan sweep 360 derajat secara berkala untuk mencari musuh.
    // Jika sudah ada target, radar akan mengunci target dengan sedikit overshoot
    // agar tracking tidak mudah hilang saat musuh bergerak.
    private void ExecuteRadarControl()
    {
        double radarDelta = Math.Abs(RadarDirection - _lastRadarDir);

        if (radarDelta > 180)
            radarDelta = 360 - radarDelta;

        _sweepAccumulated += radarDelta;
        _lastRadarDir = RadarDirection;

        if (TurnNumber - _lastSweepTurn >= SWEEP_INTERVAL)
        {
            _sweeping = true;
            _sweepAccumulated = 0.0;
            _lastSweepTurn = TurnNumber;
        }

        if (_sweeping)
        {
            SetTurnRadarLeft(45.0 * _radarSweepDir);

            if (_sweepAccumulated >= 360.0)
            {
                _sweeping = false;
                _sweepAccumulated = 0.0;
                _radarSweepDir *= -1;
            }
        }
        else if (_target != null)
        {
            double radarBearing = RadarBearingTo(_target.X, _target.Y);
            double overshoot = radarBearing >= 0 ? 22 : -22;

            if (_target.Speed > 4)
                overshoot *= 1.5;

            SetTurnRadarLeft(radarBearing + overshoot);
        }
        else
        {
            _sweeping = true;
            _sweepAccumulated = 0.0;
        }
    }

    // Fungsi ini mengatur pergerakan bot saat mengejar target.
    // Jika musuh sudah cukup dekat, bot masuk mode ram dan maju lurus.
    // Jika masih jauh, bot bergerak zig-zag agar tidak mudah ditembak.
    // Jika dekat dinding, bot akan memprioritaskan bergerak ke tengah arena.
    private void ExecuteRamChase(EnemyInfo target)
    {
        if (IsNearWall())
        {
            _isRamming = false;
            _ramSustainCounter = 0;

            SetTurnLeft(BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0));
            SetForward(200);

            TargetSpeed = 8;
            _zigzagCounter = 0;

            return;
        }

        double distance = DistanceTo(target.X, target.Y);
        double bearingToTarget = BearingTo(target.X, target.Y);

        if (_ramSustainCounter > 0)
            _ramSustainCounter--;

        bool shouldRam = distance <= RAM_ENGAGE_RANGE || _ramSustainCounter > 0;

        if (shouldRam)
        {
            // Mode ram: bot maju lurus dan melakukan overshoot
            // agar terus mendorong musuh, bukan berhenti tepat di posisi musuh.
            _isRamming = true;

            SetTurnLeft(bearingToTarget);
            SetForward(distance + 300);

            TargetSpeed = 8;
            _zigzagCounter = 0;
        }
        else
        {
            // Mode chase biasa: bot mendekati musuh dengan pola zig-zag.
            _isRamming = false;
            _zigzagCounter++;

            if (_zigzagCounter >= _zigzagPhaseLimit)
            {
                _zigzagCounter = 0;
                _zigzagDir *= -1;
                _zigzagPhaseLimit = _rng.Next(8, 17);
            }

            double zigzagBearing = bearingToTarget + ZIGZAG_ANGLE * _zigzagDir;

            SetTurnLeft(zigzagBearing);
            SetForward(distance);

            TargetSpeed = 8;
        }
    }

    // Fungsi ini mengatur tembakan saat bot mengejar musuh.
    // Bot memprediksi posisi musuh saat peluru sampai, memilih firepower terbaik,
    // lalu menembak jika arah gun sudah cukup akurat.
    private void ExecuteRamFire(EnemyInfo target)
    {
        if (GunHeat != 0)
            return;

        if (Energy < 1.0)
            return;

        double distance = DistanceTo(target.X, target.Y);

        // Estimasi firepower awal untuk membantu prediksi posisi musuh.
        double estFp = Energy < ENERGY_CRITICAL ? 1.0 :
                       _isRamming ? 3.0 :
                       distance < MEDIUM_RANGE ? 2.0 : 1.5;

        // Prediksi posisi musuh saat peluru tiba.
        // Sistem Tank Royale memakai 0 derajat = utara,
        // sehingga X memakai Sin dan Y memakai Cos.
        double predX = target.X;
        double predY = target.Y;

        for (int i = 0; i < 5; i++)
        {
            double bulletSpeed = GetBulletSpeed(estFp);
            double dist = DistanceTo(predX, predY);
            double travelTime = dist / bulletSpeed;
            double rad = target.Direction * Math.PI / 180.0;

            predX = target.X + Math.Sin(rad) * target.Speed * travelTime;
            predY = target.Y + Math.Cos(rad) * target.Speed * travelTime;

            predX = Math.Clamp(predX, WALL_MARGIN, ArenaWidth - WALL_MARGIN);
            predY = Math.Clamp(predY, WALL_MARGIN, ArenaHeight - WALL_MARGIN);
        }

        double gunBearing = GunBearingTo(predX, predY);
        double absGunBearing = Math.Abs(gunBearing);

        SetTurnGunLeft(gunBearing);

        // Semakin dekat target atau saat ramming, toleransi aim dibuat lebih longgar.
        double aimThreshold = _isRamming ? 30.0 :
                              distance < 200 ? 15.0 : 8.0;

        if (absGunBearing > aimThreshold)
            return;

        // Pilihan firepower disesuaikan dengan energi bot, jarak, dan mode ramming.
        double[] fireOptions;

        if (Energy < ENERGY_CRITICAL)
            fireOptions = new[] { 1.0 };
        else if (_isRamming || distance < CLOSE_RANGE)
            fireOptions = new[] { 2.5, 3.0 };
        else if (distance < MEDIUM_RANGE)
            fireOptions = new[] { 1.5, 2.0, 2.5 };
        else
            fireOptions = new[] { 1.0, 1.5 };

        double bestScore = double.NegativeInfinity;
        double bestFp = -1;

        foreach (double fp in fireOptions)
        {
            if (Energy <= fp + 0.2)
                continue;

            double hitChance = EstimateHitChance(distance, absGunBearing, target.Speed, fp);
            double minHitChance = _isRamming ? 0.05 : 0.10;

            if (hitChance < minHitChance)
                continue;

            double damage = 4.0 * fp + (fp > 1 ? 2.0 * (fp - 1) : 0);
            double energyPenalty = fp * (Energy < ENERGY_CRITICAL ? 2.0 : 1.0);
            double score = damage * hitChance - energyPenalty;

            if (score > bestScore)
            {
                bestScore = score;
                bestFp = fp;
            }
        }

        if (bestFp > 0)
        {
            // Prediksi ulang menggunakan firepower terbaik agar titik bidik lebih akurat.
            double finalPredX = target.X;
            double finalPredY = target.Y;

            for (int i = 0; i < 3; i++)
            {
                double bulletSpeed = GetBulletSpeed(bestFp);
                double finalDistance = DistanceTo(finalPredX, finalPredY);
                double travelTime = finalDistance / bulletSpeed;
                double rad = target.Direction * Math.PI / 180.0;

                finalPredX = target.X + Math.Sin(rad) * target.Speed * travelTime;
                finalPredY = target.Y + Math.Cos(rad) * target.Speed * travelTime;

                finalPredX = Math.Clamp(finalPredX, WALL_MARGIN, ArenaWidth - WALL_MARGIN);
                finalPredY = Math.Clamp(finalPredY, WALL_MARGIN, ArenaHeight - WALL_MARGIN);
            }

            SetTurnGunLeft(GunBearingTo(finalPredX, finalPredY));
            SetFire(bestFp);
        }
        else if (absGunBearing < 20.0 && Energy > 1.2)
        {
            // Tembakan cadangan jika tidak ada firepower terbaik,
            // tetapi arah gun sudah cukup dekat ke target.
            SetFire(Math.Min(1.0, Energy - 0.2));
        }
    }

    // Fungsi ini memperkirakan peluang peluru mengenai musuh.
    // Faktor yang dipakai adalah jarak ke target, selisih sudut gun,
    // kecepatan musuh, dan firepower peluru.
    private static double EstimateHitChance(
        double distance,
        double gunOffset,
        double enemySpeed,
        double fp)
    {
        double bulletSpeed = 20.0 - 3.0 * fp;

        double distanceFactor = Math.Clamp(
            1.2 - distance / (bulletSpeed * 35.0),
            0.1,
            1.0
        );

        double aimFactor = Math.Clamp(
            1.0 - gunOffset / 35.0,
            0.05,
            1.0
        );

        double speedFactor = Math.Clamp(
            1.0 - Math.Abs(enemySpeed) / 10.0,
            0.2,
            1.0
        );

        return Math.Clamp(
            distanceFactor * aimFactor * speedFactor,
            0.03,
            0.97
        );
    }

    // Fungsi ini menghitung kecepatan peluru berdasarkan firepower.
    // Semakin besar firepower, damage peluru semakin besar,
    // tetapi kecepatan peluru menjadi lebih lambat.
    private static double GetBulletSpeed(double fp)
    {
        return 20.0 - 3.0 * fp;
    }

    // Fungsi ini mengatur pola gerak ketika bot belum menemukan target.
    // Bot bergerak sambil mencari musuh, dan jika dekat dinding
    // bot akan bergerak kembali ke tengah arena.
    private void ExecuteSearchPattern()
    {
        bool nearWall = IsNearWall();

        SetTurnLeft(
            nearWall
                ? BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0)
                : 5.0
        );

        SetForward(nearWall ? 200 : 100);
        TargetSpeed = 6;
    }

    // Fungsi ini dijalankan saat radar mendeteksi musuh.
    // Data musuh disimpan atau diperbarui, lalu bot memilih target terdekat
    // sebagai target utama untuk dikejar.
    public override void OnScannedBot(ScannedBotEvent e)
    {
        double dx = e.X - X;
        double dy = e.Y - Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        _enemies[e.ScannedBotId] = new EnemyInfo(
            e.ScannedBotId,
            e.X,
            e.Y,
            e.Energy,
            e.Direction,
            e.Speed,
            distance,
            TurnNumber
        );

        SelectClosestTarget();
    }

    // Fungsi ini dijalankan saat bot menabrak musuh.
    // Bot langsung masuk mode ramming, menembak dengan firepower maksimum
    // jika memungkinkan, lalu terus mendorong maju untuk memberi tekanan.
    public override void OnHitBot(HitBotEvent e)
    {
        if (GunHeat == 0 && Energy > MAX_FIREPOWER + 0.1)
            SetFire(MAX_FIREPOWER);

        _ramSustainCounter = RAM_SUSTAIN_TURNS;
        _isRamming = true;
        _zigzagCounter = 0;
        _zigzagPhaseLimit = ZIGZAG_PHASE_TURNS;

        SetForward(250);
        TargetSpeed = 8;

        Go();
    }

    // Fungsi ini dijalankan saat bot terkena peluru.
    // Jika bot tidak sedang ramming, arah zig-zag dibalik agar pola gerak berubah.
    // Saat energi kritis, bot juga mundur untuk mengurangi risiko terkena serangan lanjutan.
    public override void OnHitByBullet(HitByBulletEvent e)
    {
        if (!_isRamming)
        {
            _zigzagDir *= -1;
            _zigzagCounter = 0;
            _zigzagPhaseLimit = _rng.Next(6, 13);
        }

        if (Energy < ENERGY_CRITICAL && !_isRamming)
        {
            SetBack(80);
            TargetSpeed = -4;
            Go();
        }
    }

    // Fungsi ini dijalankan saat bot menabrak dinding.
    // Bot membatalkan mode ramming, mundur, mengarah ke tengah arena,
    // membalik arah zig-zag, dan memaksa radar melakukan sweep ulang.
    public override void OnHitWall(HitWallEvent e)
    {
        _isRamming = false;
        _ramSustainCounter = 0;

        SetBack(80);
        SetTurnLeft(BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0));

        _sweeping = true;
        _sweepAccumulated = 0.0;
        _zigzagDir *= -1;
        _zigzagCounter = 0;

        Go();
    }

    // Fungsi ini dijalankan saat ada bot musuh yang mati.
    // Musuh tersebut dihapus dari daftar target.
    // Jika musuh yang mati adalah target saat ini, maka target direset
    // dan radar dipaksa melakukan sweep untuk mencari musuh lain.
    public override void OnBotDeath(BotDeathEvent e)
    {
        _enemies.Remove(e.VictimId);

        if (_target != null && _target.Id == e.VictimId)
        {
            _target = null;
            _isRamming = false;
            _ramSustainCounter = 0;
            _sweeping = true;
            _sweepAccumulated = 0.0;
        }
    }

    // Fungsi ini memilih target terdekat dari daftar musuh yang masih valid.
    // Pemilihan ini adalah bentuk greedy sederhana karena bot langsung memilih
    // musuh dengan jarak paling kecil pada kondisi saat ini.
    private void SelectClosestTarget()
    {
        EnemyInfo? closest = null;
        double minDistance = double.MaxValue;

        foreach (var kvp in _enemies)
        {
            EnemyInfo enemy = kvp.Value;

            if (enemy.Energy <= 0)
                continue;

            if (TurnNumber - enemy.LastSeenTurn > STALE_THRESHOLD)
                continue;

            if (enemy.Distance < minDistance)
            {
                minDistance = enemy.Distance;
                closest = enemy;
            }
        }

        _target = closest;
    }

    // Fungsi ini mengecek apakah target saat ini masih valid.
    // Target dianggap valid jika masih ada dan data scan-nya belum terlalu lama.
    private bool HasFreshTarget()
    {
        return _target is not null
               && TurnNumber - _target.LastSeenTurn <= STALE_THRESHOLD;
    }

    // Fungsi ini mengecek apakah bot terlalu dekat dengan dinding arena.
    // Jika iya, movement akan diprioritaskan untuk kembali ke tengah arena.
    private bool IsNearWall()
    {
        return X < WALL_MARGIN
               || Y < WALL_MARGIN
               || X > ArenaWidth - WALL_MARGIN
               || Y > ArenaHeight - WALL_MARGIN;
    }
}

// Record ini digunakan untuk menyimpan informasi penting tentang musuh.
// Data ini dipakai untuk memilih target, mengejar musuh, memprediksi posisi,
// dan menentukan keputusan tembakan.
internal record EnemyInfo(
    int Id,
    double X,
    double Y,
    double Energy,
    double Direction,
    double Speed,
    double Distance,
    int LastSeenTurn
);