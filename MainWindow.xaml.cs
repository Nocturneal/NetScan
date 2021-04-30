using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NetScan {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        public MainWindow() {
            InitializeComponent();
            ShowInTaskbar = false;

            netIfOutput.SelectionChanged += (s, e) => UpdateUiIpList();
            ipListOutput.SelectionChanged += (s, e) => { if (ipListOutput?.SelectedItem != null) Clipboard.SetText(ipListOutput.SelectedItem.ToString()); };
            StateChanged += (s, e) => ShowInTaskbar = WindowState == WindowState.Minimized;
            Loaded += (s,e) => Run();

#if DEBUG
            PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Critical;
#endif
        }

        public NetworkInterface[] nics;
        public NetworkInterface selectedNic;
        public List<IPAddressInformation> ips;
        public void Run() => Task.Run(() => {
            // Check for updated list of NIC components
            nics = NetworkInterface.GetAllNetworkInterfaces();
            UpdateUiNetList();
        });

        internal void UpdateUiNetList() {
            Dispatcher.BeginInvoke(new Action(() => netIfOutput.Items.Clear()));
            foreach (var nic in nics.Where(n=>n.OperationalStatus == OperationalStatus.Up && n.GetIPProperties().UnicastAddresses.Where(ip => ip.IsDnsEligible && ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Any())) {
                Dispatcher.BeginInvoke(new Action(() => netIfOutput.Items.Add(nic.Name)));
            }
        }

        internal void UpdateUiIpList() {
            if (netIfOutput.SelectedItem != null) {
                selectedNic = nics.Where(x => x.Name.Equals(netIfOutput.SelectedItem)).FirstOrDefault();
                Dispatcher.BeginInvoke(new Action(() => ipListOutput.Items.Clear()));
                if (selectedNic != null) {
                    var myIp = selectedNic.GetIPProperties().UnicastAddresses.Where(ip => ip.IsDnsEligible && ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).First();
                    var text = $"[{selectedNic.Name}]\n" +
                               $"IP: {myIp.Address.ToString()}\n" +
                               $"MAC: {selectedNic.GetPhysicalAddress().ToString()}\n" +
                               $"Interface: {selectedNic.NetworkInterfaceType.ToString()}\n" +
                               $"Status: {selectedNic.OperationalStatus.ToString()}\n" +
                               $"";
                    Dispatcher.BeginInvoke(new Action(() => devDetailOutput.Text = text));

                    var myIPPrefix = Regex.Match(myIp.Address.ToString(), "(\\d+.){3}").Value;
                    IpScanner.scan(myIPPrefix).ContinueWith((t) => {
                        if (!t.IsFaulted) {
                            var ipList = t.Result;
                            //IPHostEntry iphostentry = Dns.GetHostEntry(myIp.Address);
                            foreach (var ip in ipList.Select(x => Version.Parse(x.ToString())).OrderBy(x => x).Select(x => x.ToString())) {
                                Dispatcher.BeginInvoke(new Action(() => ipListOutput.Items.Add(ip.Equals(myIp.Address.ToString()) ? "*" + ip : ip)));
                            }
                        }
                    });
                }
            }
        }

        private void CloseBtn(object sender, RoutedEventArgs e) {
            Environment.Exit(0);
        }

        private void MinBtn(object sender, RoutedEventArgs e) {
            WindowState = WindowState.Minimized;
        }

        private void OnDragMoveWindow(object sender, MouseButtonEventArgs e) {
            DragMove();
        }

        public void NetUtility() {
        }
    }

    internal class IpScanner {
        private static volatile Stopwatch _timer = new Stopwatch();
        public static Task<List<IPAddress>> scan(string baseIP) {
            return Task.Run(() => {
                List<Ping> pingers = new List<Ping>();
                int instances = 0;
                int timeOut = 250;
                int ttl = 5;
                List<IPAddress> ipFound = new List<IPAddress>();
                Debug.WriteLine($"Pinging 255 destinations of D-class in {baseIP}*");

                // Generate ping for 0-255:
                for (int i = 1; i <= 255; ++i) {
                    Ping p = new Ping();
                    p.PingCompleted += (s,e) => {
                        if (e.Reply.Status == IPStatus.Success) {
                            Debug.WriteLine(string.Concat("Found: ", e.Reply.Address.ToString()));
                            lock (ipFound) {
                                ipFound.Add(e.Reply.Address);
                            }
                        }
                        Interlocked.Decrement(ref instances);
                    };
                    pingers.Add(p);
                }

                Debug.WriteLine($"Stating timer... [{_timer.ElapsedMilliseconds}ms]");
                _timer.Start();
                for(int i = 1; i < pingers.Count; ++i) {
                    Interlocked.Increment(ref instances);
                    pingers[i].SendAsync(string.Concat(baseIP, i), timeOut, new ASCIIEncoding().GetBytes("abababababababababababababababab"), new PingOptions(ttl, true));
                }
                var wait = new SpinWait();
                while (instances > 0) wait.SpinOnce();
                wait.SpinOnce();
                _timer.Stop();
                Debug.WriteLine($"Finished in {_timer.ElapsedMilliseconds}ms. Found {ipFound.Count} active IP addresses.");
                _timer.Reset();

                // Cleanup
                pingers.ForEach(p => p.Dispose());
                pingers.Clear();
                return ipFound;
            });
        }

        public static void pingGotresponseCallback(object s, PingCompletedEventArgs e) {

        }

    }
}
