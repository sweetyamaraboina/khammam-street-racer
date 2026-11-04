using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace KhammamStreetRacer
{
    public class StepInfo
    {
        public int Id;
        public string RawFile;
        public string CleanFile;
        public bool CropBottom;
        public string Label;
        public string Coords;
    }

    public class RivalBiker
    {
        public string Name;
        public Color BikeColor;
        public Color SuitColor;
        public float TrackPos; // 0.0 to 17.5
        public float LaneX;    // -0.8 (left) to +0.8 (right)
        public float Speed;    // units per tick
        public float TargetLaneX;
        public bool PassedPlayer;
    }

    public class RoadHazard
    {
        public string Type; // "car" or "scooter"
        public Color MainColor;
        public float TrackPos;
        public float LaneX;
    }

    public class MainForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private List<StepInfo> steps = new List<StepInfo>();
        private Image[] stepImages = new Image[18];
        private Image imgHouseLeft = null;
        private Image imgHouseRight = null;

        // Continuous Racing Physics & Game State
        private bool raceActive = true;
        private int countdownValue = 3; // 3, 2, 1, 0 (GO!)
        private int countdownTicks = 0;
        private bool raceFinished = false;

        private float playerTrackPos = 0.0f; // 0.0 (Step 1 Start) to 17.0 (Step 18 Finish)
        private float playerLaneX = 0.0f;    // -0.85 (Left Edge) to +0.85 (Right Edge)
        private float playerSpeedKmh = 0f;   // 0 to 155 km/h
        private float playerLeanAngle = 0f;  // -24 to +24 degrees
        private float nitroAmount = 100f;    // 0 to 100%
        private bool isNitroActive = false;
        private int score = 0;
        private int screenShakeTicks = 0;
        private string hudPopupText = "";
        private int hudPopupTicks = 0;
        private bool maIlluBonusAwarded = false;

        private Stopwatch raceStopwatch = new Stopwatch();
        private double bestRaceTimeSec = 0.0;

        // Input States
        private bool keyGas = false;
        private bool keyBrake = false;
        private bool keyLeft = false;
        private bool keyRight = false;
        private bool keyNitro = false;

        // Camera & Inspection Modes
        private bool thirdPersonCamera = true; // true = 3rd Person Racing Bike, false = 1st Person Handlebar Cockpit
        private string lookDir = "straight";   // "left" (Ma Illu), "straight", "right" (Opposite)
        private bool cleanMode = true;
        private bool isFullscreen = false;
        private FormWindowState prevWindowState;
        private FormBorderStyle prevBorderStyle;
        private Rectangle prevBounds;

        private List<RivalBiker> rivals = new List<RivalBiker>();
        private List<RoadHazard> hazards = new List<RoadHazard>();
        private Random rng = new Random();

        private System.Windows.Forms.Timer gameLoopTimer;
        private System.Windows.Forms.Timer autoUpdateTimer;

        private string appDir;
        private string assetsDir;
        private string localVersion = "1.0.2";
        private string ghOwner = "sweetyamaraboina";
        private string ghRepo = "khammam-street-racer";
        private string ghBranch = "main";
        private bool isCheckingUpdate = false;

        // UI Controls
        private Panel topBar;
        private Panel bottomBar;
        private ViewportPanel viewport;
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblUpdateBadge;
        private Button btnStartRace;
        private Button btnCameraMode;
        private Button btnLookMaIllu;
        private Button btnLookStraight;
        private Button btnLookRight;
        private Button btnCheckUpdate;
        private Button btnRepoConfig;
        private Button btnFullscreen;

        public MainForm()
        {
            appDir = AppDomain.CurrentDomain.BaseDirectory;
            assetsDir = Path.Combine(appDir, "assets");
            if (!Directory.Exists(assetsDir))
            {
                DirectoryInfo parent = Directory.GetParent(appDir.TrimEnd('\\'));
                if (parent != null)
                {
                    string parentAssets = Path.Combine(parent.FullName, "assets");
                    if (Directory.Exists(parentAssets)) assetsDir = parentAssets;
                }
            }

            LoadConfigAndVersion();
            InitSteps();
            PreloadAllImages();
            InitUI();
            StartNewRace();

            // Check for updates automatically 2 seconds after launch
            System.Windows.Forms.Timer startupCheck = new System.Windows.Forms.Timer();
            startupCheck.Interval = 2000;
            startupCheck.Tick += (s, e) =>
            {
                startupCheck.Stop();
                CheckForGitHubUpdates(false);
            };
            startupCheck.Start();

            // Periodic background auto-update check every 2 minutes
            autoUpdateTimer = new System.Windows.Forms.Timer();
            autoUpdateTimer.Interval = 120000;
            autoUpdateTimer.Tick += (s, e) => CheckForGitHubUpdates(false);
            autoUpdateTimer.Start();
        }

        private string FindConfigPath(string fileName)
        {
            string p1 = Path.Combine(appDir, fileName);
            if (File.Exists(p1)) return p1;
            DirectoryInfo parent = Directory.GetParent(appDir.TrimEnd('\\'));
            if (parent != null)
            {
                string p2 = Path.Combine(parent.FullName, fileName);
                if (File.Exists(p2)) return p2;
            }
            return p1;
        }

        private void LoadConfigAndVersion()
        {
            try
            {
                string verPath = FindConfigPath("version.json");
                if (File.Exists(verPath))
                {
                    string json = File.ReadAllText(verPath);
                    Match m = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success) localVersion = m.Groups[1].Value;
                }

                string cfgPath = FindConfigPath("github-config.json");
                if (File.Exists(cfgPath))
                {
                    string json = File.ReadAllText(cfgPath);
                    Match mOwner = Regex.Match(json, "\"owner\"\\s*:\\s*\"([^\"]*)\"");
                    Match mRepo = Regex.Match(json, "\"repo\"\\s*:\\s*\"([^\"]+)\"");
                    Match mBranch = Regex.Match(json, "\"branch\"\\s*:\\s*\"([^\"]+)\"");
                    if (mOwner.Success && !string.IsNullOrEmpty(mOwner.Groups[1].Value.Trim()))
                        ghOwner = mOwner.Groups[1].Value.Trim();
                    if (mRepo.Success) ghRepo = mRepo.Groups[1].Value.Trim();
                    if (mBranch.Success) ghBranch = mBranch.Groups[1].Value.Trim();
                }
            }
            catch { }
        }

        private void SaveGitHubConfig(string owner, string repo, string branch)
        {
            ghOwner = owner.Trim();
            ghRepo = string.IsNullOrEmpty(repo.Trim()) ? "khammam-street-racer" : repo.Trim();
            ghBranch = string.IsNullOrEmpty(branch.Trim()) ? "main" : branch.Trim();

            string json = "{\r\n  \"owner\": \"" + ghOwner + "\",\r\n  \"repo\": \"" + ghRepo + "\",\r\n  \"branch\": \"" + ghBranch + "\"\r\n}\r\n";
            try
            {
                File.WriteAllText(FindConfigPath("github-config.json"), json, Encoding.UTF8);
            }
            catch { }
        }

        private void InitSteps()
        {
            steps.Add(new StepInfo { Id = 1,  RawFile = "step_01.jpg", CleanFile = "step_01.jpg", CropBottom = true,  Coords = "17.257785° N, 80.159039° E", Label = "START LINE: Divya City Apartment" });
            steps.Add(new StepInfo { Id = 2,  RawFile = "step_02.jpg", CleanFile = "zone1_road_start_3d.jpg", CropBottom = false, Coords = "17.257628° N, 80.158944° E", Label = "Zone 1: Neem Trees & Banana/Papaya Wall" });
            steps.Add(new StepInfo { Id = 3,  RawFile = "step_03.jpg", CleanFile = "step_03.jpg", CropBottom = true,  Coords = "17.257549° N, 80.158898° E", Label = "Zone 1: Shady Neem Canopy & Peach Apartment" });
            steps.Add(new StepInfo { Id = 4,  RawFile = "step_04.jpg", CleanFile = "step_04.jpg", CropBottom = true,  Coords = "17.257470° N, 80.158851° E", Label = "Zone 2: Peach Apartment Entrance" });
            steps.Add(new StepInfo { Id = 5,  RawFile = "step_05.jpg", CleanFile = "step_05.jpg", CropBottom = true,  Coords = "17.257387° N, 80.158816° E", Label = "Zone 2: Upper-Middle Straightaway" });
            steps.Add(new StepInfo { Id = 6,  RawFile = "step_06.jpg", CleanFile = "step_06.jpg", CropBottom = true,  Coords = "17.257316° N, 80.158756° E", Label = "Zone 2: Khammam Municipal Wall Painting" });
            steps.Add(new StepInfo { Id = 7,  RawFile = "step_07.jpg", CleanFile = "step_07.jpg", CropBottom = true,  Coords = "17.257238° N, 80.158712° E", Label = "Zone 2: Yellow Veranda Building" });
            steps.Add(new StepInfo { Id = 8,  RawFile = "step_08.jpg", CleanFile = "step_08.jpg", CropBottom = true,  Coords = "17.257160° N, 80.158667° E", Label = "Zone 3: Middle Street Neem Shade" });
            steps.Add(new StepInfo { Id = 9,  RawFile = "step_09.jpg", CleanFile = "step_09.jpg", CropBottom = true,  Coords = "17.257085° N, 80.158612° E", Label = "Zone 3: Approaching Stone-Cladding House" });
            steps.Add(new StepInfo { Id = 10, RawFile = "step_10.jpg", CleanFile = "zone3_mid_approach_3d.jpg", CropBottom = false, Coords = "17.257004° N, 80.158558° E", Label = "Zone 3: Stone-Cladding House & Striped Ramp" });
            steps.Add(new StepInfo { Id = 11, RawFile = "step_11.jpg", CleanFile = "step_11.jpg", CropBottom = true,  Coords = "17.256921° N, 80.158518° E", Label = "Zone 3: Passing Silver Innova Corner" });
            steps.Add(new StepInfo { Id = 12, RawFile = "step_12.jpg", CleanFile = "step_12.jpg", CropBottom = true,  Coords = "17.256845° N, 80.158468° E", Label = "Zone 4: Neem Tree Corner Before Ma Illu" });
            steps.Add(new StepInfo { Id = 13, RawFile = "step_13.jpg", CleanFile = "exact_full_street_3d_view.jpg", CropBottom = false, Coords = "17.256749° N, 80.158489° E", Label = "CHECKPOINT: MA ILLU (YOUR EXACT HOUSE PIN)" });
            steps.Add(new StepInfo { Id = 14, RawFile = "step_14.jpg", CleanFile = "step_14.jpg", CropBottom = true,  Coords = "17.256685° N, 80.158377° E", Label = "Zone 4: Sage-Green House & Red-Chajja House" });
            steps.Add(new StepInfo { Id = 15, RawFile = "step_15.jpg", CleanFile = "step_15.jpg", CropBottom = true,  Coords = "17.256601° N, 80.158336° E", Label = "Zone 4: South Approach Past White Indica" });
            steps.Add(new StepInfo { Id = 16, RawFile = "step_16.jpg", CleanFile = "zone5_road_end_3d.jpg", CropBottom = false, Coords = "17.256518° N, 80.158292° E", Label = "Zone 5: 4-Story Grey Building & CA Office" });
            steps.Add(new StepInfo { Id = 17, RawFile = "step_17.jpg", CleanFile = "step_17.jpg", CropBottom = true,  Coords = "17.256437° N, 80.158256° E", Label = "Zone 5: Final Sprint to Junction" });
            steps.Add(new StepInfo { Id = 18, RawFile = "step_18.jpg", CleanFile = "step_18.jpg", CropBottom = true,  Coords = "17.256351° N, 80.158221° E", Label = "FINISH LINE: Main Road Exit T-Junction" });
        }

        private Image LoadImgSafe(string fileName)
        {
            try
            {
                string p = Path.Combine(assetsDir, fileName);
                if (File.Exists(p))
                {
                    using (FileStream fs = new FileStream(p, FileMode.Open, FileAccess.Read))
                    {
                        return Image.FromStream(fs);
                    }
                }
            }
            catch { }
            return null;
        }

        private void PreloadAllImages()
        {
            for (int i = 0; i < 18; i++)
            {
                StepInfo s = steps[i];
                Image img = LoadImgSafe(cleanMode ? s.CleanFile : s.RawFile);
                if (img == null) img = LoadImgSafe(s.RawFile);
                stepImages[i] = img;
            }
            imgHouseLeft = LoadImgSafe("exact_house_3d_view.jpg") ?? LoadImgSafe("view_east_wide.jpg");
            imgHouseRight = LoadImgSafe("view_west_wide.jpg");
        }

        private void StartNewRace()
        {
            lookDir = "straight";
            raceActive = true;
            raceFinished = false;
            countdownValue = 3;
            countdownTicks = 30; // ~1 second per number at 30 FPS
            playerTrackPos = 0.0f;
            playerLaneX = 0.0f;
            playerSpeedKmh = 0f;
            playerLeanAngle = 0f;
            nitroAmount = 100f;
            score = 0;
            maIlluBonusAwarded = false;
            hudPopupText = "🏁 GET READY AT DIVYA CITY APARTMENT START LINE!";
            hudPopupTicks = 65;

            rivals.Clear();
            rivals.Add(new RivalBiker { Name = "Raju (Pulsar 220)", BikeColor = Color.FromArgb(239, 68, 68), SuitColor = Color.FromArgb(30, 41, 59), TrackPos = 0.45f, LaneX = -0.35f, TargetLaneX = -0.35f, Speed = 0.043f });
            rivals.Add(new RivalBiker { Name = "Kiran (Yamaha R15)", BikeColor = Color.FromArgb(37, 99, 235), SuitColor = Color.FromArgb(15, 23, 42), TrackPos = 0.85f, LaneX = 0.35f, TargetLaneX = 0.35f, Speed = 0.048f });
            rivals.Add(new RivalBiker { Name = "Vamsi (KTM Duke)", BikeColor = Color.FromArgb(249, 115, 22), SuitColor = Color.FromArgb(24, 24, 27), TrackPos = 1.35f, LaneX = 0.0f, TargetLaneX = -0.25f, Speed = 0.053f });

            hazards.Clear();
            hazards.Add(new RoadHazard { Type = "scooter", MainColor = Color.FromArgb(100, 116, 139), TrackPos = 3.2f, LaneX = -0.58f });
            hazards.Add(new RoadHazard { Type = "car", MainColor = Color.FromArgb(248, 250, 252), TrackPos = 6.5f, LaneX = 0.55f });
            hazards.Add(new RoadHazard { Type = "car", MainColor = Color.FromArgb(148, 163, 184), TrackPos = 9.8f, LaneX = -0.45f });
            hazards.Add(new RoadHazard { Type = "scooter", MainColor = Color.FromArgb(220, 38, 38), TrackPos = 12.6f, LaneX = 0.52f });
            hazards.Add(new RoadHazard { Type = "car", MainColor = Color.FromArgb(241, 245, 249), TrackPos = 14.8f, LaneX = -0.50f });

            raceStopwatch.Reset();
        }

        private Button CreateStyledButton(string text, int width, Color bg, Color fg)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 32;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(60, 75, 95);
            b.BackColor = bg;
            b.ForeColor = fg;
            b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            return b;
        }

        private void InitUI()
        {
            this.Text = "Khammam Street Bike Racer — Dwaraka Nagar Track (v" + localVersion + ")";
            this.Size = new Size(1240, 800);
            this.MinimumSize = new Size(960, 640);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42);
            this.ForeColor = Color.White;
            this.KeyPreview = true;

            // Top Header Bar
            topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 58;
            topBar.BackColor = Color.FromArgb(15, 23, 42);
            topBar.Padding = new Padding(12, 6, 12, 6);

            lblTitle = new Label();
            lblTitle.Text = "🏍️ KHAMMAM STREET BIKE RACER — Dwaraka Nagar Track (Start → Ma Illu → Finish)";
            lblTitle.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(248, 250, 252);
            lblTitle.Location = new Point(14, 6);
            lblTitle.AutoSize = true;
            topBar.Controls.Add(lblTitle);

            lblSubtitle = new Label();
            lblSubtitle.Text = "CONTROLS: [W / Up] Gas  •  [S / Down] Brake  •  [A / D or Left / Right] Steer Bike  •  [SPACE / SHIFT] Nitro Boost 🔥";
            lblSubtitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblSubtitle.ForeColor = Color.FromArgb(56, 189, 248);
            lblSubtitle.Location = new Point(14, 31);
            lblSubtitle.AutoSize = true;
            topBar.Controls.Add(lblSubtitle);

            FlowLayoutPanel rightTopFlow = new FlowLayoutPanel();
            rightTopFlow.Dock = DockStyle.Right;
            rightTopFlow.AutoSize = true;
            rightTopFlow.FlowDirection = FlowDirection.LeftToRight;
            rightTopFlow.WrapContents = false;
            rightTopFlow.Padding = new Padding(0, 6, 0, 0);

            lblUpdateBadge = new Label();
            lblUpdateBadge.Text = "v" + localVersion + " • Ready";
            lblUpdateBadge.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblUpdateBadge.ForeColor = Color.FromArgb(52, 211, 153);
            lblUpdateBadge.BackColor = Color.FromArgb(6, 78, 59);
            lblUpdateBadge.TextAlign = ContentAlignment.MiddleCenter;
            lblUpdateBadge.AutoSize = false;
            lblUpdateBadge.Size = new Size(150, 32);
            lblUpdateBadge.Margin = new Padding(4, 0, 6, 0);
            rightTopFlow.Controls.Add(lblUpdateBadge);

            btnCheckUpdate = CreateStyledButton("🔄 Check Update", 120, Color.FromArgb(30, 41, 59), Color.FromArgb(56, 189, 248));
            btnCheckUpdate.Click += (s, e) => CheckForGitHubUpdates(true);
            rightTopFlow.Controls.Add(btnCheckUpdate);

            btnRepoConfig = CreateStyledButton("⚙️ GitHub", 90, Color.FromArgb(30, 41, 59), Color.FromArgb(226, 232, 240));
            btnRepoConfig.Click += (s, e) => ShowGitHubConfigDialog();
            rightTopFlow.Controls.Add(btnRepoConfig);

            btnFullscreen = CreateStyledButton("⛶ F11", 65, Color.FromArgb(30, 41, 59), Color.White);
            btnFullscreen.Click += (s, e) => ToggleFullscreen();
            rightTopFlow.Controls.Add(btnFullscreen);

            topBar.Controls.Add(rightTopFlow);

            // Bottom Control Bar
            bottomBar = new Panel();
            bottomBar.Dock = DockStyle.Bottom;
            bottomBar.Height = 54;
            bottomBar.BackColor = Color.FromArgb(15, 23, 42);
            bottomBar.Padding = new Padding(12, 8, 12, 8);

            FlowLayoutPanel leftBottomFlow = new FlowLayoutPanel();
            leftBottomFlow.Dock = DockStyle.Left;
            leftBottomFlow.AutoSize = true;
            leftBottomFlow.WrapContents = false;

            btnStartRace = CreateStyledButton("🏁 Restart Race (R)", 150, Color.FromArgb(220, 38, 38), Color.White);
            btnStartRace.Click += (s, e) => { StartNewRace(); this.Focus(); };
            leftBottomFlow.Controls.Add(btnStartRace);

            btnCameraMode = CreateStyledButton("🎥 Cam: 3rd-Person Bike (C)", 190, Color.FromArgb(217, 119, 6), Color.White);
            btnCameraMode.Click += (s, e) =>
            {
                thirdPersonCamera = !thirdPersonCamera;
                btnCameraMode.Text = thirdPersonCamera ? "🎥 Cam: 3rd-Person Bike (C)" : "🎥 Cam: 1st-Person Cockpit (C)";
                this.Focus();
            };
            leftBottomFlow.Controls.Add(btnCameraMode);

            Button btnJumpMaIllu = CreateStyledButton("📍 Jump to Ma Illu (13)", 160, Color.FromArgb(37, 99, 235), Color.White);
            btnJumpMaIllu.Click += (s, e) =>
            {
                playerTrackPos = 12.0f;
                playerSpeedKmh = 0f;
                lookDir = "straight";
                hudPopupText = "📍 STOPPED AT MA ILLU (17.256749° N, 80.158489° E)";
                hudPopupTicks = 60;
                this.Focus();
            };
            leftBottomFlow.Controls.Add(btnJumpMaIllu);

            bottomBar.Controls.Add(leftBottomFlow);

            FlowLayoutPanel rightBottomFlow = new FlowLayoutPanel();
            rightBottomFlow.Dock = DockStyle.Right;
            rightBottomFlow.AutoSize = true;
            rightBottomFlow.WrapContents = false;

            btnLookMaIllu = CreateStyledButton("🏠 Look Left (Ma Illu Close-Up)", 200, Color.FromArgb(30, 41, 59), Color.White);
            btnLookMaIllu.Click += (s, e) => { lookDir = "left"; playerSpeedKmh = 0f; this.Focus(); };
            rightBottomFlow.Controls.Add(btnLookMaIllu);

            btnLookStraight = CreateStyledButton("🛣️ Race Track View", 140, Color.FromArgb(5, 150, 105), Color.White);
            btnLookStraight.Click += (s, e) => { lookDir = "straight"; this.Focus(); };
            rightBottomFlow.Controls.Add(btnLookStraight);

            btnLookRight = CreateStyledButton("🏛️ Look Right (Opposite House)", 200, Color.FromArgb(30, 41, 59), Color.White);
            btnLookRight.Click += (s, e) => { lookDir = "right"; playerSpeedKmh = 0f; this.Focus(); };
            rightBottomFlow.Controls.Add(btnLookRight);

            bottomBar.Controls.Add(rightBottomFlow);

            // Center Viewport
            viewport = new ViewportPanel();
            viewport.Dock = DockStyle.Fill;
            viewport.Paint += Viewport_Paint;
            viewport.MouseClick += (s, e) => this.Focus();

            this.Controls.Add(viewport);
            this.Controls.Add(topBar);
            this.Controls.Add(bottomBar);

            // 30 FPS Game Loop Timer
            gameLoopTimer = new System.Windows.Forms.Timer();
            gameLoopTimer.Interval = 33;
            gameLoopTimer.Tick += GameLoop_Tick;
            gameLoopTimer.Start();

            this.KeyDown += MainForm_KeyDown;
            this.KeyUp += MainForm_KeyUp;
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.W || e.KeyCode == Keys.Up) { keyGas = true; lookDir = "straight"; }
            if (e.KeyCode == Keys.S || e.KeyCode == Keys.Down) keyBrake = true;
            if (e.KeyCode == Keys.A || e.KeyCode == Keys.Left) keyLeft = true;
            if (e.KeyCode == Keys.D || e.KeyCode == Keys.Right) keyRight = true;
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.ShiftKey) keyNitro = true;

            if (e.KeyCode == Keys.R) StartNewRace();
            if (e.KeyCode == Keys.C) btnCameraMode.PerformClick();
            if (e.KeyCode == Keys.F11) ToggleFullscreen();
            if (e.KeyCode == Keys.Escape && isFullscreen) ToggleFullscreen();
        }

        private void MainForm_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.W || e.KeyCode == Keys.Up) keyGas = false;
            if (e.KeyCode == Keys.S || e.KeyCode == Keys.Down) keyBrake = false;
            if (e.KeyCode == Keys.A || e.KeyCode == Keys.Left) keyLeft = false;
            if (e.KeyCode == Keys.D || e.KeyCode == Keys.Right) keyRight = false;
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.ShiftKey) keyNitro = false;
        }

        private void GameLoop_Tick(object sender, EventArgs e)
        {
            if (hudPopupTicks > 0) hudPopupTicks--;
            if (screenShakeTicks > 0) screenShakeTicks--;

            // 1. Handle Race Countdown (3.. 2.. 1.. GO!)
            if (raceActive && countdownValue > 0)
            {
                countdownTicks--;
                if (countdownTicks <= 0)
                {
                    countdownValue--;
                    countdownTicks = 28;
                    if (countdownValue == 0)
                    {
                        raceStopwatch.Start();
                        hudPopupText = "🟢 GO! GO! GO! RACE TO ROAD END!";
                        hudPopupTicks = 45;
                    }
                }
                viewport.Invalidate();
                return;
            }

            // 2. Player Bike Physics when racing
            if (lookDir == "straight" && !raceFinished)
            {
                isNitroActive = keyNitro && nitroAmount > 2f && keyGas;
                float maxSpd = isNitroActive ? 152f : 112f;
                float accel = isNitroActive ? 4.2f : 2.1f;

                if (isNitroActive)
                {
                    nitroAmount = Math.Max(0f, nitroAmount - 1.1f);
                }
                else if (nitroAmount < 100f)
                {
                    nitroAmount = Math.Min(100f, nitroAmount + 0.25f);
                }

                if (keyGas)
                {
                    playerSpeedKmh = Math.Min(maxSpd, playerSpeedKmh + accel);
                }
                else if (keyBrake)
                {
                    playerSpeedKmh = Math.Max(0f, playerSpeedKmh - 4.5f);
                }
                else
                {
                    playerSpeedKmh = Math.Max(0f, playerSpeedKmh - 1.2f);
                }

                // Steering & Bike Lean Angle
                if (keyLeft)
                {
                    playerLaneX = Math.Max(-0.82f, playerLaneX - 0.038f);
                    playerLeanAngle = Math.Max(-24f, playerLeanAngle - 3.2f);
                }
                else if (keyRight)
                {
                    playerLaneX = Math.Min(0.82f, playerLaneX + 0.038f);
                    playerLeanAngle = Math.Min(24f, playerLeanAngle + 3.2f);
                }
                else
                {
                    playerLeanAngle *= 0.78f;
                }

                // Advance along the 18 street segments
                float stepDelta = (playerSpeedKmh / 110f) * 0.052f;
                playerTrackPos += stepDelta;

                // Ma Illu Checkpoint Bonus at Step 13 (index 12.0)
                if (!maIlluBonusAwarded && playerTrackPos >= 11.8f)
                {
                    maIlluBonusAwarded = true;
                    score += 500;
                    nitroAmount = 100f;
                    hudPopupText = "🏠 MA ILLU CHECKPOINT PASSED! +500 PTS & FULL NITRO!";
                    hudPopupTicks = 65;
                }

                // Update AI Rival Bikers
                foreach (RivalBiker r in rivals)
                {
                    if (r.TrackPos < 17.0f)
                    {
                        r.TrackPos += r.Speed;
                        if (Math.Abs(r.LaneX - r.TargetLaneX) < 0.05f)
                        {
                            r.TargetLaneX = (float)(rng.NextDouble() * 1.3 - 0.65);
                        }
                        r.LaneX += (r.TargetLaneX - r.LaneX) * 0.04f;

                        // Check Overtake
                        if (!r.PassedPlayer && playerTrackPos > r.TrackPos)
                        {
                            r.PassedPlayer = true;
                            score += 250;
                            hudPopupText = "⚡ OVERTOOK " + r.Name.ToUpper() + "! +250 PTS";
                            hudPopupTicks = 45;
                        }
                        else if (r.PassedPlayer && playerTrackPos < r.TrackPos - 0.2f)
                        {
                            r.PassedPlayer = false;
                        }

                        // Check Bike-to-Bike Bump
                        if (Math.Abs(r.TrackPos - playerTrackPos) < 0.14f && Math.Abs(r.LaneX - playerLaneX) < 0.22f)
                        {
                            playerSpeedKmh *= 0.65f;
                            screenShakeTicks = 8;
                            hudPopupText = "💥 BUMPED RIVAL BIKER! STEER CLEAR!";
                            hudPopupTicks = 30;
                        }
                    }
                }

                // Check Parked Hazard Collisions
                foreach (RoadHazard h in hazards)
                {
                    if (Math.Abs(h.TrackPos - playerTrackPos) < 0.14f && Math.Abs(h.LaneX - playerLaneX) < 0.26f)
                    {
                        playerSpeedKmh = Math.Min(playerSpeedKmh, 28f);
                        screenShakeTicks = 10;
                        hudPopupText = "⚠️ WATCH OUT FOR PARKED VEHICLES!";
                        hudPopupTicks = 30;
                    }
                }

                // Check Finish Line (Step 18 = index 17.0)
                if (playerTrackPos >= 17.0f)
                {
                    playerTrackPos = 17.0f;
                    playerSpeedKmh = 0f;
                    raceFinished = true;
                    raceStopwatch.Stop();
                    double sec = raceStopwatch.Elapsed.TotalSeconds;
                    if (bestRaceTimeSec <= 0 || sec < bestRaceTimeSec) bestRaceTimeSec = sec;
                    int pos = GetPlayerRacePosition();
                    score += (pos == 1) ? 1000 : 400;
                    hudPopupText = "🏆 FINISHED " + GetOrdinal(pos) + "! TIME: " + sec.ToString("0.00") + "s  (Press R to Race Again)";
                    hudPopupTicks = 200;
                }
            }

            viewport.Invalidate();
        }

        private int GetPlayerRacePosition()
        {
            int pos = 1;
            foreach (RivalBiker r in rivals)
            {
                if (r.TrackPos > playerTrackPos) pos++;
            }
            return pos;
        }

        private string GetOrdinal(int n)
        {
            if (n == 1) return "1ST PLACE 🥇";
            if (n == 2) return "2ND PLACE 🥈";
            if (n == 3) return "3RD PLACE 🥉";
            return n + "TH PLACE";
        }

        private void Viewport_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            int vw = viewport.Width;
            int vh = viewport.Height;

            if (screenShakeTicks > 0)
            {
                g.TranslateTransform(rng.Next(-6, 7), rng.Next(-5, 6));
            }

            int stepIdx = Math.Max(0, Math.Min(17, (int)Math.Floor(playerTrackPos)));
            float frac = playerTrackPos - stepIdx; // 0.0 to 0.999 continuous interpolation within step!
            StepInfo curStep = steps[stepIdx];

            // 1. Draw Realistic Street Background with Continuous Forward Zoom & Steering Pan
            Image bgImg = stepImages[stepIdx];
            bool cropBottom = curStep.CropBottom;

            if (lookDir == "left")
            {
                bgImg = imgHouseLeft;
                cropBottom = false;
            }
            else if (lookDir == "right")
            {
                bgImg = imgHouseRight;
                cropBottom = true;
            }

            if (bgImg != null)
            {
                int srcW = bgImg.Width;
                int srcH = cropBottom ? (int)(bgImg.Height * 0.78f) : bgImg.Height;

                // Continuous zoom-in as bike rides forward through this segment (1.02x -> 1.28x)
                float zoomFactor = (lookDir == "straight") ? (1.02f + frac * 0.26f) : 1.02f;
                float baseScale = Math.Max((float)vw / srcW, (float)vh / srcH) * zoomFactor;

                int drawW = (int)(srcW * baseScale);
                int drawH = (int)(srcH * baseScale);

                // Pan camera slightly opposite to bike lane steering
                int steerPanX = (lookDir == "straight") ? (int)(-playerLaneX * 85f) : 0;
                int drawX = (vw - drawW) / 2 + steerPanX;
                int drawY = (vh - drawH) / 2;

                g.DrawImage(bgImg, new Rectangle(drawX, drawY, drawW, drawH), new Rectangle(0, 0, srcW, srcH), GraphicsUnit.Pixel);
            }

            // 2. Draw Perspective Racing Lane Guides & Speed Strips on Road Surface
            if (lookDir == "straight")
            {
                DrawRoadSpeedOverlay(g, vw, vh);
                DrawHazardsAndRivals(g, vw, vh);

                if (thirdPersonCamera)
                {
                    DrawThirdPersonRacingBike(g, vw, vh);
                }
                else
                {
                    DrawFirstPersonCockpit(g, vw, vh);
                }
            }

            // 3. Draw Racing HUD (Position, Speedometer, Nitro Bar, Lap Timer, Track Mini-Bar)
            DrawRacingHUD(g, vw, vh, curStep, stepIdx + 1);
        }

        private void DrawRoadSpeedOverlay(Graphics g, int vw, int vh)
        {
            int horizonY = (int)(vh * 0.46f);
            int vanishX = vw / 2 - (int)(playerLaneX * 55f);

            // Subtle dark asphalt racing track overlay at bottom so rivals & bike pop clearly
            Point[] roadPoly = new Point[]
            {
                new Point(vanishX - 95, horizonY),
                new Point(vanishX + 95, horizonY),
                new Point(vw + 140, vh),
                new Point(-140, vh)
            };
            using (LinearGradientBrush roadBrush = new LinearGradientBrush(
                new Point(0, horizonY), new Point(0, vh),
                Color.FromArgb(10, 15, 23, 42), Color.FromArgb(135, 15, 23, 42)))
            {
                g.FillPolygon(roadBrush, roadPoly);
            }

            // Animated dashed center line & racing curb markers
            float offset = (playerTrackPos * 10f) % 1.0f;
            using (Pen dashPen = new Pen(Color.FromArgb(150, 255, 255, 255), 3f))
            using (Pen edgePen = new Pen(Color.FromArgb(120, 16, 185, 129), 3f))
            {
                g.DrawLine(edgePen, vanishX - 90, horizonY, (int)(vw * 0.06f), vh);
                g.DrawLine(edgePen, vanishX + 90, horizonY, (int)(vw * 0.94f), vh);

                for (int i = 0; i < 6; i++)
                {
                    float t1 = (i + (1f - offset)) / 6f;
                    float t2 = Math.Min(1f, t1 + 0.08f);
                    float p1 = t1 * t1;
                    float p2 = t2 * t2;

                    int y1 = horizonY + (int)(p1 * (vh - horizonY));
                    int y2 = horizonY + (int)(p2 * (vh - horizonY));
                    int x1 = vanishX + (int)((vw / 2 - vanishX) * p1);
                    int x2 = vanishX + (int)((vw / 2 - vanishX) * p2);
                    g.DrawLine(dashPen, x1, y1, x2, y2);
                }
            }
        }

        private void DrawHazardsAndRivals(Graphics g, int vw, int vh)
        {
            int horizonY = (int)(vh * 0.45f);
            int vanishX = vw / 2 - (int)(playerLaneX * 55f);

            // Draw Parked Hazards ahead
            foreach (RoadHazard h in hazards)
            {
                float relDist = h.TrackPos - playerTrackPos;
                if (relDist > 0.05f && relDist < 2.5f)
                {
                    float norm = 1f - (relDist / 2.5f); // 0 at horizon, 1 at player
                    float persp = norm * norm;
                    int sy = horizonY + (int)(persp * (vh - horizonY - 55));
                    int roadHalfW = 75 + (int)(persp * (vw * 0.42f));
                    int sx = vanishX + (int)((h.LaneX - playerLaneX * 0.35f) * roadHalfW);
                    float scale = 0.25f + persp * 1.15f;

                    DrawObstacleSprite(g, sx, sy, scale, h.Type, h.MainColor);
                }
            }

            // Draw AI Rival Bikers ahead
            foreach (RivalBiker r in rivals)
            {
                float relDist = r.TrackPos - playerTrackPos;
                if (relDist > -0.15f && relDist < 3.0f)
                {
                    float norm = 1f - Math.Max(0f, relDist / 3.0f);
                    float persp = norm * norm;
                    int sy = horizonY + (int)(persp * (vh - horizonY - 70));
                    int roadHalfW = 70 + (int)(persp * (vw * 0.40f));
                    int sx = vanishX + (int)((r.LaneX - playerLaneX * 0.35f) * roadHalfW);
                    float scale = 0.22f + persp * 1.05f;

                    DrawBikerSprite(g, sx, sy, scale, 0f, r.BikeColor, r.SuitColor, r.Name, false);
                }
            }
        }

        private void DrawObstacleSprite(Graphics g, int cx, int cy, float scale, string type, Color color)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(scale, scale);

            if (type == "car")
            {
                using (SolidBrush bodyB = new SolidBrush(color))
                using (SolidBrush glassB = new SolidBrush(Color.FromArgb(30, 41, 59)))
                using (Pen darkP = new Pen(Color.FromArgb(51, 65, 85), 2f))
                {
                    g.FillRectangle(Brushes.Black, -48, 12, 20, 14);
                    g.FillRectangle(Brushes.Black, 28, 12, 20, 14);
                    g.FillRectangle(bodyB, -52, -16, 104, 32);
                    g.DrawRectangle(darkP, -52, -16, 104, 32);
                    g.FillPolygon(bodyB, new Point[] { new Point(-44, -16), new Point(-32, -40), new Point(32, -40), new Point(44, -16) });
                    g.FillPolygon(glassB, new Point[] { new Point(-38, -18), new Point(-28, -36), new Point(28, -36), new Point(38, -18) });
                    g.FillRectangle(Brushes.Crimson, -48, -10, 18, 8);
                    g.FillRectangle(Brushes.Crimson, 30, -10, 18, 8);
                }
            }
            else
            {
                using (SolidBrush bodyB = new SolidBrush(color))
                {
                    g.FillEllipse(Brushes.Black, -8, 0, 16, 26);
                    g.FillRectangle(bodyB, -16, -22, 32, 26);
                    g.FillEllipse(Brushes.DarkSlateGray, -12, -44, 24, 24);
                }
            }

            g.Restore(st);
        }

        private void DrawThirdPersonRacingBike(Graphics g, int vw, int vh)
        {
            int bikeX = vw / 2 + (int)(playerLaneX * (vw * 0.28f));
            int bikeY = vh - 78;
            DrawBikerSprite(g, bikeX, bikeY, 1.35f, playerLeanAngle, Color.FromArgb(220, 38, 38), Color.FromArgb(15, 23, 42), "YOU (MA ILLU RACER)", isNitroActive);
        }

        private void DrawBikerSprite(Graphics g, int cx, int cy, float scale, float leanDeg, Color bikeColor, Color suitColor, string label, bool nitroFlame)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(scale, scale);

            // Ground shadow
            using (SolidBrush shadowB = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            {
                g.FillEllipse(shadowB, -36, 22, 72, 18);
            }

            g.RotateTransform(leanDeg);

            // Rear Tire
            using (SolidBrush tireB = new SolidBrush(Color.FromArgb(20, 20, 25)))
            using (Pen rimP = new Pen(bikeColor, 2.5f))
            {
                g.FillRoundRect(tireB, -14, -8, 28, 42, 8);
                g.DrawRectangle(rimP, -10, -4, 20, 34);
            }

            // Dual Exhaust Pipes & Nitro Flames
            using (SolidBrush pipeB = new SolidBrush(Color.FromArgb(71, 85, 105)))
            {
                g.FillRectangle(pipeB, -22, 4, 7, 16);
                g.FillRectangle(pipeB, 15, 4, 7, 16);
            }
            if (nitroFlame)
            {
                using (SolidBrush outerFlame = new SolidBrush(Color.FromArgb(220, 56, 189, 248)))
                using (SolidBrush innerFlame = new SolidBrush(Color.FromArgb(255, 250, 204, 21)))
                {
                    int fh = rng.Next(22, 38);
                    g.FillPolygon(outerFlame, new Point[] { new Point(-23, 18), new Point(-14, 18), new Point(-18, 18 + fh) });
                    g.FillPolygon(outerFlame, new Point[] { new Point(14, 18), new Point(23, 18), new Point(18, 18 + fh) });
                    g.FillPolygon(innerFlame, new Point[] { new Point(-21, 18), new Point(-16, 18), new Point(-18, 18 + fh / 2) });
                    g.FillPolygon(innerFlame, new Point[] { new Point(16, 18), new Point(21, 18), new Point(18, 18 + fh / 2) });
                }
            }

            // Motorcycle Rear Tail & Fairing
            using (SolidBrush bikeB = new SolidBrush(bikeColor))
            using (SolidBrush suitB = new SolidBrush(suitColor))
            {
                Point[] tail = new Point[] { new Point(-22, -12), new Point(-16, -42), new Point(16, -42), new Point(22, -12) };
                g.FillPolygon(bikeB, tail);

                // LED Taillight
                g.FillRectangle(keyBrake ? Brushes.Red : Brushes.Crimson, -10, -16, 20, 6);

                // Rider Torso & Racing Jacket
                Point[] torso = new Point[] { new Point(-20, -38), new Point(-26, -78), new Point(26, -78), new Point(20, -38) };
                g.FillPolygon(suitB, torso);

                // Racing Stripe on Jacket
                g.FillRectangle(bikeB, -6, -76, 12, 34);

                // Handlebars & Mirrors
                using (Pen barP = new Pen(Color.Silver, 3.5f))
                {
                    g.DrawLine(barP, -36, -68, -20, -64);
                    g.DrawLine(barP, 36, -68, 20, -64);
                }

                // Rider Helmet
                g.FillEllipse(bikeB, -16, -104, 32, 32);
                g.FillRectangle(Brushes.Black, -12, -94, 24, 8);
            }

            // Rider Name Tag above helmet
            using (Font fTag = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (SolidBrush tagBg = new SolidBrush(Color.FromArgb(180, 15, 23, 42)))
            {
                SizeF sz = g.MeasureString(label, fTag);
                g.FillRectangle(tagBg, -sz.Width / 2f - 4, -124, sz.Width + 8, 16);
                g.DrawString(label, fTag, Brushes.White, -sz.Width / 2f, -123);
            }

            g.Restore(st);
        }

        private void DrawFirstPersonCockpit(Graphics g, int vw, int vh)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(vw / 2f, vh + 10f);
            g.RotateTransform(playerLeanAngle * 0.55f);

            using (Pen stemPen = new Pen(Color.FromArgb(30, 41, 59), 10f))
            {
                g.DrawLine(stemPen, -110, -45, -210, -115);
                g.DrawLine(stemPen, 110, -45, 210, -115);
            }

            using (SolidBrush mirrorShell = new SolidBrush(Color.FromArgb(15, 23, 42)))
            using (SolidBrush mirrorGlass = new SolidBrush(Color.FromArgb(120, 56, 189, 248)))
            using (Pen mirrorBorder = new Pen(Color.FromArgb(71, 85, 105), 3f))
            {
                g.FillEllipse(mirrorShell, new Rectangle(-275, -148, 92, 46));
                g.DrawEllipse(mirrorBorder, new Rectangle(-275, -148, 92, 46));
                g.FillEllipse(mirrorGlass, new Rectangle(-269, -143, 80, 36));

                g.FillEllipse(mirrorShell, new Rectangle(183, -148, 92, 46));
                g.DrawEllipse(mirrorBorder, new Rectangle(183, -148, 92, 46));
                g.FillEllipse(mirrorGlass, new Rectangle(189, -143, 80, 36));
            }

            using (Pen barPen = new Pen(Color.FromArgb(51, 65, 85), 16f))
            using (Pen gripPen = new Pen(Color.FromArgb(15, 23, 42), 22f))
            {
                g.DrawLine(barPen, -60, -36, -185, -62);
                g.DrawLine(barPen, 60, -36, 185, -62);
                g.DrawLine(gripPen, -175, -60, -255, -72);
                g.DrawLine(gripPen, 175, -60, 255, -72);
            }

            Point[] fairing = new Point[] { new Point(-105, 0), new Point(-82, -88), new Point(0, -102), new Point(82, -88), new Point(105, 0) };
            using (SolidBrush fairingBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
            {
                g.FillPolygon(fairingBrush, fairing);
            }

            Rectangle lcd = new Rectangle(-68, -82, 136, 52);
            using (SolidBrush lcdBg = new SolidBrush(Color.FromArgb(2, 44, 34)))
            using (Pen lcdBorder = new Pen(Color.FromArgb(16, 185, 129), 2f))
            using (Font fSpeed = new Font("Consolas", 15f, FontStyle.Bold))
            {
                g.FillRectangle(lcdBg, lcd);
                g.DrawRectangle(lcdBorder, lcd);
                string spdText = Math.Round(playerSpeedKmh) + " KM/H";
                SizeF sz = g.MeasureString(spdText, fSpeed);
                g.DrawString(spdText, fSpeed, Brushes.SpringGreen, -sz.Width / 2f, -72);
            }

            using (SolidBrush tankBrush = new SolidBrush(Color.FromArgb(220, 38, 38)))
            {
                g.FillEllipse(tankBrush, new Rectangle(-75, -24, 150, 48));
            }

            g.Restore(state);
        }

        private void DrawRacingHUD(Graphics g, int vw, int vh, StepInfo curStep, int stepNumber)
        {
            // 1. Top-Left Race Telemetry Card (Position, Time, Score, Location)
            using (SolidBrush hudBg = new SolidBrush(Color.FromArgb(205, 15, 23, 42)))
            using (Pen hudBorder = new Pen(Color.FromArgb(140, 56, 189, 248), 1.5f))
            using (Font fBig = new Font("Segoe UI", 14f, FontStyle.Bold))
            using (Font fMed = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            {
                g.FillRectangle(hudBg, 16, 14, 490, 76);
                g.DrawRectangle(hudBorder, 16, 14, 490, 76);

                int pos = GetPlayerRacePosition();
                g.DrawString("POS: " + pos + " / 4", fBig, Brushes.Gold, 26, 20);
                g.DrawString("TIME: " + raceStopwatch.Elapsed.TotalSeconds.ToString("00.00") + "s", fBig, Brushes.SpringGreen, 165, 20);
                g.DrawString("SCORE: " + score, fBig, Brushes.Cyan, 345, 20);

                g.DrawString("📍 STEP " + stepNumber + "/18: " + curStep.Label + " (" + curStep.Coords + ")", fMed, Brushes.White, 26, 56);
            }

            // 2. Top-Right Speedometer & Nitro Boost Bar
            using (SolidBrush hudBg = new SolidBrush(Color.FromArgb(205, 15, 23, 42)))
            using (Pen hudBorder = new Pen(Color.FromArgb(140, 249, 115, 22), 1.5f))
            using (Font fSpd = new Font("Consolas", 18f, FontStyle.Bold))
            using (Font fSmall = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            {
                int rx = vw - 265;
                g.FillRectangle(hudBg, rx, 14, 248, 76);
                g.DrawRectangle(hudBorder, rx, 14, 248, 76);

                int kmh = (int)Math.Round(playerSpeedKmh);
                g.DrawString(kmh + " KM/H", fSpd, isNitroActive ? Brushes.DeepSkyBlue : Brushes.SpringGreen, rx + 14, 18);

                g.DrawString("NITRO [SPACE]", fSmall, Brushes.Orange, rx + 14, 52);
                g.FillRectangle(Brushes.DarkSlateGray, rx + 112, 54, 120, 14);
                int nw = (int)(1.2f * nitroAmount);
                using (SolidBrush nb = new SolidBrush(isNitroActive ? Color.DeepSkyBlue : Color.OrangeRed))
                {
                    g.FillRectangle(nb, rx + 112, 54, nw, 14);
                }
                g.DrawRectangle(Pens.White, rx + 112, 54, 120, 14);
            }

            // 3. Track Progress Minimap Bar (Start -> Ma Illu Pin -> Finish)
            int barW = 420;
            int barX = (vw - barW) / 2;
            int barY = 20;
            using (SolidBrush barBg = new SolidBrush(Color.FromArgb(195, 15, 23, 42)))
            using (Font fMini = new Font("Segoe UI", 8f, FontStyle.Bold))
            {
                g.FillRectangle(barBg, barX - 10, barY - 6, barW + 20, 42);
                g.FillRectangle(Brushes.SlateGray, barX, barY + 14, barW, 8);

                float prog = Math.Min(1f, playerTrackPos / 17.0f);
                g.FillRectangle(Brushes.SpringGreen, barX, barY + 14, (int)(barW * prog), 8);

                // Ma Illu Pin marker at 12/17
                int maIlluX = barX + (int)(barW * (12.0f / 17.0f));
                g.FillEllipse(Brushes.Gold, maIlluX - 5, barY + 10, 12, 16);
                g.DrawString("🏠 Ma Illu", fMini, Brushes.Gold, maIlluX - 24, barY - 4);
                g.DrawString("START", fMini, Brushes.White, barX, barY - 4);
                g.DrawString("FINISH 🏁", fMini, Brushes.White, barX + barW - 48, barY - 4);

                // Player dot on minimap
                int px = barX + (int)(barW * prog);
                g.FillEllipse(Brushes.Red, px - 6, barY + 11, 14, 14);
                g.DrawEllipse(Pens.White, px - 6, barY + 11, 14, 14);
            }

            // 4. Countdown or Event Popup Banner in Center
            if (raceActive && countdownValue > 0)
            {
                using (SolidBrush cdBg = new SolidBrush(Color.FromArgb(210, 15, 23, 42)))
                using (Font fCd = new Font("Segoe UI", 42f, FontStyle.Bold))
                {
                    string cText = "RACE STARTS IN " + countdownValue + "...";
                    SizeF sz = g.MeasureString(cText, fCd);
                    g.FillRectangle(cdBg, (vw - sz.Width) / 2f - 24, vh / 2f - 60, sz.Width + 48, 100);
                    g.DrawString(cText, fCd, Brushes.Gold, (vw - sz.Width) / 2f, vh / 2f - 50);
                }
            }
            else if (hudPopupTicks > 0 && !string.IsNullOrEmpty(hudPopupText))
            {
                using (SolidBrush popBg = new SolidBrush(Color.FromArgb(215, 15, 23, 42)))
                using (Pen popPen = new Pen(Color.Gold, 2f))
                using (Font fPop = new Font("Segoe UI", 14f, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(hudPopupText, fPop);
                    RectangleF r = new RectangleF((vw - sz.Width) / 2f - 20, 102, sz.Width + 40, 42);
                    g.FillRectangle(popBg, r);
                    g.DrawRectangle(popPen, r.X, r.Y, r.Width, r.Height);
                    g.DrawString(hudPopupText, fPop, Brushes.Yellow, (vw - sz.Width) / 2f, 109);
                }
            }
        }

        private void ToggleFullscreen()
        {
            if (!isFullscreen)
            {
                prevWindowState = this.WindowState;
                prevBorderStyle = this.FormBorderStyle;
                prevBounds = this.Bounds;
                this.WindowState = FormWindowState.Normal;
                this.FormBorderStyle = FormBorderStyle.None;
                this.Bounds = Screen.FromControl(this).Bounds;
                isFullscreen = true;
            }
            else
            {
                this.FormBorderStyle = prevBorderStyle;
                this.WindowState = prevWindowState;
                this.Bounds = prevBounds;
                isFullscreen = false;
            }
        }

        // =====================================================================
        // GITHUB AUTO-UPDATER ENGINE
        // =====================================================================
        private void SetUpdateBadge(string text, Color fg, Color bg)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetUpdateBadge(text, fg, bg)));
                return;
            }
            lblUpdateBadge.Text = text;
            lblUpdateBadge.ForeColor = fg;
            lblUpdateBadge.BackColor = bg;
        }

        private void CheckForGitHubUpdates(bool manualClick)
        {
            if (isCheckingUpdate) return;
            LoadConfigAndVersion();

            if (string.IsNullOrEmpty(ghOwner))
            {
                SetUpdateBadge("v" + localVersion + " • Set GitHub", Color.FromArgb(253, 224, 71), Color.FromArgb(66, 32, 6));
                if (manualClick) ShowGitHubConfigDialog();
                return;
            }

            isCheckingUpdate = true;
            SetUpdateBadge("🔄 Checking GitHub...", Color.FromArgb(125, 211, 252), Color.FromArgb(12, 74, 110));

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    string rawBase = "https://raw.githubusercontent.com/" + ghOwner + "/" + ghRepo + "/" + ghBranch;
                    string verUrl = rawBase + "/version.json?t=" + DateTime.UtcNow.Ticks;

                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "KhammamStreetRacer-AutoUpdater/1.0");
                        wc.Headers.Add("Cache-Control", "no-cache");
                        string remoteJson = wc.DownloadString(verUrl);

                        Match mVer = Regex.Match(remoteJson, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                        Match mNotes = Regex.Match(remoteJson, "\"notes\"\\s*:\\s*\"([^\"]*)\"");
                        if (!mVer.Success)
                        {
                            SetUpdateBadge("v" + localVersion + " • Up to date", Color.FromArgb(52, 211, 153), Color.FromArgb(6, 78, 59));
                            isCheckingUpdate = false;
                            return;
                        }

                        string remoteVersion = mVer.Groups[1].Value.Trim();
                        string releaseNotes = mNotes.Success ? mNotes.Groups[1].Value : "New Bike Race Game update pushed to GitHub.";

                        if (IsNewerVersion(remoteVersion, localVersion))
                        {
                            SetUpdateBadge("⬇️ Updating to v" + remoteVersion + "...", Color.FromArgb(253, 224, 71), Color.FromArgb(66, 32, 6));
                            DownloadAndApplyUpdate(rawBase, remoteVersion, remoteJson, releaseNotes);
                        }
                        else
                        {
                            SetUpdateBadge("✅ v" + localVersion + " • Latest", Color.FromArgb(52, 211, 153), Color.FromArgb(6, 78, 59));
                            if (manualClick)
                            {
                                this.BeginInvoke(new Action(() =>
                                {
                                    MessageBox.Show(this,
                                        "Your app is on the latest version (v" + localVersion + ") from GitHub (" + ghOwner + "/" + ghRepo + ").",
                                        "Auto-Updater — Up to Date",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Information);
                                }));
                            }
                        }
                    }
                }
                catch
                {
                    SetUpdateBadge("v" + localVersion + " • Ready", Color.FromArgb(52, 211, 153), Color.FromArgb(6, 78, 59));
                }
                finally
                {
                    isCheckingUpdate = false;
                }
            });
        }

        private bool IsNewerVersion(string remote, string local)
        {
            if (remote == local) return false;
            try
            {
                return new Version(remote) > new Version(local);
            }
            catch
            {
                return remote != local;
            }
        }

        private void DownloadAndApplyUpdate(string rawBase, string remoteVersion, string remoteVersionJson, string notes)
        {
            try
            {
                string currentExe = Application.ExecutablePath;
                string newExeTemp = Path.Combine(appDir, "KhammamStreetRacer_new.exe");
                string exeUrl = rawBase + "/dist/KhammamStreetRacer.exe?t=" + DateTime.UtcNow.Ticks;

                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "KhammamStreetRacer-AutoUpdater/1.0");
                    wc.DownloadFile(exeUrl, newExeTemp);
                }

                File.WriteAllText(FindConfigPath("version.json"), remoteVersionJson, Encoding.UTF8);

                this.BeginInvoke(new Action(() =>
                {
                    DialogResult dr = MessageBox.Show(this,
                        "🚀 New Update v" + remoteVersion + " downloaded from GitHub!\n\nRelease Notes:\n" + notes + "\n\nClick OK to restart and apply the update now.",
                        "Auto-Update Ready (v" + remoteVersion + ")",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Information);

                    if (dr == DialogResult.OK)
                    {
                        string batPath = Path.Combine(appDir, "apply_update.bat");
                        string bat = "@echo off\r\n" +
                                     "timeout /t 1 /nobreak >nul\r\n" +
                                     "move /y \"" + newExeTemp + "\" \"" + currentExe + "\" >nul\r\n" +
                                     "start \"\" \"" + currentExe + "\"\r\n" +
                                     "del \"%~f0\"\r\n";
                        File.WriteAllText(batPath, bat, Encoding.ASCII);

                        ProcessStartInfo psi = new ProcessStartInfo(batPath);
                        psi.WindowStyle = ProcessWindowStyle.Hidden;
                        psi.CreateNoWindow = true;
                        Process.Start(psi);
                        Application.Exit();
                    }
                }));
            }
            catch { }
        }

        private void ShowGitHubConfigDialog()
        {
            Form dlg = new Form();
            dlg.Text = "⚙️ GitHub Auto-Updater Configuration";
            dlg.Size = new Size(460, 270);
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.MaximizeBox = false;
            dlg.MinimizeBox = false;
            dlg.BackColor = Color.FromArgb(15, 23, 42);
            dlg.ForeColor = Color.White;

            Label l1 = new Label { Text = "GitHub Owner:", Location = new Point(20, 18), AutoSize = true };
            TextBox txtOwner = new TextBox { Text = ghOwner, Location = new Point(20, 40), Width = 400 };

            Label l2 = new Label { Text = "GitHub Repo:", Location = new Point(20, 76), AutoSize = true };
            TextBox txtRepo = new TextBox { Text = ghRepo, Location = new Point(20, 98), Width = 400 };

            Label l3 = new Label { Text = "Branch:", Location = new Point(20, 134), AutoSize = true };
            TextBox txtBranch = new TextBox { Text = ghBranch, Location = new Point(20, 156), Width = 400 };

            Button btnSave = CreateStyledButton("💾 Save & Check Updates", 190, Color.FromArgb(16, 185, 129), Color.White);
            btnSave.Location = new Point(230, 192);
            btnSave.Click += (s, e) =>
            {
                SaveGitHubConfig(txtOwner.Text, txtRepo.Text, txtBranch.Text);
                dlg.Close();
                CheckForGitHubUpdates(true);
            };

            dlg.Controls.AddRange(new Control[] { l1, txtOwner, l2, txtRepo, l3, txtBranch, btnSave });
            dlg.ShowDialog(this);
        }

        [STAThread]
        public static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public static class GraphicsExtensions
    {
        public static void FillRoundRect(this Graphics g, Brush brush, int x, int y, int width, int height, int radius)
        {
            g.FillRectangle(brush, x, y, width, height);
        }
    }

    public class ViewportPanel : Panel
    {
        public ViewportPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.Black;
        }
    }
}
