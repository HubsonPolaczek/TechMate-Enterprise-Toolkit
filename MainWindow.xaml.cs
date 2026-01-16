using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics; // Potrzebne do PerformanceCounter
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading; // Potrzebne do Timera

namespace TechMate
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient client = new HttpClient();

        // Zmienne do Dashboardu
        private PerformanceCounter cpuCounter;
        private PerformanceCounter ramCounter;
        private DispatcherTimer dashboardTimer;

        public MainWindow()
        {
            InitializeComponent();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Uruchomienie Dashboardu przy starcie
            InitializeDashboard();
        }

        // --- DASHBOARD LOGIC (NOWE!) ---
        private void InitializeDashboard()
        {
            try
            {
                // Podstawowe dane statyczne
                TxtUserName.Text = Environment.UserName;
                TxtOsVersion.Text = Environment.OSVersion.ToString();
                TxtWelcome.Text = $"Witaj, {Environment.UserName}. System gotowy.";

                // Konfiguracja liczników wydajności
                // UWAGA: Wymaga paczki System.Diagnostics.PerformanceCounter
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use"); // Procent zajętego RAMu

                // Konfiguracja Timera (tyka co 1 sekunde)
                dashboardTimer = new DispatcherTimer();
                dashboardTimer.Interval = TimeSpan.FromSeconds(1);
                dashboardTimer.Tick += DashboardTimer_Tick;
                dashboardTimer.Start();
            }
            catch (Exception ex)
            {
                TxtWelcome.Text = "Błąd liczników (brak uprawnień?): " + ex.Message;
            }
        }

        private void DashboardTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // 1. Aktualizacja CPU
                float cpu = cpuCounter.NextValue();
                PbCpu.Value = (int)cpu;
                TxtCpuUsage.Text = $"{(int)cpu}%";

                // 2. Aktualizacja RAM
                float ram = ramCounter.NextValue();
                PbRam.Value = (int)ram;
                TxtRamUsage.Text = $"{(int)ram}%";

                // 3. Aktualizacja Uptime (Czasu pracy)
                TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                TxtUptime.Text = string.Format("{0:D2}d {1:D2}h {2:D2}m {3:D2}s",
                    uptime.Days, uptime.Hours, uptime.Minutes, uptime.Seconds);
            }
            catch { /* Ignorujemy błędy odświeżania */ }
        }

        // ------------------------------------------
        // --- PONIŻEJ STARY KOD (SIECI, ŚLEDCZY) ---
        // ------------------------------------------

        private void Log(string message)
        {
            OutputConsole.Text += $"\n[{DateTime.Now:HH:mm:ss}] {message}";
            if (OutputConsole.Parent is ScrollViewer scroller) scroller.ScrollToBottom();
        }

        private void ClearLog()
        {
            OutputConsole.Text = "--- START ZADANIA ---";
        }

        private async void BtnWifi_Click(object sender, RoutedEventArgs e)
        {
            ClearLog();
            Log("Skanowanie zapisanych profili Wi-Fi...");
            await Task.Run(() =>
            {
                try
                {
                    string output = RunCmdCommand("netsh wlan show profiles");
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    List<string> profiles = new List<string>();

                    foreach (var line in lines)
                    {
                        if (line.Contains(":"))
                        {
                            string profileName = line.Split(':')[1].Trim();
                            if (!string.IsNullOrWhiteSpace(profileName)) profiles.Add(profileName);
                        }
                    }

                    foreach (var profile in profiles)
                    {
                        string passOutput = RunCmdCommand($"netsh wlan show profile name=\"{profile}\" key=clear");
                        string password = "BRAK / Enterprise";
                        var passLines = passOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var pl in passLines)
                        {
                            if (pl.Contains("Key Content") || pl.Contains("Zawartość klucza"))
                                password = pl.Split(':')[1].Trim();
                        }
                        Dispatcher.Invoke(() => Log($"SIEC: {profile.PadRight(20)} | HASŁO: {password}"));
                    }
                }
                catch (Exception ex) { Dispatcher.Invoke(() => Log($"Błąd: {ex.Message}")); }
            });
            Log("=== KONIEC SKANOWANIA ===");
        }

        private async void BtnPortScanner_Click(object sender, RoutedEventArgs e)
        {
            ClearLog();
            string target = TxtTargetIp.Text;
            if (string.IsNullOrWhiteSpace(target)) { Log("Wpisz adres!"); return; }

            Log($"Skanowanie portów dla: {target}");
            int[] ports = { 21, 22, 23, 53, 80, 443, 3306, 3389, 8080 };

            foreach (var port in ports)
            {
                bool isOpen = await CheckPortAsync(target, port);
                if (isOpen) Log($"[+] Port {port}: OTWARTY");
                else Log($"[-] Port {port}: Zamknięty");
            }
            Log("Zakończono.");
        }

        private async void BtnMyIp_Click(object sender, RoutedEventArgs e)
        {
            ClearLog();
            try
            {
                string ip = await client.GetStringAsync("https://api.ipify.org");
                Log($"Twoje Publiczne IP: {ip}");
            }
            catch { Log("Błąd połączenia."); }
        }

        private async void BtnFixNetwork_Click(object sender, RoutedEventArgs e)
        {
            ClearLog();
            Log("ipconfig /flushdns...");
            await Task.Run(() => RunCmdCommand("ipconfig /flushdns"));
            Log("Zakończono.");
        }

        private async Task<bool> CheckPortAsync(string host, int port)
        {
            using (var tcpClient = new TcpClient())
            {
                try
                {
                    var task = tcpClient.ConnectAsync(host, port);
                    if (await Task.WhenAny(task, Task.Delay(500)) == task) return tcpClient.Connected;
                    return false;
                }
                catch { return false; }
            }
        }

        private string RunCmdCommand(string command)
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + command);
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.GetEncoding(852);
            using (Process p = Process.Start(psi)) return p.StandardOutput.ReadToEnd();
        }

        public class UsbDevice { public string Name { get; set; } public string Serial { get; set; } public string LastConnected { get; set; } }

        private void BtnUsbHistory_Click(object sender, RoutedEventArgs e)
        {
            ForensicStatus.Text = "Analizowanie rejestru...";
            GridUsb.Visibility = Visibility.Visible; TxtHostsViewer.Visibility = Visibility.Hidden;
            var usbList = new List<UsbDevice>();
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USBSTOR"))
                {
                    if (key != null)
                    {
                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            using (RegistryKey deviceKey = key.OpenSubKey(subKeyName))
                            {
                                foreach (string serialName in deviceKey.GetSubKeyNames())
                                {
                                    using (RegistryKey details = deviceKey.OpenSubKey(serialName))
                                    {
                                        string friendlyName = (string)details.GetValue("FriendlyName") ?? subKeyName;
                                        usbList.Add(new UsbDevice { Name = friendlyName, Serial = serialName, LastConnected = "Rejestr" });
                                    }
                                }
                            }
                        }
                    }
                }
                GridUsb.ItemsSource = usbList; ForensicStatus.Text = $"Znaleziono {usbList.Count} urządzeń.";
            }
            catch (Exception ex) { ForensicStatus.Text = "Uruchom jako Admin! " + ex.Message; }
        }

        private void BtnHosts_Click(object sender, RoutedEventArgs e)
        {
            ForensicStatus.Text = "Weryfikacja hosts...";
            GridUsb.Visibility = Visibility.Hidden; TxtHostsViewer.Visibility = Visibility.Visible;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            if (File.Exists(path)) { TxtHostsViewer.Text = File.ReadAllText(path); } else { TxtHostsViewer.Text = "Brak pliku hosts."; }
        }

        private void LogService(string message) { ServiceOutput.Text += $"\n[{DateTime.Now:HH:mm:ss}] {message}"; if (ServiceOutput.Parent is ScrollViewer s) s.ScrollToBottom(); }
        private async void BtnBattery_Click(object sender, RoutedEventArgs e)
        {
            LogService("Generowanie raportu...");
            await Task.Run(() => {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "battery_report.html");
                RunCmdCommand($"powercfg /batteryreport /output \"{path}\"");
                Dispatcher.Invoke(() => { LogService($"Zapisano: {path}"); try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } });
            });
        }
        private void BtnStartup_Click(object sender, RoutedEventArgs e)
        {
            LogService("Skanowanie autostartu...");
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key != null) foreach (var n in key.GetValueNames()) LogService($"[AUTOSTART] {n}");
                }
            }
            catch (Exception ex) { LogService("Błąd: " + ex.Message); }
        }
        private async void BtnCleanDisk_Click(object sender, RoutedEventArgs e)
        {
            LogService("Czyszczenie TEMP...");
            await Task.Run(() => {
                string tempPath = Path.GetTempPath();
                int count = 0;
                foreach (FileInfo f in new DirectoryInfo(tempPath).GetFiles()) { try { f.Delete(); count++; } catch { } }
                Dispatcher.Invoke(() => LogService($"Usunięto {count} plików."));
            });
        }
    }
}