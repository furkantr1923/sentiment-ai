using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace SentimentAI.PortableLauncher
{
    /// <summary>
    /// Tek dosyalık taşınabilir başlatıcı. Kurulum veya yönetici izni gerektirmez.
    /// Gömülü uygulama paketini kullanıcı klasörüne bir kez açar ve SentimentAI.exe'yi başlatır.
    /// </summary>
    internal static class Program
    {
        private const string AppExe = "SentimentAI.exe";
        private const string ResourceName = "payload.zip";
        private const string MarkerFile = ".portable-ready";

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version.ToString();
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SENTIMENT-AI", "Portable", version);
                var exePath = Path.Combine(root, AppExe);

                using (var payload = assembly.GetManifestResourceStream(ResourceName))
                {
                    if (payload == null)
                    {
                        MessageBox.Show("Uygulama paketi bu dosyada bulunamadı. Lütfen SENTIMENT-AI-Portable.exe dosyasını yeniden indirin.",
                            "SENTIMENT AI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 2;
                    }

                    var stamp = version + ":" + payload.Length;
                    var markerPath = Path.Combine(root, MarkerFile);
                    bool ready = File.Exists(exePath) && File.Exists(markerPath) && File.ReadAllText(markerPath) == stamp;

                    if (!ready)
                    {
                        Extract(payload, root);
                        File.WriteAllText(markerPath, stamp);
                    }
                }

                CleanOldVersions(Path.GetDirectoryName(root), version);

                var psi = new ProcessStartInfo(exePath)
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    Arguments = string.Join(" ", args.Select(a => "\"" + a.Replace("\"", "\\\"") + "\"")),
                };
                Process.Start(psi);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("SENTIMENT AI başlatılamadı.\n\n" + ex.Message, "SENTIMENT AI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        /// <summary>Paketi önce geçici klasöre açar, tamamlanınca hedefe taşır (yarım kalmış kurulumları önler).</summary>
        private static void Extract(Stream payload, string root)
        {
            using (var splash = new SplashForm())
            {
                Exception error = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        var temp = root + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        Directory.CreateDirectory(temp);
                        using (var zip = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true))
                        {
                            var fullTemp = Path.GetFullPath(temp) + Path.DirectorySeparatorChar;
                            foreach (var entry in zip.Entries)
                            {
                                var target = Path.GetFullPath(Path.Combine(temp, entry.FullName));
                                if (!target.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase)) continue; // zip slip koruması
                                if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                                Directory.CreateDirectory(Path.GetDirectoryName(target));
                                entry.ExtractToFile(target, overwrite: true);
                            }
                        }

                        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                        Directory.CreateDirectory(Path.GetDirectoryName(root));
                        Directory.Move(temp, root);
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        try { splash.BeginInvoke(new Action(splash.Close)); } catch { }
                    }
                }) { IsBackground = true };

                splash.Shown += (s, e) => worker.Start();
                Application.Run(splash);
                worker.Join();
                if (error != null) throw error;
            }
        }

        /// <summary>Eski sürümlerin açılmış kopyalarını siler (disk alanı için).</summary>
        private static void CleanOldVersions(string portableDir, string currentVersion)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(portableDir))
                {
                    var name = Path.GetFileName(dir);
                    if (name == currentVersion) continue;
                    try { Directory.Delete(dir, recursive: true); } catch { /* çalışıyor olabilir */ }
                }
            }
            catch { }
        }
    }

    /// <summary>İlk açılışta paket açılırken gösterilen küçük pencere.</summary>
    internal sealed class SplashForm : Form
    {
        public SplashForm()
        {
            Text = "SENTIMENT AI";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(420, 130);
            BackColor = Color.FromArgb(16, 16, 23);
            ShowInTaskbar = true;
            TopMost = true;

            Controls.Add(new Label
            {
                Text = "SENTIMENT AI",
                ForeColor = Color.FromArgb(167, 139, 250),
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 56,
            });
            Controls.Add(new Label
            {
                Text = "İlk açılış için hazırlanıyor…",
                ForeColor = Color.FromArgb(180, 180, 198),
                Font = new Font("Segoe UI", 10),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
            });
        }
    }
}
