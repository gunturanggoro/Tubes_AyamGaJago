using System;
using System.Collections.Generic;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class GreedyRammer : Bot
{
    // ── Jarak aman dari dinding sebelum bot menghindarinya ──────────
    private const double WALL_MARGIN = 80.0;

    // ── Jarak point-blank: hit chance sangat tinggi ──────────────────
    private const double CLOSE_RANGE = 10.0;

    // ── Jarak menengah: batas antara peluru berat/ringan ────────────
    private const double MEDIUM_RANGE = 350.0;

    // ── Daya tembak maksimum saat point-blank ───────────────────────
    private const double MAX_FIREPOWER = 3.0;

    // ── Ambang energi kritis ─────────────────────────────────────────
    private const double ENERGY_CRITICAL = 15.0;

    // ── Turn maksimal sebelum data musuh dianggap basi ───────────────
    private const int STALE_THRESHOLD = 8;

    // ── Setiap berapa turn paksa sweep arena penuh ───────────────────
    private const int SWEEP_INTERVAL = 20;

    // ── Durasi satu fase zig-zag (turn) sebelum arah lateral diganti ─
    private const int ZIGZAG_PHASE_TURNS = 12;

    // ── Sudut lateral zig-zag dari arah ke target (derajat) ─────────
    private const double ZIGZAG_ANGLE = 30.0;

    // ── Dictionary semua musuh yang terdeteksi radar ─────────────────
    private readonly Dictionary<int, EnemyInfo> _enemies = new();

    // ── Target greedy saat ini (musuh terdekat) ──────────────────────
    private EnemyInfo? _target;

    // ── Arah sweep radar: +1 kiri, -1 kanan, bergantian ─────────────
    private int _radarSweepDir = 1;

    // ── Turn terakhir sweep penuh dilakukan ──────────────────────────
    private int _lastSweepTurn = 0;

    // ── Flag: apakah sedang dalam mode sweep penuh ───────────────────
    private bool _sweeping = true;

    // ── Berapa derajat radar sudah berputar dalam sweep saat ini ─────
    private double _sweepAccumulated = 0.0;

    // ── Arah radar turn sebelumnya untuk menghitung akumulasi sweep ──
    private double _lastRadarDir = 0.0;

    // ── Arah lateral zig-zag saat ini: +1 = kiri target, -1 = kanan ─
    private int _zigzagDir = 1;

    // ── Counter turn dalam fase zig-zag saat ini ─────────────────────
    private int _zigzagCounter = 0;

    // ── RNG untuk variasi timing zig-zag agar tidak terpola ──────────
    private readonly Random _rng = new();

    // ── Batas turn fase zig-zag saat ini (acak tiap fase) ───────────
    private int _zigzagPhaseLimit = ZIGZAG_PHASE_TURNS;

    // ── Entry point ──────────────────────────────────────────────────
    static void Main(string[] args) => new GreedyRammer().Start();
    GreedyRammer() : base(BotInfo.FromFile("GreedyRammer.json")) { }

    // ═══════════════════════════════════════════════════════════
    // RUN — Loop utama bot
    // ═══════════════════════════════════════════════════════════
    public override void Run()
    {
        AdjustGunForBodyTurn   = true;
        AdjustRadarForBodyTurn = true;
        AdjustRadarForGunTurn  = true;

        BodyColor   = Color.FromArgb(180, 0,   0);
        TurretColor = Color.FromArgb(100, 0,   0);
        RadarColor  = Color.FromArgb(255, 80,  0);
        BulletColor = Color.FromArgb(255, 200, 0);
        ScanColor   = Color.FromArgb(255, 60,  0);

        _lastRadarDir = RadarDirection;

        while (IsRunning)
        {
            ExecuteRadarControl();

            if (HasFreshTarget())
            {
                ExecuteRamChaseZigzag(_target!);
                ExecuteRamFire(_target!);
            }
            else
            {
                _target = null;
                ExecuteSearchPattern();
            }

            Go();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // EXECUTE RADAR CONTROL — Otak sistem radar
    //
    // Fase 1 — SWEEP PENUH:
    //   Radar berputar 360° penuh untuk mendeteksi semua musuh.
    //   Dilakukan saat awal, setiap SWEEP_INTERVAL turn,
    //   setelah target mati, atau setelah nabrak dinding.
    //
    // Fase 2 — LOCK KE TARGET:
    //   Radar dikunci ke target terdekat dengan overshoot
    //   agar tidak kehilangan tracking saat target bergerak.
    // ═══════════════════════════════════════════════════════════
    private void ExecuteRadarControl()
    {
        double radarDelta = Math.Abs(RadarDirection - _lastRadarDir);
        if (radarDelta > 180) radarDelta = 360 - radarDelta;
        _sweepAccumulated += radarDelta;
        _lastRadarDir      = RadarDirection;

        if (TurnNumber - _lastSweepTurn >= SWEEP_INTERVAL)
        {
            _sweeping         = true;
            _sweepAccumulated = 0.0;
            _lastSweepTurn    = TurnNumber;
        }

        if (_sweeping)
        {
            SetTurnRadarLeft(45.0 * _radarSweepDir);
            if (_sweepAccumulated >= 360.0)
            {
                _sweeping         = false;
                _sweepAccumulated = 0.0;
                _radarSweepDir   *= -1;
            }
        }
        else if (_target != null)
        {
            double radarBearing = RadarBearingTo(_target.X, _target.Y);
            double overshoot    = radarBearing >= 0 ? 22 : -22;
            if (_target.Speed > 4)
                overshoot *= 1.5;
            SetTurnRadarLeft(radarBearing + overshoot);
        }
        else
        {
            _sweeping         = true;
            _sweepAccumulated = 0.0;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // EXECUTE RAM CHASE ZIGZAG — Kejar target dengan pola zig-zag
    //
    // Saat mengejar, bot bergerak zig-zag agar sulit ditembak.
    // Saat sudah dekat (< CLOSE_RANGE), langsung seruduk lurus.
    // ═══════════════════════════════════════════════════════════
    private void ExecuteRamChaseZigzag(EnemyInfo target)
    {
        if (IsNearWall())
        {
            double centerBearing = BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0);
            SetTurnLeft(centerBearing);
            SetForward(200);
            TargetSpeed    = 8;
            _zigzagCounter = 0;
            return;
        }

        double distance        = DistanceTo(target.X, target.Y);
        double bearingToTarget = BearingTo(target.X, target.Y);

        if (distance < CLOSE_RANGE)
        {
            // Fase RAM: sudah dekat → lurus penuh ke target
            SetTurnLeft(bearingToTarget);
            SetForward(distance + 100);
            TargetSpeed    = 8;
            _zigzagCounter = 0;
        }
        else
        {
            // Fase ZIG-ZAG: masih jauh → zig-zag sambil mendekat
            _zigzagCounter++;
            if (_zigzagCounter >= _zigzagPhaseLimit)
            {
                _zigzagCounter    = 0;
                _zigzagDir       *= -1;
                _zigzagPhaseLimit = _rng.Next(8, 17);
            }

            double zigzagBearing = bearingToTarget + ZIGZAG_ANGLE * _zigzagDir;
            SetTurnLeft(zigzagBearing);
            SetForward(distance);
            TargetSpeed = 8;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // EXECUTE RAM FIRE — Tembak sambil mengejar
    //
    // PERBAIKAN dari versi sebelumnya:
    //
    // 1. Prediksi posisi musuh saat peluru tiba (linear prediction)
    //    Tanpa prediksi, gun selalu tertinggal saat musuh berlari.
    //    Gun diarahkan ke posisi prediksi, bukan posisi saat ini.
    //
    // 2. Firepower berbasis jarak, bukan threshold hitChance:
    //    - Kritis (energi < 15)   → fp 1.0 saja (hemat energi)
    //    - Jarak dekat (< 120)    → fp 2.5-3.0 (damage maksimal)
    //    - Jarak menengah (< 350) → fp 1.5-2.5 (seimbang)
    //    - Jarak jauh (≥ 350)     → fp 1.0-1.5 (peluru cepat agar
    //                               bisa mengejar musuh yang lari)
    //
    // 3. Threshold hitChance diturunkan menjadi 0.10 (dari 0.25)
    //    Saat musuh lari, sedikit tembakan tetap lebih baik daripada
    //    tidak menembak sama sekali dan mati tanpa melawan.
    //
    // 4. Fallback minimum fire: jika semua opsi gagal scoring tapi
    //    gun sudah cukup lurus (< 15°), tetap tembak fp 1.0 agar
    //    bot tidak pernah diam sepenuhnya saat dikejar.
    // ═══════════════════════════════════════════════════════════
    private void ExecuteRamFire(EnemyInfo target)
    {
        if (GunHeat != 0) return;
        if (Energy   < 1.0) return;

        // ── Prediksi posisi musuh saat peluru tiba ───────────────────
        // Iterasi 3x hingga travelTime konvergen dengan jarak prediksi
        double predX = target.X;
        double predY = target.Y;
        // Gunakan fp 1.5 sebagai estimasi awal untuk prediksi posisi
        double estFp = Energy < ENERGY_CRITICAL ? 1.0 : 1.5;
        for (int i = 0; i < 3; i++)
        {
            double bulletSpeed = CalcBulletSpeed(estFp);
            double dist        = DistanceTo(predX, predY);
            double travelTime  = dist / bulletSpeed;
            double rad         = target.Direction * Math.PI / 180.0;
            predX = target.X + Math.Cos(rad) * target.Speed * travelTime;
            predY = target.Y + Math.Sin(rad) * target.Speed * travelTime;

            // Clamp agar prediksi tidak keluar arena
            predX = Math.Clamp(predX, WALL_MARGIN, ArenaWidth  - WALL_MARGIN);
            predY = Math.Clamp(predY, WALL_MARGIN, ArenaHeight - WALL_MARGIN);
        }

        // Arahkan gun ke posisi prediksi (bukan posisi saat ini)
        double gunBearing    = GunBearingTo(predX, predY);
        double absGunBearing = Math.Abs(gunBearing);
        SetTurnGunLeft(gunBearing);

        double distance = DistanceTo(target.X, target.Y);

        // ── Tentukan opsi firepower berdasarkan jarak & energi ───────
        double[] fireOptions;
        if (Energy < ENERGY_CRITICAL)
        {
            // Energi kritis: hemat, hanya tembak ringan
            fireOptions = new[] { 1.0 };
        }
        else if (distance < CLOSE_RANGE)
        {
            // Point-blank: peluru berat untuk damage + ram combo
            fireOptions = new[] { 2.5, 3.0 };
        }
        else if (distance < MEDIUM_RANGE)
        {
            // Jarak menengah: seimbang antara kecepatan dan damage
            fireOptions = new[] { 1.5, 2.0, 2.5 };
        }
        else
        {
            // Jarak jauh / musuh lari: peluru ringan lebih cepat sampai
            fireOptions = new[] { 1.0, 1.5 };
        }

        double bestScore = double.NegativeInfinity;
        double bestFp    = -1;

        foreach (double fp in fireOptions)
        {
            if (Energy <= fp + 0.2) continue;

            double hitChance = EstimateHitChance(distance, absGunBearing, target.Speed, fp);

            // Threshold diturunkan ke 0.10 agar tetap tembak saat musuh
            // menjauh — lebih baik tembak dengan peluang kecil daripada
            // tidak tembak sama sekali dan terus menerima damage gratis
            if (hitChance < 0.10) continue;

            double damage       = 4.0 * fp + (fp > 1 ? 2.0 * (fp - 1) : 0);
            double energyPenalty = fp * (Energy < ENERGY_CRITICAL ? 2.0 : 1.0);
            double score        = damage * hitChance - energyPenalty;

            if (score > bestScore)
            {
                bestScore = score;
                bestFp    = fp;
            }
        }

        if (bestFp > 0)
        {
            SetFire(bestFp);
        }
        else if (absGunBearing < 15.0 && Energy > 1.2)
        {
            // ── Fallback minimum fire ────────────────────────────────
            // Semua opsi gagal threshold, tapi gun sudah cukup lurus.
            // Tembak peluru ringan agar bot tidak diam tanpa melawan
            // saat musuh terus menembaki kita dari kejauhan.
            SetFire(Math.Min(1.0, Energy - 0.2));
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ESTIMATE HIT CHANCE — Estimasi peluang peluru mengenai target
    // ═══════════════════════════════════════════════════════════
    private static double EstimateHitChance(
        double distance, double gunOffset, double enemySpeed, double fp)
    {
        double bulletSpeed    = 20.0 - 3.0 * fp;
        double distanceFactor = Math.Clamp(1.2 - distance / (bulletSpeed * 35.0), 0.1, 1.0);
        double aimFactor      = Math.Clamp(1.0 - gunOffset / 35.0, 0.05, 1.0);
        double speedFactor    = Math.Clamp(1.0 - Math.Abs(enemySpeed) / 10.0, 0.2, 1.0);
        return Math.Clamp(distanceFactor * aimFactor * speedFactor, 0.03, 0.97);
    }

    // ═══════════════════════════════════════════════════════════
    // EXECUTE SEARCH PATTERN — Gerak saat tidak ada target
    // ═══════════════════════════════════════════════════════════
    private void ExecuteSearchPattern()
    {
        bool nearWall = IsNearWall();
        SetTurnLeft(nearWall
            ? BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0)
            : 5.0);
        SetForward(nearWall ? 200 : 100);
        TargetSpeed = 6;
    }

    // ═══════════════════════════════════════════════════════════
    // ON SCANNED BOT — Update data musuh dan pilih target
    // ═══════════════════════════════════════════════════════════
    public override void OnScannedBot(ScannedBotEvent e)
    {
        double dx       = e.X - X;
        double dy       = e.Y - Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        _enemies[e.ScannedBotId] = new EnemyInfo(
            e.ScannedBotId, e.X, e.Y, e.Energy,
            e.Direction, e.Speed, distance, TurnNumber
        );

        SelectClosestTarget();
    }

    // ═══════════════════════════════════════════════════════════
    // ON HIT BOT — Berhasil menabrak bot musuh
    // ═══════════════════════════════════════════════════════════
    public override void OnHitBot(HitBotEvent e)
    {
        if (GunHeat == 0 && Energy > MAX_FIREPOWER + 0.1)
            SetFire(MAX_FIREPOWER);

        _zigzagCounter    = 0;
        _zigzagPhaseLimit = ZIGZAG_PHASE_TURNS;

        SetForward(150);
        TargetSpeed = 8;
        Go();
    }

    // ═══════════════════════════════════════════════════════════
    // ON HIT BY BULLET — Kena tembakan
    // ═══════════════════════════════════════════════════════════
    public override void OnHitByBullet(HitByBulletEvent e)
    {
        _zigzagDir        *= -1;
        _zigzagCounter     = 0;
        _zigzagPhaseLimit  = _rng.Next(6, 13);

        if (Energy < ENERGY_CRITICAL)
        {
            SetBack(80);
            TargetSpeed = -4;
            Go();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ON HIT WALL — Menabrak dinding
    // ═══════════════════════════════════════════════════════════
    public override void OnHitWall(HitWallEvent e)
    {
        SetBack(80);
        SetTurnLeft(BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0));
        _sweeping         = true;
        _sweepAccumulated = 0.0;
        _zigzagDir       *= -1;
        _zigzagCounter    = 0;
        Go();
    }

    // ═══════════════════════════════════════════════════════════
    // ON BOT DEATH — Musuh mati
    // ═══════════════════════════════════════════════════════════
    public override void OnBotDeath(BotDeathEvent e)
    {
        _enemies.Remove(e.VictimId);
        if (_target != null && _target.Id == e.VictimId)
        {
            _target           = null;
            _sweeping         = true;
            _sweepAccumulated = 0.0;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // SELECT CLOSEST TARGET — Fungsi seleksi greedy utama
    //
    // Memilih musuh dengan jarak terkecil sebagai target.
    // ═══════════════════════════════════════════════════════════
    private void SelectClosestTarget()
    {
        EnemyInfo? closest     = null;
        double     minDistance = double.MaxValue;

        foreach (var kvp in _enemies)
        {
            var enemy = kvp.Value;
            if (enemy.Energy <= 0) continue;
            if (TurnNumber - enemy.LastSeenTurn > STALE_THRESHOLD) continue;

            if (enemy.Distance < minDistance)
            {
                minDistance = enemy.Distance;
                closest     = enemy;
            }
        }

        _target = closest;
    }

    // ═══════════════════════════════════════════════════════════
    // HAS FRESH TARGET — Cek apakah target masih valid dan segar
    // ═══════════════════════════════════════════════════════════
    private bool HasFreshTarget() =>
        _target is not null && TurnNumber - _target.LastSeenTurn <= STALE_THRESHOLD;

    // ═══════════════════════════════════════════════════════════
    // IS NEAR WALL — Cek apakah bot mendekati batas arena
    // ═══════════════════════════════════════════════════════════
    private bool IsNearWall() =>
        X < WALL_MARGIN || Y < WALL_MARGIN ||
        X > ArenaWidth  - WALL_MARGIN ||
        Y > ArenaHeight - WALL_MARGIN;
}

// ═══════════════════════════════════════════════════════════════
// ENEMY INFO — Data class informasi musuh
// ═══════════════════════════════════════════════════════════════
internal record EnemyInfo(
    int    Id,
    double X,
    double Y,
    double Energy,
    double Direction,
    double Speed,
    double Distance,
    int    LastSeenTurn
);