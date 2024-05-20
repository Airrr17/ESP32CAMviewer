using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AForge.Video;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.ComponentModel;
using System.IO;


namespace ESP32CAMviewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        //Vars:
        public string SETTINGS_FILE = "settings.air";
        public string espip = "127.0.0.1";                  //Dummy
        IPAddress localIP = IPAddress.Parse("127.0.0.1");   //Dummy
        public const string esphost = "esp32-";             //esp32's default host looks like esp32-ddeeff
        bool found = false;
        bool go = false;
        bool firstframe = true;                             //to get w&h
        byte streamvariant = 0;                             //Switching between two(as for now) variants:
        string[] varia = { "/", ":81/stream" };             //Goes as "http://" + IP + this string. Just add yours here.

        MJPEGStream stream;

        public MainWindow()
        {
            SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Display);                                                    //Smoothing must die!
            ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(Int32.MaxValue));   //ToolTips stays forever.

            Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);                                            //On close.

            InitializeComponent();

            if (File.Exists(SETTINGS_FILE))                 //Load IP from file
            {
                StreamReader settingsfile = new StreamReader(SETTINGS_FILE);
                string ipfile = settingsfile.ReadLine();
                if (IPAddress.TryParse(ipfile, out IPAddress address))
                {
                    settingsfile.Close();
                    espip = ipfile;
                    TextBoxIP.Foreground = System.Windows.Media.Brushes.Lime;
                    TextBoxIP.Text = espip;
                    ButtonVideo.IsEnabled = true;
                }
                else
                {
                    settingsfile.Close();
                    labelDebug.Content = "Settings file corrupt. Deleting.";
                    File.Delete(SETTINGS_FILE);
                }
                settingsfile.Close();
            }

        }

        private async void ButtonFind_Click(object sender, RoutedEventArgs e)               //Try to find ESP32 on the local net by its hostname "esp32-*"
        {                                                                                   //Most complicated shitpart:
            TextBoxIP.Foreground = System.Windows.Media.Brushes.Gold;
            TextBoxIP.Background = System.Windows.Media.Brushes.Maroon;
            TextBoxIP.Text = "WAIT";
            labelDebug.Content = "Scanning LAN hostnames for 'esp32-*'...";
            await Dispatcher.Yield();

            if (NetworkInterface.GetIsNetworkAvailable() == false)                          //Stop if no network found.
            {
                TextBoxIP.Text = "No LAN!";
                labelDebug.Content = "Check your network connection.";
                return;
            }

            int totalIPs = Dns.GetHostEntry(Dns.GetHostName()).AddressList.Length;          //If few IPs found...
            for (byte h = 0; h < totalIPs; h++)
            {
                IPAddress tIP = Dns.GetHostEntry(Dns.GetHostName()).AddressList[h];
                if ((tIP.ToString()[0]).ToString() != ":") localIP = tIP;                   //Excluding "::1" (ie 0.0.0.0)
            }

            if (localIP == IPAddress.Parse("127.0.0.1"))                                    //If not changed from default...
            {
                labelDebug.Content = "Local IP not identified! Are you connected?";
                return;
            }

            //Suppose we found IP successfully:
            byte[] AddressSections = localIP.GetAddressBytes();                             //Split
            labelDebug.Content = "Scanning " + AddressSections[0] + "." + AddressSections[1] + "." + AddressSections[2] + ".* for 'esp32-*' hostname...";

            List<IPAddress> ips = new List<IPAddress>();
            for (byte i = 1; i != 255; i++)                                                 //Build the list of all IPs in subnet
            {
                IPAddress pp = new IPAddress(new byte[] { AddressSections[0], AddressSections[1], AddressSections[2], i });
                ips.Add(pp);
            }

            List<Task> ipstasklist = new List<Task>();                                      //Ping list
            foreach (IPAddress ip in ips)
            {
                ipstasklist.Add(Pinging(ip));
            }
            async Task Pinging(IPAddress ip)
            {
                PingReply result = await new Ping().SendPingAsync(ip);
                if (result.Status.ToString() == "Success")                                  //If pinged successufly
                {
                    try
                    {
                        IPHostEntry entry2 = Dns.GetHostEntry(result.Address);              //Get hostname

                        if (string.Compare(esphost, 0, entry2.HostName, 0, 6) == 0)         //Compare strings
                        {
                            espip = result.Address.ToString();
                            found = true;
                            labelDebug.Content = "Found with IP: " + espip;
                        }

                    }
                    catch (SocketException)
                    {
                        //something
                    }

                }

            }

            while (ipstasklist.Count > 0)                                 //Runs on all complete
            {
                Task t = await Task.WhenAny(ipstasklist);
                ipstasklist.Remove(t);
                TextBoxIP.Foreground = System.Windows.Media.Brushes.White;
                TextBoxIP.Background = System.Windows.Media.Brushes.DimGray;

                if (found == true)
                {
                    ButtonFind.Content = "Found!";
                    TextBoxIP.Foreground = System.Windows.Media.Brushes.Lime;
                    TextBoxIP.Text = espip;
                    ButtonVideo.IsEnabled = true;
                }

                if (found == false)
                {
                    ButtonFind.Content = "Not Found!";
                    TextBoxIP.Foreground = System.Windows.Media.Brushes.Maroon;
                    TextBoxIP.Text = "Enter IP now!";
                    labelDebug.Content = "Not found on " + AddressSections[0] + "." + AddressSections[1] + "." + AddressSections[2] + ".*" + " See serial output of your esp32 or input IP manually.";
                    found = false;
                }


            }


        }

        private void ButtonVideo_Click(object sender, RoutedEventArgs e)          //START video
        {
            if (NetworkInterface.GetIsNetworkAvailable() == false)                          //Stop if no network found.
            {
                labelDebug.Content = "No LAN! Check your network connection.";
                return;
            }
            ButtonVideo.IsEnabled = false;
            ButtonVideo2.IsEnabled = true;
            ButtonFind.IsEnabled = false;
            labelDebug.Content = "Requesting http://" + espip + varia[streamvariant];
            stream = new MJPEGStream("http://" + espip + varia[streamvariant]);
            stream.NewFrame += new NewFrameEventHandler(video_NewFrame);
            stream.Start();
        }

        void video_NewFrame(object sender, NewFrameEventArgs eventArgs)           //Display video frames
        {
            this.Dispatcher.Invoke(() =>
            {
                if (firstframe == true)
                {
                    int a = eventArgs.Frame.Width;
                    int b = eventArgs.Frame.Height;
                    labelDebug.Content = "Playing http://" + espip + varia[streamvariant] + " @" + a + "x" + b;
                    firstframe = false;
                    //ImageVideo.Width = a;                                 //Autoresize or fixed native size.
                    //ImageVideo.Height = b;
                }
                using (MemoryStream memory = new MemoryStream())
                {
                    eventArgs.Frame.Save(memory, System.Drawing.Imaging.ImageFormat.Jpeg);
                    memory.Position = 0;
                    BitmapImage bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = memory;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    //TransformedBitmap targetBitmap = new TransformedBitmap(bitmapImage, new ScaleTransform(0.5, 0.5));   //Zooming.. never used
                    //ImageVideo.Source = targetBitmap;
                    ImageVideo.Source = bitmapImage;
                }
            });

        }

        private void ButtonVideo2_Click(object sender, RoutedEventArgs e)         //STOP  video
        {
            stream.Stop();
            labelDebug.Content = "Stopped. Thank you!";
            ButtonVideo.IsEnabled = true;
            ButtonVideo2.IsEnabled = false;
            ButtonFind.IsEnabled = true;
            streamvariant++;
            if (streamvariant > varia.Length - 1) streamvariant = 0;
            firstframe = true;
        }

        private void TextBoxIP_TextChanged(object sender, TextChangedEventArgs e)
        {
            string ip2check = TextBoxIP.Text;
            string[] split = ip2check.Split('.');
            if (IPAddress.TryParse(ip2check, out _) && (split.Length == 4))    //if IP is true
            {
                TextBoxIP.Foreground = System.Windows.Media.Brushes.Lime;
                ButtonSave.IsEnabled = true;
                ButtonVideo.IsEnabled = true;
                go = true;
                espip = ip2check;
            }
            else if (go == true)
            {
                TextBoxIP.Foreground = System.Windows.Media.Brushes.White;
                ButtonSave.IsEnabled = false;
                ButtonVideo.IsEnabled = false;
            }
        }

        private void ButtonSave_Click(object sender, RoutedEventArgs e)           //Save settings to the file
        {
            if (File.Exists(SETTINGS_FILE)) File.Delete(SETTINGS_FILE);
            using (StreamWriter settingsfile = new StreamWriter(SETTINGS_FILE))
            {
                settingsfile.WriteLine(espip);
                settingsfile.Close();
            }
        }

        private void ButtonAbout_Click(object sender, RoutedEventArgs e)          //About message
        {
            MessageBox.Show("ESP32camera viewer. (c)Airrr(r) '24\n\r" +
                            "Clicking stop button switches to the next url variant cyclically.\n\r" +
                             varia.Length + " variants loaded.\n\r\n\r" +
                            "Using AForge.NET www.aforgenet.com/framework/\n\r" +
                            "https://github.com/Airrr17" +
                            "\n\rv0.900");
        }

        void MainWindow_Closing(object sender, CancelEventArgs e)                 //On close
        {
            if (ButtonVideo.IsEnabled == false && ButtonSave.IsEnabled == true) stream.Stop();                    //Stop if currently playing before exit
            //this.Close();
        }
    }
}
