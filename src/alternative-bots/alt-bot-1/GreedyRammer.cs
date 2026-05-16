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
    private const double CLOSE_RANGE = 120.0;

    // ── Daya tembak maksimum saat point-blank ───────────────────────
    private const double MAX_FIREPOWER = 3.0;

    // ── Ambang energi kritis ─────────────────────────────────────────
    private const double ENERGY_CRITICAL = 15.0;

    // ── Turn maksimal sebelum data musuh dianggap basi ───────────────
    private const int STALE_THRESHOLD = 8;

    // ── Setiap berapa turn paksa sweep arena penuh ───────────────────
    private const int SWEEP_INTERVAL = 20;

    // ── Durasi satu fase zig-zag (turn) sebelum arah lateral diganti ─
    // Lebih pendek = zig-zag lebih rapat, lebih susah ditembak
    // Lebih panjang = arah lebih konsisten ke target
    private const int ZIGZAG_PHASE_TURNS = 12;

    // ── Sudut lateral zig-zag dari arah ke target (derajat) ─────────
    // 25° = zig-zag halus, tetap maju ke target
    // 45° = zig-zag lebih agresif, lebih susah ditembak tapi lebih lambat
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
    //
    // Urutan setiap turn:
    //   1. Tentukan mode radar (sweep atau lock)
    //   2. Jika ada target segar → kejar zig-zag dan tembak
    //   3. Jika tidak ada → gerak pencarian
    //   4. Go() kirim semua perintah
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
        // Hitung akumulasi putaran radar sejak turn lalu
        double radarDelta = Math.Abs(RadarDirection - _lastRadarDir);
        if (radarDelta > 180) radarDelta = 360 - radarDelta;
        _sweepAccumulated += radarDelta;
        _lastRadarDir      = RadarDirection;

        // Paksa sweep berkala setiap SWEEP_INTERVAL turn
        if (TurnNumber - _lastSweepTurn >= SWEEP_INTERVAL)
        {
            _sweeping         = true;
            _sweepAccumulated = 0.0;
            _lastSweepTurn    = TurnNumber;
        }

        if (_sweeping)
        {
            // Putar radar kecepatan maksimum (45°/turn)
            SetTurnRadarLeft(45.0 * _radarSweepDir);

            // Selesai 360° → beralih ke mode lock
            if (_sweepAccumulated >= 360.0)
            {
                _sweeping         = false;
                _sweepAccumulated = 0.0;
                _radarSweepDir   *= -1; // ganti arah untuk sweep berikutnya
            }
        }
        else if (_target != null)
        {
            // Lock radar ke target dengan overshoot
            double radarBearing = RadarBearingTo(_target.X, _target.Y);
            double overshoot    = radarBearing >= 0 ? 22 : -22;

            // Overshoot lebih besar jika target bergerak cepat
            if (_target.Speed > 4)
                overshoot *= 1.5;

            SetTurnRadarLeft(radarBearing + overshoot);
        }
        else
        {
            // Tidak ada target → sweep lagi
            _sweeping         = true;
            _sweepAccumulated = 0.0;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // EXECUTE RAM CHASE ZIGZAG — Kejar target dengan pola zig-zag
    //
    // Saat mengejar, bot tidak lari lurus ke target melainkan
    // bergerak dalam pola zig-zag dengan cara:
    //   - Setiap ZIGZAG_PHASE_TURNS turn, arah lateral dibalik
    //   - Arah gerak = bearing ke target ± ZIGZAG_ANGLE derajat
    //   - Hasilnya bot tetap mendekat ke target tapi lintasannya
    //     berliku sehingga susah ditebak dan sulit ditembak
    //
    // Saat sangat dekat (< CLOSE_RANGE):
    //   - Berhenti zig-zag, langsung lurus ke target untuk ram
    //   - TargetSpeed maksimal untuk memaksimalkan ram damage
    //
    // Saat dekat dinding:
    //   - Abaikan zig-zag, langsung putar ke tengah arena
    // ═══════════════════════════════════════════════════════════
    private void ExecuteRamChaseZigzag(EnemyInfo target)
    {
        // Prioritas tertinggi: hindari dinding
        if (IsNearWall())
        {
            double centerBearing = BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0);
            SetTurnLeft(centerBearing);
            SetForward(200);
            TargetSpeed    = 8;
            _zigzagCounter = 0; // reset zig-zag setelah keluar dinding
            return;
        }

        double distance        = DistanceTo(target.X, target.Y);
        double bearingToTarget = BearingTo(target.X, target.Y);

        if (distance < CLOSE_RANGE)
        {
            // ── Fase RAM: sudah dekat → lurus penuh ke target ──
            // Tidak perlu zig-zag lagi, langsung seruduk
            SetTurnLeft(bearingToTarget);
            SetForward(distance + 100); // +100 agar menembus posisi target
            TargetSpeed    = 8;
            _zigzagCounter = 0;
        }
        else
        {
            // ── Fase ZIG-ZAG: masih jauh → zig-zag sambil mendekat ──

            // Update counter dan ganti fase jika sudah cukup lama
            _zigzagCounter++;
            if (_zigzagCounter >= _zigzagPhaseLimit)
            {
                _zigzagCounter    = 0;
                _zigzagDir       *= -1; // balik arah lateral

                // Variasi durasi fase (8-16 turn) agar tidak terpola
                _zigzagPhaseLimit = _rng.Next(8, 17);
            }

            // Arah gerak = bearing ke target + offset lateral zig-zag
            // +ZIGZAG_ANGLE = miring ke kiri dari target
            // -ZIGZAG_ANGLE = miring ke kanan dari target
            double zigzagBearing = bearingToTarget + ZIGZAG_ANGLE * _zigzagDir;
            SetTurnLeft(zigzagBearing);

            // Maju cukup jauh agar bot tidak berhenti di tengah jalan
            // Jarak penuh ke target agar selalu bergerak mendekat
            SetForward(distance);
            TargetSpeed = 8; // tetap kecepatan maksimal saat zig-zag
        }
    }

    // ═══════════════════════════════════════════════════════════
    // EXECUTE RAM FIRE — Tembak sambil mengejar
    //
    // Tembakan berbasis hit chance — tidak tembak jika peluang
    // mengenai musuh terlalu kecil (buang energi sia-sia).
    //
    // Evaluasi setiap opsi firepower dan pilih yang paling
    // menguntungkan berdasarkan: damage × hitChance - energyPenalty
    //
    // Logika fire power:
    //   - Jarak jauh, gun masih miring → skip (hit chance < 0.25)
    //   - Jarak jauh, gun sudah lurus  → fp 1.5-2.0 (peluru cepat)
    //   - Jarak dekat, gun lurus       → fp 2.5-3.0 (damage besar)
    //   - Energi kritis                → penalti ×2, pilih fp hemat
    // ═══════════════════════════════════════════════════════════
    private void ExecuteRamFire(EnemyInfo target)
    {
        double distance      = DistanceTo(target.X, target.Y);
        double gunBearing    = GunBearingTo(target.X, target.Y);
        double absGunBearing = Math.Abs(gunBearing);

        // Arahkan gun ke target
        SetTurnGunLeft(gunBearing);

        if (GunHeat != 0)    return;
        if (Energy    < 1.0) return;

        double[] fireOptions = { 1.5, 2.0, 2.5, 3.0 };
        double   bestScore   = double.NegativeInfinity;
        double   bestFp      = -1;

        foreach (double fp in fireOptions)
        {
            if (Energy <= fp + 0.2) continue;

            double hitChance = EstimateHitChance(distance, absGunBearing, target.Speed, fp);

            // Tidak tembak jika peluang mengenai < 25%
            if (hitChance < 0.25) continue;

            double damage        = 4.0 * fp + (fp > 1 ? 2.0 * (fp - 1) : 0);
            double energyPenalty = fp * (Energy < ENERGY_CRITICAL ? 2.0 : 1.0);
            double score         = damage * hitChance - energyPenalty;

            if (score > bestScore)
            {
                bestScore = score;
                bestFp    = fp;
            }
        }

        if (bestFp > 0)
            SetFire(bestFp);
    }

    // ═══════════════════════════════════════════════════════════
    // ESTIMATE HIT CHANCE — Estimasi peluang peluru mengenai target
    //
    // Tiga faktor:
    //   distanceFactor : makin jauh → peluang makin kecil.
    //                    fp kecil = peluru lebih cepat = sedikit
    //                    mengurangi penalti jarak
    //   aimFactor      : makin lurus gun → peluang makin besar.
    //                    threshold ketat (/ 35) karena rammer
    //                    tidak perlu tembak saat gun masih miring
    //   speedFactor    : musuh bergerak cepat → lebih susah kena
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
    //
    // Bergerak ke tengah arena agar posisi strategis.
    // Radar sudah dihandle ExecuteRadarControl() (mode sweep).
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
    //
    // Hanya update data — tidak ada perintah gerak/tembak
    // agar tidak menimpa perintah dari Run() loop.
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
    //
    // Tembak point-blank saat tabrakan lalu langsung maju lagi
    // untuk serudukan berikutnya. Reset zig-zag agar langsung
    // lurus ke target tanpa delay ganti fase.
    // ═══════════════════════════════════════════════════════════
    public override void OnHitBot(HitBotEvent e)
    {
        if (GunHeat == 0 && Energy > MAX_FIREPOWER + 0.1)
            SetFire(MAX_FIREPOWER);

        // Reset zig-zag → fase berikutnya mulai dari nol
        _zigzagCounter    = 0;
        _zigzagPhaseLimit = ZIGZAG_PHASE_TURNS;

        SetForward(150);
        TargetSpeed = 8;
        Go();
    }

    // ═══════════════════════════════════════════════════════════
    // ON HIT BY BULLET — Kena tembakan
    //
    // Segera balik arah zig-zag dan reset counter fase agar
    // pola berubah dan peluru berikutnya tidak kena lagi.
    // Mundur hanya saat energi kritis.
    // ═══════════════════════════════════════════════════════════
    public override void OnHitByBullet(HitByBulletEvent e)
    {
        // Balik arah zig-zag dan reset fase agar pola berubah
        _zigzagDir        *= -1;
        _zigzagCounter     = 0;
        _zigzagPhaseLimit  = _rng.Next(6, 13); // fase pendek setelah kena tembak

        if (Energy < ENERGY_CRITICAL)
        {
            SetBack(80);
            TargetSpeed = -4;
            Go();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ON HIT WALL — Menabrak dinding
    //
    // Mundur, putar ke tengah arena, dan paksa sweep ulang.
    // Reset zig-zag agar tidak langsung menabrak dinding lagi.
    // ═══════════════════════════════════════════════════════════
    public override void OnHitWall(HitWallEvent e)
    {
        SetBack(80);
        SetTurnLeft(BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0));
        _sweeping         = true;
        _sweepAccumulated = 0.0;
        _zigzagDir       *= -1; // balik arah zig-zag setelah nabrak dinding
        _zigzagCounter    = 0;
        Go();
    }

    // ═══════════════════════════════════════════════════════════
    // ON BOT DEATH — Musuh mati
    //
    // Hapus dari kandidat, reset target, paksa sweep ulang
    // untuk menemukan musuh berikutnya secepatnya.
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
    //   - Musuh terdekat paling cepat dicapai → waktu tempuh minimum
    //   - Ram Damage = 2x damage biasa
    //   - Ram Damage Bonus +30% jika musuh mati karena tabrakan
    //   - Jarak pendek = paparan tembakan musuh lain lebih sedikit
    // Data basi dilewati karena posisi musuh tidak akurat untuk ram.
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
//
// Distance     : kriteria seleksi greedy (terkecil = prioritas tertinggi)
// LastSeenTurn : untuk filter data basi
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