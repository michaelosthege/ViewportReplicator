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
using System.Windows.Threading;

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
            }
        }

        private string _PathToMonitorConfigLua = "%USERPROFILE%\\Saved Games\\DCS\\Config\\MonitorSetup\\DualMFD.lua";
        public string PathToMonitorConfigLua
        {
            get { return _PathToMonitorConfigLua; }
            set
            {
                _PathToMonitorConfigLua = value;
                ViewportRegion = MonitorConfigParser.GetViewportRegion(ViewportID, PathToMonitorConfigLua);
                OnPropertyChanged();
            }
        }

        private string _ViewportID = "FA_18C_LEFT_MFCD";
        public string ViewportID
        {
            get { return _ViewportID; }
            set
            {
                _ViewportID = value;
                ViewportRegion = MonitorConfigParser.GetViewportRegion(ViewportID, PathToMonitorConfigLua);
                OnPropertyChanged();
            }
        }

        private string _RawOutputRegion = "3953,0,600,595";
        public string RawOutputRegion
        {
            get { return _RawOutputRegion; }
            set
            {
                _RawOutputRegion = value;
                OnPropertyChanged();
            }
        }

        private Rectangle? _ViewportRegion;
        public Rectangle? ViewportRegion
        {
            get { return _ViewportRegion; }
            private set
            {
                _ViewportRegion = value;
                OnPropertyChanged();
            }
        }

        #region Commands
        private RelayCommand _ActivateCommand;
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
                _DeactivateCommand ??= new RelayCommand(DeactivateRendering);
                return _DeactivateCommand;
            }
        }
        #endregion

        #region Dependent properties
        public bool IsDCSRunning { get { return _DCSProcess != null; } }
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
        public bool IsViewportOK { get { return ViewportRegion != null; } }
        public bool IsOutputOK { get { return OutputRect != Rect.Empty; } }
        public bool IsEditable { get { return !IsRenderActive; } }
        public bool CanActivate
        {
            get
            {
                return !IsRenderActive && IsEditable && IsDCSRunning && IsViewportOK && IsOutputOK;
            }
        }
        #endregion

        private Process _DCSProcess;
        private DispatcherTimer _RefreshTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(1) };
        private Rect _PreActivationRect;
        private CaptureInterface _CaptureInterface;
        private CaptureProcess _CaptureProcess;

        public MainWindow()
        {
            ViewportRegion = MonitorConfigParser.GetViewportRegion(ViewportID, PathToMonitorConfigLua);
            InitializeComponent();
            this.Deactivated += MainWindow_Deactivated;
            _RefreshTimer.Tick += _RefreshTimer_Tick;
            _RefreshTimer.Start();
        }

        private void _RefreshTimer_Tick(object sender, EventArgs e)
        {
            _DCSProcess = Process.GetProcessesByName("DCS").FirstOrDefault();
            OnPropertyChanged(nameof(IsDCSRunning));
            OnPropertyChanged(nameof(IsViewportOK));
            OnPropertyChanged(nameof(IsOutputOK));
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(CanActivate));
            ActivateCommand.RaiseCanExecuteChanged();
            DeactivateCommand.RaiseCanExecuteChanged();
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
                _CaptureProcess = new CaptureProcess(_DCSProcess, config, _CaptureInterface);
            }
            RequestScreenshot();

            // Morph Window into the render output
            WindowStyle = WindowStyle.None;
            SizeToContent = SizeToContent.Manual;
            _PreActivationRect = new Rect(Left, Top, Width, Height);
            SetWindowSize(OutputRect);
            renderOutput.Visibility = Visibility.Visible;
            IsRenderActive = true;

            // No need to keep refreshing
            _RefreshTimer.Stop();
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

            // Reactivate UI refreshing
            _RefreshTimer.Start();

            // Reset Window to previous layout
            renderOutput.Visibility = Visibility.Collapsed;
            WindowStyle = WindowStyle.SingleBorderWindow;
            SetWindowSize(_PreActivationRect);
            SizeToContent = SizeToContent.Height;
            IsRenderActive = false;

            // Stop capture process
            //_CaptureProcess.Dispose();
            //_CaptureInterface.RemoteMessage -= CaptureInterface_RemoteMessage;
            //_CaptureInterface.Disconnect();
            //_CaptureInterface = null;
        }

        private void SetWindowSize(Rect windowRect)
        {
            this.Left = windowRect.Left;
            this.Top = windowRect.Top;
            this.Height = windowRect.Height;
            this.Width = windowRect.Width;
        }
    }
}
