using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KhammamSetup
{
    public class SetupForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private ProgressBar progressBar;
        private Label lblStatus;
        private Button btnInstall;
        private CheckBox chkDesktop;
        private CheckBox chkLaunch;
        private string installDir;

        public SetupForm()
        {
            installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KhammamStreetRacer");

            this.Text = "Khammam Street Racer — Setup Wizard (v1.0.0)";
            this.Size = new Size(540, 360);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42);
            this.ForeColor = Color.White;

            Label lblHeader = new Label
            {
                Text = "🏍️ Khammam Street Racer — Desktop Installer",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(24, 22),
                AutoSize = true
            };

            Label lblDesc = new Label
            {
                Text = "Installs the standalone Dwaraka Nagar, Khammam Realistic Street View\n& Bike Race application with built-in GitHub Auto-Updater.\n\nInstall Location:\n" + installDir,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(26, 60),
                Size = new Size(480, 95)
            };

            chkDesktop = new CheckBox
            {
                Text = "Create Desktop & Start Menu Shortcuts",
                Checked = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(28, 165),
                AutoSize = true
            };

            chkLaunch = new CheckBox
            {
                Text = "Launch Khammam Street Racer immediately after installation",
                Checked = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(28, 195),
                AutoSize = true
            };

            progressBar = new ProgressBar
            {
                Location = new Point(28, 232),
                Size = new Size(468, 22),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            lblStatus = new Label
            {
                Text = "Ready to install.",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(28, 260),
                AutoSize = true
            };

            btnInstall = new Button
            {
                Text = "🚀 Install Now",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(326, 268),
                Size = new Size(170, 38),
                Cursor = Cursors.Hand
            };
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Click += (s, e) => RunInstall(false);

            this.Controls.AddRange(new Control[] { lblHeader, lblDesc, chkDesktop, chkLaunch, progressBar, lblStatus, btnInstall });
        }

        public void RunInstall(bool silent)
        {
            try
            {
                btnInstall.Enabled = false;
                lblStatus.Text = "Extracting application & realistic street assets...";
                progressBar.Value = 25;
                Application.DoEvents();

                if (Directory.Exists(installDir))
                {
                    // Stop running instance if any
                    foreach (Process p in Process.GetProcessesByName("KhammamStreetRacer"))
                    {
                        try { p.Kill(); p.WaitForExit(2000); } catch { }
                    }
                }
                else
                {
                    Directory.CreateDirectory(installDir);
                }

                string tempZip = Path.Combine(Path.GetTempPath(), "khammam_payload_" + Guid.NewGuid().ToString("N") + ".zip");
                using (Stream resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                {
                    if (resStream == null)
                        throw new Exception("Embedded payload.zip resource not found.");
                    using (FileStream fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write))
                    {
                        resStream.CopyTo(fs);
                    }
                }

                progressBar.Value = 60;
                Application.DoEvents();

                using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string fullPath = Path.Combine(installDir, entry.FullName.Replace('/', '\\'));
                        string dir = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        if (!string.IsNullOrEmpty(entry.Name))
                            entry.ExtractToFile(fullPath, true);
                    }
                }
                try { File.Delete(tempZip); } catch { }

                progressBar.Value = 85;
                string exePath = Path.Combine(installDir, "KhammamStreetRacer.exe");

                if (silent || chkDesktop.Checked)
                {
                    lblStatus.Text = "Creating Desktop & Start Menu shortcuts...";
                    Application.DoEvents();
                    string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Khammam Street Racer.lnk");
                    string startMenuLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Khammam Street Racer.lnk");
                    CreateShortcut(desktopLnk, exePath, installDir);
                    CreateShortcut(startMenuLnk, exePath, installDir);
                }

                // Register in Windows Installed Apps (Add/Remove Programs)
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\KhammamStreetRacer"))
                    {
                        if (key != null)
                        {
                            key.SetValue("DisplayName", "Khammam Street Racer");
                            key.SetValue("DisplayVersion", "1.0.1");
                            key.SetValue("Publisher", "Khammam Street Racer");
                            key.SetValue("InstallLocation", installDir);
                            key.SetValue("DisplayIcon", exePath);
                            key.SetValue("UninstallString", "cmd.exe /c rmdir /s /q \"" + installDir + "\"");
                            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                        }
                    }
                }
                catch { }

                progressBar.Value = 100;
                lblStatus.Text = "✅ Installation complete!";
                lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
                Application.DoEvents();

                if (!silent)
                {
                    if (chkLaunch.Checked && File.Exists(exePath))
                    {
                        Process.Start(new ProcessStartInfo(exePath) { WorkingDirectory = installDir });
                    }
                    else
                    {
                        MessageBox.Show(this, "Khammam Street Racer has been installed successfully!\n\nShortcut created on your Desktop.", "Setup Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show(this, "Installation error: " + ex.Message, "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    btnInstall.Enabled = true;
                }
            }
        }

        private void CreateShortcut(string shortcutPath, string targetExe, string workDir)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
                var lnk = shell.CreateShortcut(shortcutPath);
                lnk.TargetPath = targetExe;
                lnk.WorkingDirectory = workDir;
                lnk.Description = "Khammam Street Racer — Dwaraka Nagar Realistic Street View & Bike Race";
                lnk.Save();
            }
            catch { }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            SetupForm form = new SetupForm();
            if (args != null && args.Length > 0 && args[0] == "--silent")
            {
                form.RunInstall(true);
                return;
            }
            Application.Run(form);
        }
    }
}
