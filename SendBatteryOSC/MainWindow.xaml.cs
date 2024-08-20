using SharpOSC;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Valve.VR;

namespace SendBatteryOSC
{
    public class Device 
    {
        public string Name;
        public string ID;
        public float Battery;

        public Device(string name, string id, float battery) 
        { 
            Name = name;
            ID = id;
            Battery = battery;
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public List<Device> Devices = new List<Device>();

        private DispatcherTimer _timer1;

        private int waitClickSlot = -1;

        const int slotCount = 6; // スロットリストのボタン数

        const string ip = "127.0.0.1"; // VRChatのIPアドレス
        const int port = 9000; // VRChatのOSC受信用ポート
        const string batteryAddress = "/avatar/parameters/BatteryFloat";
        const int updateDeviceInterval = 10; // 10秒ごと

        private UDPSender oscSender;
        private bool disposed = false;

        List<Button> slotButtons = new();

        List<string> sendDeviceIDList;

        public MainWindow()
        {
            InitializeComponent();
            FirstUpdateTimer();

            oscSender = new UDPSender(ip, port);

            sendDeviceIDList = new();
            for (int i = 0; i < slotCount; i++)
            {
                sendDeviceIDList.Add("");
            }

            // スロットリストのボタン作成
            CreateSlotButtons();

            EVRInitError error = EVRInitError.None;
            OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);

            this.Closed += OnWindowClosed;
        }

        void FirstUpdateTimer()
        {
            DispatcherTimer initialTimer = new();
            initialTimer.Interval = TimeSpan.FromSeconds(1); // 1秒後に実行
            initialTimer.Tick += (sender, e) =>
            {
                UpdateAndSend();
                initialTimer.Stop();
                UpdateTimer();
            };
            initialTimer.Start();
        }

        void UpdateTimer()
        {
            _timer1 = new();
            _timer1.Interval = TimeSpan.FromSeconds(updateDeviceInterval);
            _timer1.Tick += (sender, e) =>
            {
                UpdateAndSend();
            };
            _timer1.Start();
        }

        void UpdateAndSend()
        {
            Console.WriteLine("UpdateDevice");

            // デバイスリスト更新
            Devices.Clear();
            Devices = ListDeviceBatteryStatus();

            // 送信する
            SendBatteryValue();

            CreateDeviceButton();
        }

        void CreateSlotButtons()
        {
            // クリア
            ClearSlotButtonGrid();

            slotButtons = new List<Button>();

            for (int i = 0; i < slotCount; i++)
            {
                Button newButton = new();

                newButton.Content = $"Slot {i}";

                newButton.FontSize = 12;
                newButton.Margin = new Thickness(5); // ボタンの周囲に5ピクセルのマージンを追加
                newButton.Padding = new Thickness(0); // ボタン内のテキストパディングを0に設定
                newButton.Width = 175;
                newButton.Height = 40;
                newButton.HorizontalAlignment = HorizontalAlignment.Center;
                newButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                newButton.Tag = i; // ボタンにタグを追加


                slotButtons.Add(newButton);
            }

            // ボタンをWrapPanelに追加
            if (slotButtons.Count == 0) return;

            // WrapPanelを作成
            WrapPanel wrapPanel = new();
            wrapPanel.Orientation = Orientation.Vertical;

            foreach (var button in slotButtons)
            {
                button.Click += new RoutedEventHandler(OnClickSlotButton);
                wrapPanel.Children.Add(button); // WrapPanelにボタンを追加
            }

            // ボタンが格納されたWrapPanelをUIに追加します。
            SlotGrid.Children.Add(wrapPanel);
        }

        void ClearDeviceListGrid()
        {
            // クリア
            DeviceListGrid.Children.Clear();
        }

        void ClearSlotButtonGrid()
        {
            // クリア
            SlotGrid.Children.Clear();
        }

        void CreateDeviceButton()
        {
            var buttons = new List<Button>();

            for (int i = 0; i < Devices.Count; i++)
            {
                Button newButton = new Button();
                Console.WriteLine(Devices[i].Name);

                // ボタンについての設定
                try
                {
                    newButton.Content = $"{Devices[i].Name}\n{Devices[i].ID}";

                    newButton.FontSize = 12;
                    newButton.Margin = new Thickness(5); // ボタンの周囲に5ピクセルのマージンを追加
                    newButton.Padding = new Thickness(0); // ボタン内のテキストパディングを0に設定
                    newButton.Width = 100;
                    newButton.Height = 40;
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine("ArgumentException caught!");
                    Console.WriteLine($"Message: {ex.Message}");
                    Console.WriteLine($"ParamName: {ex.ParamName}");
                    Console.WriteLine($"StackTrace: {ex.StackTrace}");
                }
                Console.WriteLine("Continuing execution...");

                buttons.Add(newButton);

                //panels.Add(new ScenePanel(item, newButton));
            }

            AddDeviceButton(buttons);
        }

        void AddDeviceButton(List<Button> newButtons)
        {
            ClearDeviceListGrid();

            if (newButtons.Count == 0) return;

            // WrapPanelを作成
            WrapPanel wrapPanel = new WrapPanel();
            wrapPanel.Orientation = Orientation.Horizontal;

            foreach (var button in newButtons)
            {
                button.Click += new RoutedEventHandler(OnClickDeviceButton);
                wrapPanel.Children.Add(button); // WrapPanelにボタンを追加
            }

            // ボタンが格納されたWrapPanelをUIに追加
            DeviceListGrid.Children.Add(wrapPanel);
        }

        void OnClickDeviceButton(object sender, RoutedEventArgs e)
        {
            // ボタンがクリックされたときの処理
            Button clickedButton = (Button)sender;

            // スロットが入力待機中ではないなら処理しない
            if (waitClickSlot == -1) return;

            // スロットにデバイスをセット
            SetDeviceInSlot(waitClickSlot, Devices.Find(x => clickedButton.Content.ToString().Contains(x.ID)));

            Console.WriteLine($"デバイスボタン {clickedButton.Content} をクリック");
        }

        void OnClickSlotButton(object sender, RoutedEventArgs e)
        {
            // クリックされたボタンを取得
            Button clickedButton = (Button)sender;

            int index = (int)clickedButton.Tag;

            if(waitClickSlot == index)
            {
                // 二連続でクリックされた スロットを空にする
                RemoveDeviceInSlot(index);
                waitClickSlot = -1;

                return;
            }

            // TextBlockを使わないと中央ぞろえできない
            {
                TextBlock textBlock = new TextBlock();
                textBlock.Text = "デバイスをクリック\nもう一度クリックで空にする";
                textBlock.TextAlignment = TextAlignment.Center;
                textBlock.VerticalAlignment = VerticalAlignment.Center;
                textBlock.HorizontalAlignment = HorizontalAlignment.Center;

                clickedButton.Content = textBlock;
            }

            // ボタン名のデバイスのindexをセット
            waitClickSlot = index;

            Console.WriteLine($"{clickedButton.Content} index: {index} をクリック");
        }

        void SetDeviceInSlot(int slotNum, Device device)
        {
            slotButtons[slotNum].Content = $"{device.Name}\n{device.ID}";

            sendDeviceIDList[slotNum] = device.ID;

            // 待機状態を解除
            waitClickSlot = -1;
        }

        void RemoveDeviceInSlot(int slotNum)
        {
            slotButtons[slotNum].Content = $"Slot {slotNum}";

            sendDeviceIDList[slotNum] = "";
        }

        void SendBatteryValue()
        {
            if (sendDeviceIDList.Count == 0)
            {
                Console.WriteLine("デバイスがないので処理しない");
                return;
            }

            //送信
            {
                foreach (var deviceID in sendDeviceIDList)
                {
                    var foundDevice = Devices.Find(x => x.ID == deviceID);
                    if (foundDevice == null)
                    {
                        Console.WriteLine($"デバイス {deviceID} が見つからないので処理しない");
                        continue;
                    }

                    float value = 1 - foundDevice.Battery;
                    var tempBatteryAddress = batteryAddress + sendDeviceIDList.IndexOf(deviceID).ToString("D2");

                    var message = new OscMessage(tempBatteryAddress, value);
                    oscSender.Send(message);

                    Console.WriteLine($"{ip}:{port} の {tempBatteryAddress} に {value} を送信した");
                }
            }
        }

        List<Device> ListDeviceBatteryStatus()
        {
            List<Device> devices = new();

            if (OpenVR.System == null)
            {
                Console.WriteLine("OpenVR.System is null");
                return devices;
            }

            for (int i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
            {
                ETrackedDeviceClass deviceClass = OpenVR.System.GetTrackedDeviceClass((uint)i);
                float batteryPercentage = GetTrackedDevicePropertyFloat((uint)i, ETrackedDeviceProperty.Prop_DeviceBatteryPercentage_Float);
                string aaa = GetTrackedDevicePropertyString((uint)i, ETrackedDeviceProperty.Prop_SerialNumber_String);
                Console.WriteLine("        Device " + i + ": " + aaa + " (" + deviceClass.ToString() + "), Battery: " + (batteryPercentage * 100).ToString("F0") + "%");
                if (batteryPercentage >= 0)
                {
                    if (batteryPercentage >= 0)
                    {
                        string deviceName = GetTrackedDevicePropertyString((uint)i, ETrackedDeviceProperty.Prop_ModelNumber_String);
                        string deviceID = GetTrackedDevicePropertyString((uint)i, ETrackedDeviceProperty.Prop_SerialNumber_String);
                        Console.WriteLine("Device " + i + ": " + deviceID + " (" + deviceClass.ToString() + "), Battery: " + (batteryPercentage * 100).ToString("F0") + "%");

                        // デバイスの情報をリストに追加
                        devices.Add(new (deviceName, deviceID, batteryPercentage));
                    }
                }
            }

            return devices;
        }

        float GetTrackedDevicePropertyFloat(uint deviceId, ETrackedDeviceProperty prop)
        {
            ETrackedPropertyError error = ETrackedPropertyError.TrackedProp_Success;
            float result = OpenVR.System.GetFloatTrackedDeviceProperty(deviceId, prop, ref error);
            if (error == ETrackedPropertyError.TrackedProp_Success)
            {
                return result;
            }
            return -1;
        }

        string GetTrackedDevicePropertyString(uint deviceId, ETrackedDeviceProperty prop)
        {
            ETrackedPropertyError error = ETrackedPropertyError.TrackedProp_Success;
            uint bufferSize = OpenVR.System.GetStringTrackedDeviceProperty(deviceId, prop, null, 0, ref error);
            if (bufferSize > 1)
            {
                System.Text.StringBuilder buffer = new System.Text.StringBuilder((int)bufferSize);
                OpenVR.System.GetStringTrackedDeviceProperty(deviceId, prop, buffer, bufferSize, ref error);
                return buffer.ToString();
            }
            return null;
        }

        public void OnWindowClosed(object sender, EventArgs e)
        {
            if (!disposed)
            {
                Console.WriteLine("Dispose");
                oscSender?.Close();
                OpenVR.Shutdown();
                disposed = true;
            }
        }
    }
}
