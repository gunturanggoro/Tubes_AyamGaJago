using System;
using System.Collections.Generic;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class BantengMerah : Bot
{
    // ── Jarak aman dari dinding sebelum bot menghindarinya ──────────
    private const double WALL_MARGIN = 80.0;

    // ── Jarak RAM aktif: bot mulai menekan dan tidak zigzag ─────────
    // [PERBAIKAN] Dinaikkan dari 10 → 120 agar ramming dimulai lebih awal
    private const double RAM_ENGAGE_RANGE = 120.0;

    // ── Jarak point-blank: bot sudah bersentuhan / hampir ───────────
    private const double CLOSE_RANGE = 30.0;

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

    // ── Berapa turn setelah tabrakan bot terus mendorong (ram sustain)
    private const int RAM_SUSTAIN_TURNS = 8;

    private readonly Dictionary<int, EnemyInfo> _enemies = new();
    private EnemyInfo? _target;
    private int    _radarSweepDir    = 1;
    private int    _lastSweepTurn    = 0;
    private bool   _sweeping         = true;
    private double _sweepAccumulated = 0.0;
    private double _lastRadarDir     = 0.0;
    private int    _zigzagDir        = 1;
    private int    _zigzagCounter    = 0;
    private readonly Random _rng     = new();
    private int    _zigzagPhaseLimit = ZIGZAG_PHASE_TURNS;
    private bool   _isRamming        = false;
    private int    _ramSustainCounter = 0;

    static void Main(string[] args) => new BantengMerah().Start();
    BantengMerah() : base(BotInfo.FromFile("BantengMerah.json")) { }

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
                // Tembak dulu agar gun mulai rotate sebelum badan bergerak
                ExecuteRamFire(_target!);
                ExecuteRamChase(_target!);
            }
            else
            {
                _target            = null;
                _isRamming         = false;
                _ramSustainCounter = 0;
                ExecuteSearchPattern();
            }

            Go();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // EXECUTE RADAR CONTROL
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
            if (_target.Speed > 4) overshoot *= 1.5;
            SetTurnRadarLeft(radarBearing + overshoot);
        }
        else
        {
            _sweeping         = true;
            _sweepAccumulated = 0.0;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // EXECUTE RAM CHASE — Kejar dan seruduk target
    //
    // [PERBAIKAN]
    // - RAM_ENGAGE_RANGE dinaikkan: mulai ram lebih awal (jarak 120)
    // - SetForward pakai overshoot besar agar bot terus mendorong
    //   menembus posisi musuh, bukan berhenti tepat di sana
    // - _ramSustainCounter mempertahankan momentum ram setelah HitBot
    // ═══════════════════════════════════════════════════════════
    private void ExecuteRamChase(EnemyInfo target)
    {
        if (IsNearWall())
        {
            _isRamming         = false;
            _ramSustainCounter = 0;
            SetTurnLeft(BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0));
            SetForward(200);
            TargetSpeed    = 8;
            _zigzagCounter = 0;
            return;
        }

        double distance        = DistanceTo(target.X, target.Y);
        double bearingToTarget = BearingTo(target.X, target.Y);

        if (_ramSustainCounter > 0) _ramSustainCounter--;

        bool shouldRam = distance <= RAM_ENGAGE_RANGE || _ramSustainCounter > 0;

        if (shouldRam)
        {
            // MODE RAM: tancap gas lurus, overshoot besar agar
            // bot tidak berhenti dan terus mendorong musuh
            _isRamming = true;
            SetTurnLeft(bearingToTarget);
            SetForward(distance + 300);
            TargetSpeed    = 8;
            _zigzagCounter = 0;
        }
        else
        {
            _isRamming = false;
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
    // EXECUTE RAM FIRE — Tembak seakurat mungkin saat mengejar
    //
    // PERBAIKAN:
    //
    // 1. FIX BUG TRIGONOMETRI PREDIKSI (kritis):
    //    TankRoyale: 0°=North, clockwise.
    //    vX = sin(direction) * speed  → Math.Sin untuk X  [DIFIX]
    //    vY = cos(direction) * speed  → Math.Cos untuk Y  [DIFIX]
    //    Versi lama TERBALIK → prediksi meleset saat musuh bergerak.
    //
    // 2. Iterasi prediksi: 3 → 5 untuk konvergensi lebih akurat.
    //
    // 3. Setelah fp terpilih, prediksi diulang dengan bulletSpeed
    //    yang benar-benar akan dipakai (bukan estimasi awal).
    //
    // 4. Threshold aim adaptif:
    //    - RAM aktif : 30° (dekat, sedikit offset tetap kena)
    //    - Jarak < 200: 15°
    //    - Jauh       : 8° (ketat, hemat energi)
    //
    // 5. hitChance minimum diturunkan ke 0.05 saat ram mode.
    // ═══════════════════════════════════════════════════════════
    private void ExecuteRamFire(EnemyInfo target)
    {
        if (GunHeat != 0) return;
        if (Energy   < 1.0) return;

        double distance = DistanceTo(target.X, target.Y);

        // Estimasi fp awal untuk iterasi prediksi
        double estFp = Energy < ENERGY_CRITICAL ? 1.0 :
                       _isRamming              ? 3.0 :
                       distance < MEDIUM_RANGE ? 2.0 : 1.5;

        // Prediksi posisi musuh saat peluru tiba
        // [FIX KRITIS] Sin untuk X, Cos untuk Y (sistem TankRoyale)
        double predX = target.X;
        double predY = target.Y;
        for (int i = 0; i < 5; i++)
        {
            double bulletSpeed = CalcBulletSpeed(estFp);
            double dist        = DistanceTo(predX, predY);
            double travelTime  = dist / bulletSpeed;
            double rad         = target.Direction * Math.PI / 180.0;

            predX = target.X + Math.Sin(rad) * target.Speed * travelTime;
            predY = target.Y + Math.Cos(rad) * target.Speed * travelTime;

            predX = Math.Clamp(predX, WALL_MARGIN, ArenaWidth  - WALL_MARGIN);
            predY = Math.Clamp(predY, WALL_MARGIN, ArenaHeight - WALL_MARGIN);
        }

        double gunBearing    = GunBearingTo(predX, predY);
        double absGunBearing = Math.Abs(gunBearing);
        SetTurnGunLeft(gunBearing);

        // Threshold aim adaptif
        double aimThreshold = _isRamming      ? 30.0 :
                              distance < 200  ? 15.0 : 8.0;
        if (absGunBearing > aimThreshold) return;

        // Pilih firepower
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
        double bestFp    = -1;

        foreach (double fp in fireOptions)
        {
            if (Energy <= fp + 0.2) continue;

            double hitChance    = EstimateHitChance(distance, absGunBearing, target.Speed, fp);
            double minHitChance = _isRamming ? 0.05 : 0.10;
            if (hitChance < minHitChance) continue;

            double damage        = 4.0 * fp + (fp > 1 ? 2.0 * (fp - 1) : 0);
            double energyPenalty = fp * (Energy < ENERGY_CRITICAL ? 2.0 : 1.0);
            double score         = damage * hitChance - energyPenalty;

            if (score > bestScore) { bestScore = score; bestFp = fp; }
        }

        if (bestFp > 0)
        {
            // Prediksi ulang dengan fp aktual untuk akurasi final
            double finalPredX = target.X;
            double finalPredY = target.Y;
            for (int i = 0; i < 3; i++)
            {
                double bs  = CalcBulletSpeed(bestFp);
                double d   = DistanceTo(finalPredX, finalPredY);
                double tt  = d / bs;
                double r   = target.Direction * Math.PI / 180.0;
                finalPredX = target.X + Math.Sin(r) * target.Speed * tt;
                finalPredY = target.Y + Math.Cos(r) * target.Speed * tt;
                finalPredX = Math.Clamp(finalPredX, WALL_MARGIN, ArenaWidth  - WALL_MARGIN);
                finalPredY = Math.Clamp(finalPredY, WALL_MARGIN, ArenaHeight - WALL_MARGIN);
            }
            SetTurnGunLeft(GunBearingTo(finalPredX, finalPredY));
            SetFire(bestFp);
        }
        else if (absGunBearing < 20.0 && Energy > 1.2)
        {
            SetFire(Math.Min(1.0, Energy - 0.2));
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ESTIMATE HIT CHANCE
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
    // CALC BULLET SPEED
    // ═══════════════════════════════════════════════════════════
    private static double CalcBulletSpeed(double fp) => 20.0 - 3.0 * fp;

    // ═══════════════════════════════════════════════════════════
    // EXECUTE SEARCH PATTERN
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
    // ON SCANNED BOT
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
    // [PERBAIKAN] Tidak mundur. Set _ramSustainCounter agar
    // loop utama terus mendorong selama RAM_SUSTAIN_TURNS turn.
    // ═══════════════════════════════════════════════════════════
    public override void OnHitBot(HitBotEvent e)
    {
        if (GunHeat == 0 && Energy > MAX_FIREPOWER + 0.1)
            SetFire(MAX_FIREPOWER);

        _ramSustainCounter = RAM_SUSTAIN_TURNS;
        _isRamming         = true;
        _zigzagCounter     = 0;
        _zigzagPhaseLimit  = ZIGZAG_PHASE_TURNS;

        // Terus mendorong maju
        SetForward(250);
        TargetSpeed = 8;
        Go();
    }

    // ═══════════════════════════════════════════════════════════
    // ON HIT BY BULLET
    // ═══════════════════════════════════════════════════════════
    public override void OnHitByBullet(HitByBulletEvent e)
    {
        if (!_isRamming)
        {
            _zigzagDir       *= -1;
            _zigzagCounter    = 0;
            _zigzagPhaseLimit = _rng.Next(6, 13);
        }

        if (Energy < ENERGY_CRITICAL && !_isRamming)
        {
            SetBack(80);
            TargetSpeed = -4;
            Go();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ON HIT WALL
    // ═══════════════════════════════════════════════════════════
    public override void OnHitWall(HitWallEvent e)
    {
        _isRamming         = false;
        _ramSustainCounter = 0;
        SetBack(80);
        SetTurnLeft(BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0));
        _sweeping         = true;
        _sweepAccumulated = 0.0;
        _zigzagDir       *= -1;
        _zigzagCounter    = 0;
        Go();
    }

    // ═══════════════════════════════════════════════════════════
    // ON BOT DEATH
    // ═══════════════════════════════════════════════════════════
    public override void OnBotDeath(BotDeathEvent e)
    {
        _enemies.Remove(e.VictimId);
        if (_target != null && _target.Id == e.VictimId)
        {
            _target            = null;
            _isRamming         = false;
            _ramSustainCounter = 0;
            _sweeping          = true;
            _sweepAccumulated  = 0.0;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // SELECT CLOSEST TARGET
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

    private bool HasFreshTarget() =>
        _target is not null && TurnNumber - _target.LastSeenTurn <= STALE_THRESHOLD;

    private bool IsNearWall() =>
        X < WALL_MARGIN || Y < WALL_MARGIN ||
        X > ArenaWidth  - WALL_MARGIN ||
        Y > ArenaHeight - WALL_MARGIN;
}

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