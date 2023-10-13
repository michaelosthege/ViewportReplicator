using GalaSoft.MvvmLight.Command;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

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
            }
        }

        public bool IsEditable { get { return !IsRenderActive; } }

        private static Process DCSProcess { get { return Process.GetProcessesByName("DCS.exe").FirstOrDefault();  } }
        public static bool IsDCSRunning { get { return DCSProcess != null || true; } }

        private string _PathToMonitorConfigLua = "%USERPROFILE%\\Saved Games\\DCS\\Config\\MonitorSetup\\Helios.lua";
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

        private string _RawOutputRegion = "110,0,610,610";
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


        public MainWindow()
        {
            ViewportRegion = MonitorConfigParser.GetViewportRegion(ViewportID, PathToMonitorConfigLua);
            InitializeComponent();
            this.KeyDown += MainWindow_KeyDown;
            this.Deactivated += MainWindow_Deactivated;
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (sender is null)
                return;
            Window window = (Window)sender;
            window.Topmost = true;
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DeactivateRendering();
            }
        }

        private void ActivateRendering()
        {
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
        }
    }
}
