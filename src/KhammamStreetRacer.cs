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

    public class MainForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private List<StepInfo> steps = new List<StepInfo>();
        private int currentStep = 13; // Start at User's House Pin (17.256749, 80.158489)
        private string lookDir = "straight"; // "left", "straight", "right"
        private bool cleanMode = true;
        private bool bikeCockpitVisible = true;
        private bool isFullscreen = false;
        private FormWindowState prevWindowState;
        private FormBorderStyle prevBorderStyle;
        private Rectangle prevBounds;

        private System.Windows.Forms.Timer rideTimer;
        private System.Windows.Forms.Timer animTimer;
        private System.Windows.Forms.Timer autoUpdateTimer;
        private int currentSpeedKmh = 0;
        private float handlebarLean = 0f;

        private Image cachedImage = null;
        private string cachedImagePath = "";

        private string appDir;
        private string assetsDir;
        private string localVersion = "1.0.0";
        private string ghOwner = "";
        private string ghRepo = "khammam-street-racer";
        private string ghBranch = "main";
        private string updateStatusText = "v1.0.0 • Auto-Updater Ready";
        private Color updateStatusColor = Color.FromArgb(16, 185, 129);
        private bool isCheckingUpdate = false;

        // UI Controls
        private Panel topBar;
        private Panel bottomBar;
        private ViewportPanel viewport;
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblUpdateBadge;
        private Button btnCheckUpdate;
        private Button btnRepoConfig;
        private Button btnCleanMode;
        private Button btnBikeToggle;
        private Button btnFullscreen;

        private Button btnPrev;
        private Button btnAutoRide;
        private Button btnNext;
        private Button btnLookLeft;
        private Button btnLookStraight;
        private Button btnLookRight;
        private TrackBar trackSlider;

        public MainForm()
        {
            appDir = AppDomain.CurrentDomain.BaseDirectory;
            assetsDir = Path.Combine(appDir, "assets");
            if (!Directory.Exists(assetsDir))
            {
                string parentAssets = Path.Combine(Directory.GetParent(appDir.TrimEnd('\\')).FullName, "assets");
                if (Directory.Exists(parentAssets)) assetsDir = parentAssets;
            }

            LoadConfigAndVersion();
            InitSteps();
            InitUI();
            LoadCurrentStepImage();

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
                    if (mOwner.Success) ghOwner = mOwner.Groups[1].Value.Trim();
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
            steps.Add(new StepInfo { Id = 1,  RawFile = "step_01.jpg", CleanFile = "step_01.jpg", CropBottom = true,  Coords = "17.257785° N, 80.159039° E", Label = "Step 1 (Road Starting): Divya City Apartment & White WagonR" });
            steps.Add(new StepInfo { Id = 2,  RawFile = "step_02.jpg", CleanFile = "zone1_road_start_3d.jpg", CropBottom = false, Coords = "17.257628° N, 80.158944° E", Label = "Step 2: Sage-Green House, Leaning Neem Trees & Banana/Papaya Wall" });
            steps.Add(new StepInfo { Id = 3,  RawFile = "step_03.jpg", CleanFile = "step_03.jpg", CropBottom = true,  Coords = "17.257549° N, 80.158898° E", Label = "Step 3: Shady Neem Canopy & 3-Story Peach Apartment" });
            steps.Add(new StepInfo { Id = 4,  RawFile = "step_04.jpg", CleanFile = "step_04.jpg", CropBottom = true,  Coords = "17.257470° N, 80.158851° E", Label = "Step 4: Peach Apartment Entrance & Street Trees" });
            steps.Add(new StepInfo { Id = 5,  RawFile = "step_05.jpg", CleanFile = "step_05.jpg", CropBottom = true,  Coords = "17.257387° N, 80.158816° E", Label = "Step 5: Approaching Upper-Middle Section" });
            steps.Add(new StepInfo { Id = 6,  RawFile = "step_06.jpg", CleanFile = "step_06.jpg", CropBottom = true,  Coords = "17.257316° N, 80.158756° E", Label = "Step 6: Red Staircase House & Khammam Municipal Wall Painting" });
            steps.Add(new StepInfo { Id = 7,  RawFile = "step_07.jpg", CleanFile = "step_07.jpg", CropBottom = true,  Coords = "17.257238° N, 80.158712° E", Label = "Step 7: Yellow Veranda Building & Parked Grey Scooter" });
            steps.Add(new StepInfo { Id = 8,  RawFile = "step_08.jpg", CleanFile = "step_08.jpg", CropBottom = true,  Coords = "17.257160° N, 80.158667° E", Label = "Step 8: Middle Street Neem Shade" });
            steps.Add(new StepInfo { Id = 9,  RawFile = "step_09.jpg", CleanFile = "step_09.jpg", CropBottom = true,  Coords = "17.257085° N, 80.158612° E", Label = "Step 9: Approaching Stone-Cladding Residence" });
            steps.Add(new StepInfo { Id = 10, RawFile = "step_10.jpg", CleanFile = "zone3_mid_approach_3d.jpg", CropBottom = false, Coords = "17.257004° N, 80.158558° E", Label = "Step 10: Brown Stone-Cladding House, White Sedan & Silver MPV" });
            steps.Add(new StepInfo { Id = 11, RawFile = "step_11.jpg", CleanFile = "step_11.jpg", CropBottom = true,  Coords = "17.256921° N, 80.158518° E", Label = "Step 11: Passing Silver Innova MPV toward Your House" });
            steps.Add(new StepInfo { Id = 12, RawFile = "step_12.jpg", CleanFile = "step_12.jpg", CropBottom = true,  Coords = "17.256845° N, 80.158468° E", Label = "Step 12: Neem Tree Corner Just Before Your House" });
            steps.Add(new StepInfo { Id = 13, RawFile = "step_13.jpg", CleanFile = "exact_full_street_3d_view.jpg", CropBottom = false, Coords = "17.256749° N, 80.158489° E", Label = "Step 13: MA ILLU (YOUR EXACT HOUSE PIN) — Both Sides of Street" });
            steps.Add(new StepInfo { Id = 14, RawFile = "step_14.jpg", CleanFile = "step_14.jpg", CropBottom = true,  Coords = "17.256685° N, 80.158377° E", Label = "Step 14: Sage-Green House, Umbrella Tree, Scooter & Red-Chajja House" });
            steps.Add(new StepInfo { Id = 15, RawFile = "step_15.jpg", CleanFile = "step_15.jpg", CropBottom = true,  Coords = "17.256601° N, 80.158336° E", Label = "Step 15: Passing White Indica Car toward South End" });
            steps.Add(new StepInfo { Id = 16, RawFile = "step_16.jpg", CleanFile = "zone5_road_end_3d.jpg", CropBottom = false, Coords = "17.256518° N, 80.158292° E", Label = "Step 16: Orange Terrace Canopy, 4-Story Grey Building & CA Office" });
            steps.Add(new StepInfo { Id = 17, RawFile = "step_17.jpg", CleanFile = "step_17.jpg", CropBottom = true,  Coords = "17.256437° N, 80.158256° E", Label = "Step 17: Approaching End T-Junction" });
            steps.Add(new StepInfo { Id = 18, RawFile = "step_18.jpg", CleanFile = "step_18.jpg", CropBottom = true,  Coords = "17.256351° N, 80.158221° E", Label = "Step 18 (Road Ending): Black Granite Steps & Main Road Exit Junction" });
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
            this.Text = "Khammam Street Racer — Dwaraka Nagar Realistic View (v" + localVersion + ")";
            this.Size = new Size(1220, 780);
            this.MinimumSize = new Size(920, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42);
            this.ForeColor = Color.White;
            this.KeyPreview = true;

            // Top Header Bar
            topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 64;
            topBar.BackColor = Color.FromArgb(15, 23, 42);
            topBar.Padding = new Padding(14, 8, 14, 8);

            lblTitle = new Label();
            lblTitle.Text = "🏍️ KHAMMAM STREET RACER — Dwaraka Nagar Realistic Street View (Start → End)";
            lblTitle.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(248, 250, 252);
            lblTitle.Location = new Point(14, 8);
            lblTitle.AutoSize = true;
            topBar.Controls.Add(lblTitle);

            lblSubtitle = new Label();
            lblSubtitle.Text = "Step 13 / 18 • 17.256749° N, 80.158489° E";
            lblSubtitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblSubtitle.ForeColor = Color.FromArgb(148, 163, 184);
            lblSubtitle.Location = new Point(16, 34);
            lblSubtitle.AutoSize = true;
            topBar.Controls.Add(lblSubtitle);

            FlowLayoutPanel rightTopFlow = new FlowLayoutPanel();
            rightTopFlow.Dock = DockStyle.Right;
            rightTopFlow.AutoSize = true;
            rightTopFlow.FlowDirection = FlowDirection.LeftToRight;
            rightTopFlow.WrapContents = false;
            rightTopFlow.Padding = new Padding(0, 8, 0, 0);

            lblUpdateBadge = new Label();
            lblUpdateBadge.Text = "v" + localVersion + " • Ready";
            lblUpdateBadge.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblUpdateBadge.ForeColor = Color.FromArgb(52, 211, 153);
            lblUpdateBadge.BackColor = Color.FromArgb(6, 78, 59);
            lblUpdateBadge.TextAlign = ContentAlignment.MiddleCenter;
            lblUpdateBadge.AutoSize = false;
            lblUpdateBadge.Size = new Size(165, 32);
            lblUpdateBadge.Margin = new Padding(4, 0, 6, 0);
            rightTopFlow.Controls.Add(lblUpdateBadge);

            btnCheckUpdate = CreateStyledButton("🔄 Check Update", 125, Color.FromArgb(30, 41, 59), Color.FromArgb(56, 189, 248));
            btnCheckUpdate.Click += (s, e) => CheckForGitHubUpdates(true);
            rightTopFlow.Controls.Add(btnCheckUpdate);

            btnRepoConfig = CreateStyledButton("⚙️ GitHub Repo", 120, Color.FromArgb(30, 41, 59), Color.FromArgb(226, 232, 240));
            btnRepoConfig.Click += (s, e) => ShowGitHubConfigDialog();
            rightTopFlow.Controls.Add(btnRepoConfig);

            btnCleanMode = CreateStyledButton("✨ Clean Road: ON", 132, Color.FromArgb(5, 150, 105), Color.White);
            btnCleanMode.Click += (s, e) =>
            {
                cleanMode = !cleanMode;
                btnCleanMode.Text = cleanMode ? "✨ Clean Road: ON" : "🌍 Raw Google View";
                btnCleanMode.BackColor = cleanMode ? Color.FromArgb(5, 150, 105) : Color.FromArgb(30, 41, 59);
                LoadCurrentStepImage();
            };
            rightTopFlow.Controls.Add(btnCleanMode);

            btnBikeToggle = CreateStyledButton("🏍️ Bike HUD: ON", 122, Color.FromArgb(217, 119, 6), Color.White);
            btnBikeToggle.Click += (s, e) =>
            {
                bikeCockpitVisible = !bikeCockpitVisible;
                btnBikeToggle.Text = bikeCockpitVisible ? "🏍️ Bike HUD: ON" : "🏍️ Bike HUD: OFF";
                btnBikeToggle.BackColor = bikeCockpitVisible ? Color.FromArgb(217, 119, 6) : Color.FromArgb(30, 41, 59);
                viewport.Invalidate();
            };
            rightTopFlow.Controls.Add(btnBikeToggle);

            btnFullscreen = CreateStyledButton("⛶ F11", 68, Color.FromArgb(30, 41, 59), Color.White);
            btnFullscreen.Click += (s, e) => ToggleFullscreen();
            rightTopFlow.Controls.Add(btnFullscreen);

            topBar.Controls.Add(rightTopFlow);

            // Bottom Control Bar
            bottomBar = new Panel();
            bottomBar.Dock = DockStyle.Bottom;
            bottomBar.Height = 62;
            bottomBar.BackColor = Color.FromArgb(15, 23, 42);
            bottomBar.Padding = new Padding(12, 10, 12, 10);

            FlowLayoutPanel leftBottomFlow = new FlowLayoutPanel();
            leftBottomFlow.Dock = DockStyle.Left;
            leftBottomFlow.AutoSize = true;
            leftBottomFlow.WrapContents = false;

            btnPrev = CreateStyledButton("◀ Back (S)", 96, Color.FromArgb(30, 41, 59), Color.White);
            btnPrev.Click += (s, e) => StepBackward();
            leftBottomFlow.Controls.Add(btnPrev);

            btnAutoRide = CreateStyledButton("▶ Auto-Ride (Start → End)", 185, Color.FromArgb(16, 185, 129), Color.White);
            btnAutoRide.Click += (s, e) => ToggleAutoRide();
            leftBottomFlow.Controls.Add(btnAutoRide);

            btnNext = CreateStyledButton("Forward (W) ▶", 110, Color.FromArgb(30, 41, 59), Color.White);
            btnNext.Click += (s, e) => StepForward();
            leftBottomFlow.Controls.Add(btnNext);

            Button btnJ1 = CreateStyledButton("1. Start", 72, Color.FromArgb(30, 41, 59), Color.FromArgb(203, 213, 225));
            btnJ1.Click += (s, e) => JumpToStep(1, "straight");
            leftBottomFlow.Controls.Add(btnJ1);

            Button btnJ10 = CreateStyledButton("10. Mid", 72, Color.FromArgb(30, 41, 59), Color.FromArgb(203, 213, 225));
            btnJ10.Click += (s, e) => JumpToStep(10, "straight");
            leftBottomFlow.Controls.Add(btnJ10);

            Button btnJ13 = CreateStyledButton("13. Ma Illu 📍", 108, Color.FromArgb(37, 99, 235), Color.White);
            btnJ13.Click += (s, e) => JumpToStep(13, "straight");
            leftBottomFlow.Controls.Add(btnJ13);

            Button btnJ18 = CreateStyledButton("18. End", 72, Color.FromArgb(30, 41, 59), Color.FromArgb(203, 213, 225));
            btnJ18.Click += (s, e) => JumpToStep(18, "straight");
            leftBottomFlow.Controls.Add(btnJ18);

            bottomBar.Controls.Add(leftBottomFlow);

            FlowLayoutPanel rightBottomFlow = new FlowLayoutPanel();
            rightBottomFlow.Dock = DockStyle.Right;
            rightBottomFlow.AutoSize = true;
            rightBottomFlow.WrapContents = false;

            btnLookLeft = CreateStyledButton("🏠 Look Left (Ma Illu)", 152, Color.FromArgb(30, 41, 59), Color.White);
            btnLookLeft.Click += (s, e) => { lookDir = "left"; handlebarLean = -12f; LoadCurrentStepImage(); };
            rightBottomFlow.Controls.Add(btnLookLeft);

            btnLookStraight = CreateStyledButton("🛣️ Down Road", 112, Color.FromArgb(5, 150, 105), Color.White);
            btnLookStraight.Click += (s, e) => { lookDir = "straight"; handlebarLean = 0f; LoadCurrentStepImage(); };
            rightBottomFlow.Controls.Add(btnLookStraight);

            btnLookRight = CreateStyledButton("🏛️ Look Right (Opposite)", 162, Color.FromArgb(30, 41, 59), Color.White);
            btnLookRight.Click += (s, e) => { lookDir = "right"; handlebarLean = 12f; LoadCurrentStepImage(); };
            rightBottomFlow.Controls.Add(btnLookRight);

            trackSlider = new TrackBar();
            trackSlider.Minimum = 1;
            trackSlider.Maximum = 18;
            trackSlider.Value = 13;
            trackSlider.TickStyle = TickStyle.None;
            trackSlider.Width = 140;
            trackSlider.Height = 32;
            trackSlider.Scroll += (s, e) =>
            {
                lookDir = "straight";
                currentStep = trackSlider.Value;
                LoadCurrentStepImage();
            };
            rightBottomFlow.Controls.Add(trackSlider);

            bottomBar.Controls.Add(rightBottomFlow);

            // Center Realistic Viewport
            viewport = new ViewportPanel();
            viewport.Dock = DockStyle.Fill;
            viewport.Paint += Viewport_Paint;
            viewport.MouseClick += (s, e) => this.Focus();

            this.Controls.Add(viewport);
            this.Controls.Add(topBar);
            this.Controls.Add(bottomBar);

            // Auto-Ride Timer
            rideTimer = new System.Windows.Forms.Timer();
            rideTimer.Interval = 750;
            rideTimer.Tick += (s, e) =>
            {
                if (currentStep < 18)
                {
                    currentStep++;
                    currentSpeedKmh = 46;
                    LoadCurrentStepImage();
                }
                else
                {
                    StopAutoRide();
                }
            };

            // Smooth HUD decay timer
            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 50;
            animTimer.Tick += (s, e) =>
            {
                if (!rideTimer.Enabled && currentSpeedKmh > 0)
                {
                    currentSpeedKmh = Math.Max(0, currentSpeedKmh - 3);
                    viewport.Invalidate();
                }
                if (Math.Abs(handlebarLean) > 0.2f && lookDir == "straight")
                {
                    handlebarLean *= 0.8f;
                    viewport.Invalidate();
                }
            };
            animTimer.Start();

            this.KeyDown += MainForm_KeyDown;
        }

        private void JumpToStep(int step, string dir)
        {
            StopAutoRide();
            currentStep = step;
            lookDir = dir;
            currentSpeedKmh = 35;
            LoadCurrentStepImage();
        }

        private void StepForward()
        {
            lookDir = "straight";
            currentSpeedKmh = 48;
            currentStep = currentStep < 18 ? currentStep + 1 : 1;
            LoadCurrentStepImage();
        }

        private void StepBackward()
        {
            lookDir = "straight";
            currentSpeedKmh = 25;
            currentStep = currentStep > 1 ? currentStep - 1 : 18;
            LoadCurrentStepImage();
        }

        private void ToggleAutoRide()
        {
            if (rideTimer.Enabled)
            {
                StopAutoRide();
            }
            else
            {
                lookDir = "straight";
                if (currentStep >= 18) currentStep = 1;
                currentSpeedKmh = 45;
                btnAutoRide.Text = "⏸ Pause Auto-Ride";
                btnAutoRide.BackColor = Color.FromArgb(225, 29, 72);
                LoadCurrentStepImage();
                rideTimer.Start();
            }
        }

        private void StopAutoRide()
        {
            rideTimer.Stop();
            currentSpeedKmh = 0;
            btnAutoRide.Text = "▶ Auto-Ride (Start → End)";
            btnAutoRide.BackColor = Color.FromArgb(16, 185, 129);
            viewport.Invalidate();
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

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape && isFullscreen)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.W || e.KeyCode == Keys.Up)
            {
                StepForward();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.S || e.KeyCode == Keys.Down)
            {
                StepBackward();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.A || e.KeyCode == Keys.Left)
            {
                lookDir = (lookDir == "right") ? "straight" : "left";
                handlebarLean = (lookDir == "left") ? -14f : 0f;
                LoadCurrentStepImage();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.D || e.KeyCode == Keys.Right)
            {
                lookDir = (lookDir == "left") ? "straight" : "right";
                handlebarLean = (lookDir == "right") ? 14f : 0f;
                LoadCurrentStepImage();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Space)
            {
                ToggleAutoRide();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.B)
            {
                btnBikeToggle.PerformClick();
                e.Handled = true;
            }
        }

        private void LoadCurrentStepImage()
        {
            StepInfo s = steps[currentStep - 1];
            trackSlider.Value = currentStep;

            string fileName;
            bool shouldCrop = false;

            if (lookDir == "left")
            {
                fileName = cleanMode ? "exact_house_3d_view.jpg" : "view_east_wide.jpg";
                shouldCrop = !cleanMode;
                lblSubtitle.Text = "🏠 Left View (East Side at Pin 17.256749° N, 80.158489° E): Your House Close-Up (Yellow G+1 & Olive-Cream G+2)";
                btnLookLeft.BackColor = Color.FromArgb(5, 150, 105);
                btnLookStraight.BackColor = Color.FromArgb(30, 41, 59);
                btnLookRight.BackColor = Color.FromArgb(30, 41, 59);
            }
            else if (lookDir == "right")
            {
                fileName = "view_west_wide.jpg";
                shouldCrop = true;
                lblSubtitle.Text = "🏛️ Right View (West Side at Pin): Opposite House (Red Chajjas, White Sunroof SUV & Splendor Bike)";
                btnLookRight.BackColor = Color.FromArgb(5, 150, 105);
                btnLookStraight.BackColor = Color.FromArgb(30, 41, 59);
                btnLookLeft.BackColor = Color.FromArgb(30, 41, 59);
            }
            else
            {
                fileName = cleanMode ? s.CleanFile : s.RawFile;
                shouldCrop = cleanMode ? s.CropBottom : true;
                lblSubtitle.Text = s.Label + "  •  " + s.Coords;
                btnLookStraight.BackColor = Color.FromArgb(5, 150, 105);
                btnLookLeft.BackColor = Color.FromArgb(30, 41, 59);
                btnLookRight.BackColor = Color.FromArgb(30, 41, 59);
            }

            string fullPath = Path.Combine(assetsDir, fileName);
            if (!File.Exists(fullPath))
            {
                fullPath = Path.Combine(assetsDir, s.RawFile);
                shouldCrop = true;
            }

            if (fullPath != cachedImagePath && File.Exists(fullPath))
            {
                try
                {
                    if (cachedImage != null) cachedImage.Dispose();
                    using (FileStream fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
                    {
                        cachedImage = Image.FromStream(fs);
                    }
                    cachedImagePath = fullPath;
                }
                catch { }
            }

            viewport.Tag = shouldCrop;
            viewport.Invalidate();
        }

        private void Viewport_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            int vw = viewport.Width;
            int vh = viewport.Height;

            if (cachedImage != null)
            {
                bool shouldCrop = (viewport.Tag is bool) && (bool)viewport.Tag;
                int srcW = cachedImage.Width;
                int srcH = shouldCrop ? (int)(cachedImage.Height * 0.78) : cachedImage.Height;

                float scale = Math.Max((float)vw / srcW, (float)vh / srcH);
                int drawW = (int)(srcW * scale);
                int drawH = (int)(srcH * scale);
                int drawX = (vw - drawW) / 2;
                int drawY = (vh - drawH) / 2;

                Rectangle destRect = new Rectangle(drawX, drawY, drawW, drawH);
                Rectangle srcRect = new Rectangle(0, 0, srcW, srcH);
                g.DrawImage(cachedImage, destRect, srcRect, GraphicsUnit.Pixel);
            }

            // Draw Top-Left Track Progress Overlay Pill
            using (SolidBrush pillBrush = new SolidBrush(Color.FromArgb(190, 15, 23, 42)))
            using (Pen pillPen = new Pen(Color.FromArgb(100, 52, 211, 153), 1.5f))
            {
                Rectangle hudRect = new Rectangle(18, 16, 540, 38);
                g.FillRectangle(pillBrush, hudRect);
                g.DrawRectangle(pillPen, hudRect);

                StepInfo s = steps[currentStep - 1];
                using (Font fBadge = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                using (SolidBrush bGreen = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    g.FillRectangle(bGreen, new Rectangle(24, 22, 82, 26));
                    g.DrawString("STEP " + currentStep + "/18", fBadge, Brushes.White, 30, 26);
                    g.DrawString(s.Label, fBadge, Brushes.White, 114, 26);
                }
            }

            // Draw First-Person Realistic Motorcycle Handlebar & Digital Speedometer Cockpit
            if (bikeCockpitVisible)
            {
                GraphicsState state = g.Save();
                g.TranslateTransform(vw / 2f, vh + 10f);
                g.RotateTransform(handlebarLean * 0.45f);

                // Side Mirror Stems
                using (Pen stemPen = new Pen(Color.FromArgb(30, 41, 59), 10f))
                {
                    stemPen.StartCap = LineCap.Round;
                    stemPen.EndCap = LineCap.Round;
                    g.DrawLine(stemPen, -110, -45, -210, -115);
                    g.DrawLine(stemPen, 110, -45, 210, -115);
                }

                // Left & Right Side Mirrors
                using (SolidBrush mirrorShell = new SolidBrush(Color.FromArgb(15, 23, 42)))
                using (SolidBrush mirrorGlass = new SolidBrush(Color.FromArgb(120, 56, 189, 248)))
                using (Pen mirrorBorder = new Pen(Color.FromArgb(71, 85, 105), 3f))
                {
                    Rectangle leftM = new Rectangle(-275, -148, 92, 46);
                    Rectangle rightM = new Rectangle(183, -148, 92, 46);
                    g.FillEllipse(mirrorShell, leftM);
                    g.DrawEllipse(mirrorBorder, leftM);
                    g.FillEllipse(mirrorGlass, new Rectangle(-269, -143, 80, 36));

                    g.FillEllipse(mirrorShell, rightM);
                    g.DrawEllipse(mirrorBorder, rightM);
                    g.FillEllipse(mirrorGlass, new Rectangle(189, -143, 80, 36));
                }

                // Handlebar Tubes & Grips
                using (Pen barPen = new Pen(Color.FromArgb(51, 65, 85), 16f))
                using (Pen gripPen = new Pen(Color.FromArgb(15, 23, 42), 22f))
                using (Pen redRing = new Pen(Color.FromArgb(220, 38, 38), 4f))
                {
                    g.DrawLine(barPen, -60, -36, -185, -62);
                    g.DrawLine(barPen, 60, -36, 185, -62);

                    g.DrawLine(gripPen, -175, -60, -255, -72);
                    g.DrawLine(gripPen, 175, -60, 255, -72);

                    g.DrawLine(redRing, -175, -50, -175, -70);
                    g.DrawLine(redRing, 175, -50, 175, -70);
                }

                // Center Fairing & Digital Speedometer Cluster
                Point[] fairing = new Point[]
                {
                    new Point(-105, 0),
                    new Point(-82, -88),
                    new Point(0, -102),
                    new Point(82, -88),
                    new Point(105, 0)
                };
                using (SolidBrush fairingBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
                using (Pen fairingPen = new Pen(Color.FromArgb(71, 85, 105), 3f))
                {
                    g.FillPolygon(fairingBrush, fairing);
                    g.DrawPolygon(fairingPen, fairing);
                }

                // Digital LCD Screen
                Rectangle lcd = new Rectangle(-68, -82, 136, 52);
                using (SolidBrush lcdBg = new SolidBrush(Color.FromArgb(2, 44, 34)))
                using (Pen lcdBorder = new Pen(Color.FromArgb(16, 185, 129), 2f))
                using (Font fSpeed = new Font("Consolas", 15f, FontStyle.Bold))
                using (Font fSub = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                {
                    g.FillRectangle(lcdBg, lcd);
                    g.DrawRectangle(lcdBorder, lcd);

                    string spdText = currentSpeedKmh > 0 ? (currentSpeedKmh + " KM/H") : ("STEP " + currentStep + "/18");
                    SizeF sz = g.MeasureString(spdText, fSpeed);
                    g.DrawString(spdText, fSpeed, Brushes.SpringGreen, -sz.Width / 2f, -77);
                    g.DrawString("KHAMMAM TRACK • GEAR 3", fSub, Brushes.MediumAquamarine, -60, -48);
                }

                // Red Fuel Tank Crest
                using (SolidBrush tankBrush = new SolidBrush(Color.FromArgb(220, 38, 38)))
                {
                    g.FillEllipse(tankBrush, new Rectangle(-75, -24, 150, 48));
                }

                g.Restore(state);
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
                SetUpdateBadge("v" + localVersion + " • Set GitHub Repo", Color.FromArgb(253, 224, 71), Color.FromArgb(66, 32, 6));
                if (manualClick)
                {
                    ShowGitHubConfigDialog();
                }
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
                        string releaseNotes = mNotes.Success ? mNotes.Groups[1].Value : "New update pushed to GitHub.";

                        if (IsNewerVersion(remoteVersion, localVersion))
                        {
                            SetUpdateBadge("⬇️ Updating to v" + remoteVersion + "...", Color.FromArgb(253, 224, 71), Color.FromArgb(66, 32, 6));
                            DownloadAndApplyUpdate(rawBase, remoteVersion, remoteJson, releaseNotes);
                        }
                        else
                        {
                            SetUpdateBadge("✅ v" + localVersion + " • Up to date", Color.FromArgb(52, 211, 153), Color.FromArgb(6, 78, 59));
                            if (manualClick)
                            {
                                this.BeginInvoke(new Action(() =>
                                {
                                    MessageBox.Show(this,
                                        "Your app is already on the latest version (v" + localVersion + ") from GitHub (" + ghOwner + "/" + ghRepo + ").\n\nWhenever a new version is pushed to GitHub, it will automatically download and update!",
                                        "Auto-Updater — Up to Date",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Information);
                                }));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    SetUpdateBadge("v" + localVersion + " • GitHub Offline", Color.FromArgb(252, 165, 165), Color.FromArgb(69, 10, 10));
                    if (manualClick)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show(this,
                                "Could not reach GitHub repo '" + ghOwner + "/" + ghRepo + "' (" + ex.Message + ").\n\nMake sure the repo is pushed using publish-update.ps1 or check ⚙️ GitHub Repo settings.",
                                "GitHub Auto-Updater",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }));
                    }
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
                Version vr = new Version(remote);
                Version vl = new Version(local);
                return vr > vl;
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
            catch (Exception ex)
            {
                SetUpdateBadge("Update Error", Color.FromArgb(252, 165, 165), Color.FromArgb(69, 10, 10));
                this.BeginInvoke(new Action(() =>
                {
                    MessageBox.Show(this, "Failed to download update binary: " + ex.Message, "Auto-Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
        }

        private void ShowGitHubConfigDialog()
        {
            Form dlg = new Form();
            dlg.Text = "⚙️ GitHub Auto-Updater Configuration";
            dlg.Size = new Size(480, 290);
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.MaximizeBox = false;
            dlg.MinimizeBox = false;
            dlg.BackColor = Color.FromArgb(15, 23, 42);
            dlg.ForeColor = Color.White;

            Label l1 = new Label { Text = "GitHub Username / Owner:", Location = new Point(20, 20), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            TextBox txtOwner = new TextBox { Text = ghOwner, Location = new Point(20, 44), Width = 420, Font = new Font("Segoe UI", 10f) };

            Label l2 = new Label { Text = "GitHub Repository Name:", Location = new Point(20, 82), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            TextBox txtRepo = new TextBox { Text = ghRepo, Location = new Point(20, 106), Width = 420, Font = new Font("Segoe UI", 10f) };

            Label l3 = new Label { Text = "Branch (default: main):", Location = new Point(20, 144), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            TextBox txtBranch = new TextBox { Text = ghBranch, Location = new Point(20, 168), Width = 420, Font = new Font("Segoe UI", 10f) };

            Button btnSave = CreateStyledButton("💾 Save & Check Updates", 200, Color.FromArgb(16, 185, 129), Color.White);
            btnSave.Location = new Point(240, 208);
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
