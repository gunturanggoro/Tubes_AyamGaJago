#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

// ------------------------------------------------------------------
// SharpShooter — Wall-Hugging Sniper + Lowest Energy Target
// ------------------------------------------------------------------
// Strategi Greedy:
//   Fungsi Objektif : Bullet Damage + Bullet Damage Bonus
//   Fungsi Seleksi  : Target = musuh dengan energi terendah
//   Fungsi Kelayakan: Menepi ke tembok terjauh dari musuh, jaga jarak aman
//   Heuristik       : Peluru kecil fire rate tinggi + predictive targeting
// ------------------------------------------------------------------
public class SharpShooter : Bot
{
    // ---------------------------------------------------------------
    // Konstanta
    // ---------------------------------------------------------------
    private const double SAFE_WALL_DIST      = 70.0;   // jarak ideal dari tembok
    private const double TOO_CLOSE_WALL      = 50.0;   // bahaya nabrak
    private const double AT_WALL_THRESHOLD   = 90.0;   // dianggap sudah di dekat tembok
    private const double FIRE_POWER          = 1.5;    // peluru sedang, fire rate tinggi
    private const double MIN_ENERGY_TO_FIRE  = 2.0;
    private const double FIRE_ANGLE_THRESHOLD = 12.0;
    private const double WALL_SWITCH_HYSTERESIS = 100.0; // cegah bolak-balik tembok

    // ---------------------------------------------------------------
    // State
    // ---------------------------------------------------------------
    private readonly Dictionary<int, ScannedBotEvent> _enemies = new();
    private int _strafeDir   = 1;
    private int _currentWall = -1; // 0=kiri, 1=kanan, 2=bawah, 3=atas

    // ---------------------------------------------------------------
    // Entry point
    // ---------------------------------------------------------------
    static void Main() => new SharpShooter().Start();
    SharpShooter() : base(BotInfo.FromFile("SharpShooter.json")) { }

    // ---------------------------------------------------------------
    // Run — radar sweep saja, logika di OnScannedBot
    // ---------------------------------------------------------------
    public override void Run()
    {
        BodyColor   = Color.FromArgb(0x0D, 0x0D, 0x0D);
        TurretColor = Color.FromArgb(0x23, 0x23, 0x23);
        RadarColor  = Color.FromArgb(0xFF, 0x3C, 0x00);
        BulletColor = Color.FromArgb(0x00, 0x00, 0xFF);
        ScanColor   = Color.FromArgb(0xFF, 0x3C, 0x00);

        AdjustGunForBodyTurn   = true;
        AdjustRadarForBodyTurn = true;
        AdjustRadarForGunTurn  = true;

        while (IsRunning)
        {
            TurnRadarLeft(double.PositiveInfinity);
        }
    }

    // ---------------------------------------------------------------
    // OnScannedBot — inti logika
    // ---------------------------------------------------------------
    public override void OnScannedBot(ScannedBotEvent e)
    {
        _enemies[e.ScannedBotId] = e;

        ScannedBotEvent? target = FindLowestEnergyEnemy();
        if (target == null) return;

        // Gun: predictive targeting — selalu aktif
        AimAndFire(target);

        // Body: wall-hugging movement
        HandleWallMovement(target);

        Go();
    }

    // ---------------------------------------------------------------
    // Wall-Hugging Movement
    // 1. Tentukan tembok terjauh dari musuh
    // 2. Jika belum di tembok itu → transit ke sana
    // 3. Jika sudah → strafe sepanjang tembok
    // ---------------------------------------------------------------
    private void HandleWallMovement(ScannedBotEvent target)
    {
        // Darurat: terlalu dekat tembok mana pun → dorong ke tengah sedikit
        if (IsTooCloseToWall())
        {
            double cx = ArenaWidth / 2.0;
            double cy = ArenaHeight / 2.0;
            SetTurnLeft(BearingTo(cx, cy));
            SetForward(70);
            return;
        }

        // Tentukan tembok terjauh dari musuh
        int bestWall = FindFarthestWall(target);

        // Hysteresis: hanya ganti tembok jika jauh lebih baik
        if (_currentWall < 0)
        {
            _currentWall = bestWall;
        }
        else if (bestWall != _currentWall)
        {
            double bestDist = EnemyDistToWall(target, bestWall);
            double currDist = EnemyDistToWall(target, _currentWall);
            if (bestDist > currDist + WALL_SWITCH_HYSTERESIS)
                _currentWall = bestWall;
        }

        // Cek apakah sudah di dekat tembok target
        if (IsAtTargetWall(_currentWall))
        {
            // Sudah di tembok → strafe sepanjang tembok
            StrafeAlongWall(_currentWall);
        }
        else
        {
            // Belum di tembok → transit menuju tembok target
            TransitToWall(_currentWall);
        }
    }

    // ---------------------------------------------------------------
    // Transit: gerak menuju titik terdekat di tembok target
    // ---------------------------------------------------------------
    private void TransitToWall(int wall)
    {
        double tx, ty;
        switch (wall)
        {
            case 0: tx = SAFE_WALL_DIST; ty = Clamp(Y, SAFE_WALL_DIST, ArenaHeight - SAFE_WALL_DIST); break;
            case 1: tx = ArenaWidth - SAFE_WALL_DIST; ty = Clamp(Y, SAFE_WALL_DIST, ArenaHeight - SAFE_WALL_DIST); break;
            case 2: tx = Clamp(X, SAFE_WALL_DIST, ArenaWidth - SAFE_WALL_DIST); ty = SAFE_WALL_DIST; break;
            default: tx = Clamp(X, SAFE_WALL_DIST, ArenaWidth - SAFE_WALL_DIST); ty = ArenaHeight - SAFE_WALL_DIST; break;
        }
        SetTurnLeft(BearingTo(tx, ty));
        SetForward(DistanceTo(tx, ty));
    }

    // ---------------------------------------------------------------
    // Strafe: gerak sepanjang tembok, balik arah di pojok
    // Posisi target menjaga jarak SAFE_WALL_DIST dari tembok
    // ---------------------------------------------------------------
    private void StrafeAlongWall(int wall)
    {
        bool vertical = (wall <= 1); // kiri/kanan → strafe atas-bawah
        double tx, ty;

        if (vertical)
        {
            tx = (wall == 0) ? SAFE_WALL_DIST : ArenaWidth - SAFE_WALL_DIST;
            ty = (_strafeDir > 0)
                ? ArenaHeight - SAFE_WALL_DIST - 20
                : SAFE_WALL_DIST + 20;

            // Balik arah di pojok
            if (Y > ArenaHeight - SAFE_WALL_DIST - 40) _strafeDir = -1;
            if (Y < SAFE_WALL_DIST + 40) _strafeDir = 1;
        }
        else
        {
            ty = (wall == 2) ? SAFE_WALL_DIST : ArenaHeight - SAFE_WALL_DIST;
            tx = (_strafeDir > 0)
                ? ArenaWidth - SAFE_WALL_DIST - 20
                : SAFE_WALL_DIST + 20;

            // Balik arah di pojok
            if (X > ArenaWidth - SAFE_WALL_DIST - 40) _strafeDir = -1;
            if (X < SAFE_WALL_DIST + 40) _strafeDir = 1;
        }

        SetTurnLeft(BearingTo(tx, ty));
        SetForward(150);
    }

    // ---------------------------------------------------------------
    // AimAndFire — predictive targeting + peluru kecil
    // ---------------------------------------------------------------
    private void AimAndFire(ScannedBotEvent target)
    {
        if (Energy < MIN_ENERGY_TO_FIRE) return;

        // Prediksi posisi musuh saat peluru tiba
        var (px, py) = PredictPosition(target, FIRE_POWER);

        double gunBearing = GunBearingTo(px, py);
        SetTurnGunLeft(gunBearing);

        if (GunHeat == 0 && Math.Abs(gunBearing) < FIRE_ANGLE_THRESHOLD)
        {
            SetFire(FIRE_POWER);
        }
    }

    // ---------------------------------------------------------------
    // Predictive targeting — linear extrapolation iteratif
    // ---------------------------------------------------------------
    private (double x, double y) PredictPosition(ScannedBotEvent target, double fp)
    {
        double dirRad = target.Direction * Math.PI / 180.0;
        double vx = Math.Sin(dirRad) * target.Speed;
        double vy = Math.Cos(dirRad) * target.Speed;

        double px = target.X;
        double py = target.Y;

        for (int i = 0; i < 3; i++)
        {
            double d = Math.Sqrt((px - X) * (px - X) + (py - Y) * (py - Y));
            double t = d / (20.0 - 3.0 * fp);
            px = target.X + vx * t;
            py = target.Y + vy * t;
            px = Clamp(px, 18, ArenaWidth - 18);
            py = Clamp(py, 18, ArenaHeight - 18);
        }
        return (px, py);
    }

    // ---------------------------------------------------------------
    // Helper: tembok terjauh dari musuh
    // ---------------------------------------------------------------
    private int FindFarthestWall(ScannedBotEvent target)
    {
        double dL = target.X;                    // kiri
        double dR = ArenaWidth - target.X;       // kanan
        double dB = target.Y;                    // bawah
        double dT = ArenaHeight - target.Y;      // atas

        double max = Math.Max(Math.Max(dL, dR), Math.Max(dB, dT));
        if (max == dL) return 0;
        if (max == dR) return 1;
        if (max == dB) return 2;
        return 3;
    }

    private double EnemyDistToWall(ScannedBotEvent target, int wall) => wall switch
    {
        0 => target.X,
        1 => ArenaWidth - target.X,
        2 => target.Y,
        3 => ArenaHeight - target.Y,
        _ => 0
    };

    private bool IsAtTargetWall(int wall) => wall switch
    {
        0 => X < SAFE_WALL_DIST + AT_WALL_THRESHOLD,
        1 => X > ArenaWidth - SAFE_WALL_DIST - AT_WALL_THRESHOLD,
        2 => Y < SAFE_WALL_DIST + AT_WALL_THRESHOLD,
        3 => Y > ArenaHeight - SAFE_WALL_DIST - AT_WALL_THRESHOLD,
        _ => false
    };

    private bool IsTooCloseToWall() =>
        X < TOO_CLOSE_WALL || Y < TOO_CLOSE_WALL ||
        X > ArenaWidth - TOO_CLOSE_WALL || Y > ArenaHeight - TOO_CLOSE_WALL;

    // ---------------------------------------------------------------
    // Cari musuh dengan energi terendah
    // ---------------------------------------------------------------
    private ScannedBotEvent? FindLowestEnergyEnemy()
    {
        ScannedBotEvent? w = null;
        double min = double.MaxValue;
        foreach (var e in _enemies.Values)
            if (e.Energy < min) { min = e.Energy; w = e; }
        return w;
    }

    // ---------------------------------------------------------------
    // Events
    // ---------------------------------------------------------------

    public override void OnHitWall(HitWallEvent e)
    {
        // Nabrak tembok → mundur dan belok ke tengah
        SetTurnLeft(BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0));
        SetBack(80);
        _strafeDir *= -1;
        Go();
    }

    public override void OnHitBot(HitBotEvent e)
    {
        _strafeDir *= -1;
        SetBack(60);
        Go();
    }

    public override void OnBotDeath(BotDeathEvent e) => _enemies.Remove(e.VictimId);

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------
    private static double NormalizeBearing(double a)
    {
        while (a > 180) a -= 360;
        while (a < -180) a += 360;
        return a;
    }

    private static double Clamp(double v, double min, double max) =>
        v < min ? min : (v > max ? max : v);
}