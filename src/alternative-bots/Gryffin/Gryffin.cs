// Gryffin.cs

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class Gryffin : Bot
{
    // ====================== KONSTANTA BOT ======================

    // Jarak aman minimum dari dinding untuk menentukan kapan bot harus stabilisasi.
    private const double SafeMargin = 56.0;

    // Batas jarak lembut dari dinding untuk penilaian keamanan posisi.
    private const double WallSoftMargin = 124.0;

    // Batas jarak keras dari dinding untuk kondisi bahaya dekat dinding.
    private const double WallHardMargin = 68.0;

    // Energi kritis. Jika energi berada di bawah nilai ini, bot masuk mode pemulihan.
    private const double CriticalEnergy = 12.0;

    // Energi rendah. Jika energi berada di bawah nilai ini, bot lebih berhati-hati.
    private const double LowEnergy = 20.0;

    // Ambang energi untuk menentukan kapan bot masih perlu recovery.
    private const double RecoverEnergy = 34.0;

    // Jumlah maksimum lokasi lama yang disimpan untuk menghindari pola gerak berulang.
    private const double RecentLocCount = 42;

    // Jumlah maksimum grid/cell arena yang disimpan agar bot tidak bolak-balik di tempat sama.
    private const double RecentCellCount = 30;

    // Jarak dasar proyeksi gerakan kandidat.
    private const double BaseTravel = 138.0;

    // Jarak proyeksi kandidat untuk kondisi duel.
    private const double DuelTravel = 160.0;

    // Jarak proyeksi kandidat untuk kondisi banyak musuh atau melee.
    private const double MeleeTravel = 150.0;

    // ====================== VARIABEL STATE ======================

    // Random digunakan untuk variasi kecil agar pola bot tidak terlalu mudah ditebak.
    private readonly Random _rng = new();

    // Menyimpan data semua musuh yang pernah terdeteksi.
    private readonly Dictionary<int, EnemyInfo> _enemies = new();

    // Menyimpan beberapa posisi lama bot agar movement tidak repetitif.
    private readonly LinkedList<OldLocation> _recentLocs = new();

    // Menyimpan cell arena yang pernah dikunjungi untuk memberi penalti jika kembali ke area yang sama.
    private readonly HashSet<int> _visitedCells = new();

    // Queue untuk membatasi jumlah cell lama yang disimpan.
    private readonly Queue<int> _visitedCellOrder = new();

    // Berapa turn bot masih mempertahankan arah gerak terakhir.
    private int _moveHoldTurns;

    // Arah gerak terakhir yang dipilih oleh sistem movement.
    private double _lastMoveHeading;

    // ID target terakhir, digunakan untuk bonus kontinuitas target.
    private int _lastTargetId = -1;

    // Turn terakhir saat arah orbit dibalik.
    private int _lastOrbitFlipTurn = -999;

    // Turn terakhir saat mode taktis berubah.
    private int _lastModeShiftTurn = -999;

    // Batas turn sampai bot dianggap dalam kondisi tertekan atau panic.
    private int _panicUntil;

    // Batas turn sampai bot mempertahankan tekanan.
    private int _pressUntil;

    // Batas turn sampai bot dipaksa bergerak ke tengah arena.
    private int _forceCenterUntil;

    // Arah sweep radar.
    private int _radarSweepDir = 1;

    // Arah orbit bot, bernilai 1 atau -1.
    private int _orbitSign = 1;

    // ====================== ENUM DAN DATA STRUCTURE ======================

    // Enum ini merepresentasikan mode taktis utama bot.
    // Mode digunakan agar bot dapat menyesuaikan movement dengan kondisi pertandingan.
    private enum TacticalMode
    {
        Stabilize,
        Hunt,
        BreakLine,
        Pressure,
        Recover
    }

    // Mode taktis aktif saat ini.
    private TacticalMode _mode = TacticalMode.Stabilize;

    // Class ini menyimpan semua informasi penting tentang musuh.
    // Data ini dipakai untuk memilih target, menilai ancaman, memprediksi posisi,
    // dan menentukan strategi movement.
    private sealed class EnemyInfo
    {
        public int Id { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double PrevX { get; set; }
        public double PrevY { get; set; }
        public double Energy { get; set; }
        public double Direction { get; set; }
        public double PrevDirection { get; set; }
        public double Speed { get; set; }
        public double PrevSpeed { get; set; }
        public double VelocityX { get; set; }
        public double VelocityY { get; set; }
        public double DirectionVar { get; set; }
        public double SpeedVar { get; set; }
        public double ThreatIndex { get; set; }
        public double PressureIndex { get; set; }
        public double FinishIndex { get; set; }
        public int LastSeenTurn { get; set; }
        public int ScanCount { get; set; }
        public bool Alive { get; set; } = true;
        public double DamageTaken { get; set; }
        public double DamageFactor { get; set; } = 1.0;
        public double TotalDistance { get; set; }
        public int TurnsTracked { get; set; }
        public bool HasSample { get; set; }
    }

    // Record ini menyimpan posisi lama bot.
    private readonly record struct OldLocation(double X, double Y);

    // Record ini menyimpan kandidat jalur movement.
    private readonly record struct LaneCandidate(
        double Heading,
        double SpeedFactor,
        double X,
        double Y,
        double ScoreBias
    );

    // Record ini menyimpan titik bidik hasil prediksi posisi musuh.
    private readonly record struct AimPoint(double X, double Y);

    // ====================== ENTRY POINT DAN LOOP UTAMA ======================

    // Fungsi utama program.
    // Fungsi ini menjadi titik awal program dan menjalankan bot Gryffin.
    static void Main(string[] args)
    {
        new Gryffin().Start();
    }

    // Constructor bot.
    // Fungsi ini memuat konfigurasi bot dari file Gryffin.json.
    Gryffin() : base(BotInfo.FromFile("Gryffin.json"))
    {
    }

    // Fungsi utama bot selama pertandingan berlangsung.
    // Di setiap turn, bot membersihkan data musuh lama, mengatur radar,
    // memperbarui mode taktis, menjalankan movement, memilih target, dan menembak.
    public override void Run()
    {
        BodyColor = Color.DimGray;
        GunColor = Color.Black;
        RadarColor = Color.Cyan;
        BulletColor = Color.Orange;
        ScanColor = Color.LimeGreen;
        TracksColor = Color.Silver;
        TurretColor = Color.DarkGray;

        AdjustGunForBodyTurn = true;
        AdjustRadarForBodyTurn = true;
        AdjustRadarForGunTurn = true;
        MaxSpeed = 8;

        while (IsRunning)
        {
            PurgeDeadEnemies();
            SetTurnRadarRight(360.0);

            RefreshTacticalMode();
            ExecuteMovement();

            EnemyInfo? target = SelectTarget();
            AimGun(target);

            Go();
        }
    }

    // ====================== MODE TAKTIS ======================

    // Fungsi ini menentukan mode taktis bot berdasarkan kondisi saat ini.
    // Mode dipilih berdasarkan energi, jumlah musuh hidup, tekanan musuh,
    // dan posisi bot terhadap batas arena.
    private void RefreshTacticalMode()
    {
        int alive = AliveEnemies().Count;
        bool lowEnergy = Energy < LowEnergy;
        bool critical = Energy <= CriticalEnergy;
        bool underPressure = TurnNumber < _panicUntil;
        bool forcedCenter = TurnNumber < _forceCenterUntil;

        TacticalMode next;

        if (critical)
        {
            next = TacticalMode.Recover;
        }
        else if (forcedCenter || IsNearBoundary(SafeMargin))
        {
            next = TacticalMode.Stabilize;
        }
        else if (underPressure)
        {
            next = TacticalMode.BreakLine;
        }
        else if (alive >= 3)
        {
            next = TacticalMode.Pressure;
        }
        else if (lowEnergy)
        {
            next = TacticalMode.Hunt;
        }
        else
        {
            next = TacticalMode.Hunt;
        }

        if (next != _mode)
        {
            _lastModeShiftTurn = TurnNumber;
        }

        _mode = next;
    }

    // ====================== MOVEMENT ======================

    // Fungsi ini mengatur movement utama bot.
    // Bot memilih jalur terbaik secara greedy dari beberapa kandidat jalur,
    // lalu bergerak ke arah kandidat dengan skor tertinggi.
    private void ExecuteMovement()
    {
        double centerX = ArenaWidth / 2.0;
        double centerY = ArenaHeight / 2.0;

        if (_moveHoldTurns > 0)
        {
            _moveHoldTurns--;
            DriveInDirection(_lastMoveHeading, 8.0);
            return;
        }

        if (IsNearBoundary(SafeMargin) || IsEnergyCritical())
        {
            double home = DirectionTo(centerX, centerY);

            _lastMoveHeading = home;
            _moveHoldTurns = 1;

            DriveInDirection(home, IsEnergyCritical() ? 8.0 : 7.0);
            return;
        }

        UpdateRecentLocations();

        List<EnemyInfo> enemies = AliveEnemies();

        if (enemies.Count == 0)
        {
            double roam = NormalizeAbsoluteAngle(Direction + 37.0 * _orbitSign);

            _lastMoveHeading = roam;
            _moveHoldTurns = 1;

            DriveInDirection(roam, 4.5);
            return;
        }

        EnemyInfo primary = SelectPrimaryThreat(enemies);
        List<LaneCandidate> candidates = BuildMovementCandidates(enemies, primary, centerX, centerY);

        double bestScore = double.NegativeInfinity;
        LaneCandidate best = candidates[0];

        foreach (LaneCandidate candidate in candidates)
        {
            double score = ScoreLane(candidate, enemies, primary, centerX, centerY);

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        _lastMoveHeading = best.Heading;
        _moveHoldTurns = bestScore > 28.0 ? 3 : bestScore > 18.0 ? 2 : 1;

        DriveInDirection(best.Heading, 8.0 * best.SpeedFactor);
    }

    // Fungsi ini membangun daftar kandidat jalur movement.
    // Kandidat dibuat berdasarkan arah menjauh dari ancaman, orbit terhadap musuh,
    // menjauh dari pusat ancaman, bergerak ke tengah, dan variasi arah lain.
    private List<LaneCandidate> BuildMovementCandidates(
        List<EnemyInfo> enemies,
        EnemyInfo primary,
        double centerX,
        double centerY)
    {
        double toThreat = DirectionTo(primary.X, primary.Y);
        double awayThreat = NormalizeAbsoluteAngle(toThreat + 180.0);
        double escapeCentroid = EscapeHeadingFromCentroid(enemies);
        double centerHeading = DirectionTo(centerX, centerY);

        double angularPush = 92.0 * _orbitSign;
        double shallowPush = 54.0 * _orbitSign;
        double cutPush = 128.0 * _orbitSign;

        var list = new List<LaneCandidate>
        {
            BuildLane(awayThreat, BaseTravel, 1.00),
            BuildLane(NormalizeAbsoluteAngle(toThreat + angularPush), DuelTravel, 1.00),
            BuildLane(NormalizeAbsoluteAngle(toThreat - angularPush), DuelTravel, 1.00),
            BuildLane(NormalizeAbsoluteAngle(toThreat + shallowPush), BaseTravel * 0.92, 0.97),
            BuildLane(NormalizeAbsoluteAngle(toThreat - shallowPush), BaseTravel * 0.92, 0.97),
            BuildLane(escapeCentroid, MeleeTravel, 0.94),
            BuildLane(centerHeading, BaseTravel * 0.88, 0.88),
            BuildLane(NormalizeAbsoluteAngle(Direction + 28.0), BaseTravel * 0.74, 0.82),
            BuildLane(NormalizeAbsoluteAngle(Direction - 28.0), BaseTravel * 0.74, 0.82),
            BuildLane(NormalizeAbsoluteAngle(toThreat + cutPush), BaseTravel * 0.98, 0.90),
            BuildLane(NormalizeAbsoluteAngle(toThreat - cutPush), BaseTravel * 0.98, 0.90),
            BuildLane(NormalizeAbsoluteAngle(awayThreat + 35.0 * _orbitSign), BaseTravel * 0.82, 0.87)
        };

        if (_mode == TacticalMode.Pressure)
        {
            list.Add(BuildLane(
                NormalizeAbsoluteAngle(toThreat + 180.0 + 25.0 * _orbitSign),
                BaseTravel * 0.95,
                0.95
            ));
        }

        if (_mode == TacticalMode.Recover)
        {
            list.Add(BuildLane(centerHeading, BaseTravel * 0.95, 0.80));
        }

        return list;
    }

    // Fungsi ini membuat satu kandidat jalur berdasarkan heading, jarak, dan faktor kecepatan.
    // Titik tujuan kandidat dihitung dengan ProjectStep().
    private LaneCandidate BuildLane(double heading, double distance, double speedFactor)
    {
        double normalized = NormalizeAbsoluteAngle(heading);
        var projected = ProjectStep(normalized, distance);

        return new LaneCandidate(
            normalized,
            speedFactor,
            projected.X,
            projected.Y,
            0.0
        );
    }

    // Fungsi ini memproyeksikan posisi bot jika bergerak ke arah tertentu sejauh distance.
    // Hasil proyeksi dibatasi agar tetap berada di dalam arena.
    private (double X, double Y) ProjectStep(double heading, double distance)
    {
        double radians = ToRadians(heading);

        double x = X + Math.Cos(radians) * distance;
        double y = Y + Math.Sin(radians) * distance;

        x = Math.Clamp(
            x,
            Constants.BoundingCircleRadius,
            ArenaWidth - Constants.BoundingCircleRadius
        );

        y = Math.Clamp(
            y,
            Constants.BoundingCircleRadius,
            ArenaHeight - Constants.BoundingCircleRadius
        );

        return (x, y);
    }

    // Fungsi ini memberi skor pada satu kandidat jalur.
    // Skor mempertimbangkan keamanan dari dinding, jarak ideal dari musuh,
    // jarak dari pusat ancaman, gerak lateral, penalti posisi berulang,
    // tekanan musuh, dan bahaya jarak dekat.
    private double ScoreLane(
        LaneCandidate candidate,
        List<EnemyInfo> enemies,
        EnemyInfo primary,
        double centerX,
        double centerY)
    {
        if (candidate.X < Constants.BoundingCircleRadius
            || candidate.Y < Constants.BoundingCircleRadius
            || candidate.X > ArenaWidth - Constants.BoundingCircleRadius
            || candidate.Y > ArenaHeight - Constants.BoundingCircleRadius)
        {
            return -1_000_000.0;
        }

        double minEnemy = double.PositiveInfinity;
        double weightedEnemyField = 0.0;
        double centroidX = 0.0;
        double centroidY = 0.0;
        double weightSum = 0.0;

        foreach (EnemyInfo enemy in enemies)
        {
            double distance = Distance(candidate.X, candidate.Y, enemy.X, enemy.Y);

            minEnemy = Math.Min(minEnemy, distance);
            weightedEnemyField += (1.0 + enemy.Energy / 100.0) / Math.Max(distance * distance, 1.0);

            double weight = (0.5 + enemy.ThreatIndex) / Math.Max(distance, 55.0);

            centroidX += enemy.X * weight;
            centroidY += enemy.Y * weight;
            weightSum += weight;
        }

        centroidX = weightSum > 0 ? centroidX / weightSum : primary.X;
        centroidY = weightSum > 0 ? centroidY / weightSum : primary.Y;

        double wallSafety = WallSafety(candidate.X, candidate.Y);
        double wallScore = Math.Clamp(wallSafety / WallSoftMargin, 0.0, 1.25) * 2.9;

        double targetDistance = Distance(candidate.X, candidate.Y, primary.X, primary.Y);
        double idealDistance = _mode == TacticalMode.Pressure
            ? 245.0
            : Energy > 40
                ? 280.0
                : Energy > 20
                    ? 345.0
                    : 420.0;

        double ringScore = 1.0 - Math.Min(Math.Abs(targetDistance - idealDistance), 540.0) / 540.0;

        double centroidEscape = Distance(candidate.X, candidate.Y, centroidX, centroidY);
        double centroidScore = Math.Clamp(centroidEscape / 420.0, 0.0, 1.0);

        double lateral = Math.Abs(
            Math.Sin(ToRadians(
                NormalizeRelativeAngle(candidate.Heading - DirectionTo(primary.X, primary.Y))
            ))
        );

        double lateralScore = Math.Clamp(lateral, 0.0, 1.0);

        double turnPenalty = Math.Abs(NormalizeRelativeAngle(candidate.Heading - Direction)) / 180.0;
        double novelty = 1.0 - Math.Min(turnPenalty, 1.0);

        double revisitPenalty = 0.0;

        foreach (OldLocation old in _recentLocs)
        {
            double distance = Distance(candidate.X, candidate.Y, old.X, old.Y);
            revisitPenalty += 1.0 / Math.Max(distance, 12.0);
        }

        double cellPenalty = VisitedCellPenalty(candidate.X, candidate.Y);
        double closeDanger = minEnemy < 120.0 ? -140.0 * (1.0 - minEnemy / 120.0) : 0.0;
        double pressure = weightedEnemyField * 760.0;
        double centerDistance = Distance(candidate.X, candidate.Y, centerX, centerY);

        double centerPull = _mode == TacticalMode.Stabilize
            ? 1.0 - Math.Min(centerDistance / 520.0, 1.0)
            : 0.55 - Math.Min(Math.Abs(centerDistance - 290.0), 500.0) / 1000.0;

        double bias = _mode == TacticalMode.Recover
            ? 0.8
            : _mode == TacticalMode.Pressure
                ? 0.25
                : 0.45;

        if (centerDistance < 120.0)
        {
            centerPull -= 0.25;
        }

        if (centerDistance > 330.0)
        {
            centerPull += 0.15;
        }

        double score =
            wallScore * 3.2 +
            ringScore * 2.2 +
            centroidScore * 1.6 +
            lateralScore * 1.5 +
            centerPull * 0.8 +
            novelty * 0.7 +
            bias -
            revisitPenalty * 1.4 -
            cellPenalty * 1.25 -
            pressure * 0.7 +
            closeDanger;

        if (candidate.SpeedFactor < 0.9 && Energy < RecoverEnergy)
        {
            score += 4.0;
        }

        if (IsEnemyAlignedWithPath(candidate.X, candidate.Y, primary))
        {
            score -= 16.0;
        }

        if (targetDistance < 160.0 && !ShouldRam(primary))
        {
            score -= 24.0;
        }

        if (candidate.SpeedFactor > 0.95 && _mode == TacticalMode.Pressure)
        {
            score += 3.0;
        }

        if (_mode == TacticalMode.BreakLine)
        {
            score += Math.Abs(Math.Sin(ToRadians(candidate.Heading - Direction))) * 2.0;
        }

        return score;
    }

    // Fungsi ini memberi penalti jika kandidat posisi berada di cell arena
    // yang sudah sering atau baru saja dikunjungi.
    private double VisitedCellPenalty(double x, double y)
    {
        int gridX = (int)(x / 44.0);
        int gridY = (int)(y / 44.0);
        int key = (gridX << 16) ^ gridY;

        return _visitedCells.Contains(key) ? 2.1 : 0.0;
    }

    // Fungsi ini mengecek apakah jalur gerak kandidat terlalu sejajar dengan arah musuh.
    // Jika sejajar, kandidat dianggap berbahaya karena bot bisa bergerak lurus ke garis tembak musuh.
    private bool IsEnemyAlignedWithPath(double x, double y, EnemyInfo threat)
    {
        double pathBearing = DirectionTo(x, y);
        double threatBearing = DirectionTo(threat.X, threat.Y);
        double delta = Math.Abs(NormalizeRelativeAngle(pathBearing - threatBearing));

        return delta < 28.0 || delta > 332.0;
    }

    // Fungsi ini menggerakkan bot ke heading tertentu dengan kecepatan tertentu.
    // Jika sudut terlalu besar, bot memilih bergerak mundur agar rotasi lebih efisien.
    private void DriveInDirection(double heading, double speed)
    {
        double turn = NormalizeRelativeAngle(heading - Direction);

        if (Math.Abs(turn) > 90.0)
        {
            double backTurn = NormalizeRelativeAngle(turn + 180.0);

            SetTurnLeft(backTurn);
            TargetSpeed = -Math.Abs(speed);
        }
        else
        {
            SetTurnLeft(turn);
            TargetSpeed = Math.Abs(speed);
        }
    }

    // Fungsi ini memperbarui riwayat lokasi bot.
    // Riwayat ini dipakai untuk mencegah bot bergerak bolak-balik di tempat yang sama.
    private void UpdateRecentLocations()
    {
        if (TurnNumber % 2 != 0)
        {
            return;
        }

        _recentLocs.AddFirst(new OldLocation(X, Y));

        while (_recentLocs.Count > RecentLocCount)
        {
            _recentLocs.RemoveLast();
        }

        int gridX = (int)(X / 44.0);
        int gridY = (int)(Y / 44.0);
        int key = (gridX << 16) ^ gridY;

        if (_visitedCells.Add(key))
        {
            _visitedCellOrder.Enqueue(key);
        }

        while (_visitedCellOrder.Count > RecentCellCount)
        {
            int old = _visitedCellOrder.Dequeue();
            _visitedCells.Remove(old);
        }
    }

    // Fungsi ini menghitung jarak aman suatu titik dari dinding arena.
    // Nilai besar berarti posisi lebih aman dari risiko menabrak dinding.
    private double WallSafety(double x, double y)
    {
        double left = x - Constants.BoundingCircleRadius;
        double right = ArenaWidth - Constants.BoundingCircleRadius - x;
        double bottom = y - Constants.BoundingCircleRadius;
        double top = ArenaHeight - Constants.BoundingCircleRadius - y;

        return Math.Max(
            0.0,
            Math.Min(
                Math.Min(left, right),
                Math.Min(bottom, top)
            )
        );
    }

    // Fungsi ini mengecek apakah bot sedang dekat dengan batas arena.
    // Jika iya, bot cenderung masuk mode stabilisasi dan bergerak ke tengah.
    private bool IsNearBoundary(double margin)
    {
        return X < margin
            || Y < margin
            || X > ArenaWidth - margin
            || Y > ArenaHeight - margin;
    }

    // Fungsi ini menghitung jarak Euclidean antara dua titik.
    // Dipakai untuk menghitung jarak antar posisi bot, musuh, dan kandidat posisi.
    private static double Distance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;

        return Math.Sqrt(dx * dx + dy * dy);
    }

    // Fungsi ini mengubah sudut derajat menjadi radian.
    // Radian diperlukan untuk perhitungan trigonometri Sin dan Cos.
    private static double ToRadians(double angle)
    {
        return angle * Math.PI / 180.0;
    }

    // Fungsi ini menghitung arah kabur dari pusat kumpulan musuh.
    // Bot menghitung centroid musuh berbobot ancaman, lalu bergerak menjauhinya.
    private double EscapeHeadingFromCentroid(List<EnemyInfo> enemies)
    {
        double sumX = 0.0;
        double sumY = 0.0;
        double weightSum = 0.0;

        foreach (EnemyInfo enemy in enemies)
        {
            double distance = Math.Max(60.0, DistanceTo(enemy.X, enemy.Y));
            double weight = (0.8 + Math.Min(enemy.Energy, 100.0) / 100.0) / distance;

            sumX += enemy.X * weight;
            sumY += enemy.Y * weight;
            weightSum += weight;
        }

        if (weightSum <= 0.0)
        {
            return DirectionTo(ArenaWidth / 2.0, ArenaHeight / 2.0);
        }

        return NormalizeAbsoluteAngle(
            DirectionTo(sumX / weightSum, sumY / weightSum) + 180.0
        );
    }

    // ====================== AIMING DAN FIRING ======================

    // Fungsi ini mengarahkan gun ke target dan menembak jika kondisi memungkinkan.
    // Bot memilih firepower, memprediksi posisi musuh, lalu menembak saat gun sudah cukup sejajar.
    private void AimGun(EnemyInfo? target)
    {
        if (target == null || GunHeat > 0.1)
        {
            SetFire(0);
            return;
        }

        double distance = DistanceTo(target.X, target.Y);
        double firepower = ChooseFirePower(target, distance);

        if (firepower <= 0.0)
        {
            SetFire(0);
            return;
        }

        AimPoint aim = PredictPosition(target, firepower);
        double gunTurn = NormalizeRelativeAngle(DirectionTo(aim.X, aim.Y) - GunDirection);

        SetTurnGunLeft(gunTurn);

        if (Math.Abs(gunTurn) < 8.0 && GunHeat == 0)
        {
            SetFire(firepower);
        }
        else
        {
            SetFire(0);
        }
    }

    // Fungsi ini memprediksi posisi target saat peluru sampai.
    // Prediksi memakai kecepatan peluru, velocity musuh, arah musuh, dan tingkat kestabilan gerak musuh.
    private AimPoint PredictPosition(EnemyInfo enemy, double firepower)
    {
        double bulletSpeed = CalcBulletSpeed(firepower);
        double ticks = Math.Clamp(DistanceTo(enemy.X, enemy.Y) / bulletSpeed, 1.0, 15.0);

        double headingRad = ToRadians(enemy.Direction);
        double measuredVelocityX = Math.Cos(headingRad) * enemy.Speed;
        double measuredVelocityY = Math.Sin(headingRad) * enemy.Speed;

        double stability = Math.Clamp(1.0 - Math.Abs(enemy.DirectionVar) / 20.0, 0.22, 1.0);
        double blend = 0.24 + 0.48 * stability;
        double turnBias = Math.Clamp(enemy.DirectionVar, -13.0, 13.0);

        double predictedX = enemy.X;
        double predictedY = enemy.Y;

        for (int i = 0; i < 4; i++)
        {
            double predictedHeading = ToRadians(enemy.Direction + turnBias * ticks * blend);

            double accelerationX = Math.Cos(predictedHeading) * enemy.Speed * ticks;
            double accelerationY = Math.Sin(predictedHeading) * enemy.Speed * ticks;

            double velocityX = enemy.VelocityX * (1.0 - blend) + measuredVelocityX * blend;
            double velocityY = enemy.VelocityY * (1.0 - blend) + measuredVelocityY * blend;

            predictedX = enemy.X + (velocityX * (1.0 - blend) + accelerationX * blend) * ticks * 0.9;
            predictedY = enemy.Y + (velocityY * (1.0 - blend) + accelerationY * blend) * ticks * 0.9;

            if (predictedX < Constants.BoundingCircleRadius
                || predictedX > ArenaWidth - Constants.BoundingCircleRadius
                || predictedY < Constants.BoundingCircleRadius
                || predictedY > ArenaHeight - Constants.BoundingCircleRadius)
            {
                predictedX = Math.Clamp(
                    predictedX,
                    Constants.BoundingCircleRadius,
                    ArenaWidth - Constants.BoundingCircleRadius
                );

                predictedY = Math.Clamp(
                    predictedY,
                    Constants.BoundingCircleRadius,
                    ArenaHeight - Constants.BoundingCircleRadius
                );

                break;
            }

            ticks = DistanceTo(predictedX, predictedY) / bulletSpeed;
        }

        return new AimPoint(predictedX, predictedY);
    }

    // Fungsi ini memilih target terbaik secara greedy.
    // Setiap musuh hidup diberi skor, lalu musuh dengan skor tertinggi dipilih.
    private EnemyInfo? SelectTarget()
    {
        EnemyInfo? best = null;
        double bestScore = double.NegativeInfinity;

        foreach (EnemyInfo enemy in _enemies.Values)
        {
            if (!enemy.Alive)
            {
                continue;
            }

            int age = TurnNumber - enemy.LastSeenTurn;

            if (age > 30)
            {
                continue;
            }

            double score = ScoreTarget(enemy, age);

            if (score > bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }

        if (best != null)
        {
            _lastTargetId = best.Id;
        }

        return best;
    }

    // Fungsi ini menghitung skor target.
    // Skor mempertimbangkan jarak, energi musuh, damage factor, prediktabilitas,
    // instabilitas gerak, usia data, peluang finishing, dan kontinuitas target.
    private double ScoreTarget(EnemyInfo enemy, int age)
    {
        double distance = DistanceTo(enemy.X, enemy.Y);

        double distanceScore = 1.0 - Math.Min(distance / 760.0, 1.0);
        double energyScore = 1.0 - Math.Min(enemy.Energy / 100.0, 1.0);
        double damageScore = Math.Clamp(enemy.DamageFactor / 18.0, 0.0, 1.2);
        double instabilityScore = Math.Clamp(Math.Abs(enemy.DirectionVar) / 24.0, 0.0, 1.0);
        double predictabilityScore = 1.0 - Math.Clamp(Math.Abs(enemy.SpeedVar) / 8.0, 0.0, 1.0);
        double freshnessScore = 1.0 - Math.Min(age / 30.0, 1.0);
        double finishBonus = enemy.Energy <= 15.0 ? 0.7 : 0.0;
        double continuity = enemy.Id == _lastTargetId ? 0.14 : 0.0;

        return distanceScore * 2.15
            + energyScore * 2.0
            + damageScore * 1.7
            + predictabilityScore * 1.9
            + instabilityScore * 0.9
            + freshnessScore * 0.85
            + finishBonus
            + continuity;
    }

    // Fungsi ini memilih firepower berdasarkan jarak, energi target,
    // jumlah musuh hidup, dan energi bot sendiri.
    private double ChooseFirePower(EnemyInfo target, double distance)
    {
        if (IsEnergyCritical())
        {
            return 0.0;
        }

        double power = distance switch
        {
            < 110 => 3.0,
            < 210 => 2.5,
            < 360 => 1.9,
            < 520 => 1.3,
            < 700 => 0.85,
            _ => 0.55,
        };

        if (target.Energy <= 12.0)
        {
            power = Math.Max(power, 2.8);
        }

        int alive = AliveEnemies().Count;

        if (alive >= 4)
        {
            power = Math.Min(power, 1.6);
        }
        else if (alive == 3)
        {
            power = Math.Min(power, 2.05);
        }
        else if (alive == 2)
        {
            power = Math.Min(power, 2.35);
        }

        if (Energy < LowEnergy)
        {
            power = Math.Min(power, 1.05);
        }

        if (Energy < CriticalEnergy)
        {
            power = 0.0;
        }

        return Math.Clamp(power, Constants.MinFirepower, Constants.MaxFirepower);
    }

    // ====================== EVENT HANDLER ======================

    // Fungsi ini dijalankan setiap kali radar mendeteksi musuh.
    // Bot memperbarui data musuh, menghitung velocity, threat index,
    // pressure index, damage factor, dan menentukan apakah bot sedang tertekan.
    public override void OnScannedBot(ScannedBotEvent evt)
    {
        if (IsTeammate(evt.ScannedBotId))
        {
            return;
        }

        EnemyInfo enemy = GetOrCreate(evt.ScannedBotId);
        double distance = DistanceTo(evt.X, evt.Y);

        enemy.PrevX = enemy.X;
        enemy.PrevY = enemy.Y;
        enemy.PrevDirection = enemy.Direction;
        enemy.PrevSpeed = enemy.Speed;

        if (enemy.HasSample)
        {
            int deltaTurn = Math.Max(1, evt.TurnNumber - enemy.LastSeenTurn);

            double measuredVelocityX = (evt.X - enemy.X) / deltaTurn;
            double measuredVelocityY = (evt.Y - enemy.Y) / deltaTurn;

            double directionRad = ToRadians(evt.Direction);
            double directionalVelocityX = Math.Cos(directionRad) * evt.Speed;
            double directionalVelocityY = Math.Sin(directionRad) * evt.Speed;

            enemy.VelocityX = measuredVelocityX * 0.62 + directionalVelocityX * 0.38;
            enemy.VelocityY = measuredVelocityY * 0.62 + directionalVelocityY * 0.38;
        }
        else
        {
            double directionRad = ToRadians(evt.Direction);

            enemy.VelocityX = Math.Cos(directionRad) * evt.Speed;
            enemy.VelocityY = Math.Sin(directionRad) * evt.Speed;
            enemy.HasSample = true;
        }

        enemy.X = evt.X;
        enemy.Y = evt.Y;
        enemy.Energy = evt.Energy;
        enemy.Direction = evt.Direction;
        enemy.Speed = evt.Speed;
        enemy.LastSeenTurn = evt.TurnNumber;
        enemy.Alive = true;

        enemy.ScanCount++;
        enemy.TotalDistance += distance;
        enemy.TurnsTracked++;

        double directionDelta = NormalizeRelativeAngle(enemy.Direction - enemy.PrevDirection);
        double speedDelta = enemy.Speed - enemy.PrevSpeed;

        enemy.DirectionVar = enemy.DirectionVar * 0.70 + directionDelta * 0.30;
        enemy.SpeedVar = enemy.SpeedVar * 0.70 + speedDelta * 0.30;

        double baseThreat = (100.0 - Math.Min(100.0, enemy.Energy)) / 100.0;
        double rangeThreat = 1.0 - Math.Min(distance / 720.0, 1.0);
        double instability = Math.Clamp(Math.Abs(enemy.DirectionVar) / 22.0, 0.0, 1.0);

        enemy.ThreatIndex = baseThreat * 0.55 + rangeThreat * 0.35 + instability * 0.45;

        double damageBoost =
            (enemy.DamageTaken + 9.0)
            * (1.0 + Math.Min(enemy.TotalDistance / Math.Max(enemy.TurnsTracked, 1), 520.0) / 520.0);

        double stabilityPenalty = 1.0 + Math.Min(Math.Abs(enemy.DirectionVar) / 28.0, 0.55);

        enemy.DamageFactor = damageBoost / stabilityPenalty;
        enemy.PressureIndex = enemy.ThreatIndex + Math.Clamp(enemy.DamageFactor / 18.0, 0.0, 1.2);
        enemy.FinishIndex = enemy.Energy <= 15.0 ? 1.0 : 0.0;

        if (enemy.PressureIndex > 1.1 && distance < 680.0)
        {
            _panicUntil = Math.Max(_panicUntil, TurnNumber + 9);
            _forceCenterUntil = Math.Max(_forceCenterUntil, TurnNumber + 7);
        }
    }

    // Fungsi ini dijalankan saat ada bot musuh yang mati.
    // Bot menandai musuh tersebut sebagai tidak aktif.
    public override void OnBotDeath(BotDeathEvent evt)
    {
        if (_enemies.TryGetValue(evt.VictimId, out EnemyInfo? enemy))
        {
            enemy.Alive = false;
        }
    }

    // Fungsi ini dijalankan saat bot menabrak dinding.
    // Bot akan dipaksa bergerak ke tengah arena dan arah orbit dibalik.
    public override void OnHitWall(HitWallEvent evt)
    {
        _moveHoldTurns = 2;
        _forceCenterUntil = Math.Max(_forceCenterUntil, TurnNumber + 12);
        _orbitSign *= -1;
        _lastOrbitFlipTurn = TurnNumber;
        _lastMoveHeading = DirectionTo(ArenaWidth / 2.0, ArenaHeight / 2.0);

        TargetSpeed = -8.0;
        SetTurnLeft(NormalizeRelativeAngle(_lastMoveHeading - Direction));
    }

    // Fungsi ini dijalankan saat bot menabrak bot lain.
    // Bot membalik orbit dan mencoba menjauh agar tidak terjebak di kontak dekat.
    public override void OnHitBot(HitBotEvent evt)
    {
        _moveHoldTurns = 1;
        _orbitSign *= -1;
        _lastOrbitFlipTurn = TurnNumber;

        double away = NormalizeAbsoluteAngle(BearingTo(evt.X, evt.Y) + 180.0);

        _lastMoveHeading = away;

        SetTurnLeft(NormalizeRelativeAngle(away - Direction));
        TargetSpeed = -8.0;
    }

    // Fungsi ini dijalankan saat bot terkena peluru.
    // Bot memperbarui data ancaman penembak, menaikkan pressure,
    // masuk mode panic sementara, dan membalik arah orbit.
    public override void OnHitByBullet(HitByBulletEvent evt)
    {
        int shooterId = evt.Bullet.OwnerId;

        if (_enemies.TryGetValue(shooterId, out EnemyInfo? shooter))
        {
            double damage =
                4.0 * evt.Bullet.Power
                + (evt.Bullet.Power > 1.0 ? 2.0 * (evt.Bullet.Power - 1.0) : 0.0);

            shooter.DamageTaken += damage;

            double averageDistance = shooter.TurnsTracked > 0
                ? shooter.TotalDistance / shooter.TurnsTracked
                : 500.0;

            shooter.DamageFactor =
                (shooter.DamageTaken + 10.0)
                * averageDistance
                / Math.Max(shooter.TurnsTracked, 1);

            shooter.ThreatIndex += 0.25;
            shooter.PressureIndex += 0.2;
        }

        _panicUntil = Math.Max(_panicUntil, TurnNumber + 10);
        _moveHoldTurns = Math.Max(_moveHoldTurns, 1);
        _orbitSign *= -1;
    }

    // Fungsi ini dijalankan saat bot mati.
    // Semua data musuh dan riwayat posisi dibersihkan agar tidak terbawa ke ronde berikutnya.
    public override void OnDeath(DeathEvent evt)
    {
        _enemies.Clear();
        _recentLocs.Clear();
        _visitedCells.Clear();
        _visitedCellOrder.Clear();
    }

    // ====================== DATA MUSUH DAN HELPER STRATEGI ======================

    // Fungsi ini mengambil data musuh dari dictionary.
    // Jika belum ada, fungsi akan membuat data musuh baru.
    private EnemyInfo GetOrCreate(int id)
    {
        if (!_enemies.TryGetValue(id, out EnemyInfo? enemy))
        {
            enemy = new EnemyInfo { Id = id };
            _enemies[id] = enemy;
        }

        return enemy;
    }

    // Fungsi ini mengambil semua musuh yang masih dianggap hidup.
    private List<EnemyInfo> AliveEnemies()
    {
        return _enemies.Values
            .Where(enemy => enemy.Alive)
            .ToList();
    }

    // Fungsi ini menandai musuh sebagai tidak aktif jika sudah terlalu lama tidak terlihat.
    private void PurgeDeadEnemies()
    {
        foreach (EnemyInfo enemy in _enemies.Values.Where(enemy => enemy.Alive).ToList())
        {
            if (TurnNumber - enemy.LastSeenTurn > 42)
            {
                enemy.Alive = false;
            }
        }
    }

    // Fungsi ini mengecek apakah energi bot berada pada kondisi kritis.
    private bool IsEnergyCritical()
    {
        return Energy <= CriticalEnergy;
    }

    // Fungsi ini menentukan apakah kondisi cocok untuk melakukan ram.
    // Ram dilakukan jika energi bot cukup, energi musuh rendah, dan jarak musuh dekat.
    private bool ShouldRam(EnemyInfo target)
    {
        return Energy > 26.0
            && target.Energy <= 18.0
            && DistanceTo(target.X, target.Y) < 175.0;
    }

    // Fungsi ini memilih ancaman utama dari daftar musuh hidup.
    // Musuh dinilai berdasarkan jarak, energi, posisi samping/belakang,
    // pressure index, dan usia data scan.
    private EnemyInfo SelectPrimaryThreat(List<EnemyInfo> enemies)
    {
        EnemyInfo best = enemies[0];
        double bestScore = double.NegativeInfinity;

        foreach (EnemyInfo enemy in enemies)
        {
            double distance = DistanceTo(enemy.X, enemy.Y);
            double bodyBearing = Math.Abs(
                NormalizeRelativeAngle(DirectionTo(enemy.X, enemy.Y) - Direction)
            );

            double closeFactor = 1.0 - Math.Min(distance, 780.0) / 780.0;
            double energyFactor = 0.5 + Math.Min(enemy.Energy, 100.0) / 100.0;
            double flankFactor = bodyBearing > 70.0 ? 1.05 : 0.0;
            double backFactor = bodyBearing > 135.0 ? 0.9 : 0.0;
            double agePenalty = Math.Max(0, TurnNumber - enemy.LastSeenTurn) * 0.05;

            double score =
                closeFactor * (2.0 + energyFactor + flankFactor + backFactor)
                + enemy.PressureIndex * 1.15
                - agePenalty;

            if (distance < 170.0)
            {
                score += 1.0;
            }

            if (enemy.Id == _lastTargetId)
            {
                score -= 0.08;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }

        return best;
    }
}