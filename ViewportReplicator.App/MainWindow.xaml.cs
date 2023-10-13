using Capture;
using Capture.Interface;
using GalaSoft.MvvmLight.Command;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ViewportReplicator.App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region INotifyPropertyChanged Members
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        private bool _IsRenderActive;
        public bool IsRenderActive
        {
            get { return _IsRenderActive; }
            set
            {
                _IsRenderActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanActivate));
                OnPropertyChanged(nameof(IsEditable));
                ActivateCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsEditable { get { return !IsRenderActive; } }

        private static Process DCSProcess { get { return Process.GetProcessesByName("DCS").FirstOrDefault(); } }
        public static bool IsDCSRunning { get { return DCSProcess != null; } }

        private string _PathToMonitorConfigLua = "%USERPROFILE%\\Saved Games\\DCS\\Config\\MonitorSetup\\DualMFD.lua";
        public string PathToMonitorConfigLua
        {
            get { return _PathToMonitorConfigLua; }
            set
            {
                _PathToMonitorConfigLua = value;
                OnPropertyChanged();
                ViewportRegion = MonitorConfigParser.GetViewportRegion(ViewportID, PathToMonitorConfigLua);
            }
        }

        private string _ViewportID = "FA_18C_LEFT_MFCD";
        public string ViewportID
        {
            get { return _ViewportID; }
            set
            {
                _ViewportID = value;
                OnPropertyChanged();
                ViewportRegion = MonitorConfigParser.GetViewportRegion(ViewportID, PathToMonitorConfigLua);
            }
        }

        private string _RawOutputRegion = "3953,-8,600,595";
        public string RawOutputRegion
        {
            get { return _RawOutputRegion; }
            set
            {
                _RawOutputRegion = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OutputRect));
            }
        }

        private Rectangle? _ViewportRegion;
        public Rectangle? ViewportRegion
        {
            get { return _ViewportRegion; }
            set
            {
                _ViewportRegion = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanActivate));
            }
        }

        private Rect PreActivationRect { get; set; }
        private Rect OutputRect
        {
            get
            {
                try
                {
                    string[] values = RawOutputRegion.Split(',');
                    return new Rect(
                        Convert.ToInt32(values[0]),
                        Convert.ToInt32(values[1]),
                        Convert.ToInt32(values[2]),
                        Convert.ToInt32(values[3])
                    );
                }
                catch
                {
                    return Rect.Empty;
                }
            }
        }

        public bool CanActivate
        {
            get
            {
                return !IsRenderActive && IsEditable && IsDCSRunning && ViewportRegion != null && OutputRect != Rect.Empty;
            }
        }

        private RelayCommand? _ActivateCommand;
        public RelayCommand ActivateCommand
        {
            get
            {
                _ActivateCommand ??= new RelayCommand(ActivateRendering, () => CanActivate);
                return _ActivateCommand;
            }
        }

        private RelayCommand _DeactivateCommand;
        public RelayCommand DeactivateCommand
        {
            get
            {
                if (_DeactivateCommand == null)
                {
                    _DeactivateCommand = new RelayCommand(DeactivateRendering);
                }
                return _DeactivateCommand;
            }
        }

        private CaptureInterface _CaptureInterface;
        private CaptureProcess _CaptureProcess;

        public MainWindow()
        {
            ViewportRegion = MonitorConfigParser.GetViewportRegion(ViewportID, PathToMonitorConfigLua);
            InitializeComponent();
            this.Deactivated += MainWindow_Deactivated;
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (sender is null)
                return;
            Window window = (Window)sender;
            window.Topmost = true;
        }

        private void ActivateRendering()
        {
            // Initialize the capturing process
            var config = new CaptureConfig()
            {
                Direct3DVersion = Direct3DVersion.Direct3D11,
                TargetFramesPerSecond = 20,
            };
            if (_CaptureProcess == null)
            {
                _CaptureInterface = new CaptureInterface();
                _CaptureInterface.RemoteMessage += CaptureInterface_RemoteMessage;
                _CaptureProcess = new CaptureProcess(DCSProcess, config, _CaptureInterface);
            }
            RequestScreenshot();

            // Morph Window into the render output
            WindowStyle = WindowStyle.None;
            SizeToContent = SizeToContent.Manual;
            PreActivationRect = new Rect(Left, Top, Width, Height);
            this.Height = OutputRect.Height;
            this.Width = OutputRect.Width;
            this.Left = OutputRect.Left;
            this.Top = OutputRect.Top;
            renderOutput.Visibility = Visibility.Visible;
            IsRenderActive = true;
        }

        private void RequestScreenshot()
        {
            if (ViewportRegion == null)
            {
                return;
            }
            // Initiate the screenshot & the appropriate event handler within the target process will take care of the rest
            _CaptureProcess.CaptureInterface.BeginGetScreenshot(
                region: (Rectangle)ViewportRegion,
                timeout: new TimeSpan(0, 0, 2),
                callback: Callback,
                resize: null,
                format: (ImageFormat)Enum.Parse(typeof(ImageFormat), "Bitmap")
            );
        }

        /// <summary>
        /// The callback for when the screenshot has been taken
        /// </summary>
        void Callback(IAsyncResult result)
        {
            using Screenshot screenshot = _CaptureProcess.CaptureInterface.EndGetScreenshot(result);
            try
            {
                if (screenshot != null && screenshot.Data != null)
                {
                    Bitmap bmp = screenshot.ToBitmap();

                    using (MemoryStream memory = new MemoryStream())
                    {
                        bmp.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                        memory.Position = 0;
                        Dispatcher.Invoke(delegate()
                        {
                            BitmapImage bitmapimage = new BitmapImage();
                            bitmapimage.BeginInit();
                            bitmapimage.StreamSource = memory;
                            bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapimage.EndInit();
                            renderImage.Source = bitmapimage;
                        });
                    }
                }

                Thread t = new Thread(new ThreadStart(RequestScreenshot));
                t.Start();
            }
            catch (Exception ex)
            {
                Debugger.Break();
            }
        }

        private void CaptureInterface_RemoteMessage(MessageReceivedEventArgs message)
        {
            Debug.WriteLine(message.Message);
        }

        private void DeactivateRendering()
        {
            if (!IsRenderActive) { return; }

            // Reset Window to previous layout
            renderOutput.Visibility = Visibility.Collapsed;
            WindowStyle = WindowStyle.SingleBorderWindow;
            this.Height = PreActivationRect.Height;
            this.Width = PreActivationRect.Width;
            this.Left = PreActivationRect.Left;
            this.Top = PreActivationRect.Top;
            SizeToContent = SizeToContent.Height;
            IsRenderActive = false;

            // Stop capture process
            //_CaptureProcess.Dispose();
            //_CaptureInterface.RemoteMessage -= CaptureInterface_RemoteMessage;
            //_CaptureInterface.Disconnect();
            //_CaptureInterface = null;
        }
    }
}
